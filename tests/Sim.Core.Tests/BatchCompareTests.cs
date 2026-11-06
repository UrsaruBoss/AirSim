using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class BatchCompareTests
{
    private static Scenario Make(int guns) => new()
    {
        Name = $"cmp-{guns}",
        DtSeconds = 0.5,
        DurationSeconds = 3000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 25.0 } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = 60, IntervalSeconds = 1, SpeedMps = 100, AltitudeM = 300 }
        },
        Defenses = Enumerable.Range(0, guns).Select(i => new Defense
        {
            Id = $"G{i}", Kind = DefenseKind.AaGun, LatDeg = 44.0 + i * 0.001, LonDeg = 24.95,
            RangeKm = 10, Magazine = 100, Pk = 0.5, ReloadSeconds = 1
        }).ToList()
    };

    [Fact]
    public void More_defenses_gives_significantly_fewer_impacts()
    {
        var a = BatchRunner.Run(Make(1), 100, 8, maxThreads: 2);
        var b = BatchRunner.Run(Make(4), 100, 8, maxThreads: 2);
        var rows = BatchCompare.Compare(a, b);

        var impacts = rows.First(r => r.Name == "Impacts");
        Assert.True(impacts.Diff < 0, "mai multe tunuri => mai putine impacturi");
        Assert.True(impacts.Significant);
        Assert.Equal(BatchRunner.Selectors.Count, rows.Count);
    }

    [Fact]
    public void Same_scenario_compares_to_zero()
    {
        var a = BatchRunner.Run(Make(2), 5, 4, maxThreads: 2);
        var rows = BatchCompare.Compare(a, a);
        Assert.All(rows, r => { Assert.Equal(0.0, r.Diff); Assert.False(r.Significant); });
    }

    [Fact]
    public void Different_seeds_are_rejected()
    {
        var a = BatchRunner.Run(Make(1), 1, 3, maxThreads: 1);
        var b = BatchRunner.Run(Make(1), 2, 3, maxThreads: 1);
        Assert.Throws<ArgumentException>(() => BatchCompare.Compare(a, b));
    }
}
