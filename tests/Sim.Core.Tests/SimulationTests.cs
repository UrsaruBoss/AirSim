using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Runs;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class SimulationTests
{
    private static readonly GeoPoint SitePoint = new(45.0, 30.0, 0);
    private static readonly GeoPoint TargetPoint = new(44.0, 26.0, 80);

    private static Scenario Make(
        int count, double spread,
        double speed = 250, double start = 0, double interval = 0,
        double dt = 0.5, double duration = 6000,
        UnitKind kind = UnitKind.Drone) => new()
    {
        Name = "test",
        DtSeconds = dt,
        DurationSeconds = duration,
        LaunchSites = new List<LaunchSite>
        {
            new() { Id = "L1", Name = "L1", LatDeg = SitePoint.LatDeg, LonDeg = SitePoint.LonDeg, AltM = SitePoint.AltM }
        },
        Targets = new List<Target>
        {
            new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = TargetPoint.LatDeg, LonDeg = TargetPoint.LonDeg, AltM = TargetPoint.AltM }
        },
        Waves = new List<Wave>
        {
            new()
            {
                Id = "W1", SiteId = "L1", TargetId = "T1", Kind = kind, Count = count,
                StartTimeSeconds = start, IntervalSeconds = interval,
                SpeedMps = speed, AltitudeM = 400, SpreadMeters = spread
            }
        }
    };

    private static Simulation RunSim(Scenario s, ulong seed)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    [Fact]
    public void Same_scenario_and_seed_give_identical_event_log()
    {
        var s = Make(count: 100, spread: 500, interval: 0.5);

        var a = RunSim(s, 42);
        var b = RunSim(s, 42);

        Assert.True(a.Events.Count > 0);
        Assert.Equal(a.Events.Hash(), b.Events.Hash());
    }

    [Fact]
    public void Different_seed_gives_different_event_log()
    {
        var s = Make(count: 100, spread: 500, interval: 0.5);

        Assert.NotEqual(RunSim(s, 42).Events.Hash(), RunSim(s, 43).Events.Hash());
    }

    [Fact]
    public void Every_drone_launches_and_impacts_once()
    {
        var sim = RunSim(Make(count: 50, spread: 300, interval: 1.0), 7);

        var events = sim.Events.Events;
        Assert.Equal(50, events.Count(e => e.Type == SimEventType.Launched));
        Assert.Equal(50, events.Count(e => e.Type == SimEventType.Impact));
        Assert.True(sim.IsFinished);
    }

    [Fact]
    public void Impacts_land_near_the_target()
    {
        const double sigma = 300;
        var sim = RunSim(Make(count: 50, spread: sigma), 7);

        var target = new GeoPoint(TargetPoint.LatDeg, TargetPoint.LonDeg);
        foreach (var e in sim.Events.Events.Where(e => e.Type == SimEventType.Impact))
        {
            double offset = Wgs84.GreatCircleDistanceM(new GeoPoint(e.Position.LatDeg, e.Position.LonDeg), target);
            Assert.InRange(offset, 0.0, 6 * sigma);
        }
    }

    [Fact]
    public void Zero_spread_hits_exactly_on_target_at_the_expected_time()
    {
        const double speed = 100;
        const double dt = 0.5;
        var sim = RunSim(Make(count: 1, spread: 0, speed: speed, dt: dt, duration: 20000), 1);

        double expectedS = Wgs84.GreatCircleDistanceM(SitePoint, TargetPoint) / speed;
        var impact = sim.Events.Events.Single(e => e.Type == SimEventType.Impact);

        Assert.Equal(TargetPoint.LatDeg, impact.Position.LatDeg, 9);
        Assert.Equal(TargetPoint.LonDeg, impact.Position.LonDeg, 9);

        double impactTimeS = impact.Tick * dt;
        Assert.InRange(impactTimeS, expectedS - 1e-6, expectedS + dt + 1e-6);
    }

    [Fact]
    public void Launches_follow_start_time_and_interval()
    {
        const double dt = 0.5;
        var sim = RunSim(Make(count: 3, spread: 0, start: 10, interval: 5, dt: dt), 1);

        var launches = sim.Events.Events.Where(e => e.Type == SimEventType.Launched).OrderBy(e => e.EntityId).ToList();

        Assert.Equal(new[] { 10.0, 15.0, 20.0 }, launches.Select(e => e.Tick * dt).ToArray());
    }

    [Fact]
    public void Drones_in_flight_have_cruise_altitude_and_move_toward_target()
    {
        var sim = new Simulation(Make(count: 1, spread: 0, speed: 100, duration: 20000), 1);
        for (int i = 0; i < 200; i++) sim.Step(); // 100 s de zbor

        var d = sim.Drones[0];
        Assert.Equal(Sim.Core.Entities.EntityStatus.Flying, d.Status);
        Assert.Equal(400.0, d.Position.AltM, 6);

        double total = Wgs84.GreatCircleDistanceM(SitePoint, TargetPoint);
        double remaining = Wgs84.GreatCircleDistanceM(d.Position, TargetPoint);
        Assert.InRange(remaining, total - 100 * 101, total - 100 * 99); // ~10 km parcursi
    }

    [Theory]
    [InlineData(UnitKind.Drone)]
    [InlineData(UnitKind.BallisticMissile)]
    [InlineData(UnitKind.Aircraft)]
    public void All_unit_kinds_are_supported(UnitKind kind)
    {
        var sim = RunSim(Make(count: 3, spread: 0, speed: 800, kind: kind), 1);

        Assert.Equal(3, sim.ImpactCount);
    }

    [Fact]
    public void Invalid_scenario_is_rejected()
    {
        var bad = Make(count: 1, spread: 0) with { DtSeconds = 0 };
        Assert.Throws<ArgumentException>(() => new Simulation(bad, 1));
    }

    [Fact]
    public void RunStore_writes_scenario_meta_and_events()
    {
        var s = Make(count: 5, spread: 100);
        var sim = RunSim(s, 11);
        string root = Path.Combine(Path.GetTempPath(), "airsim-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            string dir = RunStore.Save(root, s, RunMeta.Create(s, 11), sim.Events);

            Assert.True(File.Exists(Path.Combine(dir, "scenario.json")));
            Assert.True(File.Exists(Path.Combine(dir, "meta.json")));

            var lines = File.ReadAllLines(Path.Combine(dir, "events.jsonl"));
            Assert.Equal(sim.Events.Count, lines.Length);
            Assert.Contains(sim.Events.Hash(), File.ReadAllText(Path.Combine(dir, "meta.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
