using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Defenses;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class FlyingDefenseTests
{
    // Lansare la (44, 24), tinta (44, 26): ~160 km, toate unitatile pleaca la t=0.
    private static Wave W(UnitKind kind = UnitKind.Drone, int count = 10, double speed = 100, double alt = 300, double apogee = 0, string id = "W1") =>
        new() { Id = id, SiteId = "L1", TargetId = "T1", Kind = kind, Count = count, SpeedMps = speed, AltitudeM = alt, ApogeeM = apogee };

    private static Defense Radar(double lat = 44.0, double lon = 25.0, double rangeKm = 100) =>
        new() { Id = "R1", Kind = DefenseKind.Radar, LatDeg = lat, LonDeg = lon, RangeKm = rangeKm };

    private static Scenario Make(Wave wave, params Defense[] defenses) => new()
    {
        Name = "fd-test",
        DtSeconds = 0.1,
        DurationSeconds = 6000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0, AltM = 50 } },
        Waves = new List<Wave> { wave },
        Defenses = defenses.ToList()
    };

    private static Simulation Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    private static double MaxAltitude(Scenario s)
    {
        var sim = new Simulation(s, 1);
        double max = 0;
        while (!sim.IsFinished)
        {
            sim.Step();
            foreach (var d in sim.Drones)
                if (d.Status == Sim.Core.Entities.EntityStatus.Flying)
                    max = Math.Max(max, d.Position.AltM);
        }
        return max;
    }

    // ---------- rachete balistice si avioane ----------

    [Fact]
    public void Ballistic_missile_follows_a_parabola_to_the_given_apogee()
    {
        double max = MaxAltitude(Make(W(UnitKind.BallisticMissile, count: 1, speed: 1000, apogee: 100_000)));

        Assert.InRange(max, 99_500, 100_100);
    }

    [Fact]
    public void Ballistic_apogee_defaults_to_a_quarter_of_the_range()
    {
        double max = MaxAltitude(Make(W(UnitKind.BallisticMissile, count: 1, speed: 1000)));

        Assert.InRange(max, 39_000, 41_000); // ~160 km => ~40 km
    }

    [Fact]
    public void Ballistic_missile_lands_at_target_altitude()
    {
        var sim = Run(Make(W(UnitKind.BallisticMissile, count: 1, speed: 1000)));

        var impact = Assert.Single(sim.Events.Events, e => e.Type == SimEventType.Impact);
        Assert.Equal(50, impact.Position.AltM, 6);
    }

    [Fact]
    public void Aircraft_fly_at_their_cruise_altitude()
    {
        double max = MaxAltitude(Make(W(UnitKind.Aircraft, count: 1, speed: 250, alt: 8000)));

        Assert.InRange(max, 7999, 8001);
    }

    // ---------- reguli de angajare ----------

    [Fact]
    public void Abm_intercepts_ballistic_missiles()
    {
        var abm = new Defense { Id = "A1", Kind = DefenseKind.Abm, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 100, Magazine = 5, Pk = 1.0, ReloadSeconds = 0 };
        var sim = Run(Make(W(UnitKind.BallisticMissile, count: 3, speed: 1500), Radar(rangeKm: 500), abm));

        Assert.Equal(3, sim.InterceptedCount);
        Assert.Equal(0, sim.ImpactCount);
    }

    [Fact]
    public void Default_sam_does_not_engage_ballistic_missiles_but_an_override_does()
    {
        var sam = new Defense { Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 100, Magazine = 5, Pk = 1.0, ReloadSeconds = 0 };
        var wave = W(UnitKind.BallisticMissile, count: 3, speed: 1500);

        var plain = Run(Make(wave, Radar(rangeKm: 500), sam));
        Assert.Equal(0, plain.ShotsFired);
        Assert.Equal(3, plain.ImpactCount);

        var withOverride = Run(Make(wave, Radar(rangeKm: 500), sam with { Engages = new List<UnitKind> { UnitKind.BallisticMissile } }));
        Assert.Equal(3, withOverride.InterceptedCount);
    }

    [Fact]
    public void Abm_does_not_engage_drones()
    {
        var abm = new Defense { Id = "A1", Kind = DefenseKind.Abm, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 50, Magazine = 5, Pk = 1.0, ReloadSeconds = 0 };
        var sim = Run(Make(W(), Radar(), abm));

        Assert.Equal(0, sim.ShotsFired);
        Assert.Equal(10, sim.ImpactCount);
    }

    [Fact]
    public void Aa_gun_has_a_ceiling_but_a_sam_reaches_high_aircraft()
    {
        var wave = W(UnitKind.Aircraft, count: 5, speed: 250, alt: 8000);
        var gun = new Defense { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 50, Magazine = 50, Pk = 1.0, ReloadSeconds = 0 };
        var sam = gun with { Id = "S1", Kind = DefenseKind.Sam };

        Assert.Equal(0, Run(Make(wave, gun)).ShotsFired);
        Assert.Equal(5, Run(Make(wave, Radar(), sam)).InterceptedCount);
        Assert.Equal(5, Run(Make(wave, gun with { MaxAltM = 10_000 })).InterceptedCount);
    }

    [Fact]
    public void Altitude_floor_excludes_low_targets()
    {
        var sam = new Defense { Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 50, Magazine = 5, Pk = 1.0, ReloadSeconds = 0, MinAltM = 1000 };
        var sim = Run(Make(W(), Radar(), sam));

        Assert.Equal(0, sim.ShotsFired);
    }

    // ---------- orizontul radar ----------

    [Fact]
    public void Radar_horizon_hides_low_flyers_but_not_ballistic_missiles()
    {
        // radar la ~167 km de traseu, raza 400 km; orizont pentru o tinta la 300 m: ~84 km
        var far = Radar(lat: 45.5, lon: 25.0, rangeKm: 400);

        Assert.Equal(0, Run(Make(W(), far)).DetectedCount);
        Assert.Equal(1, Run(Make(W(UnitKind.BallisticMissile, count: 1, speed: 1000), far)).DetectedCount);
    }

    // ---------- avioane de patrula ----------

    private static Defense Patrol(int magazine = 5, double pk = 1.0) => new()
    {
        Id = "P1", Kind = DefenseKind.Patrol, LatDeg = 44.0, LonDeg = 25.0, AltM = 6000,
        PatrolRadiusKm = 20, PatrolSpeedMps = 200, RangeKm = 30, Magazine = magazine, Pk = pk, ReloadSeconds = 0
    };

    private static Defense Airbase(double lat = 44.0, double lon = 25.0) =>
        new() { Id = "AB1", Name = "AB1", Kind = DefenseKind.Airbase, LatDeg = lat, LonDeg = lon };

    [Fact]
    public void Patrol_aircraft_engage_detected_targets_in_range()
    {
        // reincarcarea e dezactivata (timp la sol urias): testul masoara doar primul incarcator
        var patrol = Patrol(magazine: 5) with { TurnaroundSeconds = 1_000_000 };
        var sim = Run(Make(W(), Radar(), patrol, Airbase()));

        Assert.Equal(5, sim.ShotsFired);
        Assert.Equal(5, sim.InterceptedCount);
        Assert.Equal(5, sim.ImpactCount);
    }

    [Fact]
    public void Patrol_aircraft_orbit_their_center()
    {
        // fara radar nu exista tinte alocate: avionul ramane pe cerc
        var sim = new Simulation(Make(W(), Patrol(), Airbase()), 1);

        var before = Assert.Single(sim.GetFriendlies());
        for (int i = 0; i < 1000; i++)
            sim.Step(); // 100 s => 20 km pe cerc
        var after = Assert.Single(sim.GetFriendlies());

        var center = new GeoPoint(44.0, 25.0);
        Assert.Equal(0, before.Id); // indexul unitatii in scenariu
        Assert.InRange(Wgs84.GreatCircleDistanceM(center, before.Position), 19_900, 20_100);
        Assert.InRange(Wgs84.GreatCircleDistanceM(center, after.Position), 19_900, 20_100);
        Assert.InRange(Wgs84.GreatCircleDistanceM(before.Position, after.Position), 10_000, 20_100);
        Assert.Equal(6000, after.Position.AltM);
    }

    [Fact]
    public void Patrol_returns_to_base_when_out_of_ammo_rearms_and_comes_back()
    {
        var patrol = Patrol(magazine: 3) with { TurnaroundSeconds = 100 };
        var sim = Run(Make(W(), Radar(), patrol, Airbase()));

        int returning = sim.Events.Events.Count(e => e.Type == SimEventType.PatrolReturning);
        int rearmed = sim.Events.Events.Count(e => e.Type == SimEventType.PatrolRearmed);

        Assert.True(returning >= 3, $"returning={returning}");
        Assert.True(rearmed >= 2, $"rearmed={rearmed}");
        Assert.True(sim.InterceptedCount >= 9, $"intercepted={sim.InterceptedCount}"); // mai mult decat un singur incarcator
        Assert.True(sim.ShotsFired > 3);
    }

    [Fact]
    public void Patrol_returns_when_fuel_runs_out_even_without_targets()
    {
        var patrol = Patrol() with { PatrolRadiusKm = 5, EnduranceSeconds = 50, TurnaroundSeconds = 10 };
        var sim = new Simulation(Make(W(), patrol, Airbase()), 1);
        for (int i = 0; i < 3000; i++) // 300 s
            sim.Step();

        Assert.True(sim.Events.Events.Count(e => e.Type == SimEventType.PatrolReturning) >= 3);
        Assert.True(sim.Events.Events.Count(e => e.Type == SimEventType.PatrolRearmed) >= 2);
        Assert.Equal(0, sim.ShotsFired);
    }

    [Fact]
    public void Patrol_is_not_reported_while_it_is_rearming()
    {
        var patrol = Patrol() with { PatrolRadiusKm = 5, EnduranceSeconds = 50, TurnaroundSeconds = 1000 };
        var sim = new Simulation(Make(W(), patrol, Airbase()), 1);

        var seen = new List<int>();
        for (int i = 0; i < 3000; i++)
        {
            sim.Step();
            seen.Add(sim.GetFriendlies().Count);
        }

        Assert.Equal(1, seen[0]);       // la inceput e in aer
        Assert.Equal(0, seen[^1]);      // dupa intoarcere sta la sol, in reincarcare
    }

    // ---------- drone interceptoare ----------

    private static Defense Base(int magazine = 10, double maxFlightS = 600, double speed = 200) => new()
    {
        Id = "B1", Kind = DefenseKind.InterceptorBase, LatDeg = 44.0, LonDeg = 25.0,
        RangeKm = 50, Magazine = magazine, Pk = 1.0, ReloadSeconds = 0, InterceptorSpeedMps = speed, MaxFlightSeconds = maxFlightS
    };

    [Fact]
    public void Interceptor_drones_chase_and_catch_their_targets()
    {
        var sim = Run(Make(W(), Radar(), Base()));

        Assert.Equal(10, sim.InterceptorsLaunched);
        Assert.Equal(10, sim.InterceptedCount);
        Assert.Equal(0, sim.ImpactCount);
        Assert.Equal(0, sim.InterceptorsLost);

        var launches = sim.Events.Events.Where(e => e.Type == SimEventType.InterceptorLaunched).ToList();
        Assert.Equal(10, launches.Select(e => e.EntityId).Distinct().Count());
        Assert.All(launches, e => Assert.True(e.EntityId >= DefenseEngine.InterceptorIdBase));
        Assert.All(launches, e => Assert.Equal("B1", e.Code));
    }

    [Fact]
    public void Each_target_gets_a_single_chaser()
    {
        // magazin mai mare decat numarul de tinte: tot 10 lansari, nu mai multe pe aceeasi tinta
        var sim = Run(Make(W(), Radar(), Base(magazine: 30)));

        Assert.Equal(10, sim.InterceptorsLaunched);
        Assert.Equal(20, sim.MagazineLeft);
    }

    [Fact]
    public void Interceptors_that_run_out_of_flight_time_are_lost()
    {
        var sim = Run(Make(W(), Radar(), Base(magazine: 10, maxFlightS: 5)));

        Assert.Equal(10, sim.InterceptorsLaunched);
        Assert.Equal(10, sim.InterceptorsLost);
        Assert.Equal(0, sim.InterceptedCount);
        Assert.Equal(10, sim.ImpactCount);
    }

    [Fact]
    public void Active_interceptors_are_reported_for_display()
    {
        var sim = new Simulation(Make(W(), Radar(), Base()), 1);
        while (sim.InterceptorsLaunched == 0 && !sim.IsFinished)
            sim.Step();

        Assert.Contains(sim.GetFriendlies(), f => f.Id >= DefenseEngine.InterceptorIdBase);
    }

    // ---------- general ----------

    [Fact]
    public void Full_scenario_is_deterministic_and_consistent()
    {
        var s = Make(W(count: 30), Radar(), Patrol(magazine: 3, pk: 0.6), Airbase(), Base(magazine: 10) with { Pk = 0.6 });
        s = s with { Waves = new List<Wave> { W(count: 30), W(UnitKind.Aircraft, count: 5, speed: 250, alt: 6000) with { Id = "W2" } } };

        var a = Run(s, 4);
        var b = Run(s, 4);

        Assert.Equal(a.Events.Hash(), b.Events.Hash());
        Assert.NotEqual(a.Events.Hash(), Run(s, 5).Events.Hash());
        Assert.True(a.IsFinished);
        Assert.Equal(35, a.ImpactCount + a.InterceptedCount);
    }

    [Fact]
    public void Validation_rejects_bad_flying_defenses_and_apogee()
    {
        string[] Errors(Defense d) => ScenarioLoader.Validate(Make(W(), d)).ToArray();

        Assert.Contains(Errors(Patrol() with { PatrolRadiusKm = -1 }), e => e.Contains("PatrolRadiusKm"));
        Assert.Contains(Errors(Patrol() with { PatrolSpeedMps = 0 }), e => e.Contains("PatrolSpeedMps"));
        Assert.Contains(Errors(Base() with { InterceptorSpeedMps = 0 }), e => e.Contains("InterceptorSpeedMps"));
        Assert.Contains(Errors(Base() with { MaxFlightSeconds = 0 }), e => e.Contains("MaxFlightSeconds"));
        Assert.Contains(Errors(Base() with { MinAltM = 500, MaxAltM = 100 }), e => e.Contains("MinAltM"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(apogee: -5))), e => e.Contains("ApogeeM"));
        Assert.Empty(ScenarioLoader.Validate(Make(W(), Patrol(), Base(), Radar(), Airbase())));
        Assert.Contains(ScenarioLoader.Validate(Make(W(), Patrol())), e => e.Contains("baza aeriana"));
        Assert.Contains(Errors(Patrol() with { EnduranceSeconds = 0 }), e => e.Contains("EnduranceSeconds"));
        Assert.Contains(Errors(Patrol() with { ScrambleRangeKm = 0 }), e => e.Contains("ScrambleRangeKm"));
        Assert.Contains(Errors(Patrol() with { TurnaroundSeconds = -1 }), e => e.Contains("TurnaroundSeconds"));
    }

    [Fact]
    public void New_event_wire_names()
    {
        Assert.Equal("interceptorLaunched", SimEventType.InterceptorLaunched.WireName());
        Assert.Equal("interceptorLost", SimEventType.InterceptorLost.WireName());
    }
}
