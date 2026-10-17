using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

public sealed partial class Simulation
{
    /// <summary>How long a builder has to stand next to a site before the building is done (5 s).</summary>
    public const int BuildTicks = 5 * TicksPerSecond;

    /// <summary>Most orders that can wait in a builder's queue; further queued orders are ignored.</summary>
    public const int MaxQueuedOrders = 16;

    /// <summary>Builder speed in tiles per tick (8 tiles/s, about four times a footman).</summary>
    public static readonly Fix BuilderSpeed = UnitCatalog.TilesPerSecond(80);

    /// <summary>Builders start in front of their castle, 2 tiles apart across the map.</summary>
    const int BuilderStartGap = 2;
    const int BuilderSpacing = 2;

    // Builder paths: A* over tiles, 5 per straight step and 7 per diagonal one.
    const int PathStraightCost = 5;
    const int PathDiagonalCost = 7;

    /// <summary>Extra cost per building tile when a builder stands inside a building and has to walk out of it.</summary>
    const int PathEscapeCost = 100;

    /// <summary>Extra cost of a tile touching a wall, so builders keep off cliffs and walk through the middle of ramps.</summary>
    const int PathNearWallCost = 4;

    /// <summary>Straight shortcuts are checked this many times per tile.</summary>
    const int ShortcutSamplesPerTile = 4;

    const byte FreeTile = 0, BuildingTile = 1, WallTile = 2;

    // Orthogonal steps first, then diagonal ones.
    static readonly int[] PathStepX = [1, -1, 0, 0, 1, 1, -1, -1];
    static readonly int[] PathStepY = [0, 0, 1, -1, 1, -1, 1, -1];

    /// <summary>How far a straight shortcut keeps the builder's centre from walls and buildings.</summary>
    static readonly Fix ShortcutClearance = Fix.FromRatio(3, 8);

    /// <summary>How far a straight shortcut keeps the builder's centre from walls (the star is wide).</summary>
    static readonly Fix ShortcutWallClearance = Fix.One;

    /// <summary>Changes whenever a building or construction site appears or goes, so walking builders plan again.</summary>
    int _obstacleVersion;

    // Builder path planning scratch (derived from the state, never hashed).
    int _obstaclesBuiltFor = -1;
    byte[] _obstacles = [];
    bool[] _nearWall = [];
    int[] _pathCost = [];
    int[] _pathFrom = [];
    int[] _pathSeen = [];
    int[] _pathClosed = [];
    int _pathSearch;
    readonly PriorityQueue<int, long> _pathOpen = new();
    readonly List<FixVec2> _pathRaw = new();

    /// <summary>The tiles a builder is walking to or building on (empty when it has no building order).</summary>
    public TileRect SiteOf(Builder builder)
    {
        if (builder.BuildTypeId < 0)
            return default;
        int size = Catalog[builder.BuildTypeId].FootprintTiles;
        return new TileRect(builder.BuildTileX, builder.BuildTileY, builder.BuildTileX + size, builder.BuildTileY + size);
    }

    /// <summary>The site of a queued build order.</summary>
    public TileRect SiteOf(BuilderTask task) =>
        task.Kind == BuilderTaskKind.Build
            ? new TileRect(task.TileX, task.TileY, task.TileX + Catalog[task.TypeId].FootprintTiles, task.TileY + Catalog[task.TypeId].FootprintTiles)
            : default;

    FixVec2 BuilderStart(IReadOnlyList<byte> teams, int playerId)
    {
        byte team = teams[playerId];
        int index = 0, count = 0;
        for (int i = 0; i < teams.Count; i++)
        {
            if (teams[i] != team)
                continue;
            if (i < playerId)
                index++;
            count++;
        }

        var castle = Map.CastleFootprint(team);
        Fix x = team == 0 ? Fix.FromInt(castle.MaxX + BuilderStartGap) : Fix.FromInt(castle.MinX - BuilderStartGap);
        Fix y = castle.Center.Y + Fix.FromInt(BuilderSpacing * index * 2 - BuilderSpacing * (count - 1)) / 2;
        return new FixVec2(x, y);
    }

