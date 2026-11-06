using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sim.Core.Scenarios;

namespace Sim.Server.Services;

public sealed record SavedRunInfo(
    string RunId, string Seed, string ScenarioName, string EngineVersion, bool EngineMatches, DateTimeOffset CreatedUtc,
    bool HasEvents);

public sealed record ReplayCheckDto(bool Active, string RunId, bool Finished, bool Match, string Expected, string Actual);

/// <summary>The runs saved by RunStore: runs/&lt;RunId&gt;/{scenario.json, meta.json, events.jsonl, summary.json}.</summary>
public static class RunLibrary
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void ValidateId(string runId)
    {
        if (string.IsNullOrEmpty(runId) || runId.Length > 80 || !runId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
            throw new ArgumentException("Invalid run id.");
    }

    public static IReadOnlyList<SavedRunInfo> List(string runsDir)
    {
        var list = new List<SavedRunInfo>();
        if (!Directory.Exists(runsDir))
            return list;

        foreach (string dir in Directory.EnumerateDirectories(runsDir))
        {
            string metaPath = Path.Combine(dir, "meta.json");
            string scenarioPath = Path.Combine(dir, "scenario.json");
            if (!File.Exists(metaPath) || !File.Exists(scenarioPath))
                continue;
            try
            {
                var meta = JsonSerializer.Deserialize<RunMeta>(File.ReadAllText(metaPath), Options);
                if (meta is null || string.IsNullOrEmpty(meta.RunId))
                    continue;
                list.Add(new SavedRunInfo(
                    meta.RunId, meta.Seed.ToString(), ReadName(scenarioPath), meta.EngineVersion,
                    meta.EngineVersion == Sim.Core.EngineInfo.Version, meta.CreatedUtc,
                    File.Exists(Path.Combine(dir, "events.jsonl"))));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // broken folder: ignored
            }
        }
        return list.OrderByDescending(r => r.CreatedUtc).ToList();
    }

    public static (string ScenarioJson, RunMeta Meta) Load(string runsDir, string runId)
    {
        ValidateId(runId);
        string dir = Path.Combine(runsDir, runId);
        string metaPath = Path.Combine(dir, "meta.json");
        string scenarioPath = Path.Combine(dir, "scenario.json");
        if (!File.Exists(metaPath) || !File.Exists(scenarioPath))
            throw new FileNotFoundException($"Run '{runId}' does not exist.");
        var meta = JsonSerializer.Deserialize<RunMeta>(File.ReadAllText(metaPath), Options)
                   ?? throw new InvalidDataException("Empty meta.json.");
        return (File.ReadAllText(scenarioPath), meta);
    }

    private static string ReadName(string scenarioPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(scenarioPath));
        return doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
    }
}
