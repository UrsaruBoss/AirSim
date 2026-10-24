using System;

namespace Sim.Core.Analytics;

/// <summary>Clasa de teren in care cade un impact.</summary>
public enum TerrainClass
{
    City,
    MilitaryBase,
    Field
}

public static class TerrainClassExtensions
{
    /// <summary>Numele din scenariu si de pe retea ("city", "military_base", "field").</summary>
    public static string WireName(this TerrainClass c) => c switch
    {
        TerrainClass.City => "city",
        TerrainClass.MilitaryBase => "military_base",
        _ => "field"
    };

    /// <summary>Tipul tintei ("city", "military_base", orice altceva = camp).</summary>
    public static TerrainClass FromTargetKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "city" => TerrainClass.City,
        "military_base" => TerrainClass.MilitaryBase,
        _ => TerrainClass.Field
    };
}

/// <summary>
/// Profil de teren: densitate de persoane expuse (per km2) si fractiile din cei aflati in zona letala care mor / sunt raniti.
/// VALORI ILUSTRATIVE, nu date empirice: servesc la comparatii intre scenarii, nu la predictii reale.
/// </summary>
public readonly record struct TerrainProfile(double DensityPerKm2, double FatalFraction, double InjuryFraction);

/// <summary>
/// Model simplu de victime: per impact, raza letala = LethalRadiusFactor * sarcina^(1/3) metri,
/// persoane in zona = densitate * aria zonei, apoi fractii de decese / ranii.
/// Incertitudinea (densitate lognormala per clasa, raza uniforma +-30%, zgomot Poisson) se propaga prin Monte Carlo in RunAnalyzer.
/// </summary>
public static class CasualtyModel
{
    /// <summary>Raza letala (m) = factor * kg^(1/3). Pentru 40 kg ~ 20 m.</summary>
    public const double LethalRadiusFactor = 6.0;

    /// <summary>Abaterea standard a logaritmului densitatii (incertitudinea densitatii de persoane).</summary>
    public const double DensityLogSigma = 0.5;

    /// <summary>Raza letala efectiva = raza nominala * factor uniform in [1 - x, 1 + x].</summary>
    public const double RadiusUncertainty = 0.3;

    public static TerrainProfile Profile(TerrainClass c) => c switch
    {
        TerrainClass.City => new TerrainProfile(3000.0, 0.10, 0.30),
        TerrainClass.MilitaryBase => new TerrainProfile(200.0, 0.25, 0.35),
        _ => new TerrainProfile(3.0, 0.20, 0.30)
    };

    /// <summary>Raza implicita a zonei unei tinte, daca scenariul nu o specifica (RadiusM = 0).</summary>
    public static double DefaultTargetRadiusM(TerrainClass c) => c switch
    {
        TerrainClass.City => 3000.0,
        TerrainClass.MilitaryBase => 600.0,
        _ => 0.0
    };

    /// <summary>Aria letala a unui impact, in km2.</summary>
    public static double LethalAreaKm2(double warheadKg)
    {
        double r = LethalRadiusFactor * Math.Cbrt(Math.Max(0.0, warheadKg));
        return Math.PI * r * r / 1e6;
    }
}
