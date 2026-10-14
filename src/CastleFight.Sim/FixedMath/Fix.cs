using System.Globalization;
using System.Runtime.CompilerServices;

namespace CastleFight.Sim.FixedMath;

/// <summary>
/// Deterministic fixed-point number: a <see cref="long"/> with 16 fractional bits.
/// All simulation math uses this instead of float/double so every machine in a lockstep
/// game computes bit-identical results. Safe range for products is roughly ±2 billion.
/// </summary>
public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
{
    public const int FractionalBits = 16;
    public const long OneRaw = 1L << FractionalBits;

    public readonly long Raw;

    Fix(long raw) => Raw = raw;

    public static readonly Fix Zero = default;
    public static readonly Fix One = new(OneRaw);
    public static readonly Fix Half = new(OneRaw / 2);
    public static readonly Fix MaxValue = new(long.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix FromRaw(long raw) => new(raw);

    public static Fix FromInt(int value) => new((long)value << FractionalBits);

    public static Fix FromRatio(long numerator, long denominator) => new((numerator << FractionalBits) / denominator);

    /// <summary>
    /// For converting player input (mouse positions) into commands. Never call this inside the
    /// simulation step: only the resulting <see cref="Raw"/> value is shared between machines.
    /// </summary>
    public static Fix FromFloat(float value) => new((long)MathF.Round(value * OneRaw));

    /// <summary>For rendering and UI only.</summary>
    public float ToFloat() => Raw / (float)OneRaw;

    public int FloorToInt() => (int)(Raw >> FractionalBits);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix operator +(Fix a, Fix b) => new(a.Raw + b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix operator -(Fix a, Fix b) => new(a.Raw - b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix operator -(Fix a) => new(-a.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix operator *(Fix a, Fix b) => new((a.Raw * b.Raw) >> FractionalBits);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix operator *(Fix a, int b) => new(a.Raw * b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix operator /(Fix a, Fix b) => new((a.Raw << FractionalBits) / b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fix operator /(Fix a, int b) => new(a.Raw / b);

    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

    public static Fix Abs(Fix value) => new(Math.Abs(value.Raw));
    public static Fix Min(Fix a, Fix b) => a.Raw < b.Raw ? a : b;
    public static Fix Max(Fix a, Fix b) => a.Raw > b.Raw ? a : b;
    public static Fix Clamp(Fix value, Fix min, Fix max) => value.Raw < min.Raw ? min : value.Raw > max.Raw ? max : value;

    public static Fix Sqrt(Fix value)
    {
        if (value.Raw <= 0)
            return Zero;
        return new((long)IntSqrt((ulong)value.Raw << FractionalBits));
    }

    /// <summary>
    /// Exact floor(sqrt(n)). The double estimate only seeds the search; the integer correction
    /// afterwards makes the result identical on every CPU.
    /// </summary>
    public static ulong IntSqrt(ulong n)
    {
        if (n < 2)
            return n;

        ulong r = (ulong)Math.Sqrt(n);
        if (r > uint.MaxValue)
            r = uint.MaxValue;
        while (r * r > n)
            r--;
        while (r < uint.MaxValue && (r + 1) * (r + 1) <= n)
            r++;
        return r;
    }

    public bool Equals(Fix other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix other && Equals(other);
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);
    public override string ToString() => ToFloat().ToString("0.###", CultureInfo.InvariantCulture);
}
