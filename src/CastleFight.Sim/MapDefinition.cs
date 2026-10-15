using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>One corridor between the two bases. Lanes run along X; <see cref="MinY"/>..<see cref="MaxY"/> is the band they cover.</summary>
public sealed record LaneDefinition(int Index, TileRect Corridor)
{
    public int MinY => Corridor.MinY;
    public int MaxY => Corridor.MaxY;
}

/// <summary>
/// Static map geometry, all in whole tiles. Team 0's base is on the left (low X), team 1's on the right.
/// Between the bases, X in [<see cref="LaneMinX"/>, <see cref="LaneMaxX"/>) is split into parallel lanes;
/// everything in that span that is not a lane is an impassable wall. Bases are open ground holding the
/// castle and the team's shared build zone.
/// </summary>
public sealed class MapDefinition
{
    readonly TileRect[] _castles;
    readonly TileRect[] _buildZones;
    readonly TileRect[] _stagingAreas;
    readonly LaneDefinition[] _lanes;
    readonly TileRect[] _walls;
    readonly FixRect[] _wallRects;
    readonly FixVec2[] _forward = [new(Fix.One, Fix.Zero), new(-Fix.One, Fix.Zero)];

    public MapDefinition(string name, int lengthTiles, int widthTiles, int laneMinX, int laneMaxX,
        IReadOnlyList<(int MinY, int MaxY)> lanes, TileRect[] castleFootprints, TileRect[] buildZones, TileRect[] stagingAreas)
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
        var walls = new List<TileRect>();
        int previousMaxY = 0;
        for (int i = 0; i < lanes.Count; i++)
        {
            var (minY, maxY) = lanes[i];
            if (minY < previousMaxY || maxY - minY < 4 || maxY > widthTiles)
                throw new ArgumentException($"Lane {i} must be at least 4 tiles wide, inside the map and below the next lane.", nameof(lanes));
            if (minY > previousMaxY)
                walls.Add(new TileRect(laneMinX, previousMaxY, laneMaxX, minY));
            _lanes[i] = new LaneDefinition(i, new TileRect(laneMinX, minY, laneMaxX, maxY));
            previousMaxY = maxY;
        }
        if (previousMaxY < widthTiles)
            walls.Add(new TileRect(laneMinX, previousMaxY, laneMaxX, widthTiles));
        _walls = walls.ToArray();
        _wallRects = _walls.Select(w => w.ToFixRect()).ToArray();

        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            var baseArea = BaseArea(team);
            if (!baseArea.Contains(_castles[team]) || !baseArea.Contains(_buildZones[team]))
                throw new ArgumentException($"Team {team}'s castle and build zone must lie inside its base.");
            if (_castles[team].Overlaps(_buildZones[team]) || _castles[team].IsEmpty || _buildZones[team].IsEmpty)
                throw new ArgumentException($"Team {team}'s castle and build zone must be non-empty and must not overlap.");
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
    public IReadOnlyList<TileRect> Walls => _walls;

    /// <summary>Hash of all geometry. Part of the state hash, so clients on different maps desync immediately.</summary>
    public ulong Fingerprint { get; }

    public TileRect CastleFootprint(int team) => _castles[team];
    public FixVec2 CastlePosition(int team) => _castles[team].Center;
    public TileRect BuildZone(int team) => _buildZones[team];
    public TileRect StagingArea(int team) => _stagingAreas[team];

    /// <summary>Open ground on the team's own side of the lanes.</summary>
    public TileRect BaseArea(int team) =>
        team == 0 ? new TileRect(0, 0, LaneMinX, WidthTiles) : new TileRect(LaneMaxX, 0, LengthTiles, WidthTiles);

    /// <summary>Direction the team's army marches: +X for team 0, -X for team 1.</summary>
    public FixVec2 Forward(int team) => _forward[team];

    public bool InBounds(int x, int y) => (uint)x < (uint)LengthTiles && (uint)y < (uint)WidthTiles;

    public bool IsBuildable(int team, int x, int y) => _buildZones[team].Contains(x, y);

    public bool IsWallTile(int x, int y)
    {
        foreach (var wall in _walls)
        {
            if (wall.Contains(x, y))
                return true;
        }
        return false;
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
    /// Walls are few and only exist in the lane span, so this is cheap for most units.
    /// </summary>
    public FixVec2 ResolveWalls(FixVec2 position, Fix radius)
    {
        if (!NearWallSpan(position.X, radius))
            return position;

        foreach (var wall in _wallRects)
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
        if (!NearWallSpan(position.X, within))
            return false;

        Fix bestSq = within * within;
        bool found = false;
        foreach (var wall in _wallRects)
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
        foreach (var wall in _wallRects)
        {
            if (position.X > wall.Min.X - margin && position.X < wall.Max.X + margin &&
                position.Y > wall.Min.Y - margin && position.Y < wall.Max.Y + margin)
                return true;
        }
        return false;
    }

    bool NearWallSpan(Fix x, Fix margin) =>
        _walls.Length > 0 && x + margin > Fix.FromInt(LaneMinX) && x - margin < Fix.FromInt(LaneMaxX);

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
        foreach (var rect in _buildZones) AddRect(rect);
        foreach (var rect in _stagingAreas) AddRect(rect);
        return hash;
    }
}
