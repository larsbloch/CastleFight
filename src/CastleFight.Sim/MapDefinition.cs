using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>One corridor between the two bases. Lanes run along X; <see cref="MinY"/>..<see cref="MaxY"/> is the band they cover.</summary>
public sealed record LaneDefinition(int Index, TileRect Corridor)
{
    public int MinY => Corridor.MinY;
    public int MaxY => Corridor.MaxY;
}

/// <summary>
/// A step in the ground across the whole width of the map: a thin impassable wall in X [MinX, MaxX) with a ramp
/// through every gap. The ground is <see cref="UpperLevel"/> on the <see cref="UpperSideIsMinX"/> side and one
/// level lower on the other. Height is only for looks: to the simulation a cliff is walls plus no-build ramps.
/// </summary>
/// <param name="RampRun">How far each ramp sticks out of the cliff on the lower side.</param>
public sealed record Cliff(int MinX, int MaxX, int UpperLevel, bool UpperSideIsMinX, IReadOnlyList<(int MinY, int MaxY)> Gaps, int RampRun)
{
    public int LowerLevel => UpperLevel - 1;

    /// <summary>The same cliff seen from the other end of a map that is <paramref name="mapLength"/> tiles long.</summary>
    public Cliff MirrorX(int mapLength) => this with { MinX = mapLength - MaxX, MaxX = mapLength - MinX, UpperSideIsMinX = !UpperSideIsMinX };
}

/// <summary>
/// The way through a cliff gap. <see cref="Slope"/> is where the ground tilts from <see cref="HighLevel"/> to
/// <see cref="LowLevel"/>; nothing may be built on <see cref="KeepClear"/> (the slope plus a landing at both ends).
/// </summary>
public sealed record Ramp(TileRect Slope, TileRect KeepClear, int LowLevel, bool HighAtMinX)
{
    public int HighLevel => LowLevel + 1;
}

/// <summary>A flat stretch of ground between cliffs (or a map end and a cliff), across the whole map width.</summary>
public sealed record Terrace(TileRect Area, int Level);

/// <summary>
/// Static map geometry, all in whole tiles. Team 0's base is on the left (low X), team 1's on the right.
/// Between the bases, X in [<see cref="LaneMinX"/>, <see cref="LaneMaxX"/>) is split into parallel lanes;
/// everything in that span that is not a lane is an impassable wall. Bases hold the castle and the team's shared
/// build zones, and may be split into terraces by cliffs that units can only cross on ramps.
/// </summary>
public sealed class MapDefinition
{
    /// <summary>Walls within this many tiles of a column are listed in that column's bucket.</summary>
    const int WallBucketReach = 2;

    /// <summary>Tiles kept free of buildings at both ends of a ramp, so it can never be blocked.</summary>
    const int RampLanding = 2;

    readonly TileRect[] _castles;
    readonly TileRect[][] _buildZones;
    readonly TileRect[] _stagingAreas;
    readonly LaneDefinition[] _lanes;
    readonly Cliff[] _cliffs;
    readonly Ramp[] _ramps;
    readonly Terrace[] _terraces;
    readonly TileRect[] _laneWalls;
    readonly TileRect[] _cliffWalls;
    readonly TileRect[] _walls;
    readonly FixRect[] _wallRects;
    readonly FixRect[][] _wallsNearColumn;
    readonly bool[] _wallTile;
    readonly FixVec2[] _forward = [new(Fix.One, Fix.Zero), new(-Fix.One, Fix.Zero)];

