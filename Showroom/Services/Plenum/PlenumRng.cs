namespace ShowroomPlenum;

/// <summary>The game's own small integer generator (splitmix64), wrapped as a Random so Pleroma's draw can take it.
/// The draw calls rng.NextInt64(total) once; every other Random member is derived from the same stream so nothing can
/// fall back to a system seed. One stream per draw: (match seed, draw number). Integers only, so every browser agrees.</summary>
public sealed class PlenumRng : Random
{
    private ulong _s;

    /// <summary>The seed one side of a duel deals and draws from: the match seed mixed with the seat (0 or 1), so the two
    /// sides use separate streams of the same generator.</summary>
    public static ulong SeatSeed(ulong matchSeed, int seat) => Mix(matchSeed ^ Mix((ulong)seat + 0xD1B54A32D192ED03UL));

    public PlenumRng(ulong seed, ulong stream) : base(0)
    {
        _s = Mix(seed ^ Mix(stream + 0x9E3779B97F4A7C15UL));
    }

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private ulong Next64()
    {
        _s += 0x9E3779B97F4A7C15UL;
        ulong z = _s;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform on [0, max) with no modulo bias (a draw above the largest multiple of max is thrown away).</summary>
    public override long NextInt64(long maxValue)
    {
        if (maxValue <= 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
        ulong m = (ulong)maxValue;
        ulong limit = ulong.MaxValue - (ulong.MaxValue % m);
        ulong x;
        do { x = Next64(); } while (x >= limit);
        return (long)(x % m);
    }

    public override long NextInt64(long minValue, long maxValue) => minValue + NextInt64(maxValue - minValue);
    public override long NextInt64() => (long)(Next64() >> 1);
    public override int Next() => (int)(Next64() >> 33);
    public override int Next(int maxValue) => (int)NextInt64(maxValue);
    public override int Next(int minValue, int maxValue) => minValue + (int)NextInt64(maxValue - minValue);
    public override double NextDouble() => (Next64() >> 11) * (1.0 / (1UL << 53));
    protected override double Sample() => NextDouble();
    public override void NextBytes(byte[] buffer) { for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(Next64() >> 56); }
    public override void NextBytes(Span<byte> buffer) { for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(Next64() >> 56); }
}
