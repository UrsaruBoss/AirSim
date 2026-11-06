using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sim.Core.Airspace;
using Sim.Core.Scenarios;

namespace Sim.Core.Analytics;

/// <summary>One input parameter that can be scaled for every unit of its kind (e.g. "all Pk", "all attack counts").</summary>
public sealed record SensitivityFactor(string Id, string Name, Func<Scenario, double, Scenario> Apply, Func<Scenario, bool> Applies);

/// <summary>
/// Effect of scaling one factor by (1 - delta) and (1 + delta) on one metric. Diff = scaled mean - baseline mean (paired by seed).
/// Z = Diff / standard error; |Z| &gt; 2 ~ probably a real effect.
/// </summary>
public sealed record SensitivityRow(
    string FactorId, string Factor, string Metric, double Baseline,
    double MeanLow, double MeanHigh, double DiffLow, double DiffHigh, double ZLow, double ZHigh)
{
    /// <summary>The larger of the two absolute effects: the length of the tornado bar.</summary>
    public double Swing => Math.Max(Math.Abs(DiffLow), Math.Abs(DiffHigh));
}

public sealed record SensitivityReport(
    string Scenario, string ScenarioHash, string EngineVersion, ulong BaseSeed, int Runs, double Delta,
    IReadOnlyList<string> Metrics, IReadOnlyList<SensitivityRow> Rows);

/// <summary>
/// One-at-a-time sensitivity analysis: the scenario is run with the same seeds, then once per factor with that factor scaled down and up
/// (all other inputs unchanged). Because the seeds are shared, the differences are paired (see <see cref="BatchCompare"/>), which removes part of the noise.
/// Deterministic: same arguments => same report. Cost: (1 + 2 x applicable factors) batches.
/// </summary>
public static class SensitivityRunner
{
    public const double DefaultDelta = 0.2;

    /// <summary>The metrics the tornado is built for (names from <see cref="BatchRunner.Selectors"/>).</summary>
    public static readonly IReadOnlyList<string> Metrics = new[] { "Impacts", "Estimated casualties", "Intercepted" };

    private static bool Shoots(Defense d) => d.Kind is DefenseKind.Sam or DefenseKind.AaGun or DefenseKind.Abm or DefenseKind.Patrol or DefenseKind.InterceptorBase;

    private static double Clamp01(double v) => Math.Min(1.0, Math.Max(0.0, v));

    private static Scenario MapDefenses(Scenario s, Func<Defense, Defense> f) => s with { Defenses = s.Defenses.Select(f).ToList() };
    private static Scenario MapWaves(Scenario s, Func<Wave, Wave> f) => s with { Waves = s.Waves.Select(f).ToList() };

    public static readonly IReadOnlyList<SensitivityFactor> Factors = new SensitivityFactor[]
    {
        new("pk", "Hit probability (Pk)",
            (s, k) => MapDefenses(s, d => !Shoots(d) ? d : d with
            {
                Pk = Clamp01(d.Pk * k),
                PkByKind = d.PkByKind is null ? null : new SortedDictionary<string, double>(d.PkByKind.ToDictionary(p => p.Key, p => Clamp01(p.Value * k)), StringComparer.Ordinal)
            }),
            s => s.Defenses.Any(d => Shoots(d) && d.Pk > 0.0)),

        new("range", "Engagement range",
            (s, k) => MapDefenses(s, d => !Shoots(d) ? d : d with { RangeKm = d.RangeKm * k }),
            s => s.Defenses.Any(Shoots)),

        new("radar", "Radar detection range",
            (s, k) => MapDefenses(s, d => d.Kind != DefenseKind.Radar ? d : d with { RangeKm = d.RangeKm * k }),
            s => s.Defenses.Any(d => d.Kind == DefenseKind.Radar)),

        new("magazine", "Magazine size",
            (s, k) => MapDefenses(s, d => !Shoots(d) ? d : d with { Magazine = (int)Math.Round(d.Magazine * k) }),
            s => s.Defenses.Any(d => Shoots(d) && d.Magazine > 0)),

        new("reload", "Reload time",
            (s, k) => MapDefenses(s, d => !Shoots(d) ? d : d with { ReloadSeconds = d.ReloadSeconds * k }),
            s => s.Defenses.Any(d => Shoots(d) && d.ReloadSeconds > 0.0)),

        new("jam", "Jamming rate",
            (s, k) => MapDefenses(s, d => d.Kind != DefenseKind.Ew ? d : d with { JamRatePerMinute = d.JamRatePerMinute * k }),
            s => s.Defenses.Any(d => d.Kind == DefenseKind.Ew && d.JamRatePerMinute > 0.0)),

        new("count", "Attack size",
            (s, k) => MapWaves(s, w => w with { Count = Math.Max(1, (int)Math.Round(w.Count * k)) }),
            s => s.Waves.Count > 0),

        new("speed", "Attack speed",
            (s, k) => MapWaves(s, w => w with { SpeedMps = w.SpeedMps * k }),
            s => s.Waves.Any(w => w.SpeedMps > 0.0)),

        new("failure", "Attacker failure rate",
            (s, k) => MapWaves(s, w => w with { FailureRate = Clamp01(w.FailureRate * k) }),
            s => s.Waves.Any(w => w.FailureRate > 0.0)),
    };

