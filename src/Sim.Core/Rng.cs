using System;
using System.Numerics;

namespace Sim.Core;

/// <summary>
/// PRNG propriu (xoshiro256**), seedat prin SplitMix64.
/// Nu depinde de System.Random, deci secventele nu se schimba intre versiuni de .NET.
/// Un Rng NU se partajeaza intre thread-uri: fiecare sistem/entitate isi ia propriul flux prin Derive().
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public ulong Seed { get; }

    public Rng(ulong seed)
    {
        Seed = seed;
        ulong sm = seed;
        _s0 = SplitMix64(ref sm);
        _s1 = SplitMix64(ref sm);
        _s2 = SplitMix64(ref sm);
        _s3 = SplitMix64(ref sm);
    }

    /// <summary>
    /// Flux independent, determinat doar de (seed, streamId).
    /// Nu depinde de cate valori au fost consumate din acest Rng, deci ordinea de executie nu conteaza.
    /// </summary>
    public Rng Derive(ulong streamId) => new Rng(Mix(Seed, streamId));

    public ulong NextUInt64()
    {
        unchecked
        {
            ulong result = BitOperations.RotateLeft(_s1 * 5UL, 7) * 9UL;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = BitOperations.RotateLeft(_s3, 45);

            return result;
        }
    }

    /// <summary>Uniform in [0, 1), 53 de biti de precizie.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform in [min, max).</summary>
    public double NextDouble(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(((NextUInt64() >> 32) * (ulong)maxExclusive) >> 32);
    }

    /// <summary>True cu probabilitatea p (ex: probabilitate de detectie, Pk).</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Normala standard (Box-Muller, fara stare ascunsa: consuma mereu exact doua valori).</summary>
    public double NextGaussian()
    {
        double u1 = 1.0 - NextDouble(); // (0, 1]
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public double NextGaussian(double mean, double stdDev) => mean + stdDev * NextGaussian();

    private static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    private static ulong Mix(ulong seed, ulong streamId)
    {
        unchecked
        {
            ulong state = seed ^ (streamId * 0xD1B54A32D192ED03UL + 0x9E3779B97F4A7C15UL);
            return SplitMix64(ref state);
        }
    }
}