    public MapDefinition(string name, int lengthTiles, int widthTiles, int laneMinX, int laneMaxX,
        IReadOnlyList<(int MinY, int MaxY)> lanes, TileRect[] castleFootprints, TileRect[][] buildZones, TileRect[] stagingAreas,
        IReadOnlyList<Cliff>? cliffs = null)
    {
        if (lengthTiles < 64 || widthTiles < 8 || lengthTiles > short.MaxValue || widthTiles > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(lengthTiles), "The map must be at least 64x8 tiles.");
        if (laneMinX <= 0 || laneMaxX <= laneMinX || laneMaxX >= lengthTiles)
            throw new ArgumentException("The lane span must lie strictly inside the map.", nameof(laneMinX));
        if (lanes.Count == 0)
            throw new ArgumentException("A map needs at least one lane.", nameof(lanes));
        if (castleFootprints.Length != Simulation.TeamCount || buildZones.Length != Simulation.TeamCount || stagingAreas.Length != Simulation.TeamCount)
            throw new ArgumentException("Castles, build zones and staging areas are needed for both teams.");

        Name = name;
        LengthTiles = lengthTiles;
        WidthTiles = widthTiles;
        Length = Fix.FromInt(lengthTiles);
        Width = Fix.FromInt(widthTiles);
        LaneMinX = laneMinX;
        LaneMaxX = laneMaxX;
        _castles = castleFootprints;
        _buildZones = buildZones;
        _stagingAreas = stagingAreas;

        var bounds = new TileRect(0, 0, lengthTiles, widthTiles);
        _lanes = new LaneDefinition[lanes.Count];
        var laneWalls = new List<TileRect>();
        int previousMaxY = 0;
        for (int i = 0; i < lanes.Count; i++)
        {
            var (minY, maxY) = lanes[i];
            if (minY < previousMaxY || maxY - minY < 4 || maxY > widthTiles)
                throw new ArgumentException($"Lane {i} must be at least 4 tiles wide, inside the map and below the next lane.", nameof(lanes));
            if (minY > previousMaxY)
                laneWalls.Add(new TileRect(laneMinX, previousMaxY, laneMaxX, minY));
            _lanes[i] = new LaneDefinition(i, new TileRect(laneMinX, minY, laneMaxX, maxY));
            previousMaxY = maxY;
        }
        if (previousMaxY < widthTiles)
            laneWalls.Add(new TileRect(laneMinX, previousMaxY, laneMaxX, widthTiles));
        _laneWalls = laneWalls.ToArray();

        _cliffs = (cliffs ?? []).OrderBy(c => c.MinX).ToArray();
        (_cliffWalls, _ramps) = BuildCliffs(_cliffs, bounds);
        _terraces = BuildTerraces(_cliffs, lengthTiles, widthTiles);
        foreach (var cliff in _cliffs)
        {
            if (cliff.MaxX > laneMinX && cliff.MinX < laneMaxX)
                throw new ArgumentException("Cliffs must lie in the bases, outside the lane span.", nameof(cliffs));
        }

        _walls = [.. _laneWalls, .. _cliffWalls];
        _wallRects = _walls.Select(w => w.ToFixRect()).ToArray();
        _wallTile = new bool[lengthTiles * widthTiles];
        foreach (var wall in _walls)
        {
            for (int y = wall.MinY; y < wall.MaxY; y++)
                Array.Fill(_wallTile, true, y * lengthTiles + wall.MinX, wall.Width);
        }
        _wallsNearColumn = new FixRect[lengthTiles][];
        for (int x = 0; x < lengthTiles; x++)
        {
            _wallsNearColumn[x] = Enumerable.Range(0, _walls.Length)
                .Where(i => _walls[i].MinX < x + 1 + WallBucketReach && _walls[i].MaxX > x - WallBucketReach)
                .Select(i => _wallRects[i])
                .ToArray();
        }

        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            var baseArea = BaseArea(team);
            if (!baseArea.Contains(_castles[team]) || _castles[team].IsEmpty)
                throw new ArgumentException($"Team {team}'s castle must be non-empty and lie inside its base.");
            if (_buildZones[team].Length == 0)
                throw new ArgumentException($"Team {team} needs at least one build zone.");
            foreach (var zone in _buildZones[team])
            {
                if (zone.IsEmpty || !baseArea.Contains(zone) || zone.Overlaps(_castles[team]))
                    throw new ArgumentException($"Team {team}'s build zones must be non-empty, inside its base and clear of the castle.");
                if (_walls.Any(zone.Overlaps))
                    throw new ArgumentException($"Team {team}'s build zone {zone} overlaps a wall.");
            }
            if (!bounds.Contains(_stagingAreas[team]) || _stagingAreas[team].IsEmpty)
                throw new ArgumentException($"Team {team}'s staging area must be non-empty and inside the map.");
        }

