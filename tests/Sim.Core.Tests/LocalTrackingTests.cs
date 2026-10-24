using System.Collections.Generic;
using System.Linq;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Retea de radare: pista exista doar cat un radar intact, aflat in legatura, vede tinta.</summary>
public class LocalTrackingTests
{
    // Lansare L1 (44, 24) -> T1 (44, 26), ~160 km, 100 m/s. 1 grad de longitudine ~ 80 km la lat 44.
    private static Wave W(string id, string targetId, int count = 1, double start = 0, double warheadKg = 0) =>
        new() { Id = id, SiteId = "L1", TargetId = targetId, Count = count, SpeedMps = 100, AltitudeM = 300, StartTimeSeconds = start, WarheadKg = warheadKg };

    private static Scenario Make(bool local, IEnumerable<Wave> waves, params Defense[] defenses) => new()
    {
        Name = "local-test",
        DtSeconds = 0.1,
        DurationSeconds = 9000,
        LocalTracking = local,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target>
        {
            new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0 },
            new() { Id = "TR", Name = "TR", Kind = "military_base", LatDeg = 44.0, LonDeg = 25.0 }
        },
        Waves = waves.ToList(),
        Defenses = defenses.ToList()
    };

    private static Simulation Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    private static Defense Radar(double lon, double hardness = 1e12) =>
        new() { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = lon, RangeKm = 100, HardnessKg = hardness };

    private static Defense Sam(double linkKm = 150, double selfKm = 0) =>
        new() { Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 26.0, RangeKm = 50, Magazine = 10, Pk = 1.0, ReloadSeconds = 0, LinkRangeKm = linkKm, SelfTrackKm = selfKm, HardnessKg = 1e12 };

    [Fact]
    public void Global_detection_remembers_a_target_the_radar_has_lost_but_the_network_does_not()
    {
        // radarul de la lon 24.2 vede drona doar pana la ~lon 25.25; racheta trage de la lon 25.38
        var waves = new[] { W("W1", "T1") };

        var global = Run(Make(false, waves, Radar(24.2), Sam(linkKm: 300)));
        Assert.Equal(1, global.InterceptedCount);

        var local = Run(Make(true, waves, Radar(24.2), Sam(linkKm: 300)));
        Assert.Equal(0, local.InterceptedCount);
        Assert.Equal(1, local.ImpactCount);
        Assert.Equal(1, local.DetectedCount); // evenimentul Detected ramane unul singur per tinta
    }

    [Fact]
    public void A_radar_inside_the_link_range_gives_a_track()
    {
        // radarul la lon 25.0 (~80 km de rachete) vede drona pe tot drumul pana in raza rachetei
        var sim = Run(Make(true, new[] { W("W1", "T1") }, Radar(25.0), Sam(linkKm: 100)));

        Assert.Equal(1, sim.InterceptedCount);
    }

    [Fact]
    public void A_radar_outside_the_link_range_gives_no_track()
    {
        var sim = Run(Make(true, new[] { W("W1", "T1") }, Radar(25.0), Sam(linkKm: 50)));

        Assert.Equal(0, sim.InterceptedCount);
        Assert.Equal(1, sim.ImpactCount);
    }

    [Fact]
    public void Link_range_is_ignored_when_local_tracking_is_off()
    {
        var sim = Run(Make(false, new[] { W("W1", "T1") }, Radar(25.0), Sam(linkKm: 1)));

        Assert.Equal(1, sim.InterceptedCount);
    }

    [Fact]
    public void A_destroyed_radar_no_longer_gives_tracks()
    {
        // W1 loveste radarul (lon 25.0, in afara razei rachetei); W2 vine mult mai tarziu spre T1
        var waves = new[] { W("W1", "TR", warheadKg: 400), W("W2", "T1", start: 3000) };

        var intact = Run(Make(true, waves, Radar(25.0), Sam(linkKm: 100)));
        Assert.Equal(0, intact.DefensesDestroyed);
        Assert.Equal(1, intact.InterceptedCount); // W1 nu ajunge in raza rachetei; W2 da, si e urmarita de radar
        Assert.Equal(1, intact.ImpactCount);

        var destroyed = Run(Make(true, waves, Radar(25.0, hardness: 1), Sam(linkKm: 100)));
        Assert.Equal(1, destroyed.DefensesDestroyed);
        Assert.Equal(0, destroyed.InterceptedCount);
        Assert.Equal(2, destroyed.ImpactCount);
    }

    [Fact]
    public void An_own_radar_tracks_without_any_network()
    {
        var waves = new[] { W("W1", "T1") };

        Assert.Equal(0, Run(Make(true, waves, Sam())).InterceptedCount);
        Assert.Equal(1, Run(Make(true, waves, Sam(selfKm: 50))).InterceptedCount);
    }

    [Fact]
    public void Guns_do_not_need_a_track()
    {
        var gun = new Defense { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 25.9, RangeKm = 20, Magazine = 100, Pk = 1.0, ReloadSeconds = 0 };

        Assert.Equal(1, Run(Make(true, new[] { W("W1", "T1") }, gun)).InterceptedCount);
    }

    [Fact]
    public void Local_tracking_is_deterministic()
    {
        var s = Make(true, new[] { W("W1", "T1", count: 40) with { SpreadMeters = 500 } }, Radar(25.0), Sam(linkKm: 100) with { Pk = 0.6 });

        Assert.Equal(Run(s, 7).Events.Hash(), Run(s, 7).Events.Hash());
    }

    [Fact]
    public void New_fields_survive_a_json_round_trip_and_change_the_identity()
    {
        var s = Make(true, new[] { W("W1", "T1") }, Radar(25.0), Sam(linkKm: 90, selfKm: 20));
        var back = ScenarioLoader.FromJson(ScenarioLoader.ToJson(s));

        Assert.True(back.LocalTracking);
        Assert.Equal(90, back.Defenses.Single(d => d.Id == "S1").LinkRangeKm);
        Assert.Equal(20, back.Defenses.Single(d => d.Id == "S1").SelfTrackKm);
        Assert.Equal(ScenarioLoader.Hash(s), ScenarioLoader.Hash(back));
        Assert.NotEqual(ScenarioLoader.Hash(s), ScenarioLoader.Hash(s with { LocalTracking = false }));
    }

    [Fact]
    public void Negative_ranges_are_rejected()
    {
        var waves = new[] { W("W1", "T1") };

        Assert.Contains(ScenarioLoader.Validate(Make(true, waves, Sam(linkKm: -1))), e => e.Contains("LinkRangeKm"));
        Assert.Contains(ScenarioLoader.Validate(Make(true, waves, Sam(selfKm: -1))), e => e.Contains("SelfTrackKm"));
        Assert.Empty(ScenarioLoader.Validate(Make(true, waves, Radar(25.0), Sam())));
    }
}
