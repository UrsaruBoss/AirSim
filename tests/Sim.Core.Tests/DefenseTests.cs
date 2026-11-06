using System.Collections.Generic;
using System.Linq;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class DefenseTests
{
    // Drones from (44, 24) to (44, 26), ~160 km at 100 m/s => ~1600 s of flight, all launched at t=0.
    // Defense at (44, 25): the drone passes through its range.
    private static Defense Radar(string id = "R1", double rangeKm = 100, double lon = 25.0) =>
        new() { Id = id, Name = id, Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = lon, RangeKm = rangeKm };

    private static Defense Sam(string id = "S1", int magazine = 5, double pk = 1.0, double reload = 0, double rangeKm = 50, double lon = 25.0) =>
        new() { Id = id, Name = id, Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = lon, RangeKm = rangeKm, Magazine = magazine, Pk = pk, ReloadSeconds = reload };

    private static Scenario Make(int drones, params Defense[] defenses) => new()
    {
        Name = "def-test",
        DtSeconds = 0.1,
        DurationSeconds = 3000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0, AltM = 50 } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = drones, SpeedMps = 100, AltitudeM = 300, SpreadMeters = 0 }
        },
        Defenses = defenses.ToList()
    };

    private static Simulation Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    private static int Count(Simulation sim, SimEventType type) => sim.Events.Events.Count(e => e.Type == type);

    [Fact]
    public void Without_defenses_nothing_is_intercepted()
    {
        var sim = Run(Make(10));

        Assert.Equal(10, sim.ImpactCount);
        Assert.Equal(0, sim.InterceptedCount);
        Assert.Equal(0, sim.ShotsFired);
        Assert.Equal(0, sim.DetectedCount);
    }

    [Fact]
    public void Radar_detects_each_drone_once()
    {
        var sim = Run(Make(10, Radar()));

        Assert.Equal(10, sim.DetectedCount);
        Assert.Equal(10, Count(sim, SimEventType.Detected));
        Assert.Equal(10, sim.Events.Events.Where(e => e.Type == SimEventType.Detected).Select(e => e.EntityId).Distinct().Count());
        Assert.All(sim.Events.Events.Where(e => e.Type == SimEventType.Detected), e => Assert.Equal("R1", e.Code));
        Assert.Equal(10, sim.ImpactCount);
    }

    [Fact]
    public void Sam_without_radar_does_not_fire()
    {
        var sim = Run(Make(10, Sam()));

        Assert.Equal(0, sim.ShotsFired);
        Assert.Equal(10, sim.ImpactCount);
    }

    [Fact]
    public void Aa_gun_fires_without_radar()
    {
        var gun = new Defense { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 50, Magazine = 100, Pk = 1.0, ReloadSeconds = 0 };
        var sim = Run(Make(10, gun));

        Assert.Equal(10, sim.InterceptedCount);
        Assert.Equal(0, sim.ImpactCount);
    }

    [Fact]
    public void Magazine_limits_interceptions()
    {
        var sim = Run(Make(10, Radar(), Sam(magazine: 3, pk: 1.0)));

        Assert.Equal(3, sim.ShotsFired);
        Assert.Equal(3, sim.InterceptedCount);
        Assert.Equal(7, sim.ImpactCount);
        Assert.Equal(0, sim.MagazineLeft);
    }

    [Fact]
    public void Perfect_defense_stops_everything_when_magazine_is_enough()
    {
        var sim = Run(Make(10, Radar(), Sam(magazine: 20, pk: 1.0)));

        Assert.Equal(10, sim.InterceptedCount);
        Assert.Equal(0, sim.ImpactCount);
        Assert.Equal(10, sim.ShotsFired);
        Assert.Equal(10, sim.MagazineLeft);
        Assert.True(sim.IsFinished);
    }

    [Fact]
    public void Zero_pk_always_misses_and_uses_the_magazine()
    {
        var sim = Run(Make(10, Radar(), Sam(magazine: 4, pk: 0.0)));

        Assert.Equal(4, sim.ShotsFired);
        Assert.Equal(0, sim.InterceptedCount);
        Assert.Equal(4, Count(sim, SimEventType.Missed));
        Assert.Equal(10, sim.ImpactCount);
    }

    [Fact]
    public void Reload_limits_the_rate_of_fire()
    {
        // a window of ~50 km at 100 m/s = ~1000 s; with a 400 s reload => at most 3 shots
        var sim = Run(Make(10, Radar(), Sam(magazine: 50, pk: 1.0, reload: 400)));

        Assert.InRange(sim.ShotsFired, 2, 3);
    }

    [Fact]
    public void Out_of_range_defenses_do_nothing()
    {
        var sim = Run(Make(10, Radar(lon: 10.0), Sam(lon: 10.0)));

        Assert.Equal(0, sim.DetectedCount);
        Assert.Equal(0, sim.ShotsFired);
        Assert.Equal(10, sim.ImpactCount);
    }

    [Fact]
    public void Every_drone_is_either_impacted_or_intercepted()
    {
        var sim = Run(Make(50, Radar(), Sam(magazine: 20, pk: 0.6)), seed: 7);

        Assert.True(sim.IsFinished);
        Assert.Equal(50, sim.ImpactCount + sim.InterceptedCount);
        Assert.Equal(sim.InterceptedCount, Count(sim, SimEventType.Intercepted));
        Assert.Equal(sim.ShotsFired, Count(sim, SimEventType.Intercepted) + Count(sim, SimEventType.Missed));
        Assert.Equal(sim.ImpactCount, Count(sim, SimEventType.Impact));
        Assert.Equal(0, sim.RemainingCount);
    }

    [Fact]
    public void Same_seed_gives_identical_logs_and_different_seed_differs()
    {
        var s = Make(50, Radar(), Sam(magazine: 20, pk: 0.5));

        Assert.Equal(Run(s, 5).Events.Hash(), Run(s, 5).Events.Hash());
        Assert.NotEqual(Run(s, 5).Events.Hash(), Run(s, 6).Events.Hash());
    }

    [Fact]
    public void Defense_changes_the_event_log_hash()
    {
        Assert.NotEqual(Run(Make(10)).Events.Hash(), Run(Make(10, Radar())).Events.Hash());
    }

    [Fact]
    public void Defense_ids_with_the_same_prefix_give_different_hashes()
    {
        var a = Run(Make(10, Radar("RADAR-1")));
        var b = Run(Make(10, Radar("RADAR-2")));

        Assert.NotEqual(a.Events.Hash(), b.Events.Hash());
    }

    [Fact]
    public void Intercepted_drone_stops_flying_and_never_impacts()
    {
        var sim = Run(Make(5, Radar(), Sam(magazine: 5, pk: 1.0)));

        var hit = sim.Events.Events.Where(e => e.Type == SimEventType.Intercepted).Select(e => e.EntityId).ToHashSet();
        Assert.Equal(5, hit.Count);
        Assert.DoesNotContain(sim.Events.Events, e => e.Type == SimEventType.Impact && hit.Contains(e.EntityId));
    }

    [Fact]
    public void Validation_rejects_bad_defenses()
    {
        string[] Errors(params Defense[] d) => ScenarioLoader.Validate(Make(1, d)).ToArray();

        Assert.Contains(Errors(Sam("S1"), Sam("S1")), e => e.Contains("duplicate"));
        Assert.Contains(Errors(Sam(pk: 1.5)), e => e.Contains("Pk"));
        Assert.Contains(Errors(Sam(magazine: -1)), e => e.Contains("Magazine"));
        Assert.Contains(Errors(Sam(rangeKm: 0)), e => e.Contains("RangeKm"));
        Assert.Contains(Errors(Sam(reload: -1)), e => e.Contains("ReloadSeconds"));
        Assert.Empty(Errors(Radar(), Sam()));
    }

    [Fact]
    public void Scenario_json_round_trips_defenses()
    {
        const string json = """
            { "name": "x", "launchSites": [], "targets": [], "waves": [],
              "defenses": [ { "id": "S1", "kind": "sam", "latDeg": 44, "lonDeg": 25, "rangeKm": 30, "magazine": 8, "pk": 0.8, "reloadSeconds": 3 } ] }
            """;
        var s = ScenarioLoader.FromJson(json);

        Assert.Single(s.Defenses);
        Assert.Equal(DefenseKind.Sam, s.Defenses[0].Kind);
        Assert.Equal(8, s.Defenses[0].Magazine);
        Assert.Equal(ScenarioLoader.Hash(s), ScenarioLoader.Hash(ScenarioLoader.FromJson(ScenarioLoader.ToCanonicalJson(s))));
    }

    [Fact]
    public void Defense_event_wire_names()
    {
        Assert.Equal("detected", SimEventType.Detected.WireName());
        Assert.Equal("intercepted", SimEventType.Intercepted.WireName());
        Assert.Equal("missed", SimEventType.Missed.WireName());
    }
}