    public static IReadOnlyList<SensitivityFactor> ApplicableTo(Scenario scenario) => Factors.Where(f => f.Applies(scenario)).ToList();

    public static SensitivityReport Run(Scenario scenario, ulong baseSeed, int runs, double delta = DefaultDelta,
                                        Func<AirspaceModel?>? airspace = null, int maxThreads = 0, Action<int, int>? progress = null)
    {
        if (!(delta > 0.0 && delta < 1.0))
            throw new ArgumentOutOfRangeException(nameof(delta), "delta must be between 0 and 1 (exclusive).");

        var factors = ApplicableTo(scenario);
        int batches = 1 + 2 * factors.Count;
        int finished = 0;

        BatchReport Batch(Scenario s)
        {
            var report = BatchRunner.Run(s, baseSeed, runs, airspace, maxThreads);
            progress?.Invoke(++finished, batches);
            return report;
        }

        var baseline = Batch(scenario);
        var rows = new List<SensitivityRow>();

        foreach (var factor in factors)
        {
            var low = Batch(factor.Apply(scenario, 1.0 - delta));
            var high = Batch(factor.Apply(scenario, 1.0 + delta));
            var lowRows = BatchCompare.Compare(baseline, low);
            var highRows = BatchCompare.Compare(baseline, high);

            foreach (string metric in Metrics)
            {
                var l = lowRows.First(r => r.Name == metric);
                var h = highRows.First(r => r.Name == metric);
                rows.Add(new SensitivityRow(factor.Id, factor.Name, metric, l.MeanA, l.MeanB, h.MeanB, l.Diff, h.Diff, l.Z, h.Z));
            }
        }

        // biggest swing first, per metric, so a consumer can take the rows of one metric in order
        var ordered = rows.OrderBy(r => Array.IndexOf(Metrics.ToArray(), r.Metric)).ThenByDescending(r => r.Swing).ThenBy(r => r.FactorId, StringComparer.Ordinal).ToList();

        return new SensitivityReport(scenario.Name, ScenarioLoader.Hash(scenario), EngineInfo.Version, baseSeed, runs, delta, Metrics, ordered);
    }

    /// <summary>Text table for the console.</summary>
    public static string ToText(SensitivityReport report)
    {
        var f = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"{report.Scenario}  |  {report.Runs} runs per batch, seed {report.BaseSeed}..{report.BaseSeed + (ulong)report.Runs - 1}  |  each factor x{(1 - report.Delta).ToString("0.##", f)} / x{(1 + report.Delta).ToString("0.##", f)}  |  engine {report.EngineVersion}");
        foreach (string metric in report.Metrics)
        {
            var rows = report.Rows.Where(r => r.Metric == metric).ToList();
            if (rows.Count == 0)
                continue;
            sb.AppendLine();
            sb.AppendLine($"{metric}  (baseline {rows[0].Baseline.ToString("F1", f)})");
            sb.AppendLine($"{"Factor",-28}{"Low",10}{"High",10}{"Z low",8}{"Z high",8}");
            foreach (var r in rows)
                sb.AppendLine($"{r.Factor,-28}{Signed(r.DiffLow),10}{Signed(r.DiffHigh),10}{r.ZLow.ToString("F1", f),8}{r.ZHigh.ToString("F1", f),8}");
        }
        return sb.ToString();

        string Signed(double v) => (v >= 0 ? "+" : "") + v.ToString("F1", f);
    }
}
