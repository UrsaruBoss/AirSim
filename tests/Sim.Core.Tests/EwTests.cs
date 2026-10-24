using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Razboi electronic: bruiaj care face unitatile sa-si piarda ghidarea.</summary>
public class EwTests
{
    // Lansare (44, 24) -> tinta (44, 26), ~160 km la 100 m/s. Bruiajul e la (44, 25).
    private static Wave W(UnitKind kind = UnitKind.Drone, int count = 20, double resistance = 0) => new()
    {
        Id = "W1", SiteId = "L1", TargetId = "T1", Kind = kind, Count = count, SpeedMps = 100, AltitudeM = 300, EwResistance = resistance
    };

    private static Defense Ew(double ratePerMin = 60, double rangeKm = 50, double lat = 44.0) =>
        new() { Id = "E1", Kind = DefenseKind.Ew, LatDeg = lat, LonDeg = 25.0, RangeKm = rangeKm, JamRatePerMinute = ratePerMin };

    private static Scenario Make(Wave wave, params Defense[] defenses) => new()
    {
        Name = "ew-test",
        DtSeconds = 0.1,
        DurationSeconds = 6000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0 } },
        Waves = new List<Wave> { wave },
        Defenses = defenses.ToList()
    };

    private static Simulation Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    [Fact]
    public void Strong_jamming_stops_every_drone_in_range()
    {
        var sim = Run(Make(W(), Ew()));

        Assert.Equal(20, sim.JammedCount);
        Assert.Equal(0, sim.ImpactCount);
        Assert.Equal(20, sim.Events.Events.Count(e => e.Type == SimEventType.Jammed && e.Code == "E1"));
        Assert.DoesNotContain(sim.Events.Events, e => e.Type == SimEventType.Impact);
    }

    [Fact]
    public void Jammed_units_fall_inside_the_jamming_range()
    {
        var sim = Run(Make(W(), Ew(rangeKm: 30)));

        // 30 km in jurul lon 25: aproximativ lon 24.625 - 25.375
        Assert.All(sim.Events.Events.Where(e => e.Type == SimEventType.Jammed), e => Assert.InRange(e.Position.LonDeg, 24.62, 25.38));
    }

    [Fact]
    public void Nothing_is_jammed_outside_the_range()
    {
        var sim = Run(Make(W(), Ew(lat: 45.0, rangeKm: 20)));

        Assert.Equal(0, sim.JammedCount);
        Assert.Equal(20, sim.ImpactCount);
    }

    [Fact]
    public void Resistance_reduces_the_effect()
    {
        var sensitive = Run(Make(W(count: 400, resistance: 0), Ew(ratePerMin: 0.5, rangeKm: 20)));
        var resistant = Run(Make(W(count: 400, resistance: 0.9), Ew(ratePerMin: 0.5, rangeKm: 20)));
        var immune = Run(Make(W(count: 400, resistance: 1.0), Ew(ratePerMin: 60, rangeKm: 50)));

        Assert.True(sensitive.JammedCount > 300);
        Assert.True(resistant.JammedCount < sensitive.JammedCount / 2);
        Assert.Equal(0, immune.JammedCount);
    }

    [Theory]
    [InlineData(UnitKind.Aircraft)]
    [InlineData(UnitKind.BallisticMissile)]
    public void Piloted_and_ballistic_units_are_not_jammed(UnitKind kind)
    {
        var sim = Run(Make(W(kind, count: 5), Ew()));

        Assert.Equal(0, sim.JammedCount);
    }

    [Fact]
    public void Cruise_missiles_are_jammed_by_default()
    {
        Assert.Equal(5, Run(Make(W(UnitKind.CruiseMissile, count: 5), Ew())).JammedCount);
    }

    [Fact]
    public void Every_unit_is_accounted_for_exactly_once()
    {
        var wave = W(count: 300) with { FailureRate = 0.1 };
        var gun = new Defense { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 24.5, RangeKm = 20, Magazine = 50, Pk = 0.5, ReloadSeconds = 0 };
        var sim = Run(Make(wave, Ew(ratePerMin: 0.3, rangeKm: 20), gun));

        Assert.True(sim.JammedCount > 0 && sim.InterceptedCount > 0 && sim.CrashedCount > 0);
        Assert.Equal(300, sim.JammedCount + sim.InterceptedCount + sim.CrashedCount + sim.ImpactCount);
    }

    [Fact]
    public void Ew_needs_no_radar_and_uses_no_ammo()
    {
        var sim = Run(Make(W(), Ew()));

        Assert.Equal(0, sim.ShotsFired);
        Assert.Equal(0, sim.DetectedCount);
        Assert.Equal(0, sim.MagazineLeft);
    }

    [Fact]
    public void A_destroyed_ew_system_stops_jamming()
    {
        // al doilea val loveste sistemul de bruiaj (la 44, 25) inainte sa treaca primul val
        var targets = new List<Target>
        {
            new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0 },
            new() { Id = "TE", Name = "TE", Kind = "military_base", LatDeg = 44.0, LonDeg = 25.0 }
        };
        var strike = W(count: 1) with { Id = "W0", TargetId = "TE", WarheadKg = 400, SpeedMps = 1000, EwResistance = 1.0 };
        var main = W(count: 20) with { StartTimeSeconds = 300 };
        var s = Make(main, Ew() with { HardnessKg = 1 }) with { Targets = targets, Waves = new List<Wave> { strike, main } };

        var sim = Run(s);
        Assert.Equal(1, sim.DefensesDestroyed);
        Assert.Equal(0, sim.JammedCount);
        Assert.Equal(20, sim.ImpactCount - 1); // + lovitura care a distrus sistemul
    }

    [Fact]
    public void Summary_counts_jammed_units_per_system()
    {
        var s = Make(W(count: 50), Ew());
        var sim = Run(s);
        var summary = RunAnalyzer.Analyze(s, sim.Events.Events, 1, 10);

        Assert.Equal(sim.JammedCount, summary.Jammed);
        var stat = summary.Defenses.Single(d => d.Id == "E1");
        Assert.Equal(sim.JammedCount, stat.Hits);
        Assert.Equal(0, stat.MagazineStart);
    }

    [Fact]
    public void Invalid_ew_values_are_rejected()
    {
        Assert.Contains(ScenarioLoader.Validate(Make(W(), Ew(ratePerMin: 0))), e => e.Contains("JamRatePerMinute"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(), Ew(ratePerMin: -1))), e => e.Contains("JamRatePerMinute"));
        Assert.Contains(ScenarioLoader.Validate(Make(W(resistance: 1.5), Ew())), e => e.Contains("EwResistance"));
        Assert.Empty(ScenarioLoader.Validate(Make(W(resistance: 0.5), Ew())));
    }

    [Fact]
    public void Jamming_is_deterministic()
    {
        var s = Make(W(count: 200), Ew(ratePerMin: 0.4, rangeKm: 20));

        Assert.Equal(Run(s, 9).Events.Hash(), Run(s, 9).Events.Hash());
        Assert.NotEqual(Run(s, 9).Events.Hash(), Run(s, 10).Events.Hash());
    }
}
