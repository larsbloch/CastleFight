using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>
/// The deterministic game state. Same <see cref="GameSetup"/> + same commands per tick gives a
/// bit-identical state on every machine, which is what lockstep multiplayer relies on.
/// Rules: no float/double, no System.Random, no wall-clock time, no engine calls in here.
/// </summary>
public sealed class Simulation
{
    public const int TicksPerSecond = 20;
    public const int TeamCount = 2;

    const int RetargetIntervalTicks = 5;
    const int MaxDebugSpawnPerCommand = 10_000;
    const byte NeutralOwner = byte.MaxValue;

    static readonly Fix GridCellSize = Fix.FromInt(2);
    static readonly Fix UnitSeparation = Fix.Half;
    static readonly Fix StructureSeparation = Fix.One;
    static readonly Fix SidestepStrength = Fix.FromRatio(3, 4);
    static readonly Fix MaxSpeedFactor = Fix.FromRatio(3, 2);
    static readonly Fix SpawnGap = Fix.FromRatio(1, 4);
    static readonly Fix BuildingGap = Fix.FromRatio(1, 2);

    readonly Player[] _players;
    readonly SpatialGrid[] _teamGrids;
    readonly StructureGrid _structureGrid;
    readonly int _incomePerSecond;
    DeterministicRandom _rng;
    FixVec2[] _velocity = [];
    bool _structuresChanged = true;
    int _nextUnitId = 1;

    public Simulation(GameSetup setup, MapLayout? map = null, UnitCatalog? catalog = null)
    {
        if (setup.PlayerTeams.Count is 0 or >= NeutralOwner)
            throw new ArgumentException("Invalid player count.", nameof(setup));

        Map = map ?? MapLayout.CreateDefault();
        Catalog = catalog ?? UnitCatalog.CreateDefault();
        Units = new UnitPool();
        _rng = new DeterministicRandom(setup.Seed);
        _incomePerSecond = setup.IncomePerSecond;

        _players = new Player[setup.PlayerTeams.Count];
        for (int i = 0; i < _players.Length; i++)
        {
            byte team = setup.PlayerTeams[i];
            if (team >= TeamCount)
                throw new ArgumentException($"Player {i} has invalid team {team}.", nameof(setup));
            _players[i] = new Player(i, team, setup.StartingGold);
        }

        _teamGrids = new SpatialGrid[TeamCount];
        for (int team = 0; team < TeamCount; team++)
            _teamGrids[team] = new SpatialGrid(Map.Length, Map.Width, GridCellSize);
        _structureGrid = new StructureGrid(Map.Length, Map.Width, GridCellSize);

        for (byte team = 0; team < TeamCount; team++)
            SpawnUnit(Catalog.CastleTypeId, team, NeutralOwner, Map.CastlePosition(team));
    }

    public MapLayout Map { get; }
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

        if (Tick % TicksPerSecond == 0)
        {
            foreach (var player in _players)
                player.Gold += _incomePerSecond;
        }

        RunSpawners();

        for (byte team = 0; team < TeamCount; team++)
            _teamGrids[team].Rebuild(Units, team);
        if (_structuresChanged)
        {
            _structureGrid.Rebuild(Units, Catalog.MaxMobileRadius);
            _structuresChanged = false;
        }

        AcquireTargets();
        ComputeVelocities();
        Integrate();
        ResolveAttacks();
        RemoveDead();