    void ApplyBuilderCommand(Player player, in Command command)
    {
        var builder = player.Builder;
        bool queued = command.IsQueued;
        BuilderTask task;
        if (command.Kind == CommandKind.MoveBuilder)
        {
            var to = command.Position;
            task = BuilderTask.Move(new FixVec2(Fix.Clamp(to.X, Fix.Zero, Map.Length), Fix.Clamp(to.Y, Fix.Zero, Map.Width)));
        }
        else
        {
            // The spot must be valid when ordered; it is checked again on arrival. A queued build may wait for the
            // gold, but must not overlap the builder's other planned sites.
            var result = CheckPlacement(player.Id, command.TypeId, command.TileX, command.TileY, queued);
            if (result != PlacementResult.Ok && !(queued && result == PlacementResult.NotEnoughGold))
                return;
            task = BuilderTask.Build(command.TypeId, command.TileX, command.TileY);
        }

        if (!queued)
        {
            builder.Tasks.Clear();
            StopBuilder(player);
        }
        if (builder.Order == BuilderOrder.Idle)
            StartTask(player, task);
        else if (builder.Tasks.Count < MaxQueuedOrders)
            builder.Tasks.Add(task);
    }

    /// <summary>Makes <paramref name="task"/> the current order. A site the builder can't get next to fails at once.</summary>
    void StartTask(Player player, BuilderTask task)
    {
        var builder = player.Builder;
        if (task.Kind == BuilderTaskKind.Move)
        {
            builder.Order = BuilderOrder.Move;
            builder.Destination = task.Destination;
            PlanPath(builder);
            return;
        }

        builder.Order = BuilderOrder.GoToSite;
        builder.BuildTypeId = task.TypeId;
        builder.BuildTileX = task.TileX;
        builder.BuildTileY = task.TileY;
        builder.BuildProgress = 0;
        if (!PlanPath(builder))
            FailBuild(player, PlacementResult.Unreachable);
    }

    /// <summary>Drops the current order. A building under construction is abandoned and its gold refunded.</summary>
    void StopBuilder(Player player)
    {
        if (player.Builder.Order == BuilderOrder.Construct)
            player.Gold += Catalog[player.Builder.BuildTypeId].Cost;
        ClearOrder(player.Builder);
    }

    /// <summary>Ends the current order (no refund) and starts the next queued one, if any.</summary>
    void NextTask(Player player)
    {
        var builder = player.Builder;
        ClearOrder(builder);
        if (builder.Tasks.Count == 0)
            return;
        var task = builder.Tasks[0];
        builder.Tasks.RemoveAt(0);
        StartTask(player, task);
    }

    void ClearOrder(Builder builder)
    {
        if (builder.Order == BuilderOrder.Construct)
            _obstacleVersion++; // the site is gone
        builder.Order = BuilderOrder.Idle;
        builder.BuildTypeId = -1;
        builder.BuildTileX = 0;
        builder.BuildTileY = 0;
        builder.BuildProgress = 0;
        builder.PathPoints.Clear();
    }

    /// <summary>The build could not start; the builder goes on with its queue.</summary>
    void FailBuild(Player player, PlacementResult reason)
    {
        player.Builder.BuildFailedTick = Tick;
        player.Builder.BuildFailure = reason;
        NextTask(player);
    }

    void StepBuilders()
    {
        foreach (var player in _players)
        {
            var builder = player.Builder;
            builder.PrevPos = builder.Pos;
            switch (builder.Order)
            {
                case BuilderOrder.Move:
                    if (builder.PathVersion != _obstacleVersion)
                        PlanPath(builder);
                    if (WalkBuilder(builder))
                        NextTask(player);
                    break;

                case BuilderOrder.GoToSite:
                    if (builder.PathVersion != _obstacleVersion && !PlanPath(builder))
                        FailBuild(player, PlacementResult.Unreachable);
                    else if (WalkBuilder(builder))
                        StartConstruction(player);
                    break;

                case BuilderOrder.Construct:
                    if (++builder.BuildProgress >= BuildTicks)
                        FinishConstruction(player);
                    break;
            }
        }
    }

