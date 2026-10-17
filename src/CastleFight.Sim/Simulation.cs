using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

public enum PlacementResult : byte
{
    Ok,
    Invalid,
    OutsideZone,
    Occupied,
    NotEnoughGold,
    OnRamp,

    /// <summary>The builder can't get next to the site.</summary>
    Unreachable,
}

/// <summary>
/// The deterministic game state. Same <see cref="GameSetup"/> + same commands per tick gives a
/// bit-identical state on every machine, which is what lockstep multiplayer relies on.
/// Rules: no float/double, no System.Random, no wall-clock time, no engine calls in here.
/// </summary>
public sealed partial class Simulation
{
    public const int TicksPerSecond = 20;
    public const int TeamCount = 2;

    const int RetargetIntervalTicks = 5;
    const int MaxDebugSpawnPerCommand = 10_000;
    const int DebugSpawnAttempts = 8;
    const int MaxSpawnRing = 3;
    const byte NeutralOwner = byte.MaxValue;

    static readonly Fix GridCellSize = Fix.FromInt(2);
    static readonly Fix UnitSeparation = Fix.Half;
    static readonly Fix StructureSeparation = Fix.One;
    static readonly Fix WallSeparation = Fix.One;
    static readonly Fix SidestepStrength = Fix.FromRatio(3, 4);
    static readonly Fix MaxSpeedFactor = Fix.FromRatio(3, 2);
    static readonly Fix SpawnGap = Fix.FromRatio(1, 4);
    static readonly Fix SpawnJitter = Fix.FromRatio(1, 4);
    static readonly Fix LaneEdgeMargin = Fix.Half;

    readonly Player[] _players;
    readonly SpatialGrid[] _teamGrids;
    readonly StructureGrid _structureGrid;
    readonly BaseFlowField[] _laneFields;
    readonly BaseFlowField[] _castleFields;
    readonly int[] _tileOccupant;
    readonly Fix[] _halfFootprintOfType;
    readonly List<int> _spawnCandidates = new();
    readonly int _incomePerSecond;
    DeterministicRandom _rng;
    FixVec2[] _velocity = [];
    bool _structuresChanged = true;
    int _nextUnitId = 1;

    /// <param name="map">Overrides the map chosen by <see cref="GameSetup.Map"/> (tests and benchmarks).</param>
    public Simulation(GameSetup setup, MapDefinition? map = null, UnitCatalog? catalog = null)
    {
        if (setup.PlayerTeams.Count is 0 or > GameSetup.MaxPlayers)
            throw new ArgumentException($"A match needs 1 to {GameSetup.MaxPlayers} players.", nameof(setup));

        Map = map ?? MapCatalog.Get(setup.Map);
        Catalog = catalog ?? UnitCatalog.CreateDefault();
        Units = new UnitPool();
        _rng = new DeterministicRandom(setup.Seed);
        _incomePerSecond = setup.IncomePerSecond;

        var castleType = Catalog[Catalog.CastleTypeId];
        for (int team = 0; team < TeamCount; team++)
        {
            var footprint = Map.CastleFootprint(team);
            if (footprint.Width != castleType.FootprintTiles || footprint.Height != castleType.FootprintTiles)
                throw new ArgumentException($"The map's castle footprint does not match the {castleType.FootprintTiles}-tile castle type.");
        }

        _players = new Player[setup.PlayerTeams.Count];
        for (int i = 0; i < _players.Length; i++)
        {
            byte team = setup.PlayerTeams[i];
            if (team >= TeamCount)
                throw new ArgumentException($"Player {i} has invalid team {team}.", nameof(setup));
            _players[i] = new Player(i, team, setup.StartingGold, BuilderStart(setup.PlayerTeams, i));
        }

        _halfFootprintOfType = new Fix[Catalog.Count];
        for (int id = 0; id < Catalog.Count; id++)
            _halfFootprintOfType[id] = Fix.FromInt(Catalog[id].FootprintTiles) / 2;

        _tileOccupant = new int[Map.LengthTiles * Map.WidthTiles];
        _teamGrids = new SpatialGrid[TeamCount];
        _laneFields = new BaseFlowField[TeamCount];
        _castleFields = new BaseFlowField[TeamCount];
        for (int team = 0; team < TeamCount; team++)
        {
            _teamGrids[team] = new SpatialGrid(Map.Length, Map.Width, GridCellSize);
            _laneFields[team] = BaseFlowField.ToLanes(Map, team);
            _castleFields[team] = BaseFlowField.ToEnemyCastle(Map, team);
        }
        _structureGrid = new StructureGrid(Map.Length, Map.Width, GridCellSize);

        for (byte team = 0; team < TeamCount; team++)
            SpawnUnit(Catalog.CastleTypeId, team, NeutralOwner, Map.CastlePosition(team));
        RebuildStructureData();
    }

