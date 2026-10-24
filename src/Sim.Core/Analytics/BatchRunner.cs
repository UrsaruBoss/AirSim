using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sim.Core.Airspace;
using Sim.Core.Scenarios;

namespace Sim.Core.Analytics;

/// <summary>Rezultatul unei singure rulari din serie.</summary>
public sealed record BatchRunResult(
    ulong Seed, int Launched, int Intercepted, int Impacts, int Crashed, int Jammed,
    int DefensesDestroyed, int Violations, int ShotsFired, double Killed, int DecoyShots = 0, int Retargeted = 0,
    double DefenseSpentUsd = 0, double DecoySpentUsd = 0, double AttackCostUsd = 0, double SaturatedPct = 0, double PeakLoad = 0);

/// <summary>Distributia unei metrici peste toate rularile (Sd = deviatia standard a esantionului).</summary>
public sealed record MetricStat(string Name, double Mean, double Sd, double Min, double P5, double P50, double P95, double Max);

public sealed record BatchReport(
    string Scenario, string ScenarioHash, string EngineVersion, ulong BaseSeed, int Runs,
    IReadOnlyList<MetricStat> Metrics, IReadOnlyList<BatchRunResult> Results);

/// <summary>
/// Ruleaza acelasi scenariu cu mai multe seed-uri (baseSeed, baseSeed + 1, ...), fara interfata, in paralel,
/// si rezuma distributia fiecarei metrici. Determinist: aceleasi argumente => acelasi raport, indiferent de numarul de fire.
/// </summary>
public static class BatchRunner
{
    public const int CasualtyTrials = 200;

    /// <summary>Metricile raportate, in ordine; folosite si la compararea pereche a doua rapoarte.</summary>
    public static readonly IReadOnlyList<(string Name, Func<BatchRunResult, double> Value)> Selectors = new (string, Func<BatchRunResult, double>)[]
    {
        ("Interceptate", r => r.Intercepted),
        ("Impacturi", r => r.Impacts),
        ("Cazute pe traseu", r => r.Crashed),
        ("Bruiate", r => r.Jammed),
        ("Sisteme distruse", r => r.DefensesDestroyed),
        ("Incalcari spatiu aerian", r => r.Violations),
        ("Tiruri", r => r.ShotsFired),
        ("Victime estimate", r => r.Killed),
        ("Tiruri pe momeli", r => r.DecoyShots),
        ("Redirectionate", r => r.Retargeted),
        ("Cost aparare (mil USD)", r => r.DefenseSpentUsd / 1e6),
        ("  din care pe momeli", r => r.DecoySpentUsd / 1e6),
        ("Cost atac (mil USD)", r => r.AttackCostUsd / 1e6),
        ("Bruiaj saturat (% cicluri)", r => r.SaturatedPct),
        ("Incarcare max bruior (xcap)", r => r.PeakLoad)
    };

    /// <param name="airspace">Se apeleaza o data per rulare (modelul e doar citit, dar fiecare rulare primeste al ei).</param>
    public static BatchReport Run(Scenario scenario, ulong baseSeed, int runs, Func<AirspaceModel?>? airspace = null,
                                  int maxThreads = 0, Action<int>? progress = null)
    {
        if (runs <= 0)
            throw new ArgumentOutOfRangeException(nameof(runs), "Numarul de rulari trebuie sa fie > 0.");

        var errors = ScenarioLoader.Validate(scenario);
        if (errors.Count > 0)
            throw new ArgumentException("Scenariu invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors), nameof(scenario));

        var results = new BatchRunResult[runs];
        int done = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = maxThreads > 0 ? maxThreads : Environment.ProcessorCount };

        Parallel.For(0, runs, options, i =>
        {
            ulong seed = baseSeed + (ulong)i;
            var sim = new Simulation(scenario, seed, airspace?.Invoke());
            sim.Run();
            var summary = RunAnalyzer.Analyze(scenario, sim.Events.Events, seed, CasualtyTrials);

            results[i] = new BatchRunResult(
                seed, sim.DroneCount, sim.InterceptedCount, sim.ImpactCount, sim.CrashedCount, sim.JammedCount,
                sim.DefensesDestroyed, sim.ViolationCount, sim.ShotsFired, summary.Killed.Mean, sim.DecoyShots, sim.RetargetedCount,
                sim.DefenseSpentUsd, sim.DecoySpentUsd, sim.AttackCostUsd,
                sim.EwActiveCycles > 0 ? 100.0 * sim.EwSaturatedCycles / sim.EwActiveCycles : 0.0, sim.EwPeakLoad);

            progress?.Invoke(System.Threading.Interlocked.Increment(ref done));
        });

        var metrics = Selectors.Select(m => Stat(m.Name, results.Select(m.Value))).ToList();

        return new BatchReport(scenario.Name, ScenarioLoader.Hash(scenario), EngineInfo.Version, baseSeed, runs, metrics, results);
    }

    public static MetricStat Stat(string name, IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToArray();
        double mean = v.Average();
        double sd = v.Length > 1 ? Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Length - 1)) : 0.0;
        return new MetricStat(name, mean, sd, v[0], Percentile(v, 0.05), Percentile(v, 0.50), Percentile(v, 0.95), v[^1]);
    }

    /// <summary>Percentila cu interpolare liniara; <paramref name="sorted"/> trebuie sa fie nevida si sortata crescator.</summary>
    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1)
            return sorted[0];

        double pos = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    public static string ToCsv(BatchReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("seed,launched,intercepted,impacts,crashed,jammed,defensesDestroyed,violations,shotsFired,killed,decoyShots,retargeted,defenseSpentUsd,decoySpentUsd,attackCostUsd,saturatedPct,peakLoad");
        foreach (var r in report.Results)
        {
            sb.Append(r.Seed.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(r.Launched).Append(',').Append(r.Intercepted).Append(',').Append(r.Impacts).Append(',')
              .Append(r.Crashed).Append(',').Append(r.Jammed).Append(',').Append(r.DefensesDestroyed).Append(',')
              .Append(r.Violations).Append(',').Append(r.ShotsFired).Append(',')
              .Append(r.Killed.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.DecoyShots).Append(',').Append(r.Retargeted).Append(',')
              .Append(r.DefenseSpentUsd.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.DecoySpentUsd.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.AttackCostUsd.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.SaturatedPct.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
              .AppendLine(r.PeakLoad.ToString("F2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>Tabel text pentru consola.</summary>
    public static string ToText(BatchReport report)
    {
        var f = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"{report.Scenario}  |  {report.Runs} rulari, seed {report.BaseSeed}..{report.BaseSeed + (ulong)report.Runs - 1}  |  motor {report.EngineVersion}");
        sb.AppendLine($"{"Metrica",-26}{"Media",10}{"Sd",9}{"Min",9}{"P5",9}{"Mediana",9}{"P95",9}{"Max",9}");
        foreach (var m in report.Metrics)
            sb.AppendLine($"{m.Name,-26}{m.Mean.ToString("F1", f),10}{m.Sd.ToString("F1", f),9}{m.Min.ToString("F0", f),9}{m.P5.ToString("F0", f),9}{m.P50.ToString("F0", f),9}{m.P95.ToString("F0", f),9}{m.Max.ToString("F0", f),9}");
        return sb.ToString();
    }
}
