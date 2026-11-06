using System;
using System.Globalization;
using System.Linq;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;

namespace Sim.Core.Sun;

/// <summary>
/// Time of day: the position of the sun (a pure function of place and instant) and the illustrative day/night effects on the model.
/// The formula is the same low-precision one used by the web interface (web/src/solar.ts), so the map and the model agree on what "night" is.
/// Without <c>Scenario.StartTimeUtc</c> nothing here is applied (the model behaves exactly as by day).
/// ALL FACTORS BELOW ARE ILLUSTRATIVE ESTIMATES, not measured data: they exist to compare day against night, not to predict.
/// </summary>
public static class DayNight
{
    private const double Rad = Math.PI / 180.0;
    private const double J2000UnixMs = 946_728_000_000.0; // 2000-01-01T12:00:00Z

    /// <summary>ISO 8601 date-time (a missing zone means UTC) -> milliseconds since the Unix epoch.</summary>
    public static bool TryParseUtc(string? text, out double unixMs)
    {
        unixMs = 0.0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        unixMs = parsed.ToUnixTimeMilliseconds();
        return true;
    }

    /// <summary>Sun elevation above the horizon (degrees) at a place and an instant (milliseconds since the Unix epoch).</summary>
    public static double ElevationDeg(double latDeg, double lonDeg, double unixMs)
    {
        double d = (unixMs - J2000UnixMs) / 86_400_000.0;
        double g = (357.529 + 0.98560028 * d) * Rad;
        double q = 280.459 + 0.98564736 * d;
        double lambda = (q + 1.915 * Math.Sin(g) + 0.02 * Math.Sin(2.0 * g)) * Rad;
        double eps = (23.439 - 0.00000036 * d) * Rad;
        double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lambda), Math.Cos(lambda));
        double dec = Math.Asin(Math.Sin(eps) * Math.Sin(lambda));
        double gmstDeg = (18.697374558 + 24.06570982441908 * d) * 15.0;
        double ha = (gmstDeg + lonDeg) * Rad - ra;
        double lat = latDeg * Rad;
        double sinEl = Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(ha);
        return Math.Asin(Math.Clamp(sinEl, -1.0, 1.0)) / Rad;
    }

    /// <summary>Daylight 0..1: 0 when the sun is 9 degrees or more below the horizon (night), 1 from +3 degrees up (day); smooth in between.</summary>
    public static double Daylight(double elevationDeg)
    {
        double t = Math.Clamp((elevationDeg + 9.0) / 12.0, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>Daylight at a place and an instant.</summary>
    public static double DaylightAt(double latDeg, double lonDeg, double unixMs) => Daylight(ElevationDeg(latDeg, lonDeg, unixMs));

    /// <summary>Interpolates a night factor: daylight 1 => 1 (no effect), daylight 0 => the full night value.</summary>
    public static double Blend(double nightFactor, double daylight) => 1.0 + (nightFactor - 1.0) * (1.0 - daylight);

    /// <summary>Night effect on a defensive unit: scale of the detection / engagement range and of the probability of a hit.</summary>
    public readonly record struct UnitNight(double RangeScale, double PkScale)
    {
        public bool IsNeutral => RangeScale == 1.0 && PkScale == 1.0;
    }

    /// <summary>
    /// Units that aim and see by eye or optics lose range and accuracy at night; radar-guided ones do not.
    /// (Illustrative: a gun group without thermal sights sees 60% as far and hits 25% less often.)
    /// </summary>
    public static UnitNight ForDefense(DefenseKind kind) => kind switch
    {
        DefenseKind.AaGun => new UnitNight(0.6, 0.75),
        DefenseKind.InterceptorBase => new UnitNight(1.0, 0.85),
        DefenseKind.Patrol => new UnitNight(1.0, 0.9),
        _ => new UnitNight(1.0, 1.0)
    };

    /// <summary>
    /// Night effect on the people exposed to an impact: Density = how many people are in the area, Fatal = the fraction of the exposed who die.
    /// City: more people at home, asleep and slower to reach shelter. Military base: reduced presence. Field: fewer people outdoors.
    /// (Illustrative.)
    /// </summary>
    public readonly record struct TerrainNight(double Density, double Fatal);

    public static TerrainNight ForTerrain(TerrainClass c) => c switch
    {
        TerrainClass.City => new TerrainNight(1.25, 1.2),
        TerrainClass.MilitaryBase => new TerrainNight(0.7, 1.0),
        _ => new TerrainNight(0.6, 1.0)
    };

    // ---- day / night variants of a scenario (precomputed runs for public mode) ----

    /// <summary>Date used by the time-of-day presets (near the equinox: day and night are about equally long everywhere).</summary>
    public const string PresetDate = "2026-03-20";

    /// <summary>The variants of a scenario: name (the suffix of the run id, "id.day") and the mean solar time at the center of the scenario.</summary>
    public static readonly (string Name, double Hour)[] Variants = { ("day", 12.0), ("night", 1.0) };

    /// <summary>UTC instant (ISO 8601, whole seconds) at which the mean solar time at a longitude is <paramref name="hour"/> on a date.</summary>
    public static string StartUtcForLocalSolarHour(double lonDeg, double hour, string date = PresetDate)
    {
        var day = DateTimeOffset.Parse(date + "T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        double ms = day.ToUnixTimeMilliseconds() + (hour - lonDeg / 15.0) * 3_600_000.0;
        var instant = DateTimeOffset.FromUnixTimeMilliseconds((long)(Math.Round(ms / 1000.0) * 1000.0));
        return instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Mean longitude of the targets (or of the launch sites when there are no targets); 0 for an empty scenario.</summary>
    public static double CenterLonDeg(Scenario s)
    {
        if (s.Targets.Count > 0)
            return s.Targets.Sum(t => t.LonDeg) / s.Targets.Count;
        if (s.LaunchSites.Count > 0)
            return s.LaunchSites.Sum(x => x.LonDeg) / s.LaunchSites.Count;
        return 0.0;
    }

    /// <summary>The scenario with the start time of a variant ("day" or "night"). Replaces any start time the scenario already has.</summary>
    public static Scenario WithVariant(Scenario s, string variant)
    {
        foreach (var (name, hour) in Variants)
            if (name == variant)
                return s with { StartTimeUtc = StartUtcForLocalSolarHour(CenterLonDeg(s), hour) };
        throw new ArgumentException($"Unknown variant: {variant}");
    }

    /// <summary>"ru-vs-ua.night" -> ("ru-vs-ua", "night"). A run id without a known variant suffix is its own base (variant empty).</summary>
    public static (string BaseId, string Variant) SplitVariantId(string id)
    {
        foreach (var (name, _) in Variants)
        {
            string suffix = "." + name;
            if (id.Length > suffix.Length && id.EndsWith(suffix, StringComparison.Ordinal))
                return (id[..^suffix.Length], name);
        }
        return (id, "");
    }
}
