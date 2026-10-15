using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

/// <summary>Rectangle of whole tiles. Min is inclusive, Max exclusive, so a tile (x, y) covers [x, x+1).</summary>
public readonly record struct TileRect(int MinX, int MinY, int MaxX, int MaxY)
{
    public int Width => MaxX - MinX;
    public int Height => MaxY - MinY;
    public bool IsEmpty => MaxX <= MinX || MaxY <= MinY;

    public FixVec2 Center => new(Fix.FromInt(MinX + MaxX) / 2, Fix.FromInt(MinY + MaxY) / 2);

    public bool Contains(int x, int y) => x >= MinX && x < MaxX && y >= MinY && y < MaxY;

    public bool Contains(TileRect other) =>
        other.MinX >= MinX && other.MaxX <= MaxX && other.MinY >= MinY && other.MaxY <= MaxY;

    public bool Overlaps(TileRect other) =>
        other.MinX < MaxX && other.MaxX > MinX && other.MinY < MaxY && other.MaxY > MinY;

    public FixRect ToFixRect() => new(FixVec2.FromInts(MinX, MinY), FixVec2.FromInts(MaxX, MaxY));

    /// <summary>The same rectangle seen from the other end of a map that is <paramref name="mapLength"/> tiles long.</summary>
    public TileRect MirrorX(int mapLength) => new(mapLength - MaxX, MinY, mapLength - MinX, MaxY);
}
