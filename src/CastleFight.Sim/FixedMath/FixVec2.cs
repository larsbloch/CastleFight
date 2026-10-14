using System.Runtime.CompilerServices;

namespace CastleFight.Sim.FixedMath;

/// <summary>2D vector of <see cref="Fix"/>. Simulation X runs along the lane, Y across it.</summary>
public readonly struct FixVec2 : IEquatable<FixVec2>
{
    public readonly Fix X;
    public readonly Fix Y;

    public FixVec2(Fix x, Fix y)
    {
        X = x;
        Y = y;
    }

    public static readonly FixVec2 Zero = default;

    public static FixVec2 FromInts(int x, int y) => new(Fix.FromInt(x), Fix.FromInt(y));

    public bool IsZero => X.Raw == 0 && Y.Raw == 0;

    public Fix LengthSquared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => X * X + Y * Y;
    }

    public Fix Length => Fix.Sqrt(LengthSquared);

    /// <summary>Rotated 90° counter-clockwise.</summary>
    public FixVec2 PerpendicularLeft => new(-Y, X);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new(a.X + b.X, a.Y + b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new(a.X - b.X, a.Y - b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixVec2 operator -(FixVec2 a) => new(-a.X, -a.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixVec2 operator *(FixVec2 a, Fix s) => new(a.X * s, a.Y * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixVec2 operator /(FixVec2 a, Fix s) => new(a.X / s, a.Y / s);

    public static bool operator ==(FixVec2 a, FixVec2 b) => a.Equals(b);
    public static bool operator !=(FixVec2 a, FixVec2 b) => !a.Equals(b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Z component of the 3D cross product: positive when <paramref name="b"/> is to the left of <paramref name="a"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix Cross(FixVec2 a, FixVec2 b) => a.X * b.Y - a.Y * b.X;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix DistanceSquared(FixVec2 a, FixVec2 b) => (a - b).LengthSquared;

    public FixVec2 Normalized()
    {
        Fix length = Length;
        return length.Raw == 0 ? Zero : this / length;
    }

    public FixVec2 ClampLength(Fix max)
    {
        Fix lengthSquared = LengthSquared;
        if (lengthSquared <= max * max)
            return this;
        return this * (max / Fix.Sqrt(lengthSquared));
    }

    public bool Equals(FixVec2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is FixVec2 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