    /// <summary>Moves the builder along its path at <see cref="BuilderSpeed"/>; true once it is at the end.</summary>
    static bool WalkBuilder(Builder builder)
    {
        var path = builder.PathPoints;
        Fix budget = BuilderSpeed;
        while (path.Count > 0)
        {
            FixVec2 toGo = path[0] - builder.Pos;
            Fix distanceSq = toGo.LengthSquared;
            if (distanceSq > budget * budget)
            {
                builder.Pos += toGo / Fix.Sqrt(distanceSq) * budget;
                return false;
            }
            builder.Pos = path[0];
            budget -= Fix.Sqrt(distanceSq);
            path.RemoveAt(0);
        }
        return true;
    }

    void StartConstruction(Player player)
    {
        var builder = player.Builder;
        var result = CheckPlacement(player.Id, builder.BuildTypeId, builder.BuildTileX, builder.BuildTileY);
        if (result != PlacementResult.Ok)
        {
            FailBuild(player, result);
            return;
        }
        player.Gold -= Catalog[builder.BuildTypeId].Cost;
        builder.Order = BuilderOrder.Construct;
        builder.BuildProgress = 0;
        _obstacleVersion++; // other builders walk around the site
    }

    void FinishConstruction(Player player)
    {
        var builder = player.Builder;
        SpawnUnit(builder.BuildTypeId, player.Team, (byte)player.Id, SiteOf(builder).Center);
        builder.Order = BuilderOrder.Idle; // already paid: not a refund
        NextTask(player);
    }

    /// <summary>
    /// Sites being built on are taken, so no one else can build there in the meantime; so is the tile the builder
    /// stands on while it builds.
    /// </summary>
    bool OverlapsConstruction(TileRect footprint)
    {
        foreach (var player in _players)
        {
            var builder = player.Builder;
            if (builder.Order != BuilderOrder.Construct)
                continue;
            if (SiteOf(builder).Overlaps(footprint) || footprint.Contains(builder.Pos.X.FloorToInt(), builder.Pos.Y.FloorToInt()))
                return true;
        }
        return false;
    }

