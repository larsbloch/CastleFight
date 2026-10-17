using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>
/// For every tile of one base: the step towards a goal, measured as walking distance around walls and buildings.
/// There are two kinds, both per team:
/// <list type="bullet">
/// <item><see cref="ToLanes"/>: in the team's own base, towards the nearest lane entrance. Units just follow it, so a
/// unit always heads for the lane that is closest from wherever it currently stands (through the cliff ramps); if
/// buildings or a crowd push it somewhere else, it re-routes from there. Buildings block it.</item>
/// <item><see cref="ToEnemyCastle"/>: from the last stretch of the lanes through the enemy base to its castle. Enemy
/// buildings only cost extra here, so a base walled in by buildings still leads attackers into the buildings.</item>
/// <item><see cref="ToHole"/>: once the game is over, from anywhere on the map to the hole where the fallen castle
/// stood. Buildings block it.</item>
/// </list>
/// Rebuilt only when a structure is placed or destroyed.
/// </summary>
sealed class BaseFlowField
{
    const int StraightCost = 5;
    const int DiagonalCost = 7;
    const byte None = 0;

    /// <summary>How far the enemy-castle field reaches back into the lanes, so units line up with the enemy's ramps in time.</summary>
    internal const int LaneLeadTiles = 16;

    /// <summary>Extra cost of walking through a building tile in the enemy-castle field (about a 12-tile detour).</summary>
    const int StructurePenalty = 60;

    // Index 0 means "no direction". Orthogonal steps first so they win ties.
    static readonly int[] StepX = [0, 1, -1, 0, 0, 1, 1, -1, -1];
    static readonly int[] StepY = [0, 0, 0, 1, -1, 1, -1, 1, -1];
    static readonly FixVec2[] Directions = CreateDirections();

    readonly MapDefinition _map;
    readonly TileRect _area;
    readonly TileRect[] _goals;
    readonly bool _structuresBlock;
    readonly byte _forwardStep;
    readonly byte[] _direction;
    readonly int[] _distance;
    readonly byte[] _searchOrder;
    readonly PriorityQueue<int, int> _open = new();

    BaseFlowField(MapDefinition map, int team, TileRect area, TileRect[] goals, bool structuresBlock)
    {
        _map = map;
        _area = area;
        _goals = goals;
        _structuresBlock = structuresBlock;
        _forwardStep = team == 0 ? (byte)1 : (byte)2;
        _direction = new byte[_area.Width * _area.Height];
        _distance = new int[_direction.Length];

        // Forward first, then sideways, then back: decides ties so units prefer to keep marching.
        byte back = team == 0 ? (byte)2 : (byte)1;
        _searchOrder = team == 0 ? [_forwardStep, 3, 4, back, 5, 6, 7, 8] : [_forwardStep, 3, 4, back, 7, 8, 5, 6];
    }

    /// <summary>Own base towards the lane entrances: the last column of the base, in every lane's band.</summary>
    public static BaseFlowField ToLanes(MapDefinition map, int team)
    {
        var area = map.BaseArea(team);
        int goalX = team == 0 ? area.MaxX - 1 : area.MinX;
        var goals = map.Lanes.Select(l => new TileRect(goalX, l.MinY, goalX + 1, l.MaxY)).ToArray();
        return new BaseFlowField(map, team, area, goals, structuresBlock: true);
    }

    /// <summary>The far end of the lanes and the enemy base, towards the enemy castle.</summary>
    public static BaseFlowField ToEnemyCastle(MapDefinition map, int team)
    {
        var enemyBase = map.BaseArea(1 - team);
        var area = team == 0
            ? enemyBase with { MinX = Math.Max(enemyBase.MinX - LaneLeadTiles, map.LaneMinX) }
            : enemyBase with { MaxX = Math.Min(enemyBase.MaxX + LaneLeadTiles, map.LaneMaxX) };
        return new BaseFlowField(map, team, area, [map.CastleFootprint(1 - team)], structuresBlock: false);
    }

    /// <summary>The whole map towards the hole left by the fallen castle (<paramref name="team"/> only breaks ties).</summary>
    public static BaseFlowField ToHole(MapDefinition map, int team, TileRect hole) =>
        new(map, team, new TileRect(0, 0, map.LengthTiles, map.WidthTiles), [hole], structuresBlock: true);

    /// <summary>Unit step direction at the position, or zero outside the area or where no path exists.</summary>
    public FixVec2 DirectionAt(FixVec2 position)
    {
        int x = position.X.FloorToInt(), y = position.Y.FloorToInt();
        if (!_area.Contains(x, y))
            return FixVec2.Zero;
        return Directions[_direction[Index(x, y)]];
    }

