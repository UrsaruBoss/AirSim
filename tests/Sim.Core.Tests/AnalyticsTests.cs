using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sim.Core.Analytics;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Runs;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class AnalyticsTests
{
    private static Scenario Make(int drones, string kind = "city", double spread = 0, double warheadKg = 40, double radiusM = 0) => new()
    {
        Name = "an-test",
        DtSeconds = 0.5,
        DurationSeconds = 6000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = kind, LatDeg = 44.0, LonDeg = 26.0, AltM = 50, RadiusM = radiusM } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = drones, SpeedMps = 250, AltitudeM = 300, SpreadMeters = spread, WarheadKg = warheadKg }
        }
    };

    private static (Scenario Scenario, Simulation Sim) Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return (s, sim);
    }

    private static RunSummary Analyze(Scenario s, ulong seed = 1, int trials = RunAnalyzer.DefaultTrials)
    {
        var (_, sim) = Run(s, seed);
        return RunAnalyzer.Analyze(s, sim.Events.Events, seed, trials);
    }

    [Fact]
    public void Counts_match_the_simulation()
    {
        var (s, sim) = Run(Make(10));
        var summary = RunAnalyzer.Analyze(s, sim.Events.Events, 1);

        Assert.Equal(10, summary.Total);
        Assert.Equal(10, summary.Launched);
        Assert.Equal(10, summary.Impacted);
        Assert.Equal(0, summary.Intercepted);
        Assert.Equal(0, summary.Violations);
    }

    [Theory]
    [InlineData("city", "city")]
    [InlineData("military_base", "military_base")]
    [InlineData("field", "field")]
    [InlineData("something_else", "field")]
    public void Impacts_inside_the_target_zone_take_the_target_class(string kind, string expected)
    {
        var summary = Analyze(Make(20, kind));

        var stat = Assert.Single(summary.Classes);
        Assert.Equal(expected, stat.Class);
        Assert.Equal(20, stat.Impacts);
    }

    [Fact]
    public void Impacts_outside_the_zone_fall_on_field()
    {
        // zona de 1 km, dispersie de 1 km: ~39% in zona, restul pe camp
        var summary = Analyze(Make(300, "military_base", spread: 1000, radiusM: 1000));

        Assert.Equal(2, summary.Classes.Count);
        Assert.Equal(300, summary.Classes.Sum(c => c.Impacts));
        Assert.Contains(summary.Classes, c => c.Class == "military_base" && c.Impacts > 50);
        Assert.Contains(summary.Classes, c => c.Class == "field" && c.Impacts > 50);
    }

    [Fact]
    public void Same_input_gives_identical_summary()
    {
        var s = Make(100, spread: 800, radiusM: 1000);

        string a = JsonSerializer.Serialize(Analyze(s, 5));
        string b = JsonSerializer.Serialize(Analyze(s, 5));

        Assert.Equal(a, b);
    }

    [Fact]
    public void City_casualties_match_the_closed_form_expectation()
    {
        // asteptare: densitate * arie letala * impacturi * fractie ~ 3000 * 0.00132 * 100 * 0.10 ~ 40
        var summary = Analyze(Make(100, "city", warheadKg: 40));

        Assert.InRange(summary.Killed.Mean, 35, 47);
        Assert.InRange(summary.Injured.Mean, 105, 140);
    }

    [Fact]
    public void Interval_brackets_the_mean()
    {
        var summary = Analyze(Make(100, "city"));

        Assert.True(summary.Killed.P5 <= summary.Killed.Mean);
        Assert.True(summary.Killed.Mean <= summary.Killed.P95);
        Assert.True(summary.Killed.P5 < summary.Killed.P95);
    }

    [Fact]
    public void Bigger_warhead_means_more_casualties()
    {
        var small = Analyze(Make(100, "city", warheadKg: 20));
        var big = Analyze(Make(100, "city", warheadKg: 200));

        Assert.True(big.Killed.Mean > small.Killed.Mean * 3);
    }

    [Fact]
    public void Cities_are_deadlier_than_fields()
    {
        var city = Analyze(Make(100, "city"));
        var field = Analyze(Make(100, "field"));

        Assert.True(city.Killed.Mean > field.Killed.Mean * 100);
    }

    [Fact]
    public void Total_mean_is_the_sum_of_class_means()
    {
        var summary = Analyze(Make(300, "military_base", spread: 1000, radiusM: 1000));

        Assert.Equal(summary.Killed.Mean, summary.Classes.Sum(c => c.Killed.Mean), 6);
        Assert.Equal(summary.Injured.Mean, summary.Classes.Sum(c => c.Injured.Mean), 6);
    }

    [Fact]
    public void No_impacts_means_no_casualties()
    {
        var summary = RunAnalyzer.Analyze(Make(10), new List<SimEvent>(), 1);

        Assert.Empty(summary.Classes);
        Assert.Equal(0, summary.Killed.Mean);
        Assert.Equal(0, summary.Injured.P95);
    }

    [Fact]
    public void Violations_are_grouped_by_country_in_ordinal_order()
    {
        var p = new GeoPoint(43, 28, 400);
        var events = new List<SimEvent>
        {
            new(1, SimEventType.AirspaceViolation, 0, p, "MD"),
            new(2, SimEventType.AirspaceViolation, 1, p, "BG"),
            new(3, SimEventType.AirspaceViolation, 2, p, "BG")
        };

        var summary = RunAnalyzer.Analyze(Make(3), events, 1);

        Assert.Equal(3, summary.Violations);
        Assert.Equal(new[] { ("BG", 2), ("MD", 1) }, summary.ViolationsByCountry.Select(v => (v.Code, v.Count)).ToArray());
    }

    [Fact]
    public void Per_unit_statistics_come_from_the_event_log()
    {
        var radar = new Defense { Id = "R1", Name = "Radar", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 100 };
        var sam = new Defense { Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 50, Magazine = 3, Pk = 1.0, ReloadSeconds = 0 };
        var s = Make(10) with { Defenses = new List<Defense> { radar, sam } };

        var (_, sim) = Run(s);
        var summary = RunAnalyzer.Analyze(s, sim.Events.Events, 1);

        var r = summary.Defenses.Single(d => d.Id == "R1");
        Assert.Equal("Radar", r.Name);
        Assert.Equal("radar", r.Kind);
        Assert.Equal(10, r.Detected);

        var sStat = summary.Defenses.Single(d => d.Id == "S1");
        Assert.Equal("S1", sStat.Name); // fara nume => id-ul
        Assert.Equal(3, sStat.Shots);
        Assert.Equal(3, sStat.Hits);
        Assert.Equal(0, sStat.Misses);
        Assert.Equal(3, sStat.MagazineStart);
        Assert.Equal(summary.Intercepted, summary.Defenses.Sum(d => d.Hits));
    }

    [Fact]
    public void Trials_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunAnalyzer.Analyze(Make(1), new List<SimEvent>(), 1, trials: 0));
    }

    [Fact]
    public void Validation_rejects_negative_warhead_and_radius()
    {
        var errors = ScenarioLoader.Validate(Make(1, warheadKg: -1, radiusM: -5));

        Assert.Contains(errors, e => e.Contains("WarheadKg"));
        Assert.Contains(errors, e => e.Contains("RadiusM"));
    }

    [Fact]
    public void Saved_run_contains_a_summary_matching_the_live_one()
    {
        var (s, sim) = Run(Make(20), 9);
        var meta = RunMeta.Create(s, 9);
        string root = Path.Combine(Path.GetTempPath(), "airsim-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            string dir = RunStore.Save(root, s, meta, sim.Events);
            string json = File.ReadAllText(Path.Combine(dir, "summary.json"));
            using var doc = JsonDocument.Parse(json);

            Assert.Equal(20, doc.RootElement.GetProperty("impacted").GetInt32());
            Assert.Equal("city", doc.RootElement.GetProperty("classes")[0].GetProperty("class").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
