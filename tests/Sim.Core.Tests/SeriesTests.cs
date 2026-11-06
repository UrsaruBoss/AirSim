using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Time series, per-wave outcomes, cost and the casualty histogram in the run summary.</summary>
public class SeriesTests
{
    private static Scenario Make() => new()
    {
        Name = "series-test",
        DtSeconds = 0.1,
        DurationSeconds = 6000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0 } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = 60, SpeedMps = 100, AltitudeM = 300, CostUsd = 20_000 },
            new() { Id = "W2", SiteId = "L1", TargetId = "T1", Count = 40, SpeedMps = 100, AltitudeM = 300, CostUsd = 50_000, StartTimeSeconds = 600 }
        },
        Defenses = new List<Defense>
        {
            new() { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 24.8, RangeKm = 150 },
            new() { Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 25.3, RangeKm = 40, Magazine = 30, Pk = 0.6, ReloadSeconds = 1, ShotCostUsd = 100_000 }
        }
    };

    private static (Scenario Scenario, Simulation Sim, RunSummary Summary) Analyze()
    {
        var s = Make();
        var sim = new Simulation(s, 1);
        sim.Run();
        return (s, sim, RunAnalyzer.Analyze(s, sim.Events.Events, 1));
    }

    [Fact]
    public void The_series_ends_at_the_totals_and_never_decreases()
    {
        var (_, _, summary) = Analyze();
        var series = Assert.IsType<RunSeries>(summary.Series);

        Assert.Equal(summary.Launched, series.Launched[^1]);
        Assert.Equal(summary.Intercepted, series.Intercepted[^1]);
        Assert.Equal(summary.Impacted, series.Impacts[^1]);
        Assert.Equal(summary.Detected, series.Detected[^1]);
        Assert.Equal(30, series.MagazineTotal);

        foreach (var list in new[] { series.Launched, series.Detected, series.Intercepted, series.Impacts, series.Shots })
            for (int i = 1; i < list.Count; i++)
                Assert.True(list[i] >= list[i - 1]);
    }

    [Fact]
    public void Shots_in_the_series_match_the_per_defense_statistics()
    {
        var (_, _, summary) = Analyze();
        var series = summary.Series!;

        Assert.Equal(summary.Defenses.Sum(d => d.Shots), series.Shots[^1]);
    }

    [Fact]
    public void Waves_account_for_every_unit()
    {
        var (_, _, summary) = Analyze();
        var waves = summary.Waves!;

        Assert.Equal(new[] { "W1", "W2" }, waves.Select(w => w.Id).ToArray());
        Assert.Equal(new[] { 60, 40 }, waves.Select(w => w.Count).ToArray());
        Assert.Equal(summary.Launched, waves.Sum(w => w.Launched));
        Assert.Equal(summary.Intercepted, waves.Sum(w => w.Intercepted));
        Assert.Equal(summary.Impacted, waves.Sum(w => w.Impacted));
        foreach (var w in waves)
            Assert.True(w.Intercepted + w.Jammed + w.Crashed + w.Impacted <= w.Launched);
    }

    [Fact]
    public void Cost_is_the_launched_units_against_the_shots_fired()
    {
        var (_, _, summary) = Analyze();
        var cost = summary.Cost!;
        var waves = summary.Waves!;

        Assert.Equal(waves[0].Launched * 20_000.0 + waves[1].Launched * 50_000.0, cost.AttackUsd);
        Assert.Equal(summary.Defenses.Sum(d => d.Shots) * 100_000.0, cost.DefenseUsd);
    }

    [Fact]
    public void The_casualty_histogram_counts_every_trial()
    {
        var (_, _, summary) = Analyze();
        var h = summary.CasualtyHistogram!;

        Assert.Equal(summary.Trials, h.Killed.Sum());
        Assert.Equal(summary.Trials, h.Injured.Sum());
        Assert.Equal(h.Killed.Count, h.Injured.Count);
        Assert.True(h.BinWidth >= 1.0);
    }
}
