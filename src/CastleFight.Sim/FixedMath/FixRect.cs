namespace CastleFight.Sim.FixedMath;

/// <summary>Axis-aligned rectangle, inclusive on both edges.</summary>
public readonly record struct FixRect(FixVec2 Min, FixVec2 Max)
{
    public FixVec2 Center => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2);

    public bool Contains(FixVec2 point) =>
        point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;

    public FixRect Inset(Fix amount) =>
        new(new FixVec2(Min.X + amount, Min.Y + amount), new FixVec2(Max.X - amount, Max.Y - amount));
}
