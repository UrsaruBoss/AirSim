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

    // Forma canonica: compacta, ordine fixa a proprietatilor (cea din declaratia tipurilor).
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
    /// Incarca un scenariu. Valurile pot referi un tip din catalog ("unit": "shahed-136") iar apararile la fel ("type": "iris-t-slm"):
    /// campurile tipului se copiaza in intrare, iar ce scrie scenariul peste ele are prioritate. Rezultatul e complet rezolvat
    /// (identitatea unei rulari depinde de valori, nu de catalogul de azi).
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
                throw new InvalidDataException("Scenariu invalid: JSON gol sau nu e un obiect.");

            ResolveReferences(root, "waves", "unit", "unitType", catalog?.Offense, nameFromType: false, "tipul de unitate");
            ResolveReferences(root, "defenses", "type", "typeId", catalog?.Defense, nameFromType: true, "tipul de aparare");

            scenario = JsonSerializer.Deserialize<Scenario>(root, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Scenariu invalid (JSON): {ex.Message}", ex);
        }

        if (scenario is null)
            throw new InvalidDataException("Scenariu invalid: JSON gol.");

        var errors = Validate(scenario);
        if (errors.Count > 0)
            throw new InvalidDataException("Scenariu invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));

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
                throw new InvalidDataException($"{where}: refera {what} '{id}', dar nu exista catalog.");
            if (!types.TryGetValue(id, out var type))
                throw new InvalidDataException($"{where}: {what} '{id}' nu exista in catalog.");

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

    /// <summary>JSON canonic: aceeasi continuu logic => acelasi text, indiferent de spatii sau ordinea proprietatilor din fisier.</summary>
    public static string ToCanonicalJson(Scenario scenario)
    {
        // deserializarea creeaza un SortedDictionary cu comparator implicit (dependent de cultura):
        // il refacem cu ordine ordinala, ca hash-ul sa fie identic pe orice masina
        var normalized = scenario with
        {
            Countries = new SortedDictionary<string, Sim.Core.Airspace.CountryRole>(scenario.Countries, StringComparer.Ordinal),
            Defenses = scenario.Defenses.Select(d => d with
            {
                PkByKind = d.PkByKind is null ? null : new SortedDictionary<string, double>(d.PkByKind, StringComparer.Ordinal),
                Engages = d.Engages?.Distinct().OrderBy(k => (int)k).ToList(), // ordinea nu conteaza semantic
                PrimaryKinds = d.PrimaryKinds?.Distinct().OrderBy(k => (int)k).ToList()
            }).ToList()
        };
        return JsonSerializer.Serialize(normalized, CanonicalOptions);
    }

    /// <summary>SHA-256 (hex, litere mici) al formei canonice. Face parte din identitatea unei rulari.</summary>
    public static string Hash(Scenario scenario)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ToCanonicalJson(scenario));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static IReadOnlyList<string> Validate(Scenario s)
    {
        var errors = new List<string>();

        if (!(s.DtSeconds > 0.0) || double.IsInfinity(s.DtSeconds))
            errors.Add("DtSeconds trebuie sa fie pozitiv si finit.");
        if (!(s.DurationSeconds > 0.0) || double.IsInfinity(s.DurationSeconds))
            errors.Add("DurationSeconds trebuie sa fie pozitiv si finit.");

        CheckUnique("LaunchSites", s.LaunchSites.Select(x => x.Id), errors);
        CheckUnique("Targets", s.Targets.Select(x => x.Id), errors);
        CheckUnique("Waves", s.Waves.Select(x => x.Id), errors);

        foreach (var site in s.LaunchSites)
            CheckCoords($"LaunchSite '{site.Id}'", site.LatDeg, site.LonDeg, errors);
        foreach (var t in s.Targets)
            CheckCoords($"Target '{t.Id}'", t.LatDeg, t.LonDeg, errors);
        foreach (var t in s.Targets)
        {
            if (!(t.RadiusM >= 0.0) || double.IsInfinity(t.RadiusM)) errors.Add($"Target '{t.Id}': RadiusM trebuie sa fie >= 0 si finit.");
            if (t.ImpactsToDestroy < 0) errors.Add($"Target '{t.Id}': ImpactsToDestroy nu poate fi negativ.");
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
                if (!siteIds.Contains(id)) errors.Add($"{who}: SiteId '{id}' nu exista.");
            }
            foreach (string id in waveTargets.Distinct())
            {
                if (!targetIds.Contains(id)) errors.Add($"{who}: TargetId '{id}' nu exista.");
            }
            if (!(w.FailureRate >= 0.0 && w.FailureRate <= 1.0)) errors.Add($"{who}: FailureRate trebuie sa fie intre 0 si 1.");
            if (!(w.EwResistance >= 0.0 && w.EwResistance <= 1.0)) errors.Add($"{who}: EwResistance trebuie sa fie intre 0 si 1.");
            if (!(w.PerceivedCostUsd >= 0.0) || double.IsInfinity(w.PerceivedCostUsd)) errors.Add($"{who}: PerceivedCostUsd trebuie sa fie >= 0 si finit.");
            if (w.Retarget && w.Kind == UnitKind.BallisticMissile) errors.Add($"{who}: rachetele balistice nu se pot redirectiona.");
            if (!(w.RetargetAtKm > 0.0) || double.IsInfinity(w.RetargetAtKm)) errors.Add($"{who}: RetargetAtKm trebuie sa fie > 0 si finit.");
            if (!(w.RetargetRangeKm > 0.0) || double.IsInfinity(w.RetargetRangeKm)) errors.Add($"{who}: RetargetRangeKm trebuie sa fie > 0 si finit.");
            if (w.Count <= 0) errors.Add($"{who}: Count trebuie sa fie > 0.");
            if (w.StartTimeSeconds < 0.0) errors.Add($"{who}: StartTimeSeconds nu poate fi negativ.");
            if (w.IntervalSeconds < 0.0) errors.Add($"{who}: IntervalSeconds nu poate fi negativ.");
            if (!(w.SpeedMps > 0.0)) errors.Add($"{who}: SpeedMps trebuie sa fie > 0.");
            if (w.SpreadMeters < 0.0) errors.Add($"{who}: SpreadMeters nu poate fi negativ.");
            if (!(w.RangeKm >= 0.0) || double.IsInfinity(w.RangeKm)) errors.Add($"{who}: RangeKm trebuie sa fie >= 0 si finit.");
            if (w.RangeKm > 0.0)
            {
                // toate perechile lansare -> tinta posibile; se raporteaza cea mai departata
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
                    errors.Add($"{who}: o tinta e la {worstKm:F0} km de punctul de lansare, peste raza de actiune de {w.RangeKm:F0} km.");
            }
            if (!(w.CostUsd >= 0.0) || double.IsInfinity(w.CostUsd)) errors.Add($"{who}: CostUsd trebuie sa fie >= 0 si finit.");
            if (!(w.ApogeeM >= 0.0) || double.IsInfinity(w.ApogeeM)) errors.Add($"{who}: ApogeeM trebuie sa fie >= 0 si finit.");
            if (!(w.WarheadKg >= 0.0) || double.IsInfinity(w.WarheadKg)) errors.Add($"{who}: WarheadKg trebuie sa fie >= 0 si finit.");
        }

        if (!(s.DefenseIntervalSeconds > 0.0) || double.IsInfinity(s.DefenseIntervalSeconds))
            errors.Add("DefenseIntervalSeconds trebuie sa fie pozitiv si finit.");

        CheckUnique("Defenses", s.Defenses.Select(x => x.Id), errors);
        foreach (var d in s.Defenses)
        {
            string who = $"Defense '{d.Id}'";
            CheckCoords(who, d.LatDeg, d.LonDeg, errors);
            if (!(d.RangeKm > 0.0) || double.IsInfinity(d.RangeKm)) errors.Add($"{who}: RangeKm trebuie sa fie pozitiv si finit.");
            if (!(d.MinAltM >= 0.0) || !(d.MaxAltM >= 0.0)) errors.Add($"{who}: MinAltM / MaxAltM nu pot fi negative.");
            if (d.MaxAltM > 0.0 && d.MinAltM > d.MaxAltM) errors.Add($"{who}: MinAltM nu poate depasi MaxAltM.");
            if (d.Kind == DefenseKind.Patrol)
            {
                if (!(d.PatrolRadiusKm >= 0.0) || double.IsInfinity(d.PatrolRadiusKm)) errors.Add($"{who}: PatrolRadiusKm trebuie sa fie >= 0 si finit.");
                if (!(d.PatrolSpeedMps > 0.0)) errors.Add($"{who}: PatrolSpeedMps trebuie sa fie > 0.");
                if (!(d.EnduranceSeconds > 0.0)) errors.Add($"{who}: EnduranceSeconds trebuie sa fie > 0.");
                if (!(d.TurnaroundSeconds >= 0.0) || double.IsInfinity(d.TurnaroundSeconds)) errors.Add($"{who}: TurnaroundSeconds trebuie sa fie >= 0 si finit.");
                if (!(d.ScrambleRangeKm > 0.0)) errors.Add($"{who}: ScrambleRangeKm trebuie sa fie > 0.");
                if (!s.Defenses.Any(x => x.Kind == DefenseKind.Airbase)) errors.Add($"{who}: patrula necesita cel putin o baza aeriana (kind: airbase).");
            }
            if (d.Kind == DefenseKind.InterceptorBase)
            {
                if (!(d.InterceptorSpeedMps > 0.0)) errors.Add($"{who}: InterceptorSpeedMps trebuie sa fie > 0.");
                if (!(d.MaxFlightSeconds > 0.0)) errors.Add($"{who}: MaxFlightSeconds trebuie sa fie > 0.");
            }
            if (!(d.ShotCostUsd >= 0.0) || double.IsInfinity(d.ShotCostUsd)) errors.Add($"{who}: ShotCostUsd trebuie sa fie >= 0 si finit.");
            if (!(d.CostUsd >= 0.0) || double.IsInfinity(d.CostUsd)) errors.Add($"{who}: CostUsd trebuie sa fie >= 0 si finit.");
            if (!(d.MaxCostRatio >= 0.0) || double.IsInfinity(d.MaxCostRatio)) errors.Add($"{who}: MaxCostRatio trebuie sa fie >= 0 si finit.");
            if (!(d.LeakerDistanceKm >= 0.0) || double.IsInfinity(d.LeakerDistanceKm)) errors.Add($"{who}: LeakerDistanceKm trebuie sa fie >= 0 si finit.");
            if (!(d.JamRatePerMinute >= 0.0) || double.IsInfinity(d.JamRatePerMinute)) errors.Add($"{who}: JamRatePerMinute trebuie sa fie >= 0 si finit.");
            if (d.JamCapacity < 0) errors.Add($"{who}: JamCapacity nu poate fi negativ.");
            if (!(d.JamFalloff >= 0.0 && d.JamFalloff <= 1.0)) errors.Add($"{who}: JamFalloff trebuie sa fie intre 0 si 1.");
            if (d.Kind == DefenseKind.Ew && !(d.JamRatePerMinute > 0.0)) errors.Add($"{who}: un sistem de razboi electronic (ew) cere JamRatePerMinute > 0.");
            if (!(d.LinkRangeKm >= 0.0) || double.IsInfinity(d.LinkRangeKm)) errors.Add($"{who}: LinkRangeKm trebuie sa fie >= 0 si finit.");
            if (!(d.SelfTrackKm >= 0.0) || double.IsInfinity(d.SelfTrackKm)) errors.Add($"{who}: SelfTrackKm trebuie sa fie >= 0 si finit.");
            if (!(d.HardnessKg >= 0.0) || double.IsInfinity(d.HardnessKg)) errors.Add($"{who}: HardnessKg trebuie sa fie >= 0 si finit.");
            if (!(d.SizeM >= 0.0) || double.IsInfinity(d.SizeM)) errors.Add($"{who}: SizeM trebuie sa fie >= 0 si finit.");
            if (d.ReserveRounds < 0) errors.Add($"{who}: ReserveRounds nu poate fi negativ.");
            if (d.MaxRoundsPerTarget < 1) errors.Add($"{who}: MaxRoundsPerTarget trebuie sa fie >= 1.");
            if (d.PkByKind is not null)
            {
                foreach (var (kindName, pk) in d.PkByKind)
                {
                    if (!Enum.TryParse<UnitKind>(kindName, ignoreCase: true, out _))
                        errors.Add($"{who}: PkByKind are un tip necunoscut '{kindName}'.");
                    if (!(pk >= 0.0 && pk <= 1.0))
                        errors.Add($"{who}: PkByKind['{kindName}'] trebuie sa fie intre 0 si 1.");
                }
            }
            if (d.Kind != DefenseKind.Radar)
            {
                if (d.Magazine < 0) errors.Add($"{who}: Magazine nu poate fi negativ.");
                if (!(d.Pk >= 0.0 && d.Pk <= 1.0)) errors.Add($"{who}: Pk trebuie sa fie intre 0 si 1.");
                if (!(d.ReloadSeconds >= 0.0) || double.IsInfinity(d.ReloadSeconds)) errors.Add($"{who}: ReloadSeconds trebuie sa fie >= 0 si finit.");
            }
        }

        if (s.Countries.Count > 0 && string.IsNullOrWhiteSpace(s.AirspaceMap))
            errors.Add("Countries cere AirspaceMap (numele hartii de tari).");
        if (!string.IsNullOrEmpty(s.AirspaceMap) && !IsSafeName(s.AirspaceMap))
            errors.Add("AirspaceMap trebuie sa fie un nume simplu (litere, cifre, '-', '_', '.').");
        foreach (string code in s.Countries.Keys)
        {
            if (code.Length is < 2 or > 3 || !code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                errors.Add($"Countries: cod de tara invalid '{code}' (2-3 litere mari sau cifre, ex. RO).");
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
            errors.Add($"{what}: id duplicat '{group.Key}'.");
        if (ids.Any(string.IsNullOrWhiteSpace))
            errors.Add($"{what}: id gol.");
    }

    private static void CheckCoords(string who, double lat, double lon, List<string> errors)
    {
        if (lat < -90.0 || lat > 90.0) errors.Add($"{who}: latitudine in afara [-90, 90].");
        if (lon < -180.0 || lon > 180.0) errors.Add($"{who}: longitudine in afara [-180, 180].");
    }
}