        Fingerprint = ComputeFingerprint();
    }

    public string Name { get; }
    public int LengthTiles { get; }
    public int WidthTiles { get; }
    public Fix Length { get; }
    public Fix Width { get; }

    /// <summary>Every lane spans X in [LaneMinX, LaneMaxX).</summary>
    public int LaneMinX { get; }

    public int LaneMaxX { get; }

    public IReadOnlyList<LaneDefinition> Lanes => _lanes;

    /// <summary>Every impassable rectangle: <see cref="LaneWalls"/> followed by <see cref="CliffWalls"/>.</summary>
    public IReadOnlyList<TileRect> Walls => _walls;

    /// <summary>The thick walls (forest) between and beside the lanes.</summary>
    public IReadOnlyList<TileRect> LaneWalls => _laneWalls;

    /// <summary>The thin cliff walls in the bases, i.e. every cliff minus its gaps.</summary>
    public IReadOnlyList<TileRect> CliffWalls => _cliffWalls;

    public IReadOnlyList<Cliff> Cliffs => _cliffs;
    public IReadOnlyList<Ramp> Ramps => _ramps;

    /// <summary>Flat ground between the cliffs, ordered by X. Cliff columns themselves belong to no terrace.</summary>
    public IReadOnlyList<Terrace> Terraces => _terraces;

    /// <summary>Hash of all geometry. Part of the state hash, so clients on different maps desync immediately.</summary>
    public ulong Fingerprint { get; }

    public TileRect CastleFootprint(int team) => _castles[team];
    public FixVec2 CastlePosition(int team) => _castles[team].Center;

    /// <summary>The team's build zones, castle terrace first. A building must fit inside one of them.</summary>
    public IReadOnlyList<TileRect> BuildZones(int team) => _buildZones[team];

    public TileRect StagingArea(int team) => _stagingAreas[team];

    /// <summary>Ground on the team's own side of the lanes.</summary>
    public TileRect BaseArea(int team) =>
        team == 0 ? new TileRect(0, 0, LaneMinX, WidthTiles) : new TileRect(LaneMaxX, 0, LengthTiles, WidthTiles);

    /// <summary>Direction the team's army marches: +X for team 0, -X for team 1.</summary>
    public FixVec2 Forward(int team) => _forward[team];

    public bool InBounds(int x, int y) => (uint)x < (uint)LengthTiles && (uint)y < (uint)WidthTiles;

    /// <summary>True when the footprint lies inside one of the team's build zones (ramps are checked separately).</summary>
    public bool IsInBuildZone(int team, TileRect footprint)
    {
        foreach (var zone in _buildZones[team])
        {
            if (zone.Contains(footprint))
                return true;
        }
        return false;
    }

    /// <summary>True when the footprint touches a ramp or its landings, where nothing may be built.</summary>
    public bool BlocksRamp(TileRect footprint)
    {
        foreach (var ramp in _ramps)
        {
            if (ramp.KeepClear.Overlaps(footprint))
                return true;
        }
        return false;
    }

    public bool IsWallTile(int x, int y) => InBounds(x, y) && _wallTile[y * LengthTiles + x];

    /// <summary>The terrace containing column <paramref name="x"/>, or null on a cliff column.</summary>
    public Terrace? TerraceAt(int x)
    {
        foreach (var terrace in _terraces)
        {
            if (x >= terrace.Area.MinX && x < terrace.Area.MaxX)
                return terrace;
        }
        return null;
    }

    /// <summary>True while the position is still on the team's own side of the lanes.</summary>
    public bool IsInOwnBase(int team, Fix x) => team == 0 ? x < Fix.FromInt(LaneMinX) : x >= Fix.FromInt(LaneMaxX);

    /// <summary>True once the position is past the far end of the lanes, i.e. in the enemy base.</summary>
    public bool IsPastLanes(int team, Fix x) => team == 0 ? x >= Fix.FromInt(LaneMaxX) : x < Fix.FromInt(LaneMinX);

    /// <summary>The lane whose band contains <paramref name="y"/>, or the nearest one (lower index on ties).</summary>
    public LaneDefinition NearestLane(Fix y)
    {
        LaneDefinition best = _lanes[0];
        Fix bestDistance = Fix.MaxValue;
        foreach (var lane in _lanes)
        {
            Fix min = Fix.FromInt(lane.MinY), max = Fix.FromInt(lane.MaxY);
            Fix distance = y < min ? min - y : y > max ? y - max : Fix.Zero;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = lane;
            }
        }
        return best;
    }

    /// <summary>
    /// Pushes a circle out of every wall it overlaps (rounded corners, so units slide around wall ends).
    /// Only the walls near the position's column are checked, so this is cheap for most units.
    /// </summary>
    public FixVec2 ResolveWalls(FixVec2 position, Fix radius)
    {
        foreach (var wall in WallsNear(position.X, radius))
        {
            if (position.X + radius <= wall.Min.X || position.X - radius >= wall.Max.X ||
                position.Y + radius <= wall.Min.Y || position.Y - radius >= wall.Max.Y)
                continue;

            var closest = new FixVec2(Fix.Clamp(position.X, wall.Min.X, wall.Max.X), Fix.Clamp(position.Y, wall.Min.Y, wall.Max.Y));
            FixVec2 away = position - closest;
            if (away.IsZero)
            {
                // Centre inside the wall: leave by the shortest way, preferring the lane sides.
                Fix down = position.Y - wall.Min.Y, up = wall.Max.Y - position.Y;
                Fix left = position.X - wall.Min.X, right = wall.Max.X - position.X;
                Fix least = Fix.Min(Fix.Min(down, up), Fix.Min(left, right));
                position = least == down ? new FixVec2(position.X, wall.Min.Y - radius)
                    : least == up ? new FixVec2(position.X, wall.Max.Y + radius)
                    : least == left ? new FixVec2(wall.Min.X - radius, position.Y)
                    : new FixVec2(wall.Max.X + radius, position.Y);
                continue;
            }

            Fix distanceSq = away.LengthSquared;
            if (distanceSq >= radius * radius)
                continue;
            Fix distance = Fix.Sqrt(distanceSq);
            if (distance.Raw == 0)
                continue;
            position = closest + away * (radius / distance);
        }
        return position;
    }

    /// <summary>Closest point on any wall within <paramref name="within"/> of the position (centre outside walls only).</summary>
    public bool TryNearestWallPoint(FixVec2 position, Fix within, out FixVec2 point)
    {
        point = default;
        Fix bestSq = within * within;
        bool found = false;
        foreach (var wall in WallsNear(position.X, within))
        {
            var closest = new FixVec2(Fix.Clamp(position.X, wall.Min.X, wall.Max.X), Fix.Clamp(position.Y, wall.Min.Y, wall.Max.Y));
            Fix distanceSq = FixVec2.DistanceSquared(position, closest);
            if (distanceSq.Raw == 0 || distanceSq >= bestSq)
                continue;
            bestSq = distanceSq;
            point = closest;
            found = true;
        }
        return found;
    }

    /// <summary>True when the straight line from a to b passes through a wall (no shooting or chasing through walls).</summary>
    public bool SegmentCrossesWall(FixVec2 a, FixVec2 b)
    {
        if (_wallRects.Length == 0)
            return false;
        Fix minX = Fix.Min(a.X, b.X), maxX = Fix.Max(a.X, b.X);
        Fix minY = Fix.Min(a.Y, b.Y), maxY = Fix.Max(a.Y, b.Y);

        foreach (var wall in _wallRects)
        {
            if (maxX <= wall.Min.X || minX >= wall.Max.X || maxY <= wall.Min.Y || minY >= wall.Max.Y)
                continue;

            // Liang-Barsky clip of the segment against the rectangle.
            Fix t0 = Fix.Zero, t1 = Fix.One;
            Fix dx = b.X - a.X, dy = b.Y - a.Y;
            if (Clip(-dx, a.X - wall.Min.X, ref t0, ref t1) && Clip(dx, wall.Max.X - a.X, ref t0, ref t1) &&
                Clip(-dy, a.Y - wall.Min.Y, ref t0, ref t1) && Clip(dy, wall.Max.Y - a.Y, ref t0, ref t1) &&
                t0 < t1)
                return true;
        }
        return false;

        static bool Clip(Fix p, Fix q, ref Fix t0, ref Fix t1)
        {
            if (p.Raw == 0)
                return q >= Fix.Zero;
            Fix r = q / p;
            if (p < Fix.Zero)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }
    }

    public bool IsInsideWall(FixVec2 position, Fix margin)
    {
        foreach (var wall in WallsNear(position.X, margin))
        {
            if (position.X > wall.Min.X - margin && position.X < wall.Max.X + margin &&
                position.Y > wall.Min.Y - margin && position.Y < wall.Max.Y + margin)
                return true;
        }
        return false;
    }

    /// <summary>Every wall that a circle at <paramref name="x"/> with the given radius can touch (in the same order as <see cref="Walls"/>).</summary>
    ReadOnlySpan<FixRect> WallsNear(Fix x, Fix margin)
    {
        if (margin > Fix.FromInt(WallBucketReach))
            return _wallRects;
        int column = Math.Clamp(x.FloorToInt(), 0, LengthTiles - 1);
        return _wallsNearColumn[column];
    }

    static (TileRect[] Walls, Ramp[] Ramps) BuildCliffs(Cliff[] cliffs, TileRect bounds)
    {
        var walls = new List<TileRect>();
        var ramps = new List<Ramp>();
        int previousMaxX = 0;
        foreach (var cliff in cliffs)
        {
            if (cliff.MinX < previousMaxX || cliff.MaxX <= cliff.MinX || cliff.MaxX > bounds.MaxX || cliff.UpperLevel < 1 || cliff.RampRun < 0)
                throw new ArgumentException("Cliffs must be non-empty, inside the map, apart from each other and above level 0.", nameof(cliffs));
            previousMaxX = cliff.MaxX;

            int wallStartY = 0;
            foreach (var (minY, maxY) in cliff.Gaps.OrderBy(g => g.MinY))
            {
                if (minY < wallStartY || maxY <= minY || maxY > bounds.MaxY)
                    throw new ArgumentException("A cliff's gaps must be non-empty, inside the map and apart from each other.", nameof(cliffs));
                if (minY > wallStartY)
                    walls.Add(new TileRect(cliff.MinX, wallStartY, cliff.MaxX, minY));
                wallStartY = maxY;

                var slope = cliff.UpperSideIsMinX
                    ? new TileRect(cliff.MinX, minY, cliff.MaxX + cliff.RampRun, maxY)
                    : new TileRect(cliff.MinX - cliff.RampRun, minY, cliff.MaxX, maxY);
                var keepClear = new TileRect(
                    Math.Max(slope.MinX - RampLanding, 0), minY, Math.Min(slope.MaxX + RampLanding, bounds.MaxX), maxY);
                ramps.Add(new Ramp(slope, keepClear, cliff.LowerLevel, cliff.UpperSideIsMinX));
            }
            if (wallStartY < bounds.MaxY)
                walls.Add(new TileRect(cliff.MinX, wallStartY, cliff.MaxX, bounds.MaxY));
        }
        return (walls.ToArray(), ramps.ToArray());
    }

    static Terrace[] BuildTerraces(Cliff[] cliffs, int length, int width)
    {
        if (cliffs.Length == 0)
            return [new Terrace(new TileRect(0, 0, length, width), 0)];

        var terraces = new List<Terrace>();
        int startX = 0;
        int level = cliffs[0].UpperSideIsMinX ? cliffs[0].UpperLevel : cliffs[0].LowerLevel;
        foreach (var cliff in cliffs)
        {
            int before = cliff.UpperSideIsMinX ? cliff.UpperLevel : cliff.LowerLevel;
            if (before != level)
                throw new ArgumentException("Neighbouring cliffs must agree on the level of the ground between them.", nameof(cliffs));
            if (cliff.MinX > startX)
                terraces.Add(new Terrace(new TileRect(startX, 0, cliff.MinX, width), level));
            startX = cliff.MaxX;
            level = cliff.UpperSideIsMinX ? cliff.LowerLevel : cliff.UpperLevel;
        }
        if (startX < length)
            terraces.Add(new Terrace(new TileRect(startX, 0, length, width), level));
        return terraces.ToArray();
    }

    ulong ComputeFingerprint()
    {
        ulong hash = 14695981039346656037UL;
        void Add(int value)
        {
            hash ^= (uint)value;
            hash *= 1099511628211UL;
        }
        void AddRect(TileRect r)
        {
            Add(r.MinX); Add(r.MinY); Add(r.MaxX); Add(r.MaxY);
        }

        Add(LengthTiles); Add(WidthTiles); Add(LaneMinX); Add(LaneMaxX);
        foreach (var lane in _lanes) AddRect(lane.Corridor);
        foreach (var rect in _castles) AddRect(rect);
        foreach (var zones in _buildZones)
        {
            Add(zones.Length);
            foreach (var rect in zones) AddRect(rect);
        }
        foreach (var rect in _stagingAreas) AddRect(rect);
        foreach (var rect in _walls) AddRect(rect);
        foreach (var ramp in _ramps) AddRect(ramp.KeepClear);
        return hash;
    }
}
