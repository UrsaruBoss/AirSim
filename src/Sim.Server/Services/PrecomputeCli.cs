using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sim.Core;
using Sim.Core.Airspace;
using Sim.Core.Analytics;
using Sim.Core.Entities;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Sim.Core.Sun;
using Sim.Server.Models;

namespace Sim.Server.Services;

/// <summary>
/// Generates precomputed runs for public mode:
/// dotnet run -c Release --project src/Sim.Server -- precompute [scenario=all|id] [seed=1] [maxmb=30] [out=path] [variants=day,night|none] [sens=16] [threads=8] [resume=on]
/// By default every scenario is run twice, by day and by night (same seed): the files are <c>id.day.*</c> and <c>id.night.*</c>.
/// With <c>variants=none</c> the scenario is run as it is (<c>id.*</c>).
/// <c>sens=N</c> (default 16, 0 = off) also runs the one-at-a-time sensitivity analysis (N runs per batch) and stores it in the summary of each run;
/// it costs (1 + 2 x factors) x N extra simulations per run.
/// <c>threads=N</c> (default: all cores) runs up to N scenario variants at the same time (each one single-threaded when there are several).
/// <c>resume=on</c> skips the runs that are already complete for the same scenario, seed, engine version and <c>sens</c> (a run is complete once its meta file exists).
/// For each scenario: <c>id.bin</c> (snapshots at a fixed step, same format as live), <c>id.data.json</c> (per-frame counters,
/// events, summary) and <c>id.meta.json</c> (key, engine version, scenario hash). The run is done twice and the log hashes
/// must match (determinism check); the step is the smallest of {1,2,5,10,20,30} s that fits in maxmb.
/// </summary>
public static class PrecomputeCli
{
    private static readonly double[] Steps = { 1, 2, 5, 10, 20, 30 };

    public static int Run(string[] args)
    {
        try
        {
            var opts = args.Select(a => a.Split('=', 2)).Where(p => p.Length == 2)
                           .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());

            string which = opts.GetValueOrDefault("scenario", "all");
            ulong seed = opts.TryGetValue("seed", out var s) ? ulong.Parse(s) : 1UL;
            double maxMb = opts.TryGetValue("maxmb", out var m) ? double.Parse(m, CultureInfo.InvariantCulture) : 30.0;
            int sensRuns = opts.TryGetValue("sens", out var sr) ? int.Parse(sr, CultureInfo.InvariantCulture) : 16;
            if (sensRuns < 0)
                throw new ArgumentException("sens must be >= 0.");
            int threads = opts.TryGetValue("threads", out var th) ? int.Parse(th, CultureInfo.InvariantCulture) : Environment.ProcessorCount;
            if (threads < 1)
                throw new ArgumentException("threads must be >= 1.");
            bool resume = opts.TryGetValue("resume", out var rs) && !rs.Equals("off", StringComparison.OrdinalIgnoreCase) && rs != "0";

            // "day,night" (default) | "none" | any subset of the known variants
            string variantOpt = opts.GetValueOrDefault("variants", string.Join(',', DayNight.Variants.Select(v => v.Name)));
            string[] variants = variantOpt.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? new[] { "" }
                : variantOpt.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string v in variants)
                if (v.Length > 0 && !DayNight.Variants.Any(x => x.Name == v))
                    throw new ArgumentException($"Unknown variant '{v}' (use day, night or none).");

