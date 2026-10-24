using System.Collections.Generic;
using System.Linq;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class DoctrineTests
{
    // Lansare (44, 24) -> tinta (44, 26): ~160 km. Radar si SAM la (44, 25): drona trece prin raza lor.
    private static Wave W(UnitKind kind = UnitKind.Drone, int count = 10, double costUsd = 0, string id = "W1") =>
        new() { Id = id, SiteId = "L1", TargetId = "T1", Kind = kind, Count = count, SpeedMps = 100, AltitudeM = 300, CostUsd = costUsd };

    private static Defense Radar() =>
        new() { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 100 };

    private static Defense Sam(string id = "S1", int magazine = 100, double pk = 1.0, double shotCost = 0) =>
        new() { Id = id, Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 50, Magazine = magazine, Pk = pk, ReloadSeconds = 0, ShotCostUsd = shotCost };

    private static Scenario Make(IEnumerable<Wave> waves, bool cheapestFirst, params Defense[] defenses) => new()
    {
        Name = "doctrine-test",
        DtSeconds = 0.1,
        DurationSeconds = 6000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0, AltM = 50 } },
        Waves = waves.ToList(),
        Defenses = defenses.ToList(),
        CheapestFirst = cheapestFirst
    };

    private static Scenario Make(Wave wave, params Defense[] defenses) => Make(new[] { wave }, false, defenses);

    private static Simulation Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    // ---- cost ----

    [Fact]
    public void Expensive_shot_is_not_used_on_a_cheap_target()
    {
        var sam = Sam(shotCost: 1_000_000) with { MaxCostRatio = 1.0 };
        var sim = Run(Make(W(costUsd: 35_000), Radar(), sam));

        Assert.Equal(0, sim.ShotsFired);
        Assert.Equal(10, sim.ImpactCount);
    }

    [Fact]
    public void Cheap_enough_shot_is_used()
    {
        var sam = Sam(shotCost: 10_000) with { MaxCostRatio = 1.0 };
        var sim = Run(Make(W(costUsd: 35_000), Radar(), sam));

        Assert.Equal(10, sim.InterceptedCount);
    }

    [Fact]
    public void Unknown_costs_are_not_restricted()
    {
        var unknownTarget = Sam(shotCost: 1_000_000) with { MaxCostRatio = 1.0 };
        Assert.Equal(10, Run(Make(W(costUsd: 0), Radar(), unknownTarget)).InterceptedCount);

        var unknownShot = Sam(shotCost: 0) with { MaxCostRatio = 1.0 };
        Assert.Equal(10, Run(Make(W(costUsd: 35_000), Radar(), unknownShot)).InterceptedCount);
    }

    [Fact]
    public void Primary_kinds_ignore_the_cost_rule()
    {
        // avion = tinta principala implicita
        var sam = Sam(shotCost: 4_000_000) with { MaxCostRatio = 1.0 };
        Assert.Equal(10, Run(Make(W(UnitKind.Aircraft, costUsd: 1_000), Radar(), sam)).InterceptedCount);

        // dar si o drona devine principala daca o declari
        var droneFirst = sam with { PrimaryKinds = new List<UnitKind> { UnitKind.Drone } };
        Assert.Equal(10, Run(Make(W(costUsd: 35_000), Radar(), droneFirst)).InterceptedCount);
    }

    [Fact]
    public void Leakers_close_to_impact_are_engaged_despite_cost()
    {
        var sam = Sam(shotCost: 1_000_000) with { MaxCostRatio = 1.0 };
        var wave = W(costUsd: 35_000);

        // drona intra in raza SAM (50 km inainte de ea) cand mai are peste 100 km pana la tinta
        Assert.Equal(0, Run(Make(wave, Radar(), sam with { LeakerDistanceKm = 10 })).ShotsFired);
        Assert.Equal(10, Run(Make(wave, Radar(), sam with { LeakerDistanceKm = 500 })).InterceptedCount);
    }

    // ---- rezerva de munitie ----

    [Fact]
    public void Reserve_keeps_the_last_rounds_for_primary_targets()
    {
        var sam = Sam(magazine: 5) with { ReserveRounds = 3 };

        var drones = Run(Make(W(), Radar(), sam));
        Assert.Equal(2, drones.ShotsFired); // 5 -> 3, apoi se opreste
        Assert.Equal(3, drones.MagazineLeft);

        var aircraft = Run(Make(W(UnitKind.Aircraft), Radar(), sam));
        Assert.Equal(5, aircraft.ShotsFired); // avioanele pot folosi si rezerva
    }

    // ---- tiruri pe rand ----

    [Fact]
    public void Shoot_look_shoot_stops_at_the_first_hit()
    {
        var sam = Sam(magazine: 100, pk: 1.0) with { MaxRoundsPerTarget = 3 };
        var sim = Run(Make(W(UnitKind.Aircraft, count: 4), Radar(), sam));

        Assert.Equal(4, sim.InterceptedCount);
        Assert.Equal(4, sim.ShotsFired); // o lovitura la primul tir => un singur tir per tinta
    }

    [Fact]
    public void Several_rounds_are_fired_at_a_primary_target_in_one_cycle()
    {
        var sam = Sam(magazine: 9, pk: 0.0) with { MaxRoundsPerTarget = 3 };
        var sim = Run(Make(W(UnitKind.Aircraft, count: 1), Radar(), sam));

        var misses = sim.Events.Events.Where(e => e.Type == SimEventType.Missed).ToList();
        Assert.Equal(9, misses.Count);
        Assert.Equal(9, sim.ShotsFired);
        Assert.Equal(3, misses.GroupBy(e => e.Tick).First().Count());
    }

    [Fact]
    public void Opportunity_targets_get_a_single_round()
    {
        var sam = Sam(magazine: 9, pk: 0.0) with { MaxRoundsPerTarget = 3 };
        var sim = Run(Make(W(UnitKind.Drone, count: 1), Radar(), sam));

        var misses = sim.Events.Events.Where(e => e.Type == SimEventType.Missed).ToList();
        Assert.Equal(1, misses.GroupBy(e => e.Tick).First().Count());
    }

    // ---- prioritate si ordine ----

    [Fact]
    public void Threat_priority_prefers_the_more_dangerous_kind()
    {
        // drona (id 0) si avionul (id 1) zboara identic: la egalitate, prioritatea implicita alege id-ul mai mic
        var waves = new[] { W(UnitKind.Drone, count: 1, id: "W1"), W(UnitKind.Aircraft, count: 1, id: "W2") };
        var sam = Sam(magazine: 1);

        int First(Defense d) => Run(Make(waves, false, Radar(), d))
            .Events.Events.First(e => e.Type == SimEventType.Intercepted).EntityId;

        Assert.Equal(0, First(sam));
        Assert.Equal(1, First(sam with { Priority = TargetPriority.Threat }));
    }

    [Fact]
    public void Cheapest_first_lets_the_cheap_layer_shoot_before_the_expensive_one()
    {
        var expensive = Sam("S1", shotCost: 1_000_000);
        var cheap = new Defense { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 50, Magazine = 100, Pk = 1.0, ReloadSeconds = 0, ShotCostUsd = 1_000 };

        string Shooter(bool cheapestFirst) => Run(Make(new[] { W(count: 1) }, cheapestFirst, Radar(), expensive, cheap))
            .Events.Events.Single(e => e.Type == SimEventType.Intercepted).Code!;

        Assert.Equal("S1", Shooter(false)); // ordinea din lista
        Assert.Equal("G1", Shooter(true));
    }

    // ---- scenariu ----

    [Fact]
    public void Doctrine_is_deterministic()
    {
        var sam = Sam(magazine: 20, pk: 0.5, shotCost: 500_000) with
        {
            MaxCostRatio = 20, ReserveRounds = 4, MaxRoundsPerTarget = 2, LeakerDistanceKm = 60, Priority = TargetPriority.Threat
        };
        var s = Make(new[] { W(count: 30, costUsd: 35_000), W(UnitKind.Aircraft, count: 5, id: "W2") }, true, Radar(), sam);

        Assert.Equal(Run(s, 7).Events.Hash(), Run(s, 7).Events.Hash());
        Assert.NotEqual(Run(s, 7).Events.Hash(), Run(s, 8).Events.Hash());
    }

    [Fact]
    public void Doctrine_fields_survive_a_json_round_trip_and_hash_canonically()
    {
        var sam = Sam() with
        {
            MaxCostRatio = 3, LeakerDistanceKm = 12, ReserveRounds = 2, MaxRoundsPerTarget = 2,
            Priority = TargetPriority.Threat,
            PrimaryKinds = new List<UnitKind> { UnitKind.Aircraft, UnitKind.Drone }
        };
        var s = Make(W(), Radar(), sam) with { CheapestFirst = true };

        var back = ScenarioLoader.FromJson(ScenarioLoader.ToJson(s));
        var d = back.Defenses.Single(x => x.Id == "S1");

        Assert.True(back.CheapestFirst);
        Assert.Equal(3, d.MaxCostRatio);
        Assert.Equal(12, d.LeakerDistanceKm);
        Assert.Equal(2, d.ReserveRounds);
        Assert.Equal(2, d.MaxRoundsPerTarget);
        Assert.Equal(TargetPriority.Threat, d.Priority);
        Assert.Equal(ScenarioLoader.Hash(s), ScenarioLoader.Hash(back));

        // ordinea din PrimaryKinds nu conteaza
        var reordered = s with
        {
            Defenses = s.Defenses.Select(x => x.Id == "S1" ? x with { PrimaryKinds = new List<UnitKind> { UnitKind.Drone, UnitKind.Aircraft } } : x).ToList()
        };
        Assert.Equal(ScenarioLoader.Hash(s), ScenarioLoader.Hash(reordered));
    }

    [Fact]
    public void Invalid_doctrine_values_are_rejected()
    {
        Assert.Contains(ScenarioLoader.Validate(Make(W(), Radar(), Sam() with { MaxRoundsPerTarget = 0 })), e => e.Contains("MaxRoundsPerTarget"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(), Radar(), Sam() with { MaxCostRatio = -1 })), e => e.Contains("MaxCostRatio"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(), Radar(), Sam() with { LeakerDistanceKm = -5 })), e => e.Contains("LeakerDistanceKm"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(), Radar(), Sam() with { ReserveRounds = -1 })), e => e.Contains("ReserveRounds"));
        Assert.Empty(ScenarioLoader.Validate(Make(W(), Radar(), Sam())));
    }
}