    public MapDefinition Map { get; }
    public UnitCatalog Catalog { get; }
    public UnitPool Units { get; }
    public IReadOnlyList<Player> Players => _players;

    /// <summary>Number of completed steps. The next <see cref="Step"/> executes this tick.</summary>
    public int Tick { get; private set; }

    public int WinningTeam { get; private set; } = -1;
    public bool IsGameOver => WinningTeam >= 0;

    /// <summary>Advances one tick. <paramref name="commands"/> must be in canonical order (see <see cref="CommandSchedule"/>).</summary>
    public void Step(ReadOnlySpan<Command> commands)
    {
        Array.Copy(Units.Pos, Units.PrevPos, Units.HighWater);

        foreach (ref readonly var command in commands)
            ApplyCommand(in command);

        if (!IsGameOver && Tick % TicksPerSecond == 0)
        {
            foreach (var player in _players)
                player.Gold += _incomePerSecond;
        }
        if (IsGameOver && !IsHoleOpen && Tick - GameOverTick >= TeardownTicks)
            OpenHole();

        StepBuilders();
        if (!IsGameOver)
            RunSpawners();

        for (byte team = 0; team < TeamCount; team++)
            _teamGrids[team].Rebuild(Units, team);
        if (_structuresChanged)
            RebuildStructureData();

        if (!IsGameOver)
            AcquireTargets();
        ComputeVelocities();
        Integrate();
        if (!IsGameOver)
            ResolveAttacks();
        RemoveDead();
        if (IsHoleOpen)
            DropIntoHole();

        Tick++;
    }

    /// <summary>
    /// Whether <paramref name="playerId"/> can place <paramref name="typeId"/> with its footprint's lowest
    /// tile at (<paramref name="tileX"/>, <paramref name="tileY"/>): enough gold, the footprint inside one of
    /// the team's build zones, off the ramps and free (no building, construction site or builder building there).
    /// Adjacent buildings are fine.
    /// </summary>
    public bool CanPlaceBuilding(int playerId, int typeId, int tileX, int tileY) =>
        CheckPlacement(playerId, typeId, tileX, tileY) == PlacementResult.Ok;

    /// <summary>Like <see cref="CanPlaceBuilding"/>, but says why not (for the placement preview).</summary>
    /// <param name="queued">For a queued build order: the sites the player's builder is already heading for or has
    /// queued count as occupied too.</param>
    public PlacementResult CheckPlacement(int playerId, int typeId, int tileX, int tileY, bool queued = false)
    {
        if (IsGameOver || (uint)playerId >= (uint)_players.Length || (uint)typeId >= (uint)Catalog.Count)
            return PlacementResult.Invalid;

        var type = Catalog[typeId];
        var player = _players[playerId];
        if (!type.IsStructure || type.IsCastle || type.Cost <= 0)
            return PlacementResult.Invalid;

        int size = type.FootprintTiles;
        var footprint = new TileRect(tileX, tileY, tileX + size, tileY + size);
        if (!Map.IsInBuildZone(player.Team, footprint))
            return PlacementResult.OutsideZone;
        if (Map.BlocksRamp(footprint))
            return PlacementResult.OnRamp;
        for (int y = tileY; y < tileY + size; y++)
        {
            for (int x = tileX; x < tileX + size; x++)
            {
                if (_tileOccupant[y * Map.LengthTiles + x] != 0)
                    return PlacementResult.Occupied;
            }
        }
        if (OverlapsConstruction(footprint) || (queued && OverlapsPlannedSite(player.Builder, footprint)))
            return PlacementResult.Occupied;
        return player.Gold < type.Cost ? PlacementResult.NotEnoughGold : PlacementResult.Ok;
    }

