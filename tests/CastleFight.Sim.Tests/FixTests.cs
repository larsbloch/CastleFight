using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Tests;

public class FixTests
{
    [Fact]
    public void Arithmetic_matches_expected_values()
    {
        Fix a = Fix.FromRatio(3, 2);
        Fix b = Fix.FromInt(4);

        Assert.Equal(Fix.FromRatio(11, 2), a + b);
        Assert.Equal(Fix.FromRatio(-5, 2), a - b);
        Assert.Equal(Fix.FromInt(6), a * b);
        Assert.Equal(Fix.FromRatio(3, 8), a / b);
        Assert.Equal(Fix.FromInt(-6), -a * b);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(10_000, 100)]
    public void Sqrt_of_perfect_squares_is_exact(int value, int expected)
    {
        Assert.Equal(Fix.FromInt(expected), Fix.Sqrt(Fix.FromInt(value)));
    }

    [Fact]
    public void Sqrt_of_two_is_within_one_raw_step()
    {
        long expected = (long)(Math.Sqrt(2) * Fix.OneRaw);
        Assert.InRange(Fix.Sqrt(Fix.FromInt(2)).Raw, expected - 1, expected + 1);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(3UL)]
    [InlineData(1UL << 52)]
    [InlineData((1UL << 53) + 1)]
    [InlineData(ulong.MaxValue)]
    public void IntSqrt_returns_exact_floor(ulong n)
    {
        ulong r = Fix.IntSqrt(n);
        Assert.True((System.Numerics.BigInteger)r * r <= n);
        Assert.True((System.Numerics.BigInteger)(r + 1) * (r + 1) > n);
    }

    [Fact]
    public void Random_is_reproducible_from_seed()
    {
        var a = new DeterministicRandom(123);
        var b = new DeterministicRandom(123);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }
}
