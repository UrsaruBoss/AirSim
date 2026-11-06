using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class SensitivityTests
{
    private static Scenario Make() => new()
    {
        Name = "sens",
        DtSeconds = 0.5,
        DurationSeconds = 3000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 25.0 } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = 60, IntervalSeconds = 1, SpeedMps = 100, AltitudeM = 300 }
        },
        Defenses = new List<Defense>
        {
            new() { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 24.95, RangeKm = 10, Magazine = 100, Pk = 0.5, ReloadSeconds = 1 }
        }
    };

    [Fact]
    public void Only_applicable_factors_are_used()
    {
        var ids = SensitivityRunner.ApplicableTo(Make()).Select(f => f.Id).ToHashSet();
        Assert.Contains("pk", ids);
        Assert.Contains("count", ids);
        Assert.DoesNotContain("jam", ids);     // no EW unit
        Assert.DoesNotContain("radar", ids);   // no radar
        Assert.DoesNotContain("failure", ids); // the wave has no failure rate
    }

    [Fact]
    public void Factors_keep_the_scenario_valid()
    {
        var scenario = Make();
        foreach (var factor in SensitivityRunner.ApplicableTo(scenario))
            foreach (double k in new[] { 0.2, 0.8, 1.2, 5.0 })
                Assert.Empty(ScenarioLoader.Validate(factor.Apply(scenario, k)));
    }

    [Fact]
    public void Pk_is_clamped_to_one()
    {
        var scaled = SensitivityRunner.Factors.First(f => f.Id == "pk").Apply(Make() with { Defenses = new List<Defense>
        {
            new() { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 24.95, RangeKm = 10, Magazine = 100, Pk = 0.9 }
        } }, 2.0);
        Assert.Equal(1.0, scaled.Defenses[0].Pk);
    }

    [Fact]
    public void Attack_size_never_drops_below_one()
    {
        var scaled = SensitivityRunner.Factors.First(f => f.Id == "count").Apply(Make() with { Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = 1, SpeedMps = 100, AltitudeM = 300 }
        } }, 0.2);
        Assert.Equal(1, scaled.Waves[0].Count);
    }

    [Fact]
    public void Higher_pk_and_smaller_attack_lower_the_impacts_and_a_bigger_attack_raises_them()
    {
        var report = SensitivityRunner.Run(Make(), 100, 8, maxThreads: 2);
        var impacts = report.Rows.Where(r => r.Metric == "Impacts").ToDictionary(r => r.FactorId);

        Assert.True(impacts["pk"].DiffHigh < impacts["pk"].DiffLow, "mai mare Pk => mai putine impacturi decat la Pk mai mic");
        Assert.True(impacts["count"].DiffHigh > 0 && impacts["count"].DiffLow < 0);
    }

    [Fact]
    public void Rows_are_ordered_by_swing_within_each_metric_and_the_run_is_deterministic()
    {
        var a = SensitivityRunner.Run(Make(), 100, 6, maxThreads: 1);
        var b = SensitivityRunner.Run(Make(), 100, 6, maxThreads: 3);

        Assert.Equal(a.Rows, b.Rows);
        foreach (string metric in a.Metrics)
        {
            var swings = a.Rows.Where(r => r.Metric == metric).Select(r => r.Swing).ToList();
            Assert.Equal(swings.OrderByDescending(x => x).ToList(), swings);
        }
    }

    [Fact]
    public void Delta_outside_zero_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitivityRunner.Run(Make(), 1, 2, delta: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitivityRunner.Run(Make(), 1, 2, delta: 1.0));
    }
}
