using System.IO;
using System.Text;
using System.Text.Json;
using Sim.Core.Analytics;
using Sim.Core.Events;
using Sim.Core.Scenarios;

namespace Sim.Core.Runs;

/// <summary>
/// Saves a run to disk: runs/&lt;RunId&gt;/{scenario.json, meta.json, events.jsonl, summary.json}.
/// Replay and statistics can be rebuilt from these without a database.
/// </summary>
public static class RunStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private sealed record EventLine(long Tick, string Type, int Id, double LatDeg, double LonDeg, double AltM, string? Code);

    /// <summary>Returns the run folder. If it already exists (same scenario + seed), the files are rewritten.</summary>
    public static string Save(string runsRoot, Scenario scenario, RunMeta meta, EventLog log)
    {
        string dir = Path.Combine(runsRoot, meta.RunId);
        Directory.CreateDirectory(dir);

        var finalMeta = meta with { EventLogHash = log.Hash() };

        File.WriteAllText(Path.Combine(dir, "scenario.json"), ScenarioLoader.ToJson(scenario), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(finalMeta, Options), Encoding.UTF8);

        File.WriteAllText(Path.Combine(dir, "summary.json"),
            JsonSerializer.Serialize(RunAnalyzer.Analyze(scenario, log.Events, meta.Seed), Options), Encoding.UTF8);

        using var writer = new StreamWriter(Path.Combine(dir, "events.jsonl"), append: false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (var e in log.Events)
        {
            var line = new EventLine(e.Tick, e.Type.WireName(), e.EntityId,
                e.Position.LatDeg, e.Position.LonDeg, e.Position.AltM, e.Code);
            writer.WriteLine(JsonSerializer.Serialize(line, LineOptions));
        }

        return dir;
    }
}