        Tick++;
    }

    public bool CanPlaceBuilding(int playerId, int typeId, FixVec2 position)
    {
        if (IsGameOver || (uint)playerId >= (uint)_players.Length || (uint)typeId >= (uint)Catalog.Count)
            return false;

        var type = Catalog[typeId];
        var player = _players[playerId];
        if (!type.IsStructure || type.IsCastle || type.Cost <= 0 || player.Gold < type.Cost)
            return false;
        if (!Map.BuildZone(player.Team).Inset(type.Radius).Contains(position))
            return false;

        var units = Units;
        for (int i = 0; i < units.HighWater; i++)
        {
            if (!units.Alive[i] || !units.IsStructure[i])
                continue;
            Fix minDistance = units.Radius[i] + type.Radius + BuildingGap;
            if (FixVec2.DistanceSquared(units.Pos[i], position) < minDistance * minDistance)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Hash of the full simulation state. Clients exchange this every few ticks; a mismatch
    /// means a desync (some code path was not deterministic).
    /// </summary>
    public ulong ComputeStateHash()
    {
        var hash = new StateHash();
        hash.Add(Tick);
        hash.Add(WinningTeam);
        hash.Add((long)_rng.State);
        hash.Add(_nextUnitId);
        foreach (var player in _players)
            hash.Add(player.Gold);

        var u = Units;
        hash.Add(u.HighWater);
        for (int i = 0; i < u.HighWater; i++)
        {
            hash.Add(u.Alive[i] ? 1 : 0);
            if (!u.Alive[i])
                continue;
            hash.Add(u.Id[i]);
            hash.Add(u.Generation[i]);
            hash.Add(u.Type[i]);
            hash.Add(u.Team[i]);
            hash.Add(u.Owner[i]);
            hash.Add(u.Pos[i].X.Raw);
            hash.Add(u.Pos[i].Y.Raw);
            hash.Add(u.Hp[i]);
            hash.Add(u.Target[i]);
            hash.Add(u.TargetGeneration[i]);
            hash.Add(u.AttackCooldown[i]);
            hash.Add(u.RetargetTimer[i]);
            hash.Add(u.SpawnTimer[i]);
            hash.Add(u.PreferredY[i].Raw);
        }
        foreach (int slot in u.FreeSlots)
            hash.Add(slot);

        return hash.Value;
    }

    void ApplyCommand(in Command command)
    {
        if ((uint)command.PlayerId >= (uint)_players.Length)
            return;
        var player = _players[command.PlayerId];

        switch (command.Kind)
        {
            case CommandKind.PlaceBuilding:
                if (!CanPlaceBuilding(command.PlayerId, command.TypeId, command.Position))
                    return;
                player.Gold -= Catalog[command.TypeId].Cost;
                SpawnUnit(command.TypeId, player.Team, (byte)player.Id, command.Position);
                break;

            case CommandKind.DebugSpawn:
                if (command.TypeId >= Catalog.Count || Catalog[command.TypeId].IsStructure)
                    return;
                FixRect area = Map.StagingArea(player.Team);
                int count = Math.Clamp(command.Count, 0, MaxDebugSpawnPerCommand);
                for (int k = 0; k < count; k++)
                {
                    var position = new FixVec2(_rng.NextFix(area.Min.X, area.Max.X), _rng.NextFix(area.Min.Y, area.Max.Y));
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
        u.PreferredY[slot] = type.Behavior == UnitBehavior.Spreader
            ? _rng.NextFix(type.Radius, Map.Width - type.Radius)
            : position.Y;

        if (type.IsStructure)
            _structuresChanged = true;
        return slot;
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
            var spawnType = Catalog[type.SpawnTypeId];
            byte team = u.Team[i];
            Fix structureRadius = u.Radius[i];
            FixVec2 ahead = Map.LaneDirection(team) * (structureRadius + spawnType.Radius + SpawnGap);
            var sideways = new FixVec2(Fix.Zero, _rng.NextFix(-structureRadius, structureRadius));
            SpawnUnit(type.SpawnTypeId, team, u.Owner[i], u.Pos[i] + ahead + sideways);
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
    /// full range for their preferred type.
    /// </summary>
    int FindTarget(int self, UnitType selfType)
    {
        var u = Units;
        var grid = _teamGrids[1 - u.Team[self]];
        var positions = u.Pos;
        var radii = u.Radius;
        var types = u.Type;
        FixVec2 origin = positions[self];
        Fix aggroReach = selfType.AggroRange + radii[self];
        int preferredType = selfType.Behavior == UnitBehavior.Hunter ? selfType.PreferredTargetTypeId : -1;

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
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    best = candidate;
                }
                if (types[candidate] == preferredType && distanceSq < bestPreferredDistanceSq)
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
    /// Desired movement (chase target, or march down the lane) plus soft separation from
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
        var preferredY = u.PreferredY;
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

            if (u.HasValidTarget(i))
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
                heading = Map.LaneDirection(teams[i]);
                desired = heading * type.Speed;
                if (type.Behavior == UnitBehavior.Spreader)
                {
                    Fix maxSideways = type.Speed * type.SpreadFactor;
                    desired += new FixVec2(Fix.Zero, Fix.Clamp(preferredY[i] - position.Y, -maxSideways, maxSideways));
                }
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

            foreach (int structure in _structureGrid.At(position))
            {
                Separate(position, radius, ids[i], positions[structure], radii[structure], ids[structure],
                    heading, StructureSeparation, ref push, ref sidestep);
            }

            _velocity[i] = (desired + push + sidestep).ClampLength(type.Speed * MaxSpeedFactor);
        }
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

        for (int i = 0; i < u.HighWater; i++)
        {
            if (!alive[i] || isStructure[i])
                continue;

            FixVec2 velocity = _velocity[i];
            FixVec2 next = positions[i] + velocity;
            Fix radius = radii[i];
            velocities[i] = velocity;
            positions[i] = new FixVec2(
                Fix.Clamp(next.X, radius, Map.Length - radius),
                Fix.Clamp(next.Y, radius, Map.Width - radius));
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
                _structuresChanged = true;
                if (Catalog[u.Type[i]].IsCastle && !IsGameOver)
                    WinningTeam = 1 - u.Team[i];
            }
            u.Free(i);
        }
    }

    struct StateHash
    {
        ulong _value;

        public StateHash() => _value = 14695981039346656037UL;

        public void Add(long value)
        {
            _value ^= (ulong)value;
            _value *= 0x9E3779B97F4A7C15UL;
            _value ^= _value >> 32;
        }

        public readonly ulong Value => _value;
    }
}