            string root = FindUp(Directory.GetCurrentDirectory()) ?? FindUp(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();
            var paths = new SimPaths(
                Path.Combine(root, "scenarios"), Path.Combine(root, "runs"),
                Path.Combine(root, "data", "countries"), Path.Combine(root, "data", "catalog"));
            string outDir = opts.TryGetValue("out", out var o) ? Path.GetFullPath(o) : Path.Combine(root, "precomputed");
            Directory.CreateDirectory(outDir);

            var catalog = new CatalogProvider(paths);
            var airspaces = new AirspaceCatalog(paths);

            var ids = which.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? ScenarioCatalog.List(paths.ScenariosDir, catalog.Get).Where(i => i.Valid).Select(i => i.Id).ToList()
                : new List<string> { which };

            // every (output id, scenario) to generate; the files of different runs are independent, so they can be made in parallel
            var work = new List<(string Id, Scenario Scenario)>();
            foreach (string id in ids)
            {
                if (!ScenarioCatalog.IsSafeId(id)) throw new ArgumentException($"Invalid id: {id}");
                string file = ScenarioCatalog.PathFor(paths.ScenariosDir, id);
                if (!File.Exists(file)) throw new FileNotFoundException($"Scenario does not exist: {file}");
                var scenario = ScenarioLoader.FromFile(file, catalog.Get());
                foreach (string variant in variants)
                    work.Add(variant.Length == 0 ? (id, scenario) : (id + "." + variant, DayNight.WithVariant(scenario, variant)));
            }

            if (resume)
            {
                int before = work.Count;
                work = work.Where(w => !IsComplete(outDir, w.Id, w.Scenario, seed, sensRuns)).ToList();
                Console.WriteLine($"Resume: {before - work.Count} of {before} runs are already complete, {work.Count} left.");
            }

            int jobs = Math.Max(1, Math.Min(threads, work.Count));
            int innerThreads = Math.Max(1, threads / jobs); // threads each run may use for its sensitivity batches
            Console.WriteLine($"Generating {work.Count} runs, {jobs} at a time ({innerThreads} thread(s) each).");

            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
            System.Threading.Tasks.Parallel.ForEach(work, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = jobs }, w =>
            {
                try
                {
                    Generate(w.Id, w.Scenario, seed, airspaces, outDir, maxMb, sensRuns, innerThreads);
                }
                catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidDataException or FormatException or InvalidOperationException or IOException)
                {
                    failures.Add($"[{w.Id}] {ex.Message}");
                }
            });
            if (!failures.IsEmpty)
            {
                foreach (string f in failures.OrderBy(x => x, StringComparer.Ordinal))
                    Console.Error.WriteLine(f);
                return 1;
            }
            Console.WriteLine($"Done. Results in {outDir}");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidDataException or FormatException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int CountFlying(Simulation sim)
    {
        var drones = sim.Drones;
        int n = 0;
        for (int i = 0; i < drones.Length; i++)
            if (drones[i].Status == EntityStatus.Flying) n++;
        return n;
    }

    private static double NextDue(double t, double step) => (Math.Floor(t / step) + 1) * step;

    private static void Generate(string id, Scenario scenario, ulong seed, AirspaceCatalog airspaces, string outDir, double maxMb, int sensRuns, int threads)
    {
        Simulation NewSim() => new(scenario, seed, string.IsNullOrEmpty(scenario.AirspaceMap) ? null : airspaces.Create(scenario.AirspaceMap, scenario.Countries));

        Console.WriteLine($"[{id}] step 1/2: measuring the size...");
        var sim1 = NewSim();
        var next = new double[Steps.Length];
        var bytes = new double[Steps.Length];
        while (true)
        {
            double t = sim1.Clock.Time;
            int count = -1;
            for (int i = 0; i < Steps.Length; i++)
            {
                if (t < next[i]) continue;
                if (count < 0) count = CountFlying(sim1) + sim1.GetFriendlies().Count;
                bytes[i] += SnapshotWriter.HeaderSize + count * SnapshotWriter.RecordSize;
                next[i] = NextDue(t, Steps[i]);
            }
            if (sim1.IsFinished) break;
            sim1.Step();
        }
        string hash1 = sim1.Events.Hash();

        int chosen = Steps.Length - 1;
        for (int i = 0; i < Steps.Length; i++)
        {
            Console.WriteLine($"[{id}]   step {Steps[i],4} s -> ~{bytes[i] / 1e6:F1} MB");
            if (bytes[i] <= maxMb * 1e6) { chosen = i; break; }
        }
        double step = Steps[chosen];
        Console.WriteLine($"[{id}] step 2/2: writing with a {step} s step");

        var sim = NewSim();
        double dt = sim.Clock.Dt;
        string binTmp = Path.Combine(outDir, id + ".bin.tmp");
        var frames = new List<object>();
        long offset = 0;
        double nextT = 0;
        long lastTick = -1;

        using (var bin = File.Create(binTmp))
        {
            while (true)
            {
                double t = sim.Clock.Time;
                bool due = t >= nextT;
                if (due || (sim.IsFinished && sim.Clock.Tick != lastTick))
                {
                    int flying = CountFlying(sim);
                    var friendlies = sim.GetFriendlies();
                    byte[] snap = SnapshotWriter.Write(sim.Clock.Tick, t, sim.Drones, flying, friendlies);
                    bin.Write(snap, 0, snap.Length);
                    // counters, in order: flying, impacted, violations, detected, intercepted, shotsFired, magazineLeft, interceptorsLaunched, interceptorsLost, crashed, jammed
                    frames.Add(new
                    {
                        t,
                        off = offset,
                        len = snap.Length,
                        c = new[]
                        {
                            flying, sim.ImpactCount, sim.ViolationCount, sim.DetectedCount, sim.InterceptedCount, sim.ShotsFired,
                            sim.MagazineLeft, sim.InterceptorsLaunched, sim.InterceptorsLost, sim.CrashedCount, sim.JammedCount
                        },
                    });
                    offset += snap.Length;
                    lastTick = sim.Clock.Tick;
                    if (due) nextT = NextDue(t, step);
                }
                if (sim.IsFinished) break;
                sim.Step();
            }
        }

        string hash2 = sim.Events.Hash();
        if (hash1 != hash2)
        {
            File.Delete(binTmp);
            throw new InvalidOperationException($"[{id}] NON-DETERMINISTIC: the hashes of the two runs differ ({hash1} vs {hash2}). Not writing the result.");
        }

        var log = sim.Events.Events;
        var events = new EventDto[log.Count];
        for (int i = 0; i < log.Count; i++)
        {
            var e = log[i];
            events[i] = new EventDto(e.Tick, e.Tick * dt, e.Type.WireName(), e.EntityId, e.Position.LatDeg, e.Position.LonDeg, e.Position.AltM, e.Code);
        }

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var summary = RunAnalyzer.Analyze(sim.Scenario, sim.Events.Events, sim.Seed);
        if (sensRuns > 0)
        {
            Console.WriteLine($"[{id}] sensitivity: {1 + 2 * SensitivityRunner.ApplicableTo(scenario).Count} batches x {sensRuns} runs...");
            var sensitivity = SensitivityRunner.Run(scenario, seed, sensRuns, SensitivityRunner.DefaultDelta,
                () => string.IsNullOrEmpty(scenario.AirspaceMap) ? null : airspaces.Create(scenario.AirspaceMap, scenario.Countries), threads);
            summary = summary with { Sensitivity = sensitivity };
        }

        string scenarioHash = ScenarioLoader.Hash(scenario);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scenarioHash}|{seed}|{EngineInfo.Version}|{step}"))).ToLowerInvariant()[..16];

        string dataTmp = Path.Combine(outDir, id + ".data.json.tmp");
        File.WriteAllText(dataTmp, JsonSerializer.Serialize(new { frames, events, summary }, json));

        string metaTmp = Path.Combine(outDir, id + ".meta.json.tmp");
        File.WriteAllText(metaTmp, JsonSerializer.Serialize(new
        {
            key, id, name = scenario.Name, seed = seed.ToString(), engineVersion = EngineInfo.Version, scenarioHash,
            stepS = step, dt, durationS = sim.Clock.Time, frameCount = frames.Count, totalUnits = sim.DroneCount,
            binBytes = offset, eventLogHash = hash2, generatedAt = DateTime.UtcNow.ToString("o"),
        }, json));

        // meta last: an unfinished run does not show up in the list
        File.Move(binTmp, Path.Combine(outDir, id + ".bin"), true);
        File.Move(dataTmp, Path.Combine(outDir, id + ".data.json"), true);
        File.Move(metaTmp, Path.Combine(outDir, id + ".meta.json"), true);
        Console.WriteLine($"[{id}] done: {frames.Count} frames, {offset / 1e6:F1} MB, {events.Length} events, key {key}");
    }

    /// <summary>True if the run was already generated completely for this scenario, seed, engine version and sensitivity setting.</summary>
    private static bool IsComplete(string outDir, string id, Scenario scenario, ulong seed, int sensRuns)
    {
        string meta = Path.Combine(outDir, id + ".meta.json");
        if (!File.Exists(meta) || !File.Exists(Path.Combine(outDir, id + ".bin")) || !File.Exists(Path.Combine(outDir, id + ".data.json")))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(meta));
            var r = doc.RootElement;
            return r.TryGetProperty("scenarioHash", out var h) && h.GetString() == ScenarioLoader.Hash(scenario)
                && r.TryGetProperty("engineVersion", out var v) && v.GetString() == EngineInfo.Version
                && r.TryGetProperty("seed", out var sd) && sd.GetString() == seed.ToString(CultureInfo.InvariantCulture)
                && StoredSensitivityRuns(Path.Combine(outDir, id + ".data.json")) == sensRuns;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// How many runs per batch the sensitivity stored in a data file used (0 = none). The summary is the last part of the file,
    /// so only its tail is read; this also works for files made before the meta file recorded it.
    /// </summary>
    private static int StoredSensitivityRuns(string dataFile)
    {
        using var fs = File.OpenRead(dataFile);
        int len = (int)Math.Min(fs.Length, 256 * 1024);
        fs.Seek(-len, SeekOrigin.End);
        var buffer = new byte[len];
        fs.ReadExactly(buffer, 0, len);
        var m = System.Text.RegularExpressions.Regex.Match(Encoding.UTF8.GetString(buffer), "\"sensitivity\":\\{.*?\"runs\":(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static string? FindUp(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.sln").Any() || dir.EnumerateFiles("*.slnx").Any())
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
