using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;
using Sim.Core.Sun;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Time of day: the sun position, validation of the start time and the day/night effects on the model.</summary>
public class DayNightTests
{
    private static double Ms(string iso)
    {
        Assert.True(DayNight.TryParseUtc(iso, out double ms));
        return ms;
    }

    [Fact]
    public void Sun_is_high_at_noon_and_far_below_the_horizon_at_midnight_on_the_equator()
    {
        Assert.True(DayNight.ElevationDeg(0.0, 0.0, Ms("2026-03-20T12:00:00Z")) > 85.0);
        Assert.True(DayNight.ElevationDeg(0.0, 0.0, Ms("2026-03-20T00:00:00Z")) < -85.0);
    }

    [Fact]
    public void Bucharest_has_the_expected_seasonal_sun()
    {
        // June solstice, 13:00 local (EEST = UTC+3): the sun is high; at 01:00 local it is about 22 degrees below the horizon
        Assert.InRange(DayNight.ElevationDeg(44.4, 26.1, Ms("2026-06-21T10:00:00Z")), 60.0, 70.0);
        Assert.InRange(DayNight.ElevationDeg(44.4, 26.1, Ms("2026-06-21T22:00:00Z")), -25.0, -15.0);
        // December solstice, noon: about 22 degrees above the horizon
        Assert.InRange(DayNight.ElevationDeg(44.4, 26.1, Ms("2026-12-21T10:00:00Z")), 18.0, 26.0);
    }

    [Fact]
    public void Daylight_goes_from_night_to_day_monotonically()
    {
        Assert.Equal(0.0, DayNight.Daylight(-20.0));
        Assert.Equal(1.0, DayNight.Daylight(10.0));
        double previous = -1.0;
        for (double el = -15.0; el <= 10.0; el += 0.5)
        {
            double d = DayNight.Daylight(el);
            Assert.InRange(d, 0.0, 1.0);
            Assert.True(d >= previous);
            previous = d;
        }
        Assert.InRange(DayNight.Daylight(-3.0), 0.1, 0.9);
    }

    [Fact]
    public void Parsing_accepts_iso_dates_and_assumes_utc_without_a_zone()
    {
        Assert.True(DayNight.TryParseUtc("2026-03-01T22:30:00Z", out double a));
        Assert.True(DayNight.TryParseUtc("2026-03-01T22:30:00", out double b));
        Assert.True(DayNight.TryParseUtc("2026-03-02T00:30:00+02:00", out double c));
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.False(DayNight.TryParseUtc("not a date", out _));
        Assert.False(DayNight.TryParseUtc("", out _));
        Assert.False(DayNight.TryParseUtc(null, out _));
    }

    [Fact]
    public void An_invalid_start_time_is_a_validation_error()
    {
        var bad = new Scenario { Name = "x", StartTimeUtc = "tomorrow-ish" };
        Assert.Contains(ScenarioLoader.Validate(bad), e => e.Contains("StartTimeUtc"));

        var good = new Scenario { Name = "x", StartTimeUtc = "2026-03-01T22:30:00Z" };
        Assert.DoesNotContain(ScenarioLoader.Validate(good), e => e.Contains("StartTimeUtc"));
    }

    [Fact]
    public void Without_a_start_time_the_canonical_form_is_unchanged()
    {
        Assert.DoesNotContain("startTimeUtc", ScenarioLoader.ToCanonicalJson(new Scenario { Name = "x" }));
        Assert.Contains("startTimeUtc", ScenarioLoader.ToCanonicalJson(new Scenario { Name = "x", StartTimeUtc = "2026-03-01T22:30:00Z" }));
        // and the start time is part of the run's identity
        Assert.NotEqual(
            ScenarioLoader.Hash(new Scenario { Name = "x", StartTimeUtc = "2026-03-01T10:00:00Z" }),
            ScenarioLoader.Hash(new Scenario { Name = "x", StartTimeUtc = "2026-03-01T22:00:00Z" }));
    }

    private const string Day = "2026-06-21T09:00:00Z";   // 12:00 local at 25E
    private const string Night = "2026-06-21T23:00:00Z"; // 02:00 local at 25E

