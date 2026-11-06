using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Waves with several launch points / targets, and units that fall along their route.</summary>
public class MultiTargetTests
{
    private static Scenario Make(Wave wave, params Defense[] defenses) => new()
    {
        Name = "multi-test",
        DtSeconds = 0.1,
        DurationSeconds = 9000,
        LaunchSites = new List<LaunchSite>
        {
            new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 },
            new() { Id = "L2", Name = "L2", LatDeg = 44.5, LonDeg = 24.0 }
        },
        Targets = new List<Target>
        {
            new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0 },
            new() { Id = "T2", Name = "T2", Kind = "military_base", LatDeg = 44.5, LonDeg = 26.0 },
            new() { Id = "T3", Name = "T3", Kind = "city", LatDeg = 44.25, LonDeg = 26.0 }
        },
        Waves = new List<Wave> { wave },
        Defenses = defenses.ToList()
    };

    private static Wave W(int count = 200, double failureRate = 0, List<string>? sites = null, List<string>? targets = null) => new()
    {
        Id = "W1", SiteId = "L1", TargetId = "T1", SiteIds = sites, TargetIds = targets,
        Count = count, SpeedMps = 100, AltitudeM = 300, FailureRate = failureRate
    };

    private static Simulation Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    private static Dictionary<double, int> LatCounts(IEnumerable<SimEvent> events) =>
        events.GroupBy(e => System.Math.Round(e.Position.LatDeg, 2)).ToDictionary(g => g.Key, g => g.Count());

    // ---- target / launch lists ----

    [Fact]
    public void A_wave_can_spread_over_several_targets()
    {
        var sim = Run(Make(W(targets: new List<string> { "T1", "T2", "T3" })));

        var counts = LatCounts(sim.Events.Events.Where(e => e.Type == SimEventType.Impact));
        Assert.Equal(new[] { 44.0, 44.25, 44.5 }, counts.Keys.OrderBy(k => k).ToArray());
        Assert.All(counts.Values, c => Assert.InRange(c, 40, 100)); // ~ 200 / 3 each
        Assert.Equal(200, counts.Values.Sum());
    }

    [Fact]
    public void A_repeated_id_gives_a_target_more_weight()
    {
        var sim = Run(Make(W(count: 400, targets: new List<string> { "T1", "T1", "T1", "T2" })));

        var counts = LatCounts(sim.Events.Events.Where(e => e.Type == SimEventType.Impact));
        Assert.True(counts[44.0] > 2 * counts[44.5]);
    }

    [Fact]
    public void A_wave_can_launch_from_several_sites()
    {
        var sim = Run(Make(W(sites: new List<string> { "L1", "L2" })));

        var counts = LatCounts(sim.Events.Events.Where(e => e.Type == SimEventType.Launched));
        Assert.Equal(2, counts.Count);
        Assert.All(counts.Values, c => Assert.InRange(c, 60, 140));
    }

    [Fact]
    public void Without_lists_the_single_ids_are_used()
    {
        var sim = Run(Make(W(count: 20)));

        Assert.All(sim.Events.Events.Where(e => e.Type == SimEventType.Impact), e => Assert.Equal(44.0, System.Math.Round(e.Position.LatDeg, 2)));
    }

    [Fact]
    public void Lists_are_deterministic_and_their_order_is_part_of_the_identity()
    {
        var a = Make(W(targets: new List<string> { "T1", "T2" }));
        var b = Make(W(targets: new List<string> { "T2", "T1" }));

        Assert.Equal(Run(a, 5).Events.Hash(), Run(a, 5).Events.Hash());
        Assert.NotEqual(ScenarioLoader.Hash(a), ScenarioLoader.Hash(b));
    }

    [Fact]
    public void Lists_survive_a_json_round_trip()
    {
        var s = Make(W(targets: new List<string> { "T1", "T2" }, sites: new List<string> { "L1", "L2" }, failureRate: 0.1));
        var back = ScenarioLoader.FromJson(ScenarioLoader.ToJson(s));

        Assert.Equal(new[] { "T1", "T2" }, back.Waves[0].TargetIds);
        Assert.Equal(new[] { "L1", "L2" }, back.Waves[0].SiteIds);
        Assert.Equal(0.1, back.Waves[0].FailureRate);
        Assert.Equal(ScenarioLoader.Hash(s), ScenarioLoader.Hash(back));
    }

    // ---- falling along the route ----

    [Fact]
    public void Failure_rate_one_means_everything_falls_short()
    {
        var sim = Run(Make(W(count: 50, failureRate: 1.0)));

        Assert.Equal(50, sim.CrashedCount);
        Assert.Equal(0, sim.ImpactCount);
        Assert.DoesNotContain(sim.Events.Events, e => e.Type == SimEventType.Impact);

        // they fall along the route: between launch (lon 24) and target (lon 26), not at the target
        var crashes = sim.Events.Events.Where(e => e.Type == SimEventType.Crashed).ToList();
        Assert.Equal(50, crashes.Count);
        Assert.All(crashes, e => Assert.InRange(e.Position.LonDeg, 24.0 + 0.05 * 1.9, 24.0 + 0.95 * 2.1));
    }

    [Fact]
    public void Failure_rate_is_a_probability()
    {
        var sim = Run(Make(W(count: 1000, failureRate: 0.3)));

        Assert.InRange(sim.CrashedCount, 240, 360);
        Assert.Equal(1000, sim.CrashedCount + sim.ImpactCount + sim.InterceptedCount);
    }

    [Fact]
    public void Units_that_fall_short_do_not_destroy_defenses()
    {
        var radar = new Defense { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 26.0, RangeKm = 100, HardnessKg = 1 };
        var sim = Run(Make(W(count: 20, failureRate: 1.0), radar));

        Assert.Equal(0, sim.DefensesDestroyed);
    }

    [Fact]
    public void Defenses_still_intercept_units_that_would_have_failed()
    {
        var gun = new Defense { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 24.5, RangeKm = 20, Magazine = 100, Pk = 1.0, ReloadSeconds = 0 };
        var sim = Run(Make(W(count: 10, failureRate: 1.0), gun));

        Assert.Equal(10, sim.CrashedCount + sim.InterceptedCount);
    }

    // ---- statistics ----

    [Fact]
    public void Summary_classifies_impacts_by_the_nearest_target_and_counts_failures()
    {
        var s = Make(W(count: 300, failureRate: 0.2, targets: new List<string> { "T1", "T2" }));
        var sim = Run(s);
        var summary = RunAnalyzer.Analyze(s, sim.Events.Events, 1, 50);

        Assert.Equal(sim.CrashedCount, summary.Failed);
        Assert.Equal(sim.ImpactCount, summary.Impacted);
        Assert.Contains(summary.Classes, c => c.Class == "city");
        Assert.Contains(summary.Classes, c => c.Class == "military_base");
    }

    // ---- validation ----

    [Fact]
    public void Invalid_lists_and_failure_rates_are_rejected()
    {
        Assert.Contains(ScenarioLoader.Validate(Make(W(targets: new List<string> { "T1", "TX" }))), e => e.Contains("TargetId 'TX'"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(sites: new List<string> { "LX" }))), e => e.Contains("SiteId 'LX'"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(failureRate: 1.5))), e => e.Contains("FailureRate"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(failureRate: -0.1))), e => e.Contains("FailureRate"));
        Assert.Empty(ScenarioLoader.Validate(Make(W(targets: new List<string> { "T1", "T2" }, sites: new List<string> { "L1", "L2" }))));
    }

    [Fact]
    public void Range_is_checked_against_the_farthest_site_target_pair()
    {
        // L1 -> T1 is ~160 km, L2 -> T1 is ~170 km: 165 km covers the first pair, not the second
        var wave = W(sites: new List<string> { "L1", "L2" }) with { RangeKm = 165 };
        Assert.Contains(ScenarioLoader.Validate(Make(wave)), e => e.Contains("operational range"));
        Assert.Empty(ScenarioLoader.Validate(Make(wave with { RangeKm = 200 })));
    }
}
