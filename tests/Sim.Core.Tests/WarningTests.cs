using System.Collections.Generic;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Public warning: the first detection of a threat gives the people at the target time to reach shelter.</summary>
public class WarningTests
{
    [Fact]
    public void Nobody_is_sheltered_without_warning_or_in_the_open_field()
    {
        Assert.Equal(0.0, CasualtyModel.ShelteredShare(TerrainClass.City, 0.0, 1.0));
        Assert.Equal(0.0, CasualtyModel.ShelteredShare(TerrainClass.City, CasualtyModel.AlertDelayS, 1.0)); // the alert has only just arrived
        Assert.Equal(0.0, CasualtyModel.ShelteredShare(TerrainClass.Field, 10_000.0, 1.0));
    }

    [Fact]
    public void A_long_warning_shelters_the_covered_share_and_night_makes_it_slower()
    {
        Assert.Equal(CasualtyModel.ShelterCoverage(TerrainClass.City), CasualtyModel.ShelteredShare(TerrainClass.City, 10_000.0, 1.0), 9);
        double day = CasualtyModel.ShelteredShare(TerrainClass.City, 150.0, 1.0);
        double night = CasualtyModel.ShelteredShare(TerrainClass.City, 150.0, 0.0);
        Assert.True(night < day, $"day {day}, night {night}");
        Assert.True(night > 0.0);
    }

    private static Scenario Make(bool? warning, bool radar = true) => new()
    {
        Name = "warning-test",
        DtSeconds = 0.5,
        DurationSeconds = 6000,
        PublicWarning = warning,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 26.0 } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = 80, SpeedMps = 100, AltitudeM = 300, WarheadKg = 50 }
        },
        Defenses = radar
            ? new List<Defense> { new() { Id = "R1", Kind = DefenseKind.Radar, LatDeg = 44.0, LonDeg = 24.5, RangeKm = 300 } }
            : new List<Defense>()
    };

    private static RunSummary Analyze(Scenario s)
    {
        var sim = new Simulation(s, 5);
        sim.Run();
        return RunAnalyzer.Analyze(s, sim.Events.Events, 5);
    }

    [Fact]
    public void Early_detection_lowers_the_estimated_deaths_and_turning_the_warning_off_restores_them()
    {
        var withWarning = Analyze(Make(null));
        var off = Analyze(Make(false));
        var undetected = Analyze(Make(null, radar: false));

        Assert.True(withWarning.Impacted > 0);
        Assert.True(withWarning.Killed.Mean < off.Killed.Mean, $"with {withWarning.Killed.Mean}, off {off.Killed.Mean}");
        // no detection at all: nobody is warned, same as having the system off
        Assert.Equal(off.Killed.Mean, undetected.Killed.Mean, 6);
    }

    [Fact]
    public void The_warning_statistics_describe_the_impacts()
    {
        var warned = Assert.IsType<WarningStat>(Analyze(Make(null)).Warning);
        Assert.True(warned.Enabled);
        Assert.True(warned.Impacts > 0);
        Assert.Equal(0, warned.Undetected);
        Assert.True(warned.MeanWarningS > CasualtyModel.AlertDelayS);
        Assert.InRange(warned.MeanShelteredPct, 1.0, 100.0);

        var blind = Assert.IsType<WarningStat>(Analyze(Make(null, radar: false)).Warning);
        Assert.Equal(blind.Impacts, blind.Undetected);
        Assert.Equal(0.0, blind.MeanWarningS);
        Assert.Equal(0.0, blind.MeanShelteredPct);

        var off = Assert.IsType<WarningStat>(Analyze(Make(false)).Warning);
        Assert.False(off.Enabled);
        Assert.Equal(0.0, off.MeanShelteredPct);
    }

    [Fact]
    public void The_switch_is_left_out_of_the_canonical_form_when_absent()
    {
        Assert.DoesNotContain("publicWarning", ScenarioLoader.ToCanonicalJson(Make(null)));
        Assert.Contains("\"publicWarning\":false", ScenarioLoader.ToCanonicalJson(Make(false)));
    }
}