    /// <summary>Whether the footprint overlaps a site the builder is walking to or has queued.</summary>
    bool OverlapsPlannedSite(Builder builder, TileRect footprint)
    {
        if (builder.Order == BuilderOrder.GoToSite && SiteOf(builder).Overlaps(footprint))
            return true;
        foreach (var task in builder.Tasks)
        {
            if (task.Kind == BuilderTaskKind.Build && SiteOf(task).Overlaps(footprint))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Plans the path for the current walk: A* over tiles (8 directions, no cutting corners past walls, buildings or
    /// construction sites), then straightened wherever a direct line keeps clear of them. Move: to the ordered point,
    /// or as close as the builder can get. GoToSite: to the nearest free tile next to the site, which becomes the
    /// <see cref="Builder.Destination"/>. False when the site can't be reached.
    /// </summary>
    bool PlanPath(Builder builder)
    {
        builder.PathVersion = _obstacleVersion;
        builder.PathPoints.Clear();
        PrepareObstacles();

        int length = Map.LengthTiles;
        int startX = Math.Clamp(builder.Pos.X.FloorToInt(), 0, length - 1);
        int startY = Math.Clamp(builder.Pos.Y.FloorToInt(), 0, Map.WidthTiles - 1);
        int start = startY * length + startX;
        // Standing inside a building (one was finished on top of an idle builder): it may walk out through it.
        bool escaping = _obstacles[start] == BuildingTile;

        bool toSite = builder.Order == BuilderOrder.GoToSite;
        var site = SiteOf(builder);
        TileRect goal = toSite
            ? new TileRect(site.MinX - 1, site.MinY - 1, site.MaxX + 1, site.MaxY + 1)
            : TileAt(builder.Destination);

        int search = ++_pathSearch;
        _pathOpen.Clear();
        _pathSeen[start] = search;
        _pathCost[start] = 0;
        _pathFrom[start] = -1;
        _pathOpen.Enqueue(start, Priority(0, Heuristic(goal, startX, startY)));
        int reached = -1, closest = start, closestH = int.MaxValue, closestCost = 0;

        while (_pathOpen.TryDequeue(out int index, out _))
        {
            if (_pathClosed[index] == search)
                continue;
            _pathClosed[index] = search;
            int x = index % length, y = index / length, cost = _pathCost[index];
            if (toSite ? IsStandTile(site, goal, x, y) : goal.Contains(x, y))
            {
                reached = index;
                break;
            }
            int h = Heuristic(goal, x, y);
            if (h < closestH || (h == closestH && cost < closestCost))
            {
                closest = index;
                closestH = h;
                closestCost = cost;
            }

            for (int step = 0; step < PathStepX.Length; step++)
            {
                int nx = x + PathStepX[step], ny = y + PathStepY[step];
                bool diagonal = step >= 4;
                if (!IsWalkable(nx, ny, escaping) || (diagonal && (!IsWalkable(nx, y, escaping) || !IsWalkable(x, ny, escaping))))
                    continue;
                int next = ny * length + nx;
                int nextCost = cost + (diagonal ? PathDiagonalCost : PathStraightCost)
                    + (_obstacles[next] == BuildingTile ? PathEscapeCost : 0) + (_nearWall[next] ? PathNearWallCost : 0);
                if (_pathSeen[next] == search && _pathCost[next] <= nextCost)
                    continue;
                _pathSeen[next] = search;
                _pathCost[next] = nextCost;
                _pathFrom[next] = index;
                _pathOpen.Enqueue(next, Priority(nextCost, Heuristic(goal, nx, ny)));
            }
        }

        if (reached < 0 && toSite)
            return false;
        int end = reached >= 0 ? reached : closest;

        // Tile centres from the start (exclusive) to the end; a move that got there ends on the exact point.
        var raw = _pathRaw;
        raw.Clear();
        for (int index = end; index != start; index = _pathFrom[index])
            raw.Add(TileCenter(index));
        raw.Reverse();
        if (toSite && end == start)
            raw.Add(TileCenter(start)); // already next to the site: step to the middle of the tile
        if (!toSite && reached >= 0)
        {
            if (raw.Count > 0)
                raw[^1] = builder.Destination;
            else
                raw.Add(builder.Destination);
        }
        if (toSite)
            builder.Destination = raw[^1];
        else if (reached < 0)
            builder.Destination = TileCenter(end); // so planning again later does not search the whole map

        // Out of a building corner by corner first, then straight wherever the way is clear.
        FixVec2 anchor = builder.Pos;
        int k = 0;
        while (escaping && k < raw.Count && _obstacles[TileIndex(raw[k])] != FreeTile)
        {
            builder.PathPoints.Add(raw[k]);
            anchor = raw[k++];
        }
        while (k < raw.Count)
        {
            int far = k;
            while (far + 1 < raw.Count && IsShortcutClear(anchor, raw[far + 1]))
                far++;
            builder.PathPoints.Add(raw[far]);
            anchor = raw[far];
            k = far + 1;
        }
        return true;
    }

    /// <summary>A free tile next to the site (corners count) that no other builder is building from.</summary>
    bool IsStandTile(TileRect site, TileRect ring, int x, int y)
    {
        if (!ring.Contains(x, y) || site.Contains(x, y) || _obstacles[y * Map.LengthTiles + x] != FreeTile)
            return false;
        foreach (var player in _players)
        {
            var other = player.Builder;
            if (other.Order == BuilderOrder.Construct && other.Pos.X.FloorToInt() == x && other.Pos.Y.FloorToInt() == y)
                return false;
        }
        return true;
    }

    bool IsWalkable(int x, int y, bool escaping)
    {
        if (!Map.InBounds(x, y))
            return false;
        byte tile = _obstacles[y * Map.LengthTiles + x];
        return tile == FreeTile || (escaping && tile == BuildingTile);
    }

    /// <summary>
    /// Whether the builder can walk straight from a to b keeping <see cref="ShortcutClearance"/> from anything solid
    /// and <see cref="ShortcutWallClearance"/> from walls.
    /// </summary>
    bool IsShortcutClear(FixVec2 a, FixVec2 b)
    {
        FixVec2 delta = b - a;
        int samples = (delta.Length * ShortcutSamplesPerTile).FloorToInt() + 1;
        for (int i = 0; i <= samples; i++)
        {
            var point = a + new FixVec2(delta.X * i / samples, delta.Y * i / samples);
            if (!IsClearAround(point, ShortcutClearance, FreeTile) || !IsClearAround(point, ShortcutWallClearance, WallTile))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Whether the square of half-size <paramref name="margin"/> (at most one tile) around the point stays off walls
    /// (<paramref name="avoid"/> = <see cref="WallTile"/>) or off anything solid (<see cref="FreeTile"/>). Its corners,
    /// edge middles and centre are at most a tile apart, so together they touch every tile the square covers.
    /// </summary>
    bool IsClearAround(FixVec2 point, Fix margin, byte avoid)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!IsClearAt(point.X + margin * dx, point.Y + margin * dy, avoid))
                    return false;
            }
        }
        return true;
    }

    bool IsClearAt(Fix x, Fix y, byte avoid)
    {
        int tileX = x.FloorToInt(), tileY = y.FloorToInt();
        if (!Map.InBounds(tileX, tileY))
            return avoid == WallTile; // the map edge is not a wall, but nothing may stand outside it
        byte tile = _obstacles[tileY * Map.LengthTiles + tileX];
        return avoid == WallTile ? tile != WallTile : tile == FreeTile;
    }

    /// <summary>Rebuilds the tile grid of walls, buildings and construction sites if anything changed since last time.</summary>
    void PrepareObstacles()
    {
        int count = Map.LengthTiles * Map.WidthTiles;
        if (_obstacles.Length != count)
        {
            _obstacles = new byte[count];
            _pathCost = new int[count];
            _pathFrom = new int[count];
            _pathSeen = new int[count];
            _pathClosed = new int[count];
            _nearWall = new bool[count];
            for (int y = 0; y < Map.WidthTiles; y++)
            {
                for (int x = 0; x < Map.LengthTiles; x++)
                {
                    for (int step = 0; step < PathStepX.Length; step++)
                        _nearWall[y * Map.LengthTiles + x] |= Map.IsWallTile(x + PathStepX[step], y + PathStepY[step]);
                }
            }
        }
        if (_obstaclesBuiltFor == _obstacleVersion)
            return;

        for (int y = 0; y < Map.WidthTiles; y++)
        {
            for (int x = 0; x < Map.LengthTiles; x++)
            {
                int index = y * Map.LengthTiles + x;
                _obstacles[index] = Map.IsWallTile(x, y) ? WallTile : _tileOccupant[index] != 0 ? BuildingTile : FreeTile;
            }
        }
        foreach (var player in _players)
        {
            if (player.Builder.Order != BuilderOrder.Construct)
                continue;
            var site = SiteOf(player.Builder);
            for (int y = site.MinY; y < site.MaxY; y++)
            {
                for (int x = site.MinX; x < site.MaxX; x++)
                {
                    if (Map.InBounds(x, y) && _obstacles[y * Map.LengthTiles + x] == FreeTile)
                        _obstacles[y * Map.LengthTiles + x] = BuildingTile;
                }
            }
        }
        _obstaclesBuiltFor = _obstacleVersion;
    }

    /// <summary>Octile distance to the nearest tile of <paramref name="goal"/>, in path cost units.</summary>
    static int Heuristic(TileRect goal, int x, int y)
    {
        int dx = Math.Max(Math.Max(goal.MinX - x, x - (goal.MaxX - 1)), 0);
        int dy = Math.Max(Math.Max(goal.MinY - y, y - (goal.MaxY - 1)), 0);
        int diagonal = Math.Min(dx, dy);
        return diagonal * PathDiagonalCost + (Math.Max(dx, dy) - diagonal) * PathStraightCost;
    }

    /// <summary>Lowest estimated total first; on ties the tile closer to the goal.</summary>
    static long Priority(int cost, int heuristic) => ((long)(cost + heuristic) << 32) | (uint)heuristic;

    TileRect TileAt(FixVec2 position)
    {
        int x = Math.Clamp(position.X.FloorToInt(), 0, Map.LengthTiles - 1);
        int y = Math.Clamp(position.Y.FloorToInt(), 0, Map.WidthTiles - 1);
        return new TileRect(x, y, x + 1, y + 1);
    }

    FixVec2 TileCenter(int index) =>
        new(Fix.FromInt(index % Map.LengthTiles) + Fix.Half, Fix.FromInt(index / Map.LengthTiles) + Fix.Half);

    int TileIndex(FixVec2 position) =>
        Math.Clamp(position.Y.FloorToInt(), 0, Map.WidthTiles - 1) * Map.LengthTiles + Math.Clamp(position.X.FloorToInt(), 0, Map.LengthTiles - 1);
}
