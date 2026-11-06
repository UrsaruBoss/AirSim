using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Climate;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Pk as a function of the target's altitude (Defense.PkAltitude) and the weather presets (Scenario.Weather).</summary>
public class AltitudeAndWeatherTests
{
    private static Scenario Gun(UnitKind kind = UnitKind.Drone, List<AltitudePoint>? curve = null, string? weather = null) => new()
    {
        Name = "alt-weather",
        DtSeconds = 0.5,
        DurationSeconds = 3000,
        Weather = weather,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 25.0 } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Kind = kind, Count = 60, IntervalSeconds = 1, SpeedMps = 100, AltitudeM = 300 }
        },
        Defenses = new List<Defense>
        {
            new() { Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 24.95, RangeKm = 12, Magazine = 200, Pk = 0.5, ReloadSeconds = 1, PkAltitude = curve }
        }
    };

    private static Scenario SamWithRadar(string? weather) => Gun(UnitKind.CruiseMissile, null, weather) with
    {
        Defenses = new List<Defense>
        {
            new() { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 24.9, RangeKm = 120 },
            new() { Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 24.9, RangeKm = 40, Magazine = 200, Pk = 0.6, ReloadSeconds = 1 }
        }
    };

    private static double MeanIntercepted(Scenario s) => BatchRunner.Run(s, 100, 8, maxThreads: 2).Results.Average(r => (double)r.Intercepted);

    // ---------- altitude ----------

    [Fact]
    public void Altitude_curve_is_validated()
    {
        Assert.Empty(ScenarioLoader.Validate(Gun(curve: new() { new(0, 1.0), new(1000, 0.5) })));
        Assert.NotEmpty(ScenarioLoader.Validate(Gun(curve: new())));
        Assert.NotEmpty(ScenarioLoader.Validate(Gun(curve: new() { new(500, 1.0), new(500, 0.5) })));   // not strictly ascending
        Assert.NotEmpty(ScenarioLoader.Validate(Gun(curve: new() { new(0, 1.5) })));                    // factor > 1
        Assert.NotEmpty(ScenarioLoader.Validate(Gun(curve: new() { new(-10, 1.0) })));                  // negative altitude
    }

    [Fact]
    public void Scenarios_without_a_curve_or_weather_keep_their_canonical_form()
    {
        string json = ScenarioLoader.ToCanonicalJson(Gun());
        Assert.DoesNotContain("\"pkAltitude\"", json);
        Assert.DoesNotContain("\"weather\"", json); // the property, not the scenario name
        Assert.Contains("pkAltitude", ScenarioLoader.ToCanonicalJson(Gun(curve: new() { new(0, 1.0) })));
        Assert.Contains("\"weather\":\"fog\"", ScenarioLoader.ToCanonicalJson(Gun(weather: "fog")));
    }

    [Fact]
    public void A_zero_curve_stops_the_gun_and_a_half_curve_weakens_it()
    {
        double baseline = MeanIntercepted(Gun());
        double zero = MeanIntercepted(Gun(curve: new() { new(0, 0.0) }));
        // drones fly at 300 m: the curve gives 0.5 there (linear between 0 m -> 1.0 and 600 m -> 0.0)
        double half = MeanIntercepted(Gun(curve: new() { new(0, 1.0), new(600, 0.0) }));

        Assert.True(baseline > 0.0);
        Assert.Equal(0.0, zero);
        Assert.True(half > zero && half < baseline, $"zero {zero}, half {half}, baseline {baseline}");
    }

    [Fact]
    public void A_flat_curve_of_one_changes_nothing()
    {
        var a = BatchRunner.Run(Gun(), 100, 6, maxThreads: 1);
        var b = BatchRunner.Run(Gun(curve: new() { new(0, 1.0), new(5000, 1.0) }), 100, 6, maxThreads: 1);
        Assert.Equal(a.Results.Select(r => r.Intercepted), b.Results.Select(r => r.Intercepted));
    }

    // ---------- weather ----------

    [Fact]
    public void Weather_ids_are_validated()
    {
        foreach (string id in WeatherModel.Presets)
            Assert.Empty(ScenarioLoader.Validate(Gun(weather: id)));
        Assert.NotEmpty(ScenarioLoader.Validate(Gun(weather: "hail")));
    }

    [Fact]
    public void Clear_weather_is_the_same_as_no_weather()
    {
        var a = BatchRunner.Run(Gun(), 100, 6, maxThreads: 1);
        var b = BatchRunner.Run(Gun(weather: "clear"), 100, 6, maxThreads: 1);
        Assert.Equal(a.Results.Select(r => (r.Intercepted, r.Impacts, r.Crashed)), b.Results.Select(r => (r.Intercepted, r.Impacts, r.Crashed)));
    }

    [Fact]
    public void Fog_weakens_an_optical_gun()
    {
        Assert.True(MeanIntercepted(Gun(weather: "fog")) < MeanIntercepted(Gun()));
    }

    [Fact]
    public void Fog_does_not_touch_a_radar_guided_sam_against_cruise_missiles()
    {
        var clear = BatchRunner.Run(SamWithRadar(null), 100, 6, maxThreads: 1);
        var fog = BatchRunner.Run(SamWithRadar("fog"), 100, 6, maxThreads: 1);
        Assert.Equal(clear.Results.Select(r => (r.Intercepted, r.Impacts)), fog.Results.Select(r => (r.Intercepted, r.Impacts)));
    }

    [Fact]
    public void A_storm_makes_small_drones_fail_more_often()
    {
        int clear = BatchRunner.Run(Gun(), 100, 8, maxThreads: 2).Results.Sum(r => r.Crashed);
        int storm = BatchRunner.Run(Gun(weather: "storm"), 100, 8, maxThreads: 2).Results.Sum(r => r.Crashed);
        Assert.True(storm > clear, $"clear {clear}, storm {storm}");
    }

    [Fact]
    public void Weather_does_not_make_the_run_nondeterministic()
    {
        var a = BatchRunner.Run(Gun(weather: "rain"), 100, 6, maxThreads: 1);
        var b = BatchRunner.Run(Gun(weather: "rain"), 100, 6, maxThreads: 3);
        Assert.Equal(a.Results, b.Results);
    }
}
