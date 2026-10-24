using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sim.Core.Airspace;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;
using Sim.Server.Models;

namespace Sim.Server.Services;

/// <summary>
/// Mod linie de comanda, fara server web:
/// dotnet run --project src/Sim.Server -- batch scenario=ru-vs-ua runs=30 [seed=1] [threads=4] [out=cale] [retarget=off]
/// Scrie runs/batch-&lt;scenariu&gt;-&lt;seed&gt;-&lt;rulari&gt;.json si .csv (sau in out=) si afiseaza tabelul rezumat.
/// </summary>
public static class BatchCli
{
    public static int Run(string[] args)
    {
        try
        {
            var opts = args.Select(a => a.Split('=', 2)).Where(p => p.Length == 2)
                           .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());

            if (!opts.TryGetValue("scenario", out var scenarioArg))
            {
                Console.Error.WriteLine("Utilizare: batch scenario=<id> runs=<n> [seed=<n>] [threads=<n>] [out=<folder>]");
                return 2;
            }

            int runs = opts.TryGetValue("runs", out var r) ? int.Parse(r) : 30;
            ulong seed = opts.TryGetValue("seed", out var s) ? ulong.Parse(s) : 1UL;
            int threads = opts.TryGetValue("threads", out var t) ? int.Parse(t) : 0;

            string root = FindUp(Directory.GetCurrentDirectory()) ?? FindUp(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();
            var paths = new SimPaths(
                Path.Combine(root, "scenarios"), Path.Combine(root, "runs"),
                Path.Combine(root, "data", "countries"), Path.Combine(root, "data", "catalog"));

            string file = File.Exists(scenarioArg) ? scenarioArg
                : ScenarioCatalog.IsSafeId(scenarioArg) ? ScenarioCatalog.PathFor(paths.ScenariosDir, scenarioArg)
                : throw new ArgumentException("Scenariu invalid.");
            if (!File.Exists(file))
                throw new FileNotFoundException($"Nu exista scenariul: {file}");

            var scenario = ScenarioLoader.FromFile(file, new CatalogProvider(paths).Get());
            var airspaces = new AirspaceCatalog(paths);
            if (opts.TryGetValue("retarget", out var rt) && rt.Equals("off", StringComparison.OrdinalIgnoreCase))
                scenario = scenario with { Waves = scenario.Waves.Select(w => w with { Retarget = false }).ToList() };

            Func<AirspaceModel?>? airspace = string.IsNullOrEmpty(scenario.AirspaceMap)
                ? null
                : () => airspaces.Create(scenario.AirspaceMap, scenario.Countries);

            if (opts.TryGetValue("jam", out var jamArg))
                return Sweep(scenario, jamArg, seed, runs, threads, airspace, paths.RunsDir, Path.GetFileNameWithoutExtension(file), opts.GetValueOrDefault("out"));

            Console.WriteLine($"Rulez {runs} x {Path.GetFileNameWithoutExtension(file)} (seed {seed}..{seed + (ulong)runs - 1})...");
            var report = BatchRunner.Run(scenario, seed, runs, airspace, threads,
                done => { if (done % 5 == 0 || done == runs) Console.Write($"\r  {done}/{runs}"); });
            Console.WriteLine();
            Console.WriteLine(BatchRunner.ToText(report));

            string outDir = opts.TryGetValue("out", out var o) ? o : paths.RunsDir;
            Directory.CreateDirectory(outDir);
            string stem = Path.Combine(outDir, $"batch-{Path.GetFileNameWithoutExtension(file)}-{seed}-{runs}");
            var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            File.WriteAllText(stem + ".json", JsonSerializer.Serialize(report, json));
            File.WriteAllText(stem + ".csv", BatchRunner.ToCsv(report));
            Console.WriteLine($"Salvat: {stem}.json / .csv");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidDataException or OverflowException)
        {
            Console.Error.WriteLine("Eroare: " + ex.Message);
            return 1;
        }
    }

    /// <summary>Baleiaj: aceleasi rulari pentru fiecare factor de scara aplicat ratei de bruiaj a TUTUROR sistemelor ew (ex. jam=0.5,1,1.5).</summary>
    private static int Sweep(Scenario scenario, string jamArg, ulong seed, int runs, int threads,
                             Func<AirspaceModel?>? airspace, string runsDir, string name, string? outDir)
    {
        var scales = jamArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (scales.Length == 0 || scales.Any(x => x < 0.0 || double.IsNaN(x) || double.IsInfinity(x)))
            throw new ArgumentException("jam= cere valori >= 0, separate prin virgula (ex. jam=0.5,1,1.5).");

        var f = System.Globalization.CultureInfo.InvariantCulture;
        var csv = new System.Text.StringBuilder("jamScale,runs,interceptedMean,interceptedSd,impactsMean,impactsSd,impactsP5,impactsP95,jammedMean,crashedMean,destroyedMean,killedMean\n");
        Console.WriteLine($"Baleiaj bruiaj x {runs} rulari, seed {seed}..{seed + (ulong)runs - 1}");
        Console.WriteLine($"{"Scara",8}{"Interc.",10}{"Impact",10}{"Impact P5-P95",16}{"Bruiate",10}{"Distruse",10}{"Victime",10}");

        foreach (double k in scales)
        {
            var scaled = scenario with
            {
                Defenses = scenario.Defenses.Select(d => d.Kind == DefenseKind.Ew ? d with { JamRatePerMinute = d.JamRatePerMinute * k } : d).ToList()
            };
            var report = BatchRunner.Run(scaled, seed, runs, airspace, threads);
            MetricStat M(string n) => report.Metrics.First(m => m.Name == n);
            var ic = M("Interceptate"); var im = M("Impacturi"); var jm = M("Bruiate"); var cr = M("Cazute pe traseu");
            var de = M("Sisteme distruse"); var ki = M("Victime estimate");

            Console.WriteLine($"{k.ToString("0.##", f),8}{ic.Mean.ToString("F0", f),10}{im.Mean.ToString("F0", f),10}{im.P5.ToString("F0", f) + "-" + im.P95.ToString("F0", f),16}{jm.Mean.ToString("F0", f),10}{de.Mean.ToString("F1", f),10}{ki.Mean.ToString("F0", f),10}");
            csv.AppendLine(string.Join(",", k.ToString(f), runs, ic.Mean.ToString("F1", f), ic.Sd.ToString("F1", f), im.Mean.ToString("F1", f), im.Sd.ToString("F1", f),
                im.P5.ToString("F1", f), im.P95.ToString("F1", f), jm.Mean.ToString("F1", f), cr.Mean.ToString("F1", f), de.Mean.ToString("F2", f), ki.Mean.ToString("F1", f)));
        }

        string dir = outDir ?? runsDir;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"sweep-jam-{name}-{seed}-{runs}.csv");
        File.WriteAllText(path, csv.ToString());
        Console.WriteLine($"Salvat: {path}");
        return 0;
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
