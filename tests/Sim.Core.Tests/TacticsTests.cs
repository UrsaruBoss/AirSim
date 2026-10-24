using System.Collections.Generic;
using System.Linq;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Momeli, redirectionare in zbor si saturarea bruiajului.</summary>
public class TacticsTests
{
    // L1 (44, 24) -> T1 (44, 26), T2 (44.1, 26), T3 (44.2, 26); ~160 km, 100 m/s => ~1600 s.
    private static Scenario Make(IEnumerable<Wave> waves, IEnumerable<Defense>? defenses = null, int t1Destroy = 0) => new()
    {
        Name = "tactics-test",
        DtSeconds = 0.5,
        DurationSeconds = 9000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target>
        {
            new() { Id = "T1", Name = "T1", Kind = "military_base", LatDeg = 44.0, LonDeg = 26.0, ImpactsToDestroy = t1Destroy },
            new() { Id = "T2", Name = "T2", Kind = "city", LatDeg = 44.1, LonDeg = 26.0 },
            new() { Id = "T3", Name = "T3", Kind = "city", LatDeg = 44.2, LonDeg = 26.0 }
        },
        Waves = waves.ToList(),
        Defenses = (defenses ?? System.Array.Empty<Defense>()).ToList()
    };

    private static Wave W(string id = "W1", int count = 1, double start = 0, string target = "T1") =>
        new() { Id = id, SiteId = "L1", TargetId = target, Count = count, StartTimeSeconds = start, SpeedMps = 100, AltitudeM = 300, WarheadKg = 0 };

    private static Simulation Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    // ---- momeli ----

    private static Defense Gun() =>
        new() { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 25.9, RangeKm = 20, Magazine = 100, Pk = 1.0, ReloadSeconds = 0 };

    [Fact]
    public void Shots_at_decoys_are_counted()
    {
        var decoy = Run(Make(new[] { W(count: 5) with { Decoy = true } }, new[] { Gun() }));
        Assert.Equal(5, decoy.DecoyShots);
        Assert.Equal(5, decoy.DecoysIntercepted);

        var real = Run(Make(new[] { W(count: 5) }, new[] { Gun() }));
        Assert.Equal(0, real.DecoyShots);
        Assert.Equal(5, real.InterceptedCount);
    }

    [Fact]
    public void A_cheap_decoy_that_looks_expensive_draws_expensive_shots()
    {
        var radar = new Defense { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 25.5, RangeKm = 100 };
        var sam = new Defense
        {
            Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 26.0, RangeKm = 50, Magazine = 10, Pk = 1.0, ReloadSeconds = 0,
            ShotCostUsd = 1_000_000, MaxCostRatio = 2
        };
        var cheap = W(count: 1) with { CostUsd = 10_000 };

        // costul real (10 000) e prea mic pentru o racheta de 1 000 000 => nu trage
        Assert.Equal(0, Run(Make(new[] { cheap }, new Defense[] { radar, sam })).InterceptedCount);

        // acelasi lucru, dar arata ca o arma de 600 000 => trage
        var disguised = cheap with { PerceivedCostUsd = 600_000 };
        var sim = Run(Make(new[] { disguised }, new Defense[] { radar, sam }));
        Assert.Equal(1, sim.InterceptedCount);
        Assert.Equal(1, sim.ShotsFired);
    }

    // ---- redirectionare ----

    private static Scenario Retargeting(bool retarget, int t1Destroy = 2, double rangeKm = 150) => Make(new[]
    {
        W("W1", count: 2, target: "T1"),
        W("W2", count: 60, start: 1000) with { TargetIds = new List<string> { "T1", "T2", "T3" }, Retarget = retarget, RetargetRangeKm = rangeKm }
    }, t1Destroy: t1Destroy);

    private static int ImpactsAt(Simulation sim, double lat) =>
        sim.Events.Events.Count(e => e.Type == SimEventType.Impact && System.Math.Abs(e.Position.LatDeg - lat) < 0.01);

    [Fact]
    public void Units_leave_a_destroyed_target()
    {
        var sim = Run(Retargeting(retarget: true));

        Assert.Equal(2, ImpactsAt(sim, 44.0)); // doar primele doua, cele care au distrus-o
        Assert.True(sim.RetargetedCount > 5);
        Assert.Equal(62, sim.ImpactCount);

        var events = sim.Events.Events.Where(e => e.Type == SimEventType.Retargeted).ToList();
        Assert.Equal(sim.RetargetedCount, events.Count);
        Assert.All(events, e => Assert.Contains(e.Code, new[] { "T2", "T3" }));
    }

    [Fact]
    public void Without_the_flag_units_keep_hitting_the_destroyed_target()
    {
        var sim = Run(Retargeting(retarget: false));

        Assert.Equal(0, sim.RetargetedCount);
        Assert.True(ImpactsAt(sim, 44.0) > 10);
    }