    /// <summary>Walking cost to the nearest goal (5 per straight tile), or -1 when unreachable/blocked.</summary>
    internal int DistanceAt(int x, int y) => _area.Contains(x, y) ? _distance[Index(x, y)] : -1;

    /// <param name="tileOccupant">Map-sized occupancy (row-major, non-zero = structure).</param>
    public void Rebuild(int[] tileOccupant)
    {
        Array.Fill(_distance, -1);
        Array.Clear(_direction);
        _open.Clear();

        foreach (var goal in _goals)
        {
            for (int y = goal.MinY; y < goal.MaxY; y++)
            {
                for (int x = goal.MinX; x < goal.MaxX; x++)
                {
                    if (!_area.Contains(x, y) || _map.IsWallTile(x, y) || (_structuresBlock && IsStructure(tileOccupant, x, y)))
                        continue;
                    int index = Index(x, y);
                    _distance[index] = 0;
                    _direction[index] = _forwardStep;
                    _open.Enqueue(index, 0);
                }
            }
        }

        // Dijkstra. Pop order among equal priorities does not matter: the final distances are unique,
        // and directions are derived from them afterwards in a fixed order.
        while (_open.TryDequeue(out int index, out int distance))
        {
            if (distance != _distance[index])
                continue;
            int x = _area.MinX + index % _area.Width, y = _area.MinY + index / _area.Width;
            int enterCost = EnterCost(tileOccupant, x, y); // a unit at the neighbour walks into this tile
            for (int step = 1; step < StepX.Length; step++)
            {
                int nx = x + StepX[step], ny = y + StepY[step];
                if (!CanStep(tileOccupant, x, y, step))
                    continue;
                int next = Index(nx, ny);
                int cost = distance + (step >= 5 ? DiagonalCost : StraightCost) + enterCost;
                if (_distance[next] >= 0 && _distance[next] <= cost)
                    continue;
                _distance[next] = cost;
                _open.Enqueue(next, cost);
            }
        }

        for (int y = _area.MinY; y < _area.MaxY; y++)
        {
            for (int x = _area.MinX; x < _area.MaxX; x++)
            {
                int index = Index(x, y);
                if (_distance[index] <= 0)
                    continue; // unreachable, blocked, or a goal tile (already points forward)

                int best = int.MaxValue;
                byte bestStep = None;
                foreach (byte step in _searchOrder)
                {
                    if (!CanStep(tileOccupant, x, y, step))
                        continue;
                    int nx = x + StepX[step], ny = y + StepY[step];
                    int neighbour = _distance[Index(nx, ny)];
                    if (neighbour < 0)
                        continue;
                    int total = neighbour + (step >= 5 ? DiagonalCost : StraightCost) + EnterCost(tileOccupant, nx, ny);
                    if (total < best)
                    {
                        best = total;
                        bestStep = step;
                    }
                }
                _direction[index] = bestStep;
            }
        }
    }

    bool CanStep(int[] tileOccupant, int x, int y, int step)
    {
        int nx = x + StepX[step], ny = y + StepY[step];
        if (IsBlocked(tileOccupant, nx, ny))
            return false;
        // No cutting corners past a wall or building: both orthogonal neighbours of a diagonal step must be free.
        return step < 5 || (IsFree(tileOccupant, nx, y) && IsFree(tileOccupant, x, ny));
    }

    bool IsBlocked(int[] tileOccupant, int x, int y) =>
        !_area.Contains(x, y) || _map.IsWallTile(x, y) || (_structuresBlock && IsStructure(tileOccupant, x, y));

    bool IsFree(int[] tileOccupant, int x, int y) => !IsBlocked(tileOccupant, x, y) && !IsStructure(tileOccupant, x, y);

    int EnterCost(int[] tileOccupant, int x, int y) =>
        !_structuresBlock && _distance[Index(x, y)] != 0 && IsStructure(tileOccupant, x, y) ? StructurePenalty : 0;

    bool IsStructure(int[] tileOccupant, int x, int y) => tileOccupant[y * _map.LengthTiles + x] != 0;

    int Index(int x, int y) => (y - _area.MinY) * _area.Width + (x - _area.MinX);

    static FixVec2[] CreateDirections()
    {
        Fix diagonal = Fix.FromRaw(46341); // 1/sqrt(2) in 16.16
        var directions = new FixVec2[StepX.Length];
        for (int step = 1; step < StepX.Length; step++)
        {
            Fix scale = step >= 5 ? diagonal : Fix.One;
            directions[step] = new FixVec2(scale * StepX[step], scale * StepY[step]);
        }
        return directions;
    }
}
