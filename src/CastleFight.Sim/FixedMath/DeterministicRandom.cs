using System.Numerics;

namespace CastleFight.Sim.FixedMath;

/// <summary>
/// PCG32 random generator. Same seed gives the same sequence on every machine, unlike
/// <see cref="System.Random"/> whose algorithm is not guaranteed across .NET versions.
/// Mutable struct: store it in a non-readonly field.
/// </summary>
public struct DeterministicRandom
{
    const ulong Multiplier = 6364136223846793005UL;
    const ulong Increment = 1442695040888963407UL;

    ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public readonly ulong State => _state;

    public uint NextUInt()
    {
        ulong old = _state;
        _state = old * Multiplier + Increment;
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return BitOperations.RotateRight(xorShifted, rotation);
    }

    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive) => (int)(((ulong)NextUInt() * (uint)maxExclusive) >> 32);

    /// <summary>Uniform value in [min, max).</summary>
    public Fix NextFix(Fix min, Fix max)
    {
        long range = max.Raw - min.Raw;
        if (range <= 0)
            return min;
        return Fix.FromRaw(min.Raw + (long)(((ulong)NextUInt() * (ulong)range) >> 32));
    }
}
