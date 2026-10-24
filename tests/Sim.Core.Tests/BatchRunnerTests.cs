using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class BatchRunnerTests
{
    private static Scenario Make() => new()
    {
        Name = "batch-test",
        DtSeconds = 0.5,
        DurationSeconds = 3000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 25.0 } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = 80, IntervalSeconds = 1, SpeedMps = 100, AltitudeM = 300, FailureRate = 0.3 }
        },
        Defenses = new List<Defense>
        {
            new() { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 24.95, RangeKm = 10, Magazine = 100, Pk = 0.5, ReloadSeconds = 1 }
        }
    };

    [Fact]
    public void Runs_use_consecutive_seeds_and_match_single_runs()
    {
        var s = Make();
        var report = BatchRunner.Run(s, 10, 4, maxThreads: 2);

        Assert.Equal(new ulong[] { 10, 11, 12, 13 }, report.Results.Select(r => r.Seed).ToArray());

        var single = new Simulation(s, 12);
        single.Run();
        var r12 = report.Results[2];
        Assert.Equal(single.InterceptedCount, r12.Intercepted);
        Assert.Equal(single.ImpactCount, r12.Impacts);
        Assert.Equal(single.CrashedCount, r12.Crashed);
        Assert.All(report.Results, r => Assert.Equal(80, r.Launched));
        Assert.All(report.Results, r => Assert.Equal(80, r.Intercepted + r.Impacts + r.Crashed + r.Jammed));
    }

    [Fact]
    public void The_report_does_not_depend_on_the_number_of_threads()
    {
        var s = Make();
        var one = BatchRunner.Run(s, 1, 8, maxThreads: 1);
        var many = BatchRunner.Run(s, 1, 8, maxThreads: 4);

        Assert.Equal(one.Results.ToArray(), many.Results.ToArray());
        Assert.Equal(one.ScenarioHash, many.ScenarioHash);
    }

    [Fact]
    public void Different_seeds_give_a_spread()
    {
        var report = BatchRunner.Run(Make(), 1, 12);
        var crashed = report.Metrics.Single(m => m.Name == "Cazute pe traseu");

        Assert.True(crashed.Sd > 0);
        Assert.InRange(crashed.Mean, 10, 40); // ~ 30% din 80
        Assert.True(crashed.Min <= crashed.P5 && crashed.P5 <= crashed.P50 && crashed.P50 <= crashed.P95 && crashed.P95 <= crashed.Max);
    }

    [Fact]
    public void Statistics_are_computed_correctly()
    {
        var stat = BatchRunner.Stat("x", new double[] { 5, 1, 4, 2, 3 });

        Assert.Equal(3, stat.Mean, 9);
        Assert.Equal(Math.Sqrt(2.5), stat.Sd, 9);
        Assert.Equal(1, stat.Min);
        Assert.Equal(5, stat.Max);
        Assert.Equal(3, stat.P50, 9);
        Assert.Equal(1.2, stat.P5, 9);
        Assert.Equal(4.8, stat.P95, 9);

        var one = BatchRunner.Stat("y", new[] { 7.0 });
        Assert.Equal(0, one.Sd);
        Assert.Equal(7, one.P95);
    }

    [Fact]
    public void Csv_has_a_header_and_one_row_per_run()
    {
        var csv = BatchRunner.ToCsv(BatchRunner.Run(Make(), 1, 3));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);
        Assert.StartsWith("seed,", lines[0]);
        Assert.StartsWith("1,80,", lines[1]);
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BatchRunner.Run(Make(), 1, 0));
        Assert.Throws<ArgumentException>(() => BatchRunner.Run(Make() with { DtSeconds = -1 }, 1, 2));
    }
}