    [Fact]
    public void A_target_that_cannot_be_destroyed_is_never_abandoned()
    {
        Assert.Equal(0, Run(Retargeting(retarget: true, t1Destroy: 0)).RetargetedCount);
    }

    [Fact]
    public void No_alternative_in_range_means_no_retargeting()
    {
        var sim = Run(Retargeting(retarget: true, rangeKm: 1));

        Assert.Equal(0, sim.RetargetedCount);
        Assert.True(ImpactsAt(sim, 44.0) > 10);
    }

    [Fact]
    public void Retargeting_is_deterministic()
    {
        var s = Retargeting(retarget: true);

        Assert.Equal(Run(s, 4).Events.Hash(), Run(s, 4).Events.Hash());
    }

    // ---- saturarea bruiajului ----

    private static Scenario JamScenario(int capacity) => Make(
        new[] { W(count: 100) },
        new[] { new Defense { Id = "E1", Kind = DefenseKind.Ew, LatDeg = 44.0, LonDeg = 24.5, RangeKm = 30, JamRatePerMinute = 2.0, JamCapacity = capacity } });

    [Fact]
    public void Jammers_saturate_when_too_many_units_are_in_range()
    {
        int unlimited = Run(JamScenario(0)).JammedCount;
        int saturated = Run(JamScenario(1)).JammedCount;

        Assert.True(unlimited >= 95, $"unlimited={unlimited}");
        Assert.InRange(saturated, 1, 40);
    }

    [Fact]
    public void Capacity_above_the_traffic_changes_nothing()
    {
        var a = Run(JamScenario(0), 2);
        var b = Run(JamScenario(1000), 2);

        Assert.Equal(a.Events.Hash(), b.Events.Hash());
    }

    // ---- diagnostice ----

    [Fact]
    public void Shot_costs_are_tracked_in_total_and_on_decoys()
    {
        var gun = Gun() with { ShotCostUsd = 100 };
        var real = W("W1", count: 3) with { CostUsd = 1000 };
        var decoy = W("W2", count: 5) with { CostUsd = 400, Decoy = true };
        var sim = Run(Make(new[] { real, decoy }, new[] { gun }));

        Assert.Equal(8, sim.InterceptedCount);
        Assert.Equal(800, sim.DefenseSpentUsd, 6);
        Assert.Equal(500, sim.DecoySpentUsd, 6);
        Assert.Equal(3 * 1000 + 5 * 400, sim.AttackCostUsd, 6);
    }

    [Fact]
    public void Saturation_is_measured()
    {
        var saturated = Run(JamScenario(1));
        Assert.True(saturated.EwActiveCycles > 0);
        Assert.True(saturated.EwSaturatedCycles > 0);
        Assert.True(saturated.EwSaturatedCycles <= saturated.EwActiveCycles);
        Assert.True(saturated.EwPeakLoad >= 50, $"peak={saturated.EwPeakLoad}");

        var unlimited = Run(JamScenario(0));
        Assert.Equal(0, unlimited.EwActiveCycles);
        Assert.Equal(0, unlimited.EwPeakLoad);
    }

    // ---- validare ----

    [Fact]
    public void Invalid_values_are_rejected()
    {
        Assert.Contains(ScenarioLoader.Validate(JamScenario(-1)), e => e.Contains("JamCapacity"));
        Assert.Contains(ScenarioLoader.Validate(Make(new[] { W() with { PerceivedCostUsd = -1 } })), e => e.Contains("PerceivedCostUsd"));
        Assert.Contains(ScenarioLoader.Validate(Make(new[] { W() with { Retarget = true, RetargetAtKm = 0 } })), e => e.Contains("RetargetAtKm"));
        Assert.Contains(ScenarioLoader.Validate(Make(new[] { W() with { Retarget = true, Kind = UnitKind.BallisticMissile } })), e => e.Contains("balistice"));
        Assert.Contains(ScenarioLoader.Validate(Make(new[] { W() }, t1Destroy: -1)), e => e.Contains("ImpactsToDestroy"));
    }

    [Fact]
    public void New_fields_survive_a_json_round_trip()
    {
        var s = Make(new[] { W() with { Decoy = true, PerceivedCostUsd = 5, Retarget = true, RetargetAtKm = 30, RetargetRangeKm = 90 } }, t1Destroy: 3);
        var back = ScenarioLoader.FromJson(ScenarioLoader.ToJson(s));

        Assert.True(back.Waves[0].Decoy);
        Assert.Equal(5, back.Waves[0].PerceivedCostUsd);
        Assert.True(back.Waves[0].Retarget);
        Assert.Equal(30, back.Waves[0].RetargetAtKm);
        Assert.Equal(90, back.Waves[0].RetargetRangeKm);
        Assert.Equal(3, back.Targets[0].ImpactsToDestroy);
        Assert.Equal(ScenarioLoader.Hash(s), ScenarioLoader.Hash(back));
    }
}