    /// <summary>Slot of the structure covering the tile, or -1.</summary>
    public int TileOccupant(int tileX, int tileY) =>
        Map.InBounds(tileX, tileY) ? _tileOccupant[tileY * Map.LengthTiles + tileX] - 1 : -1;

    /// <summary>The footprint's lowest tile for a structure of <paramref name="footprintTiles"/> centred at <paramref name="center"/>.</summary>
    public static (int X, int Y) FootprintOrigin(FixVec2 center, int footprintTiles)
    {
        Fix half = Fix.FromInt(footprintTiles) / 2;
        return ((center.X - half).FloorToInt(), (center.Y - half).FloorToInt());
    }

    /// <summary>Which lane a mobile unit at <paramref name="position"/> is heading for (or walking in) right now.</summary>
    public int LaneOf(int team, FixVec2 position)
    {
        if (!Map.IsInOwnBase(team, position.X))
            return Map.NearestLane(position.Y).Index;

        // Follow the flow field to the lane entrance; it is short, and this is only for tests and UI.
        var field = _laneFields[team];
        for (int step = 0; step < Map.LengthTiles * 2 && Map.IsInOwnBase(team, position.X); step++)
        {
            FixVec2 direction = field.DirectionAt(position);
            if (direction.IsZero)
                break;
            position += direction;
        }
        return Map.NearestLane(position.Y).Index;
    }

    void ApplyCommand(in Command command)
    {
        if ((uint)command.PlayerId >= (uint)_players.Length)
            return;
        var player = _players[command.PlayerId];

        switch (command.Kind)
        {
            case CommandKind.PlaceBuilding:
                if (!CanPlaceBuilding(command.PlayerId, command.TypeId, command.TileX, command.TileY))
                    return;
                var type = Catalog[command.TypeId];
                player.Gold -= type.Cost;
                var center = new FixVec2(
                    Fix.FromInt(command.TileX * 2 + type.FootprintTiles) / 2,
                    Fix.FromInt(command.TileY * 2 + type.FootprintTiles) / 2);
                SpawnUnit(command.TypeId, player.Team, (byte)player.Id, center);
                break;

            case CommandKind.MoveBuilder:
            case CommandKind.Build:
                if (IsGameOver)
                    return;
                ApplyBuilderCommand(player, in command);
                break;

            case CommandKind.DebugSpawn:
                if (command.TypeId >= Catalog.Count || Catalog[command.TypeId].IsStructure)
                    return;
                FixRect area = Map.StagingArea(player.Team).ToFixRect();
                Fix radius = Catalog[command.TypeId].Radius;
                int count = Math.Clamp(command.Count, 0, MaxDebugSpawnPerCommand);
                for (int k = 0; k < count; k++)
                {
                    FixVec2 position = default;
                    for (int attempt = 0; attempt < DebugSpawnAttempts; attempt++)
                    {
                        position = new FixVec2(_rng.NextFix(area.Min.X, area.Max.X), _rng.NextFix(area.Min.Y, area.Max.Y));
                        if (!Map.IsInsideWall(position, radius))
                            break;
                    }
                    SpawnUnit(command.TypeId, player.Team, (byte)player.Id, position);
                }
                break;
        }
    }

