using System;
using System.Collections.Generic;
using System.Linq;

namespace Sim.Core.Analytics;

/// <summary>Difference B - A for one metric, computed over pairs of runs with the same seed.</summary>
public sealed record ComparisonRow(
    string Name, double MeanA, double MeanB, double Diff, double DiffSd, double StdError, double Z, bool Significant);

/// <summary>
/// Compares two reports run with the same seeds. The difference is taken pairwise (seed i of A with seed i of B),
/// which removes part of the noise; Z = mean difference / standard error. |Z| &gt; 2 ~ probably a real difference (≈95%).
/// </summary>
public static class BatchCompare
{
    public const double SignificantZ = 2.0;

    public static IReadOnlyList<ComparisonRow> Compare(BatchReport a, BatchReport b)
    {
        if (a.Runs != b.Runs || a.BaseSeed != b.BaseSeed)
            throw new ArgumentException("The two reports must have the same base seed and the same number of runs.");

        var ra = a.Results.OrderBy(r => r.Seed).ToArray();
        var rb = b.Results.OrderBy(r => r.Seed).ToArray();
        var rows = new List<ComparisonRow>();

        foreach (var (name, value) in BatchRunner.Selectors)
        {
            var va = ra.Select(value).ToArray();
            var vb = rb.Select(value).ToArray();
            var d = va.Zip(vb, (x, y) => y - x).ToArray();
            double mean = d.Average();
            double sd = d.Length > 1 ? Math.Sqrt(d.Sum(x => (x - mean) * (x - mean)) / (d.Length - 1)) : 0.0;
            double se = d.Length > 0 ? sd / Math.Sqrt(d.Length) : 0.0;
            double z = se > 1e-12 ? mean / se : (Math.Abs(mean) > 1e-12 ? double.PositiveInfinity * Math.Sign(mean) : 0.0);
            rows.Add(new ComparisonRow(name, va.Average(), vb.Average(), mean, sd, se, double.IsInfinity(z) ? Math.Sign(z) * 99.0 : z,
                                       Math.Abs(z) > SignificantZ));
        }
        return rows;
    }
}
