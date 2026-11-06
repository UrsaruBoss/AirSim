using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sim.Core.Catalog;
using Sim.Core.Geo;

namespace Sim.Core.Scenarios;

public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Canonical form: compact, fixed property order (the one from the type declarations).
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions PrettyOptions = new(CanonicalOptions)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Loads a scenario. Waves may reference a catalog type ("unit": "shahed-136") and so may defenses ("type": "iris-t-slm"):
    /// the type's fields are copied into the entry, and whatever the scenario writes on top takes precedence. The result is fully resolved
    /// (a run's identity depends on values, not on today's catalog).
    /// </summary>
    public static Scenario FromJson(string json, UnitCatalog? catalog = null)
    {
        Scenario? scenario;
        try
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) as JsonObject;

            if (root is null)
                throw new InvalidDataException("Invalid scenario: empty JSON or not an object.");

            ResolveReferences(root, "waves", "unit", "unitType", catalog?.Offense, nameFromType: false, "the unit type");
            ResolveReferences(root, "defenses", "type", "typeId", catalog?.Defense, nameFromType: true, "the defense type");

            scenario = JsonSerializer.Deserialize<Scenario>(root, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid scenario (JSON): {ex.Message}", ex);
        }

        if (scenario is null)
            throw new InvalidDataException("Invalid scenario: empty JSON.");

        var errors = Validate(scenario);
        if (errors.Count > 0)
            throw new InvalidDataException("Invalid scenario:" + Environment.NewLine + string.Join(Environment.NewLine, errors));

        return scenario;
    }

    public static Scenario FromFile(string path, UnitCatalog? catalog = null) => FromJson(File.ReadAllText(path), catalog);

    private static void ResolveReferences(
        JsonObject root, string arrayName, string refKey, string resolvedKey,
        IReadOnlyDictionary<string, JsonObject>? types, bool nameFromType, string what)
    {
        if (FindProperty(root, arrayName) is not JsonArray array)
            return;

        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject entry || FindProperty(entry, refKey) is not JsonNode refNode)
                continue;

            string id = refNode.GetValueKind() == JsonValueKind.String ? refNode.GetValue<string>() : "";
            string where = $"{arrayName}[{i}]";
            if (types is null)
                throw new InvalidDataException($"{where}: references {what} '{id}', but there is no catalog.");
            if (!types.TryGetValue(id, out var type))
                throw new InvalidDataException($"{where}: {what} '{id}' does not exist in the catalog.");

            var merged = new JsonObject();
            foreach (var (key, value) in type)
            {
                if (key is "id" or "notes" or "source" || (key == "name" && !nameFromType))
                    continue;
                merged[CamelCase(key)] = value?.DeepClone();
            }

            foreach (var (key, value) in entry)
            {
                if (key.Equals(refKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                merged[CamelCase(key)] = value?.DeepClone();
            }

            merged[resolvedKey] = id;
            array[i] = merged;
        }
    }

    private static JsonNode? FindProperty(JsonObject obj, string name)
    {
        foreach (var (key, value) in obj)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    private static string CamelCase(string key) => key.Length == 0 ? key : char.ToLowerInvariant(key[0]) + key.Substring(1);

    public static string ToJson(Scenario scenario) => JsonSerializer.Serialize(scenario, PrettyOptions);

    /// <summary>Canonical JSON: the same logical content => the same text, regardless of whitespace or property order in the file.</summary>
    public static string ToCanonicalJson(Scenario scenario)
    {
        // deserialization creates a SortedDictionary with the default (culture-dependent) comparer:
        // rebuild it with ordinal order so the hash is identical on any machine
        var normalized = scenario with
        {
            Countries = new SortedDictionary<string, Sim.Core.Airspace.CountryRole>(scenario.Countries, StringComparer.Ordinal),
            Defenses = scenario.Defenses.Select(d => d with
            {
                PkByKind = d.PkByKind is null ? null : new SortedDictionary<string, double>(d.PkByKind, StringComparer.Ordinal),
                Engages = d.Engages?.Distinct().OrderBy(k => (int)k).ToList(), // order does not matter semantically
                PrimaryKinds = d.PrimaryKinds?.Distinct().OrderBy(k => (int)k).ToList()
            }).ToList()
        };
        return JsonSerializer.Serialize(normalized, CanonicalOptions);
    }

    /// <summary>SHA-256 (lowercase hex) of the canonical form. Part of a run's identity.</summary>
    public static string Hash(Scenario scenario)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ToCanonicalJson(scenario));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static IReadOnlyList<string> Validate(Scenario s)
    {
        var errors = new List<string>();

        if (!(s.DtSeconds > 0.0) || double.IsInfinity(s.DtSeconds))
            errors.Add("DtSeconds must be positive and finite.");
        if (!(s.DurationSeconds > 0.0) || double.IsInfinity(s.DurationSeconds))
            errors.Add("DurationSeconds must be positive and finite.");
        if (s.StartTimeUtc is not null && !Sim.Core.Sun.DayNight.TryParseUtc(s.StartTimeUtc, out _))
            errors.Add("StartTimeUtc is not a valid ISO 8601 date-time (e.g. 2026-03-01T22:30:00Z).");
        if (s.Weather is not null && !Sim.Core.Climate.WeatherModel.IsKnown(s.Weather))
            errors.Add($"Weather '{s.Weather}' is not a known preset ({string.Join(", ", Sim.Core.Climate.WeatherModel.Presets)}).");

        CheckUnique("LaunchSites", s.LaunchSites.Select(x => x.Id), errors);
        CheckUnique("Targets", s.Targets.Select(x => x.Id), errors);
        CheckUnique("Waves", s.Waves.Select(x => x.Id), errors);

        foreach (var site in s.LaunchSites)
            CheckCoords($"LaunchSite '{site.Id}'", site.LatDeg, site.LonDeg, errors);
        foreach (var t in s.Targets)
            CheckCoords($"Target '{t.Id}'", t.LatDeg, t.LonDeg, errors);
        foreach (var t in s.Targets)
        {
            if (!(t.RadiusM >= 0.0) || double.IsInfinity(t.RadiusM)) errors.Add($"Target '{t.Id}': RadiusM must be >= 0 and finite.");
            if (t.ImpactsToDestroy < 0) errors.Add($"Target '{t.Id}': ImpactsToDestroy cannot be negative.");
        }

        var siteIds = s.LaunchSites.Select(x => x.Id).ToHashSet();
        var targetIds = s.Targets.Select(x => x.Id).ToHashSet();
        var siteById = s.LaunchSites.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var targetById = s.Targets.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var w in s.Waves)
        {
            string who = $"Wave '{w.Id}'";
            var waveSites = w.SiteIds is { Count: > 0 } ? w.SiteIds : new List<string> { w.SiteId };
            var waveTargets = w.TargetIds is { Count: > 0 } ? w.TargetIds : new List<string> { w.TargetId };
            foreach (string id in waveSites.Distinct())
            {
                if (!siteIds.Contains(id)) errors.Add($"{who}: SiteId '{id}' does not exist.");
            }
            foreach (string id in waveTargets.Distinct())
            {
                if (!targetIds.Contains(id)) errors.Add($"{who}: TargetId '{id}' does not exist.");
            }
            if (!(w.FailureRate >= 0.0 && w.FailureRate <= 1.0)) errors.Add($"{who}: FailureRate must be between 0 and 1.");
            if (!(w.EwResistance >= 0.0 && w.EwResistance <= 1.0)) errors.Add($"{who}: EwResistance must be between 0 and 1.");
            if (!(w.PerceivedCostUsd >= 0.0) || double.IsInfinity(w.PerceivedCostUsd)) errors.Add($"{who}: PerceivedCostUsd must be >= 0 and finite.");
            if (w.Retarget && w.Kind == UnitKind.BallisticMissile) errors.Add($"{who}: ballistic missiles cannot be retargeted.");
            if (!(w.RetargetAtKm > 0.0) || double.IsInfinity(w.RetargetAtKm)) errors.Add($"{who}: RetargetAtKm must be > 0 and finite.");
            if (!(w.RetargetRangeKm > 0.0) || double.IsInfinity(w.RetargetRangeKm)) errors.Add($"{who}: RetargetRangeKm must be > 0 and finite.");
            if (w.Count <= 0) errors.Add($"{who}: Count must be > 0.");
            if (w.StartTimeSeconds < 0.0) errors.Add($"{who}: StartTimeSeconds cannot be negative.");
            if (w.IntervalSeconds < 0.0) errors.Add($"{who}: IntervalSeconds cannot be negative.");
            if (!(w.SpeedMps > 0.0)) errors.Add($"{who}: SpeedMps must be > 0.");
            if (w.SpreadMeters < 0.0) errors.Add($"{who}: SpreadMeters cannot be negative.");
            if (!(w.RangeKm >= 0.0) || double.IsInfinity(w.RangeKm)) errors.Add($"{who}: RangeKm must be >= 0 and finite.");
            if (w.RangeKm > 0.0)
            {
                // all possible launch -> target pairs; the farthest one is reported
                double worstKm = 0.0;
                foreach (string sid in waveSites.Distinct())
                {
                    foreach (string tid in waveTargets.Distinct())
                    {
                        if (!siteById.TryGetValue(sid, out var rangeSite) || !targetById.TryGetValue(tid, out var rangeTarget))
                            continue;
                        double km = Wgs84.GreatCircleDistanceM(new GeoPoint(rangeSite.LatDeg, rangeSite.LonDeg), new GeoPoint(rangeTarget.LatDeg, rangeTarget.LonDeg)) / 1000.0;
                        worstKm = Math.Max(worstKm, km);
                    }
                }
                if (worstKm > w.RangeKm)
                    errors.Add($"{who}: a target is {worstKm:F0} km from the launch point, beyond the operational range of {w.RangeKm:F0} km.");
            }
            if (!(w.CostUsd >= 0.0) || double.IsInfinity(w.CostUsd)) errors.Add($"{who}: CostUsd must be >= 0 and finite.");
            if (!(w.ApogeeM >= 0.0) || double.IsInfinity(w.ApogeeM)) errors.Add($"{who}: ApogeeM must be >= 0 and finite.");
            if (!(w.WarheadKg >= 0.0) || double.IsInfinity(w.WarheadKg)) errors.Add($"{who}: WarheadKg must be >= 0 and finite.");
        }

        if (!(s.DefenseIntervalSeconds > 0.0) || double.IsInfinity(s.DefenseIntervalSeconds))
            errors.Add("DefenseIntervalSeconds must be positive and finite.");

        CheckUnique("Defenses", s.Defenses.Select(x => x.Id), errors);
        foreach (var d in s.Defenses)
        {
            string who = $"Defense '{d.Id}'";
            CheckCoords(who, d.LatDeg, d.LonDeg, errors);
            if (!(d.RangeKm > 0.0) || double.IsInfinity(d.RangeKm)) errors.Add($"{who}: RangeKm must be positive and finite.");
            if (!(d.MinAltM >= 0.0) || !(d.MaxAltM >= 0.0)) errors.Add($"{who}: MinAltM / MaxAltM cannot be negative.");
            if (d.MaxAltM > 0.0 && d.MinAltM > d.MaxAltM) errors.Add($"{who}: MinAltM cannot exceed MaxAltM.");
            if (d.Kind == DefenseKind.Patrol)
            {
                if (!(d.PatrolRadiusKm >= 0.0) || double.IsInfinity(d.PatrolRadiusKm)) errors.Add($"{who}: PatrolRadiusKm must be >= 0 and finite.");
                if (!(d.PatrolSpeedMps > 0.0)) errors.Add($"{who}: PatrolSpeedMps must be > 0.");
                if (!(d.EnduranceSeconds > 0.0)) errors.Add($"{who}: EnduranceSeconds must be > 0.");
                if (!(d.TurnaroundSeconds >= 0.0) || double.IsInfinity(d.TurnaroundSeconds)) errors.Add($"{who}: TurnaroundSeconds must be >= 0 and finite.");
                if (!(d.ScrambleRangeKm > 0.0)) errors.Add($"{who}: ScrambleRangeKm must be > 0.");
                if (!s.Defenses.Any(x => x.Kind == DefenseKind.Airbase)) errors.Add($"{who}: a patrol requires at least one air base (kind: airbase).");
            }
            if (d.Kind == DefenseKind.InterceptorBase)
            {
                if (!(d.InterceptorSpeedMps > 0.0)) errors.Add($"{who}: InterceptorSpeedMps must be > 0.");
                if (!(d.MaxFlightSeconds > 0.0)) errors.Add($"{who}: MaxFlightSeconds must be > 0.");
            }
            if (!(d.ShotCostUsd >= 0.0) || double.IsInfinity(d.ShotCostUsd)) errors.Add($"{who}: ShotCostUsd must be >= 0 and finite.");
            if (!(d.CostUsd >= 0.0) || double.IsInfinity(d.CostUsd)) errors.Add($"{who}: CostUsd must be >= 0 and finite.");
            if (!(d.MaxCostRatio >= 0.0) || double.IsInfinity(d.MaxCostRatio)) errors.Add($"{who}: MaxCostRatio must be >= 0 and finite.");
            if (!(d.LeakerDistanceKm >= 0.0) || double.IsInfinity(d.LeakerDistanceKm)) errors.Add($"{who}: LeakerDistanceKm must be >= 0 and finite.");
            if (!(d.JamRatePerMinute >= 0.0) || double.IsInfinity(d.JamRatePerMinute)) errors.Add($"{who}: JamRatePerMinute must be >= 0 and finite.");
            if (d.JamCapacity < 0) errors.Add($"{who}: JamCapacity cannot be negative.");
            if (!(d.JamFalloff >= 0.0 && d.JamFalloff <= 1.0)) errors.Add($"{who}: JamFalloff must be between 0 and 1.");
            if (d.Kind == DefenseKind.Ew && !(d.JamRatePerMinute > 0.0)) errors.Add($"{who}: an electronic warfare (ew) system requires JamRatePerMinute > 0.");
            if (!(d.LinkRangeKm >= 0.0) || double.IsInfinity(d.LinkRangeKm)) errors.Add($"{who}: LinkRangeKm must be >= 0 and finite.");
            if (!(d.SelfTrackKm >= 0.0) || double.IsInfinity(d.SelfTrackKm)) errors.Add($"{who}: SelfTrackKm must be >= 0 and finite.");
            if (!(d.HardnessKg >= 0.0) || double.IsInfinity(d.HardnessKg)) errors.Add($"{who}: HardnessKg must be >= 0 and finite.");
            if (!(d.SizeM >= 0.0) || double.IsInfinity(d.SizeM)) errors.Add($"{who}: SizeM must be >= 0 and finite.");
            if (d.ReserveRounds < 0) errors.Add($"{who}: ReserveRounds cannot be negative.");
            if (d.MaxRoundsPerTarget < 1) errors.Add($"{who}: MaxRoundsPerTarget must be >= 1.");
            if (d.PkAltitude is { } curve)
            {
                if (curve.Count == 0) errors.Add($"{who}: PkAltitude cannot be empty.");
                for (int k = 0; k < curve.Count; k++)
                {
                    if (double.IsNaN(curve[k].AltM) || double.IsInfinity(curve[k].AltM) || curve[k].AltM < 0.0)
                        errors.Add($"{who}: PkAltitude[{k}].AltM must be >= 0 and finite.");
                    if (!(curve[k].Factor >= 0.0 && curve[k].Factor <= 1.0))
                        errors.Add($"{who}: PkAltitude[{k}].Factor must be between 0 and 1.");
                    if (k > 0 && !(curve[k].AltM > curve[k - 1].AltM))
                        errors.Add($"{who}: PkAltitude must be in strictly ascending order of altitude.");
                }
            }
            if (d.PkByKind is not null)
            {
                foreach (var (kindName, pk) in d.PkByKind)
                {
                    if (!Enum.TryParse<UnitKind>(kindName, ignoreCase: true, out _))
                        errors.Add($"{who}: PkByKind are un tip necunoscut '{kindName}'.");
                    if (!(pk >= 0.0 && pk <= 1.0))
                        errors.Add($"{who}: PkByKind['{kindName}'] must be between 0 and 1.");
                }
            }
            if (d.Kind != DefenseKind.Radar)
            {
                if (d.Magazine < 0) errors.Add($"{who}: Magazine cannot be negative.");
                if (!(d.Pk >= 0.0 && d.Pk <= 1.0)) errors.Add($"{who}: Pk must be between 0 and 1.");
                if (!(d.ReloadSeconds >= 0.0) || double.IsInfinity(d.ReloadSeconds)) errors.Add($"{who}: ReloadSeconds must be >= 0 and finite.");
            }
        }

        if (s.Countries.Count > 0 && string.IsNullOrWhiteSpace(s.AirspaceMap))
            errors.Add("Countries requires AirspaceMap (the name of the country map).");
        if (!string.IsNullOrEmpty(s.AirspaceMap) && !IsSafeName(s.AirspaceMap))
            errors.Add("AirspaceMap must be a simple name (letters, digits, '-', '_', '.').");
        foreach (string code in s.Countries.Keys)
        {
            if (code.Length is < 2 or > 3 || !code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                errors.Add($"Countries: invalid country code '{code}' (2-3 capital letters or digits, e.g. RO).");
        }

        return errors;
    }

    private static bool IsSafeName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length < 100
        && !name.Contains("..")
        && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    private static void CheckUnique(string what, IEnumerable<string> ids, List<string> errors)
    {
        foreach (var group in ids.GroupBy(x => x).Where(g => g.Count() > 1))
            errors.Add($"{what}: duplicate id '{group.Key}'.");
        if (ids.Any(string.IsNullOrWhiteSpace))
            errors.Add($"{what}: empty id.");
    }

    private static void CheckCoords(string who, double lat, double lon, List<string> errors)
    {
        if (lat < -90.0 || lat > 90.0) errors.Add($"{who}: latitudine in afara [-90, 90].");
        if (lon < -180.0 || lon > 180.0) errors.Add($"{who}: longitudine in afara [-180, 180].");
    }
}
