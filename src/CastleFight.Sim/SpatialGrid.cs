using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>
/// Uniform grid of one team's units, rebuilt every tick with a counting sort. Never allocates
/// after warm-up, and units inside a cell stay in slot order so iteration is deterministic.
/// </summary>
public sealed class SpatialGrid
{
    readonly int[] _cellStart;
    readonly int[] _cursor;
    int[] _cellOfSlot = [];
    int[] _items = [];

    public SpatialGrid(Fix length, Fix width, Fix cellSize)
    {
        CellSize = cellSize;
        Columns = (length / cellSize).FloorToInt() + 1;
        Rows = (width / cellSize).FloorToInt() + 1;
        _cellStart = new int[Columns * Rows + 1];
        _cursor = new int[Columns * Rows];
    }

    public Fix CellSize { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int Count { get; private set; }

    public int CellX(Fix x) => Math.Clamp((int)(x.Raw / CellSize.Raw), 0, Columns - 1);
    public int CellY(Fix y) => Math.Clamp((int)(y.Raw / CellSize.Raw), 0, Rows - 1);

    /// <summary>Slots in the cell, or empty when the cell is outside the grid.</summary>
    public ReadOnlySpan<int> Cell(int cellX, int cellY)
    {
        if ((uint)cellX >= (uint)Columns || (uint)cellY >= (uint)Rows)
            return ReadOnlySpan<int>.Empty;
        int cell = cellY * Columns + cellX;
        int start = _cellStart[cell];
        return _items.AsSpan(start, _cellStart[cell + 1] - start);
    }

    /// <summary>Inclusive range of cells overlapping the square around <paramref name="center"/>.</summary>
    public void GetCellRange(FixVec2 center, Fix radius, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = CellX(center.X - radius);
        minY = CellY(center.Y - radius);
        maxX = CellX(center.X + radius);
        maxY = CellY(center.Y + radius);
    }

    public void Rebuild(UnitPool units, byte team)
    {
        int highWater = units.HighWater;
        if (_cellOfSlot.Length < highWater)
            _cellOfSlot = new int[units.Capacity];

        var alive = units.Alive;
        var teams = units.Team;
        var positions = units.Pos;
        Array.Clear(_cellStart);

        int count = 0;
        for (int i = 0; i < highWater; i++)
        {
            if (!alive[i] || teams[i] != team)
            {
                _cellOfSlot[i] = -1;
                continue;
            }

            int cell = CellY(positions[i].Y) * Columns + CellX(positions[i].X);
            _cellOfSlot[i] = cell;
            _cellStart[cell + 1]++;
            count++;
        }

        for (int cell = 0; cell < _cursor.Length; cell++)
        {
            _cellStart[cell + 1] += _cellStart[cell];
            _cursor[cell] = _cellStart[cell];
        }

        if (_items.Length < count)
            _items = new int[Math.Max(count, _items.Length * 2)];

        for (int i = 0; i < highWater; i++)
        {
            int cell = _cellOfSlot[i];
            if (cell >= 0)
                _items[_cursor[cell]++] = i;
        }

        Count = count;
    }
}

/// <summary>
/// Maps every cell to the structures whose footprint (plus the largest mobile unit radius)
/// touches it, so a moving unit only checks the few buildings registered in its own cell.
/// Rebuilt only when a structure is added or removed.
/// </summary>
sealed class StructureGrid
{
    readonly SpatialGrid _layout;
    readonly int[] _cellStart;
    readonly int[] _cursor;
    int[] _items = [];

    public StructureGrid(Fix length, Fix width, Fix cellSize)
    {
        _layout = new SpatialGrid(length, width, cellSize);
        _cellStart = new int[_layout.Columns * _layout.Rows + 1];
        _cursor = new int[_layout.Columns * _layout.Rows];
    }

    public ReadOnlySpan<int> At(FixVec2 position)
    {
        int cell = _layout.CellY(position.Y) * _layout.Columns + _layout.CellX(position.X);
        int start = _cellStart[cell];
        return _items.AsSpan(start, _cellStart[cell + 1] - start);
    }

    public void Rebuild(UnitPool units, Fix margin)
    {
        Array.Clear(_cellStart);
        int total = 0;
        ForEachCoveredCell(units, margin, (cell, _) => { _cellStart[cell + 1]++; total++; });

        for (int cell = 0; cell < _cursor.Length; cell++)
        {
            _cellStart[cell + 1] += _cellStart[cell];
            _cursor[cell] = _cellStart[cell];
        }

        if (_items.Length < total)
            _items = new int[Math.Max(total, _items.Length * 2)];
        ForEachCoveredCell(units, margin, (cell, slot) => _items[_cursor[cell]++] = slot);
    }

    void ForEachCoveredCell(UnitPool units, Fix margin, Action<int, int> visit)
    {
        for (int i = 0; i < units.HighWater; i++)
        {
            if (!units.Alive[i] || !units.IsStructure[i])
                continue;

            _layout.GetCellRange(units.Pos[i], units.Radius[i] + margin, out int minX, out int minY, out int maxX, out int maxY);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    visit(y * _layout.Columns + x, i);
        }
    }
}