    int SpawnUnit(int typeId, byte team, byte owner, FixVec2 position)
    {
        var type = Catalog[typeId];
        var u = Units;
        int slot = u.Allocate();
        int id = _nextUnitId++;

        position = new FixVec2(
            Fix.Clamp(position.X, type.Radius, Map.Length - type.Radius),
            Fix.Clamp(position.Y, type.Radius, Map.Width - type.Radius));
        if (!type.IsStructure)
            position = Map.ResolveWalls(position, type.Radius);

        u.Id[slot] = id;
        u.Type[slot] = (ushort)typeId;
        u.Team[slot] = team;
        u.Owner[slot] = owner;
        u.IsStructure[slot] = type.IsStructure;
        u.Pos[slot] = position;
        u.PrevPos[slot] = position;
        u.Vel[slot] = FixVec2.Zero;
        u.Radius[slot] = type.Radius;
        u.Hp[slot] = type.MaxHp;
        u.Target[slot] = -1;
        u.TargetGeneration[slot] = 0;
        u.AttackCooldown[slot] = 0;
        u.RetargetTimer[slot] = 1 + id % RetargetIntervalTicks; // stagger target searches across ticks
        u.SpawnTimer[slot] = type.SpawnIntervalTicks;
        u.SpreadFraction[slot] = type.Behavior == UnitBehavior.Spreader ? _rng.NextFix(Fix.Zero, Fix.One) : Fix.Half;

        if (type.IsStructure)
        {
            MarkFootprint(slot, slot + 1);
            _structuresChanged = true;
        }
        return slot;
    }

    void MarkFootprint(int slot, int value)
    {
        _obstacleVersion++;
        int size = Catalog[Units.Type[slot]].FootprintTiles;
        var (originX, originY) = FootprintOrigin(Units.Pos[slot], size);
        for (int y = Math.Max(originY, 0); y < Math.Min(originY + size, Map.WidthTiles); y++)
        {
            for (int x = Math.Max(originX, 0); x < Math.Min(originX + size, Map.LengthTiles); x++)
                _tileOccupant[y * Map.LengthTiles + x] = value;
        }
    }

    void RebuildStructureData()
    {
        _structureGrid.Rebuild(Units, Catalog.MaxMobileRadius);
        foreach (var field in _laneFields)
            field.Rebuild(_tileOccupant);
        foreach (var field in _castleFields)
            field.Rebuild(_tileOccupant);
        _holeField?.Rebuild(_tileOccupant);
        _structuresChanged = false;
    }

    void RunSpawners()
    {
        var u = Units;
        int highWater = u.HighWater;
        for (int i = 0; i < highWater; i++)
        {
            // Spawning may grow the pool, so read arrays through the properties here.
            if (!u.Alive[i] || !u.IsStructure[i])
                continue;
            var type = Catalog[u.Type[i]];
            if (type.SpawnTypeId < 0 || --u.SpawnTimer[i] > 0)
                continue;

            u.SpawnTimer[i] = type.SpawnIntervalTicks;
            byte team = u.Team[i];
            SpawnUnit(type.SpawnTypeId, team, u.Owner[i], FindSpawnPoint(i, type, Catalog[type.SpawnTypeId].Radius));
        }
    }

    /// <summary>
    /// A free tile just outside the structure's footprint, preferring the side facing the enemy. Buildings
    /// packed together therefore spawn into the nearest gap instead of inside a neighbour.
    /// </summary>
    FixVec2 FindSpawnPoint(int structure, UnitType structureType, Fix spawnRadius)
    {
        var u = Units;
        byte team = u.Team[structure];
        int size = structureType.FootprintTiles;
        var (originX, originY) = FootprintOrigin(u.Pos[structure], size);
        var candidates = _spawnCandidates;

        for (int ring = 1; ring <= MaxSpawnRing; ring++)
        {
            int minX = originX - ring, maxX = originX + size - 1 + ring;
            int minY = originY - ring, maxY = originY + size - 1 + ring;
            int frontX = team == 0 ? maxX : minX;

            // Front column first; only if it is full, any other tile of the ring.
            candidates.Clear();
            for (int y = minY; y <= maxY; y++)
                AddIfFree(frontX, y);
            if (candidates.Count == 0)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (x == frontX)
                        continue;
                    AddIfFree(x, minY);
                    AddIfFree(x, maxY);
                    if (x == minX || x == maxX)
                    {
                        for (int y = minY + 1; y < maxY; y++)
                            AddIfFree(x, y);
                    }
                }
            }
            if (candidates.Count == 0)
                continue;

