using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>
/// For every tile of one team's base: the step towards the nearest lane entrance, measured as walking
/// distance around buildings. Units in their own base just follow it, so a unit always heads for the lane
/// that is closest from wherever it currently stands; if buildings or a crowd push it somewhere else, it
/// re-routes from there. Rebuilt only when a structure is placed or destroyed.
/// </summary>
sealed class BaseFlowField
{
    const int StraightCost = 5;
    const int DiagonalCost = 7;
    const byte None = 0;

    // Index 0 means "no direction". Orthogonal steps first so they win ties.
    static readonly int[] StepX = [0, 1, -1, 0, 0, 1, 1, -1, -1];
    static readonly int[] StepY = [0, 0, 0, 1, -1, 1, -1, 1, -1];
    static readonly FixVec2[] Directions = CreateDirections();

    readonly MapDefinition _map;
    readonly TileRect _area;
    readonly int _goalX;
    readonly byte _forwardStep;
    readonly byte[] _direction;
    readonly int[] _distance;
    readonly byte[] _searchOrder;
    readonly PriorityQueue<int, int> _open = new();

    public BaseFlowField(MapDefinition map, int team)
    {
        _map = map;
        _area = map.BaseArea(team);
        _goalX = team == 0 ? _area.MaxX - 1 : _area.MinX;
        _forwardStep = team == 0 ? (byte)1 : (byte)2;
        _direction = new byte[_area.Width * _area.Height];
        _distance = new int[_direction.Length];

        // Forward first, then sideways, then back: decides ties so units prefer to keep marching.
        byte back = team == 0 ? (byte)2 : (byte)1;
        _searchOrder = team == 0 ? [_forwardStep, 3, 4, back, 5, 6, 7, 8] : [_forwardStep, 3, 4, back, 7, 8, 5, 6];
    }

    /// <summary>Unit step direction at the position, or zero outside the base or where no path exists.</summary>
    public FixVec2 DirectionAt(FixVec2 position)
    {
        int x = position.X.FloorToInt(), y = position.Y.FloorToInt();
        if (!_area.Contains(x, y))
            return FixVec2.Zero;
        return Directions[_direction[Index(x, y)]];
    }

    /// <summary>Walking cost to the nearest lane entrance (5 per straight tile), or -1 when unreachable/blocked.</summary>
    internal int DistanceAt(int x, int y) => _area.Contains(x, y) ? _distance[Index(x, y)] : -1;

    /// <param name="tileOccupant">Map-sized occupancy (row-major, non-zero = structure).</param>
    public void Rebuild(int[] tileOccupant)
    {
        Array.Fill(_distance, -1);
        Array.Clear(_direction);
        _open.Clear();

        foreach (var lane in _map.Lanes)
        {
            for (int y = lane.MinY; y < lane.MaxY; y++)
            {
                if (IsBlocked(tileOccupant, _goalX, y))
                    continue;
                int index = Index(_goalX, y);
                _distance[index] = 0;
                _direction[index] = _forwardStep;
                _open.Enqueue(index, 0);
            }
        }

        // Dijkstra. Pop order among equal priorities does not matter: the final distances are unique,
        // and directions are derived from them afterwards in a fixed order.
        while (_open.TryDequeue(out int index, out int distance))
        {
            if (distance != _distance[index])
                continue;
            int x = _area.MinX + index % _area.Width, y = _area.MinY + index / _area.Width;
            for (int step = 1; step < StepX.Length; step++)
            {
                int nx = x + StepX[step], ny = y + StepY[step];
                if (!CanStep(tileOccupant, x, y, step))
                    continue;
                int next = Index(nx, ny);
                int cost = distance + (step >= 5 ? DiagonalCost : StraightCost);
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
                    int neighbour = _distance[Index(x + StepX[step], y + StepY[step])];
                    if (neighbour < 0)
                        continue;
                    int total = neighbour + (step >= 5 ? DiagonalCost : StraightCost);
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
        if (!_area.Contains(nx, ny) || IsBlocked(tileOccupant, nx, ny))
            return false;
        // No cutting corners past a building: both orthogonal neighbours of a diagonal step must be free.
        return step < 5 || (!IsBlocked(tileOccupant, nx, y) && !IsBlocked(tileOccupant, x, ny));
    }

    bool IsBlocked(int[] tileOccupant, int x, int y) =>
        !_area.Contains(x, y) || tileOccupant[y * _map.LengthTiles + x] != 0;

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
