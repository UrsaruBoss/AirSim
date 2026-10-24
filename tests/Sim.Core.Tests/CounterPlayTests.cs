using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Contra-jocul atacatorului: un impact langa un sistem defensiv il poate distruge.</summary>
public class CounterPlayTests
{
    // Lansare L1 (44, 24). Tinte: T1 (44, 26) si TA (44, 24.5) - aceasta din urma e pozitia bazei aeriene.
    private static Wave W(string id, string targetId, int count = 1, double start = 0, double warheadKg = 400) =>
        new() { Id = id, SiteId = "L1", TargetId = targetId, Count = count, SpeedMps = 100, AltitudeM = 300, StartTimeSeconds = start, WarheadKg = warheadKg };

    private static Scenario Make(IEnumerable<Wave> waves, params Defense[] defenses) => new()
    {
        Name = "counter-test",
        DtSeconds = 0.1,
        DurationSeconds = 9000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target>
        {
            new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0, AltM = 50 },
            new() { Id = "TA", Name = "TA", Kind = "military_base", LatDeg = 44.0, LonDeg = 24.5 }
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

    private static Defense Radar(string id = "R1", double lat = 44.0, double lon = 26.0, double hardness = 1) =>
        new() { Id = id, Kind = DefenseKind.Radar, LatDeg = lat, LonDeg = lon, RangeKm = 100, HardnessKg = hardness };

    [Fact]
    public void Impact_on_a_defense_destroys_it()
    {
        var sim = Run(Make(new[] { W("W1", "T1") }, Radar()));

        var e = Assert.Single(sim.Events.Events, x => x.Type == SimEventType.DefenseDestroyed);
        Assert.Equal("R1", e.Code);
        Assert.Equal(0, e.EntityId);
        Assert.Equal(1, sim.DefensesDestroyed);
    }

    [Fact]
    public void Defense_outside_the_lethal_radius_survives()
    {
        // ~5,5 km de punctul de impact
        var sim = Run(Make(new[] { W("W1", "T1") }, Radar(lat: 44.05)));

        Assert.Equal(0, sim.DefensesDestroyed);
        Assert.DoesNotContain(sim.Events.Events, x => x.Type == SimEventType.DefenseDestroyed);
    }

    [Fact]
    public void A_big_defense_is_hit_from_further_away()
    {
        // 300 m de impact: un radar (30 m + raza letala ~44 m) scapa, o baza aeriana (600 m) nu
        double lat = 44.0 + 300.0 / 111_000.0;
        Assert.Equal(0, Run(Make(new[] { W("W1", "T1") }, Radar(lat: lat))).DefensesDestroyed);

        var airbase = new Defense { Id = "AB1", Kind = DefenseKind.Airbase, LatDeg = lat, LonDeg = 26.0, HardnessKg = 1 };
        Assert.Equal(1, Run(Make(new[] { W("W1", "T1") }, airbase)).DefensesDestroyed);
    }

    [Fact]
    public void Hardness_decides_the_outcome()
    {
        var soft = Run(Make(new[] { W("W1", "T1") }, Radar(hardness: 1)));
        var hard = Run(Make(new[] { W("W1", "T1") }, Radar(hardness: 1e12)));

        Assert.Equal(1, soft.DefensesDestroyed);
        Assert.Equal(0, hard.DefensesDestroyed);
    }

    [Fact]
    public void A_small_warhead_rarely_destroys_a_hard_defense()
    {
        var weak = W("W1", "T1", count: 300, warheadKg: 1);
        var radar = Radar(hardness: 1e9);
        Assert.Equal(0, Run(Make(new[] { weak }, radar)).DefensesDestroyed);
    }

    [Fact]
    public void Destroyed_radar_stops_detecting()
    {
        // primul val loveste radarul (la 44, 25), al doilea trece pe langa el mult mai tarziu
        var radar = Radar(lon: 25.0, hardness: 1);
        var hitRadar = new Target { Id = "TR", Name = "TR", Kind = "military_base", LatDeg = 44.0, LonDeg = 25.0 };

        var s = Make(new[] { W("W1", "TR"), W("W2", "T1", count: 5, start: 2000) }, radar);
        s = s with { Targets = s.Targets.Append(hitRadar).ToList() };

        var destroyed = Run(s);
        Assert.Equal(1, destroyed.DefensesDestroyed);
        Assert.Equal(1, destroyed.DetectedCount); // doar drona care l-a lovit

        var intact = Run(s with { Defenses = new List<Defense> { radar with { HardnessKg = 1e12 } } });
        Assert.Equal(6, intact.DetectedCount);
    }

    [Fact]
    public void Destroyed_shooter_loses_its_magazine()
    {
        var sam = new Defense { Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 26.0, RangeKm = 50, Magazine = 20, HardnessKg = 1 };
        var sim = Run(Make(new[] { W("W1", "T1") }, sam));

        Assert.Equal(1, sim.DefensesDestroyed);
        Assert.Equal(0, sim.MagazineLeft);
    }

    [Fact]
    public void Patrol_cannot_be_destroyed()
    {
        var patrol = new Defense { Id = "P1", Kind = DefenseKind.Patrol, LatDeg = 44.0, LonDeg = 26.0, RangeKm = 30, Magazine = 1, HardnessKg = 1 };
        var airbase = new Defense { Id = "AB1", Kind = DefenseKind.Airbase, LatDeg = 44.0, LonDeg = 20.0 };
        var sim = Run(Make(new[] { W("W1", "T1") }, patrol, airbase));

        Assert.Equal(0, sim.DefensesDestroyed);
    }

    // ---- baza aeriana distrusa => avionul de patrula ramane fara baza ----

    private static Simulation PatrolRun(double airbaseHardness)
    {
        var radar = new Defense { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 100, HardnessKg = 1e12 };
        var patrol = new Defense
        {
            Id = "P1", Kind = DefenseKind.Patrol, LatDeg = 44.0, LonDeg = 25.0, AltM = 6000, PatrolRadiusKm = 0, PatrolSpeedMps = 200,
            RangeKm = 30, ScrambleRangeKm = 30, Magazine = 1, Pk = 1.0, ReloadSeconds = 0, TurnaroundSeconds = 10
        };
        var airbase = new Defense { Id = "AB1", Kind = DefenseKind.Airbase, LatDeg = 44.0, LonDeg = 24.5, HardnessKg = airbaseHardness };

        // W1 loveste baza (t ~ 400 s); W2 si W3 trec pe langa patrula, mult mai tarziu
        var waves = new[] { W("W1", "TA"), W("W2", "T1", start: 2000), W("W3", "T1", start: 5000) };
        return Run(Make(waves, radar, patrol, airbase));
    }

    [Fact]
    public void Patrol_with_an_intact_airbase_rearms_and_engages_again()
    {
        var sim = PatrolRun(airbaseHardness: 1e12);

        Assert.Equal(0, sim.DefensesDestroyed);
        Assert.Equal(2, sim.InterceptedCount);
        Assert.Contains(sim.Events.Events, e => e.Type == SimEventType.PatrolRearmed);
    }

    [Fact]
    public void Patrol_is_stranded_when_its_airbase_is_destroyed()
    {
        var sim = PatrolRun(airbaseHardness: 1);

        Assert.Equal(1, sim.DefensesDestroyed);
        Assert.Equal(1, sim.InterceptedCount); // doar W2; dupa aceea nu mai are unde sa se reincarce
        Assert.DoesNotContain(sim.Events.Events, e => e.Type == SimEventType.PatrolRearmed);
    }

    // ---- statistici, validare, determinism ----

    [Fact]
    public void Summary_marks_destroyed_defenses()
    {
        var s = Make(new[] { W("W1", "T1") }, Radar(), Radar("R2", lat: 45.0, lon: 25.0));
        var sim = Run(s);
        var summary = RunAnalyzer.Analyze(s, sim.Events.Events, 1, 10);

        Assert.True(summary.Defenses.Single(d => d.Id == "R1").Destroyed);
        Assert.False(summary.Defenses.Single(d => d.Id == "R2").Destroyed);
    }

    [Fact]
    public void Destruction_is_deterministic()
    {
        var s = Make(new[] { W("W1", "T1", count: 50, warheadKg: 40) }, Radar(hardness: 100));

        Assert.Equal(Run(s, 3).Events.Hash(), Run(s, 3).Events.Hash());
    }

    [Fact]
    public void Invalid_vulnerability_values_are_rejected()
    {
        var s = Make(new[] { W("W1", "T1") }, Radar(hardness: -1));
        Assert.Contains(ScenarioLoader.Validate(s), e => e.Contains("HardnessKg"));

        s = Make(new[] { W("W1", "T1") }, Radar() with { SizeM = -5 });
        Assert.Contains(ScenarioLoader.Validate(s), e => e.Contains("SizeM"));
    }
}