            int tile = candidates[_rng.NextInt(candidates.Count)];
            int tileX = tile % Map.LengthTiles, tileY = tile / Map.LengthTiles;
            return new FixVec2(Fix.FromInt(tileX) + Fix.Half, Fix.FromInt(tileY) + Fix.Half + _rng.NextFix(-SpawnJitter, SpawnJitter));
        }

        // Completely walled in: spawn in front anyway and let separation sort it out.
        Fix distance = _halfFootprintOfType[u.Type[structure]] + spawnRadius + SpawnGap;
        return u.Pos[structure] + Map.Forward(team) * distance;

        void AddIfFree(int x, int y)
        {
            if (Map.InBounds(x, y) && _tileOccupant[y * Map.LengthTiles + x] == 0 && !Map.IsWallTile(x, y))
                candidates.Add(y * Map.LengthTiles + x);
        }
    }

    void AcquireTargets()
    {
        var u = Units;
        var alive = u.Alive;
        var type = u.Type;
        var target = u.Target;
        var targetGeneration = u.TargetGeneration;
        var generation = u.Generation;
        var retargetTimer = u.RetargetTimer;

        for (int i = 0; i < u.HighWater; i++)
        {
            if (!alive[i])
                continue;
            var unitType = Catalog[type[i]];
            if (unitType.Damage == 0)
                continue;

            bool hasTarget = u.HasValidTarget(i);
            if (hasTarget && InAttackRange(i, target[i], unitType))
                continue; // engaged: keep hitting the same target

            if (target[i] >= 0 && !hasTarget)
                retargetTimer[i] = 0; // target just died: look for a new one right away
            if (--retargetTimer[i] > 0)
                continue;

            retargetTimer[i] = RetargetIntervalTicks;
            int found = FindTarget(i, unitType);
            target[i] = found;
            if (found >= 0)
                targetGeneration[i] = generation[found];
        }
    }

    /// <summary>
    /// Nearest enemy within aggro range, searching the enemy grid in rings of cells outwards and
    /// stopping as soon as no unvisited ring can hold anything closer. Hunters keep searching the
    /// full range for their preferred type. Nothing is seen through walls.
    /// </summary>
    int FindTarget(int self, UnitType selfType)
    {
        var u = Units;
        var map = Map;
        var grid = _teamGrids[1 - u.Team[self]];
        var positions = u.Pos;
        var radii = u.Radius;
        var types = u.Type;
        FixVec2 origin = positions[self];
        Fix aggroReach = selfType.AggroRange + radii[self];
        int preferredType = selfType.Behavior == UnitBehavior.Hunter ? selfType.PreferredTargetTypeId : -1;
        bool checkWalls = map.Walls.Count > 0;

        int centerX = grid.CellX(origin.X);
        int centerY = grid.CellY(origin.Y);
        int maxRing = ((aggroReach + Catalog.MaxRadius) / grid.CellSize).FloorToInt() + 1;

        int best = -1, bestPreferred = -1;
        Fix bestDistanceSq = Fix.MaxValue, bestPreferredDistanceSq = Fix.MaxValue;

        for (int ring = 0; ring <= maxRing; ring++)
        {
            if (ring > 0 && best >= 0 && preferredType < 0)
            {
                Fix ringDistance = grid.CellSize * (ring - 1);
                if (ringDistance * ringDistance >= bestDistanceSq)
                    break;
            }

            int minX = centerX - ring, maxX = centerX + ring, minY = centerY - ring, maxY = centerY + ring;
            for (int x = minX; x <= maxX; x++)
            {
                Visit(grid.Cell(x, minY));
                if (ring > 0)
                    Visit(grid.Cell(x, maxY));
            }
            for (int y = minY + 1; y < maxY; y++)
            {
                Visit(grid.Cell(minX, y));
                Visit(grid.Cell(maxX, y));
            }
        }

        return bestPreferred >= 0 ? bestPreferred : best;

        void Visit(ReadOnlySpan<int> cell)
        {
            foreach (int candidate in cell)
            {
                Fix reach = aggroReach + radii[candidate];
                Fix distanceSq = FixVec2.DistanceSquared(origin, positions[candidate]);
                if (distanceSq > reach * reach)
                    continue;
                bool closer = distanceSq < bestDistanceSq;
                bool closerPreferred = types[candidate] == preferredType && distanceSq < bestPreferredDistanceSq;
                if (!closer && !closerPreferred)
                    continue;
                if (checkWalls && map.SegmentCrossesWall(origin, positions[candidate]))
                    continue;
                if (closer)
                {
                    bestDistanceSq = distanceSq;
                    best = candidate;
                }
                if (closerPreferred)
                {
                    bestPreferredDistanceSq = distanceSq;
                    bestPreferred = candidate;
                }
            }
        }
    }

    bool InAttackRange(int attacker, int target, UnitType attackerType)
    {
        var u = Units;
        Fix reach = attackerType.AttackRange + u.Radius[attacker] + u.Radius[target];
        return FixVec2.DistanceSquared(u.Pos[attacker], u.Pos[target]) <= reach * reach;
    }

    /// <summary>
    /// Desired movement (chase target, or march towards the enemy) plus soft separation from
    /// neighbours. Units never block each other hard; they get pushed apart and slide around
    /// anything in their way. Velocities are computed for everyone before anyone moves, so the
    /// result does not depend on update order.
    /// </summary>
    void ComputeVelocities()
    {
        var u = Units;
        if (_velocity.Length < u.Capacity)
            _velocity = new FixVec2[u.Capacity];

        var alive = u.Alive;
        var isStructure = u.IsStructure;
        var positions = u.Pos;
        var radii = u.Radius;
        var types = u.Type;
        var teams = u.Team;
        var targets = u.Target;
        var spreadFraction = u.SpreadFraction;
        var ids = u.Id;
        Fix maxMobileRadius = Catalog.MaxMobileRadius;

        for (int i = 0; i < u.HighWater; i++)
        {
            if (!alive[i] || isStructure[i])
                continue;

            var type = Catalog[types[i]];
            FixVec2 position = positions[i];
            Fix radius = radii[i];
            FixVec2 heading = FixVec2.Zero;
            FixVec2 desired = FixVec2.Zero;

            if (IsGameOver)
            {
                // Waiting for the castle to be torn down, then off to the hole.
                heading = IsHoleOpen ? HoleHeading(position) : FixVec2.Zero;
                desired = heading * type.Speed;
            }
            else if (u.HasValidTarget(i))
            {
                int target = targets[i];
                FixVec2 toTarget = positions[target] - position;
                Fix reach = type.AttackRange + radius + radii[target];
                Fix distanceSq = toTarget.LengthSquared;
                if (distanceSq > reach * reach)
                {
                    Fix distance = Fix.Sqrt(distanceSq);
                    heading = toTarget / distance;
                    desired = heading * Fix.Min(type.Speed, distance - reach);
                }
            }
            else
            {
                heading = MarchHeading(teams[i], position, radius, type, spreadFraction[i], out Fix sideways);
                desired = heading * type.Speed + new FixVec2(Fix.Zero, sideways);
            }

            FixVec2 push = FixVec2.Zero;
            FixVec2 sidestep = FixVec2.Zero;
            Fix queryRadius = radius + maxMobileRadius;
            for (int team = 0; team < TeamCount; team++)
            {
                var grid = _teamGrids[team];
                grid.GetCellRange(position, queryRadius, out int minX, out int minY, out int maxX, out int maxY);
                for (int cellY = minY; cellY <= maxY; cellY++)
                {
                    for (int cellX = minX; cellX <= maxX; cellX++)
                    {
                        foreach (int other in grid.Cell(cellX, cellY))
                        {
                            if (other == i || isStructure[other])
                                continue;
                            Separate(position, radius, ids[i], positions[other], radii[other], ids[other],
                                heading, UnitSeparation, ref push, ref sidestep);
                        }
                    }
                }
            }

            // Structures are square: separate from the closest point of the footprint.
            foreach (int structure in _structureGrid.At(position))
            {
                FixVec2 center = positions[structure];
                Fix half = _halfFootprintOfType[types[structure]];
                var closest = new FixVec2(
                    Fix.Clamp(position.X, center.X - half, center.X + half),
                    Fix.Clamp(position.Y, center.Y - half, center.Y + half));
                if (closest == position) // centre inside the footprint: push out from the middle
                    Separate(position, radius, ids[i], center, half + half, ids[structure], heading, StructureSeparation, ref push, ref sidestep);
                else
                    Separate(position, radius, ids[i], closest, Fix.Zero, ids[structure], heading, StructureSeparation, ref push, ref sidestep);
            }

            if (Map.TryNearestWallPoint(position, radius, out FixVec2 wallPoint))
                Separate(position, radius, ids[i], wallPoint, Fix.Zero, int.MaxValue, heading, WallSeparation, ref push, ref sidestep);

            _velocity[i] = (desired + push + sidestep).ClampLength(type.Speed * MaxSpeedFactor);
        }
    }

    /// <summary>
    /// Where a unit with nothing to fight walks. In its own base it follows the flow field to the nearest
    /// lane entrance; inside a lane it walks straight on, staying off the walls; near the far end of the lane
    /// and in the enemy base it follows the flow field up the enemy's ramps to their castle. Lane targets are
    /// bands rather than points, so crowds never converge on one spot.
    /// </summary>
    FixVec2 MarchHeading(byte team, FixVec2 position, Fix radius, UnitType type, Fix spreadFraction, out Fix sideways)
    {
        sideways = Fix.Zero;
        FixVec2 forward = Map.Forward(team);

        if (Map.IsInOwnBase(team, position.X))
        {
            FixVec2 flow = _laneFields[team].DirectionAt(position);
            return flow.IsZero ? forward : flow;
        }

        FixVec2 attack = _castleFields[team].DirectionAt(position);
        if (!attack.IsZero)
            return attack;

        if (!Map.IsPastLanes(team, position.X))
        {
            var lane = Map.NearestLane(position.Y);
            Fix margin = radius + LaneEdgeMargin;
            Fix low = Fix.FromInt(lane.MinY) + margin, high = Fix.FromInt(lane.MaxY) - margin;
            if (low > high)
                low = high = Fix.FromInt(lane.MinY + lane.MaxY) / 2;

            if (type.Behavior == UnitBehavior.Spreader)
            {
                Fix preferredY = low + (high - low) * spreadFraction;
                Fix maxSideways = type.Speed * type.SpreadFactor;
                sideways = Fix.Clamp(preferredY - position.Y, -maxSideways, maxSideways);
            }

            Fix exitX = Fix.FromInt(team == 0 ? Map.LaneMaxX : Map.LaneMinX);
            return TowardBand(position, exitX, low, high, forward);
        }

        var castle = Map.CastleFootprint(1 - team);
        Fix castleX = castle.Center.X;
        Fix castleLow = Fix.FromInt(castle.MinY), castleHigh = Fix.FromInt(castle.MaxY);
        var towardsCastle = new FixVec2(castleX >= position.X ? Fix.One : -Fix.One, Fix.Zero);
        return TowardBand(position, castleX, castleLow, castleHigh, towardsCastle);
    }

    /// <summary>Straight along <paramref name="straight"/> when already inside [low, high], else towards the nearest point of the band at <paramref name="gateX"/>.</summary>
    static FixVec2 TowardBand(FixVec2 position, Fix gateX, Fix low, Fix high, FixVec2 straight)
    {
        Fix targetY = Fix.Clamp(position.Y, low, high);
        if (targetY == position.Y)
            return straight;
        var toGate = new FixVec2(gateX - position.X, targetY - position.Y);
        return toGate.Normalized();
    }

    static void Separate(FixVec2 position, Fix radius, int id, FixVec2 otherPosition, Fix otherRadius, int otherId,
        FixVec2 heading, Fix strength, ref FixVec2 push, ref FixVec2 sidestep)
    {
        Fix minDistance = radius + otherRadius;
        FixVec2 away = position - otherPosition;
        Fix distanceSq = away.LengthSquared;
        if (distanceSq >= minDistance * minDistance)
            return;

        FixVec2 direction;
        Fix distance;
        if (distanceSq.Raw == 0)
        {
            // Exactly stacked: split them apart along the lane width, decided by spawn order.
            direction = new FixVec2(Fix.Zero, id < otherId ? Fix.One : -Fix.One);
            distance = Fix.Zero;
        }
        else
        {
            distance = Fix.Sqrt(distanceSq);
            direction = away / distance;
        }

        Fix overlap = minDistance - distance;
        push += direction * (overlap * strength);

        // Something is in our way: slide sideways around it instead of pushing head-on into it.
        // Head-on pushing against a crowd is what made Warcraft 3 units stand still.
        if (!heading.IsZero && FixVec2.Dot(direction, heading) < Fix.Zero)
        {
            Fix side = FixVec2.Cross(heading, -away); // > 0: the obstacle is on our left
            int sign = side.Raw != 0 ? (side.Raw > 0 ? -1 : 1) : (id < otherId ? 1 : -1);
            sidestep += heading.PerpendicularLeft * (overlap * SidestepStrength * sign);
        }
    }

    void Integrate()
    {
        var u = Units;
        var alive = u.Alive;
        var isStructure = u.IsStructure;
        var positions = u.Pos;
        var velocities = u.Vel;
        var radii = u.Radius;
        var map = Map;

        for (int i = 0; i < u.HighWater; i++)
        {
            if (!alive[i] || isStructure[i])
                continue;

            FixVec2 velocity = _velocity[i];
            FixVec2 next = positions[i] + velocity;
            Fix radius = radii[i];
            velocities[i] = velocity;
            next = new FixVec2(
                Fix.Clamp(next.X, radius, map.Length - radius),
                Fix.Clamp(next.Y, radius, map.Width - radius));
            positions[i] = map.ResolveWalls(next, radius);
        }
    }

    void ResolveAttacks()
    {
        var u = Units;
        var alive = u.Alive;
        var hp = u.Hp;
        var types = u.Type;
        var targets = u.Target;
        var cooldowns = u.AttackCooldown;

        for (int i = 0; i < u.HighWater; i++)
        {
            if (!alive[i] || hp[i] <= 0)
                continue;
            if (cooldowns[i] > 0)
                cooldowns[i]--;
            if (cooldowns[i] > 0)
                continue;

            var type = Catalog[types[i]];
            if (type.Damage == 0 || !u.HasValidTarget(i))
                continue;
            int target = targets[i];
            if (hp[target] <= 0 || !InAttackRange(i, target, type))
                continue;

            hp[target] -= type.Damage;
            cooldowns[i] = type.AttackCooldownTicks;
        }
    }

    void RemoveDead()
    {
        var u = Units;
        for (int i = 0; i < u.HighWater; i++)
        {
            if (!u.Alive[i] || u.Hp[i] > 0)
                continue;

            if (u.IsStructure[i])
            {
                MarkFootprint(i, 0);
                _structuresChanged = true;
                if (Catalog[u.Type[i]].IsCastle && !IsGameOver)
                    EndGame(i);
            }
            u.Free(i);
        }
    }

    /// <summary>Rebuilds tile occupancy from the live structures and compares (tests: it is derived state, never hashed).</summary>
    internal bool ValidateOccupancy()
    {
        var expected = new int[_tileOccupant.Length];
        var u = Units;
        for (int i = 0; i < u.HighWater; i++)
        {
            if (!u.Alive[i] || !u.IsStructure[i])
                continue;
            int size = Catalog[u.Type[i]].FootprintTiles;
            var (originX, originY) = FootprintOrigin(u.Pos[i], size);
            for (int y = originY; y < originY + size; y++)
            {
                for (int x = originX; x < originX + size; x++)
                {
                    if (expected[y * Map.LengthTiles + x] != 0)
                        return false; // two structures on one tile
                    expected[y * Map.LengthTiles + x] = i + 1;
                }
            }
        }
        return expected.AsSpan().SequenceEqual(_tileOccupant);
    }

    internal BaseFlowField FlowField(int team) => _laneFields[team];

    internal BaseFlowField CastleField(int team) => _castleFields[team];
}
