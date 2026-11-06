using System.Collections.Generic;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

/// <summary>Jamming weakens with distance from the jammer.</summary>
public class JamFalloffTests
{
    private static Scenario Make(double falloff) => new()
    {
        Name = "falloff-test",
        DtSeconds = 0.5,
        DurationSeconds = 3000,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 44.0, LonDeg = 24.0 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 44.0, LonDeg = 25.0 } },
        Waves = new List<Wave>
        {
            new() { Id = "W1", SiteId = "L1", TargetId = "T1", Count = 400, IntervalSeconds = 0.5, SpeedMps = 100, AltitudeM = 300 }
        },
        // jammer in the middle of the route, range 30 km: the drones pass through the center
        Defenses = new List<Defense>
        {
            new() { Id = "E1", Kind = DefenseKind.Ew, LatDeg = 44.0, LonDeg = 24.5, RangeKm = 30, JamRatePerMinute = 0.1, JamFalloff = falloff }
        }
    };

    private static int Jammed(double falloff, ulong seed = 1)
    {
        var sim = new Simulation(Make(falloff), seed);
        sim.Run();
        return sim.JammedCount;
    }

    [Fact]
    public void Falloff_reduces_the_number_of_jammed_units()
    {
        int flat = Jammed(0.0);
        int fading = Jammed(1.0);

        Assert.True(fading > 0);
        Assert.True(flat > fading + 40, $"flat={flat}, fading={fading}");
    }

    [Fact]
    public void Falloff_is_deterministic()
    {
        Assert.Equal(Jammed(0.7, 3), Jammed(0.7, 3));
    }

    [Fact]
    public void Falloff_must_be_between_zero_and_one()
    {
        Assert.Contains(ScenarioLoader.Validate(Make(1.5)), e => e.Contains("JamFalloff"));
        Assert.Contains(ScenarioLoader.Validate(Make(-0.1)), e => e.Contains("JamFalloff"));
        Assert.Empty(ScenarioLoader.Validate(Make(1.0)));
    }
}
