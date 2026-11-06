using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sim.Core;
using Sim.Core.Scenarios;
using Sim.Core.Sun;
using Sim.Server.Models;

namespace Sim.Server.Services;

/// <summary>A precomputed run, ready to serve. The key = hash(scenario, seed, engine version, step) and changes whenever any of them changes.</summary>
public sealed record PrecomputedEntry(
    string Id, string Name, string Key, string Seed, string EngineVersion, string ScenarioHash,
    double StepS, double Dt, double DurationS, int TotalUnits, long BinBytes, string DataPath, string BinPath);

/// <summary>
/// Reads the "precomputed" folder once, at startup. A run is accepted only if the engine version
/// and the current scenario hash match those in the meta; otherwise it is ignored (regenerate it with `precompute`).
/// </summary>
public sealed class PrecomputedStore
{
    private readonly Dictionary<string, PrecomputedEntry> _entries = new(StringComparer.Ordinal);

    public PrecomputedStore(SimOptions options, SimPaths paths, CatalogProvider catalog, ILogger<PrecomputedStore> log)
    {
        if (!Directory.Exists(options.PrecomputedDir))
        {
            log.LogWarning("The precomputed runs folder does not exist: {Dir}", options.PrecomputedDir);
            return;
        }

        foreach (string metaPath in Directory.EnumerateFiles(options.PrecomputedDir, "*.meta.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            string id = Path.GetFileName(metaPath)[..^".meta.json".Length];
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                var r = doc.RootElement;
                string engine = r.GetProperty("engineVersion").GetString() ?? "";
                string hash = r.GetProperty("scenarioHash").GetString() ?? "";
                string dataPath = Path.Combine(options.PrecomputedDir, id + ".data.json");
                string binPath = Path.Combine(options.PrecomputedDir, id + ".bin");

                if (!ScenarioCatalog.IsSafeId(id) || !File.Exists(dataPath) || !File.Exists(binPath))
                {
                    log.LogWarning("Incomplete precomputed run: {Id}", id);
                    continue;
                }
                if (engine != EngineInfo.Version)
                {
                    log.LogWarning("Stale precomputed run ({Id}): engine {Saved}, current {Now}. Regenerate it with `precompute`.", id, engine, EngineInfo.Version);
                    continue;
                }
                // "id.night" is the scenario "id" started at night: its hash is the one of that variant
                var (baseId, variant) = DayNight.SplitVariantId(id);
                string scenarioFile = ScenarioCatalog.PathFor(paths.ScenariosDir, baseId);
                if (!File.Exists(scenarioFile) || ScenarioLoader.Hash(LoadVariant(scenarioFile, variant, catalog)) != hash)
                {
                    log.LogWarning("Precomputed run does not match the current scenario: {Id}. Regenerate it with `precompute`.", id);
                    continue;
                }

                _entries[id] = new PrecomputedEntry(
                    id, r.GetProperty("name").GetString() ?? id, r.GetProperty("key").GetString() ?? "",
                    r.GetProperty("seed").GetString() ?? "", engine, hash,
                    r.GetProperty("stepS").GetDouble(), r.GetProperty("dt").GetDouble(), r.GetProperty("durationS").GetDouble(),
                    r.GetProperty("totalUnits").GetInt32(), r.GetProperty("binBytes").GetInt64(), dataPath, binPath);
            }
            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidDataException)
            {
                log.LogWarning(ex, "Unreadable meta: {Path}", metaPath);
            }
        }
        log.LogInformation("Precomputed runs available: {Count}", _entries.Count);
    }

    private static Scenario LoadVariant(string file, string variant, CatalogProvider catalog)
    {
        var scenario = ScenarioLoader.FromFile(file, catalog.Get());
        return variant.Length == 0 ? scenario : DayNight.WithVariant(scenario, variant);
    }

    public bool Has(string id) => _entries.ContainsKey(id);

    /// <summary>The scenario has a precomputed run as it is or in any of its day / night variants.</summary>
    public bool HasAny(string baseId) =>
        _entries.ContainsKey(baseId) || DayNight.Variants.Any(v => _entries.ContainsKey(baseId + "." + v.Name));

    public PrecomputedEntry? Get(string id) => _entries.GetValueOrDefault(id);

    public IReadOnlyList<object> List() =>
        _entries.Values.OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => (object)new { e.Id, e.Name, e.Key, e.Seed, e.DurationS, e.TotalUnits, e.StepS, e.Dt, e.BinBytes })
            .ToList();
}
