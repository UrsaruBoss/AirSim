using System.Collections.Generic;
using Xunit;

namespace Sim.Core.Tests;

public class RngTests
{
    [Fact]
    public void Same_seed_gives_same_sequence()
    {
        var a = new Rng(12345);
        var b = new Rng(12345);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void Different_seeds_give_different_sequences()
    {
        var a = new Rng(1);
        var b = new Rng(2);
        var first = new List<ulong>();
        var second = new List<ulong>();
        for (int i = 0; i < 10; i++)
        {
            first.Add(a.NextUInt64());
            second.Add(b.NextUInt64());
        }
        Assert.NotEqual(first, second);
    }

    // Reference values computed with an independent (Python) implementation of xoshiro256** + SplitMix64.
    // If this test fails, the algorithm changed => all saved runs become unreproducible (bump EngineInfo.Version).
    [Fact]
    public void Golden_values_for_seed_42()
    {
        var r = new Rng(42);
        Assert.Equal(1546998764402558742UL, r.NextUInt64());
        Assert.Equal(6990951692964543102UL, r.NextUInt64());
        Assert.Equal(12544586762248559009UL, r.NextUInt64());
    }

    [Fact]
    public void Golden_values_for_seed_0()
    {
        var r = new Rng(0);
        Assert.Equal(11091344671253066420UL, r.NextUInt64());
        Assert.Equal(13793997310169335082UL, r.NextUInt64());
        Assert.Equal(1900383378846508768UL, r.NextUInt64());
    }

    [Fact]
    public void Derived_stream_does_not_depend_on_parent_consumption()
    {
        var parent1 = new Rng(99);
        var parent2 = new Rng(99);
        for (int i = 0; i < 500; i++) parent2.NextUInt64(); // "consumed" parent

        var d1 = parent1.Derive(7);
        var d2 = parent2.Derive(7);
        for (int i = 0; i < 100; i++)
            Assert.Equal(d1.NextUInt64(), d2.NextUInt64());
    }

    [Fact]
    public void Derived_streams_with_different_ids_differ()
    {
        var root = new Rng(99);
        Assert.NotEqual(root.Derive(1).NextUInt64(), root.Derive(2).NextUInt64());
    }

    [Fact]
    public void NextDouble_is_in_unit_interval()
    {
        var r = new Rng(5);
        for (int i = 0; i < 10000; i++)
        {
            double d = r.NextDouble();
            Assert.InRange(d, 0.0, 1.0);
            Assert.NotEqual(1.0, d);
        }
    }

    [Fact]
    public void NextInt_stays_in_range_and_covers_it()
    {
        var r = new Rng(5);
        var seen = new HashSet<int>();
        for (int i = 0; i < 5000; i++)
        {
            int v = r.NextInt(10);
            Assert.InRange(v, 0, 9);
            seen.Add(v);
        }
        Assert.Equal(10, seen.Count);
    }

    [Fact]
    public void Gaussian_has_roughly_zero_mean_and_unit_variance()
    {
        var r = new Rng(2024);
        const int n = 50000;
        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double g = r.NextGaussian();
            sum += g;
            sumSq += g * g;
        }
        double mean = sum / n;
        double variance = sumSq / n - mean * mean;
        Assert.InRange(mean, -0.03, 0.03);
        Assert.InRange(variance, 0.95, 1.05);
    }
}
