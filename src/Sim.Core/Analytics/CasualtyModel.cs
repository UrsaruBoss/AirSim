using System;

namespace Sim.Core.Analytics;

/// <summary>Terrain class an impact lands in.</summary>
public enum TerrainClass
{
    City,
    MilitaryBase,
    Field
}

public static class TerrainClassExtensions
{
    /// <summary>Name in the scenario and on the wire ("city", "military_base", "field").</summary>
    public static string WireName(this TerrainClass c) => c switch
    {
        TerrainClass.City => "city",
        TerrainClass.MilitaryBase => "military_base",
        _ => "field"
    };

    /// <summary>Target type ("city", "military_base", anything else = field).</summary>
    public static TerrainClass FromTargetKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "city" => TerrainClass.City,
        "military_base" => TerrainClass.MilitaryBase,
        _ => TerrainClass.Field
    };
}

/// <summary>
/// Terrain profile: density of exposed people (per km2) and the fractions of those in the lethal area who die / are injured.
/// ILLUSTRATIVE VALUES, not empirical data: meant for comparing scenarios, not for real predictions.
/// </summary>
public readonly record struct TerrainProfile(double DensityPerKm2, double FatalFraction, double InjuryFraction);

/// <summary>
/// Simple casualty model: per impact, lethal radius = LethalRadiusFactor * warhead^(1/3) meters,
/// people in the area = density * area, then fractions killed / injured.
/// Uncertainty (lognormal density per class, uniform +-30% radius, Poisson noise) is propagated by Monte Carlo in RunAnalyzer.
/// </summary>
public static class CasualtyModel
{
    /// <summary>Lethal radius (m) = factor * kg^(1/3). About 20 m for 40 kg.</summary>
    public const double LethalRadiusFactor = 6.0;

    /// <summary>Standard deviation of the density logarithm (uncertainty of the population density).</summary>
    public const double DensityLogSigma = 0.5;

    /// <summary>Effective lethal radius = nominal radius * uniform factor in [1 - x, 1 + x].</summary>
    public const double RadiusUncertainty = 0.3;

    public static TerrainProfile Profile(TerrainClass c) => c switch
    {
        TerrainClass.City => new TerrainProfile(3000.0, 0.10, 0.30),
        TerrainClass.MilitaryBase => new TerrainProfile(200.0, 0.25, 0.35),
        _ => new TerrainProfile(3.0, 0.20, 0.30)
    };

    // ---- Public warning: people who have time to reach shelter before the impact are much less likely to die. ILLUSTRATIVE. ----

    /// <summary>Seconds between the first detection of a threat and the moment the public knows (sirens, phone alerts).</summary>
    public const double AlertDelayS = 30.0;

    /// <summary>Seconds a person needs to react and reach shelter by day; asleep or slow at night: this much more (scaled by darkness).</summary>
    public const double ShelterReachDayS = 90.0;
    public const double ShelterReachNightExtraS = 120.0;

    /// <summary>The fraction of the fatal outcomes avoided for a person who is sheltered.</summary>
    public const double ShelterProtection = 0.8;

    /// <summary>The share of the exposed people who have shelter or hard cover within reach (cities: basements, metro; bases: hardened facilities; open field: none).</summary>
    public static double ShelterCoverage(TerrainClass c) => c switch
    {
        TerrainClass.City => 0.6,
        TerrainClass.MilitaryBase => 0.9,
        _ => 0.0
    };

    /// <summary>
    /// Share (0..1) of the exposed people who are sheltered when an impact comes <paramref name="warningS"/> seconds after the first detection
    /// (0 if the threat was never detected). Daylight 1 = day, 0 = night.
    /// </summary>
    public static double ShelteredShare(TerrainClass c, double warningS, double daylight)
    {
        double reach = ShelterReachDayS + ShelterReachNightExtraS * (1.0 - daylight);
        double ready = Math.Clamp((warningS - AlertDelayS) / reach, 0.0, 1.0);
        return ShelterCoverage(c) * ready;
    }

    /// <summary>Default radius of a target's area when the scenario does not specify one (RadiusM = 0).</summary>
    public static double DefaultTargetRadiusM(TerrainClass c) => c switch
    {
        TerrainClass.City => 3000.0,
        TerrainClass.MilitaryBase => 600.0,
        _ => 0.0
    };

    /// <summary>Lethal area of one impact, in km2.</summary>
    public static double LethalAreaKm2(double warheadKg)
    {
        double r = LethalRadiusFactor * Math.Cbrt(Math.Max(0.0, warheadKg));
        return Math.PI * r * r / 1e6;
    }
}