    private static Scenario Make(string? start, int drones, params Defense[] defenses) => new()
    {
        Name = "dn-test",
        DtSeconds = 0.1,
        DurationSeconds = 6000,
        StartTimeUtc = start,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0 } },
        Waves = new List<Wave> { new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = drones, SpeedMps = 100, AltitudeM = 300 } },
        Defenses = defenses.ToList()
    };

    private static Simulation Run(Scenario s, ulong seed = 1)
    {
        var sim = new Simulation(s, seed);
        sim.Run();
        return sim;
    }

    private static Defense Gun() => new()
    {
        Id = "G1", Kind = DefenseKind.AaGun, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 20, Magazine = 100_000, Pk = 0.5, ReloadSeconds = 0.5
    };

    [Fact]
    public void Without_a_start_time_the_model_is_the_daytime_one()
    {
        // in full daylight every factor is exactly 1, so "no time of day" and "midday" give the same run
        var neutral = Run(Make(null, 200, Gun()));
        var midday = Run(Make(Day, 200, Gun()));

        Assert.Equal(neutral.InterceptedCount, midday.InterceptedCount);
        Assert.Equal(neutral.Events.Hash(), midday.Events.Hash());
    }

    [Fact]
    public void An_optical_gun_group_shoots_down_fewer_drones_at_night()
    {
        var day = Run(Make(Day, 200, Gun()));
        var night = Run(Make(Night, 200, Gun()));

        Assert.True(night.InterceptedCount < day.InterceptedCount,
            $"day {day.InterceptedCount}, night {night.InterceptedCount}");
    }

    [Fact]
    public void Radar_guided_defenses_do_not_depend_on_the_time_of_day()
    {
        var sam = new Defense { Id = "S1", Kind = DefenseKind.Sam, LatDeg = 44.0, LonDeg = 25.0, RangeKm = 40, Magazine = 100_000, Pk = 0.5, ReloadSeconds = 0.5 };
        var radar = new Defense { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 24.5, RangeKm = 150 };

        var day = Run(Make(Day, 100, radar, sam));
        var night = Run(Make(Night, 100, radar, sam));

        Assert.Equal(day.InterceptedCount, night.InterceptedCount);
        Assert.Equal(day.Events.Hash(), night.Events.Hash());
    }

    [Fact]
    public void A_night_raid_on_a_city_has_more_casualties_than_a_daytime_one()
    {
        var day = Make(Day, 100);
        var night = Make(Night, 100);

        var daySummary = RunAnalyzer.Analyze(day, Run(day).Events.Events, 1);
        var nightSummary = RunAnalyzer.Analyze(night, Run(night).Events.Events, 1);

        Assert.True(nightSummary.Killed.Mean > daySummary.Killed.Mean);
    }

    [Fact]
    public void A_night_raid_on_a_field_has_fewer_casualties_than_a_daytime_one()
    {
        Scenario Field(string start) => Make(start, 100) with
        {
            Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "field", LatDeg = 44.0, LonDeg = 26.0 } }
        };
        var day = Field(Day);
        var night = Field(Night);

        var daySummary = RunAnalyzer.Analyze(day, Run(day).Events.Events, 1);
        var nightSummary = RunAnalyzer.Analyze(night, Run(night).Events.Events, 1);

        Assert.True(nightSummary.Killed.Mean < daySummary.Killed.Mean);
    }

    [Theory]
    [InlineData(25.0, 12.0, "2026-03-20T10:20:00Z")]
    [InlineData(0.0, 1.0, "2026-03-20T01:00:00Z")]
    [InlineData(-90.0, 1.0, "2026-03-20T07:00:00Z")]
    [InlineData(150.0, 1.0, "2026-03-19T15:00:00Z")]
    public void Local_solar_hour_maps_to_the_expected_utc_instant(double lon, double hour, string expected)
    {
        Assert.Equal(expected, DayNight.StartUtcForLocalSolarHour(lon, hour));
    }

    [Fact]
    public void The_day_and_night_variants_are_really_day_and_night_at_the_center()
    {
        var s = Make(null, 10);
        double lon = DayNight.CenterLonDeg(s);
        var day = DayNight.WithVariant(s, "day");
        var night = DayNight.WithVariant(s, "night");

        Assert.True(DayNight.TryParseUtc(day.StartTimeUtc, out double dayMs));
        Assert.True(DayNight.TryParseUtc(night.StartTimeUtc, out double nightMs));
        Assert.True(DayNight.ElevationDeg(44.0, lon, dayMs) > 30.0);
        Assert.True(DayNight.ElevationDeg(44.0, lon, nightMs) < -20.0);
        Assert.Throws<ArgumentException>(() => DayNight.WithVariant(s, "dusk"));
    }

    [Theory]
    [InlineData("ru-vs-ua.night", "ru-vs-ua", "night")]
    [InlineData("ru-vs-ua.day", "ru-vs-ua", "day")]
    [InlineData("ru-vs-ua", "ru-vs-ua", "")]
    [InlineData("night", "night", "")]
    [InlineData("a.dayx", "a.dayx", "")]
    public void Run_ids_split_into_scenario_and_variant(string id, string baseId, string variant)
    {
        Assert.Equal((baseId, variant), DayNight.SplitVariantId(id));
    }
}
