using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Scenarios;

namespace Sim.Core.Analytics;

/// <summary>
/// Statistici de final: numaratori din log + estimare de victime pe clase de teren (Monte Carlo).
/// Determinist: acelasi scenariu + seed + log => acelasi rezultat; functioneaza si pe o rulare salvata.
/// </summary>
public static class RunAnalyzer
{
    public const int DefaultTrials = 2000;

    private const ulong StreamId = 1UL << 41;
    private const int ClassCount = 3;

    public static RunSummary Analyze(Scenario scenario, IReadOnlyList<SimEvent> events, ulong seed, int trials = DefaultTrials)
    {
        if (trials <= 0)
            throw new ArgumentOutOfRangeException(nameof(trials));

        // id-urile de drone sunt atribuite in ordinea valurilor => val = primul a carui limita cumulata depaseste id-ul
        int total = scenario.Waves.Sum(w => w.Count);
        var waveEnd = new int[scenario.Waves.Count];
        int running = 0;
        for (int i = 0; i < waveEnd.Length; i++)
            waveEnd[i] = running += scenario.Waves[i].Count;

        int launched = 0, detected = 0, intercepted = 0, missed = 0, impacted = 0, violations = 0, failed = 0, jammed = 0;
        var impactsPerClass = new int[ClassCount];
        var areaPerClass = new double[ClassCount]; // suma ariilor letale (km2) per clasa
        var violationsByCountry = new SortedDictionary<string, int>(StringComparer.Ordinal);

        // per unitate defensiva: [detectate, lansari, lovituri, rataciri, pierdute, intoarceri, distrus]
        var perDefense = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var def in scenario.Defenses)
            perDefense[def.Id] = new int[7];
        void Bump(SimEvent e, int slot)
        {
            if (e.Code is not null && perDefense.TryGetValue(e.Code, out var counters))
                counters[slot]++;
        }

        foreach (var e in events)
        {
            switch (e.Type)
            {
                case SimEventType.Launched: launched++; break;
                case SimEventType.Crashed: failed++; break;
                case SimEventType.Jammed: jammed++; Bump(e, 2); break;
                case SimEventType.Detected: detected++; Bump(e, 0); break;
                case SimEventType.Intercepted: intercepted++; Bump(e, 2); break;
                case SimEventType.Missed: missed++; Bump(e, 3); break;
                case SimEventType.InterceptorLaunched: Bump(e, 1); break;
                case SimEventType.InterceptorLost: Bump(e, 4); break;
                case SimEventType.PatrolReturning: Bump(e, 5); break;
                case SimEventType.DefenseDestroyed: Bump(e, 6); break;
                case SimEventType.AirspaceViolation:
                    violations++;
                    string code = e.Code ?? "?";
                    violationsByCountry[code] = violationsByCountry.GetValueOrDefault(code) + 1;
                    break;
                case SimEventType.Impact:
                {
                    impacted++;
                    int wi = WaveOf(waveEnd, e.EntityId);
                    if (wi < 0)
                        break;

                    var wave = scenario.Waves[wi];
                    var cls = Classify(scenario.Targets, e.Position);
                    impactsPerClass[(int)cls]++;
                    areaPerClass[(int)cls] += CasualtyModel.LethalAreaKm2(wave.WarheadKg);
                    break;
                }
            }
        }

        // Monte Carlo
        var rng = new Rng(seed).Derive(StreamId);
        var killedTotal = new double[trials];
        var injuredTotal = new double[trials];
        var killedPerClass = new double[ClassCount][];
        var injuredPerClass = new double[ClassCount][];
        for (int c = 0; c < ClassCount; c++)
        {
            killedPerClass[c] = new double[trials];
            injuredPerClass[c] = new double[trials];
        }

        for (int t = 0; t < trials; t++)
        {
            double radiusFactor = rng.NextDouble(1.0 - CasualtyModel.RadiusUncertainty, 1.0 + CasualtyModel.RadiusUncertainty);
            double areaFactor = radiusFactor * radiusFactor;

            for (int c = 0; c < ClassCount; c++)
            {
                // densitatea e incerta, dar comuna tuturor impacturilor din aceeasi clasa (corelatie: daca orasul e mai dens, e mai dens peste tot)
                double densityFactor = Math.Exp(rng.NextGaussian() * CasualtyModel.DensityLogSigma
                                                - 0.5 * CasualtyModel.DensityLogSigma * CasualtyModel.DensityLogSigma);

                if (impactsPerClass[c] == 0)
                    continue;

                var profile = CasualtyModel.Profile((TerrainClass)c);
                double exposed = profile.DensityPerKm2 * densityFactor * areaPerClass[c] * areaFactor;
                double killed = Poisson(rng, exposed * profile.FatalFraction);
                double injured = Poisson(rng, exposed * profile.InjuryFraction);

                killedPerClass[c][t] = killed;
                injuredPerClass[c][t] = injured;
                killedTotal[t] += killed;
                injuredTotal[t] += injured;
            }
        }

        var classes = new List<ClassStat>();
        for (int c = 0; c < ClassCount; c++)
        {
            if (impactsPerClass[c] == 0)
                continue;
            classes.Add(new ClassStat(((TerrainClass)c).WireName(), impactsPerClass[c],
                Summarize(killedPerClass[c]), Summarize(injuredPerClass[c])));
        }

        var defenseStats = new List<DefenseStat>();
        foreach (var def in scenario.Defenses)
        {
            var c = perDefense[def.Id];
            int shots = def.Kind == DefenseKind.InterceptorBase ? c[1] : def.Kind == DefenseKind.Ew ? c[2] : c[2] + c[3];
            string kind = char.ToLowerInvariant(def.Kind.ToString()[0]) + def.Kind.ToString().Substring(1);
            int magazine = def.Kind is DefenseKind.Radar or DefenseKind.Airbase or DefenseKind.Ew ? 0 : def.Magazine;
            defenseStats.Add(new DefenseStat(def.Id, string.IsNullOrEmpty(def.Name) ? def.Id : def.Name, kind,
                c[0], shots, c[2], c[3], c[4], c[5], magazine, c[6] > 0));
        }

        return new RunSummary(
            total, launched, detected, intercepted, missed, impacted, violations,
            classes, Summarize(killedTotal), Summarize(injuredTotal),
            violationsByCountry.Select(kv => new CountryViolationCount(kv.Key, kv.Value)).ToList(),
            defenseStats,
            trials,
            failed,
            jammed);
    }

    /// <summary>
    /// Impactul din raza zonei unei tinte (orice tinta din scenariu, nu doar cea a valului) ia clasa celei mai apropiate dintre ele;
    /// in rest cade pe camp. Asa merg si tintele multiple dintr-un oras, si loviturile colaterale.
    /// </summary>
    private static TerrainClass Classify(IReadOnlyList<Target> targets, GeoPoint impact)
    {
        var best = TerrainClass.Field;
        double bestDist = double.MaxValue;
        foreach (var target in targets)
        {
            var kind = TerrainClassExtensions.FromTargetKind(target.Kind);
            if (kind == TerrainClass.Field)
                continue;

            double radius = target.RadiusM > 0.0 ? target.RadiusM : CasualtyModel.DefaultTargetRadiusM(kind);
            double dist = Wgs84.GreatCircleDistanceM(new GeoPoint(target.LatDeg, target.LonDeg, target.AltM), impact);
            if (dist <= radius && dist < bestDist)
            {
                bestDist = dist;
                best = kind;
            }
        }
        return best;
    }

    private static int WaveOf(int[] waveEnd, int droneId)
    {
        for (int i = 0; i < waveEnd.Length; i++)
            if (droneId < waveEnd[i])
                return i;
        return -1;
    }

    /// <summary>Poisson(lambda): Knuth pentru valori mici, aproximare normala pentru valori mari.</summary>
    private static double Poisson(Rng rng, double lambda)
    {
        if (lambda <= 0.0)
            return 0.0;

        if (lambda < 30.0)
        {
            double limit = Math.Exp(-lambda);
            double p = 1.0;
            int k = 0;
            do
            {
                k++;
                p *= rng.NextDouble();
            } while (p > limit);
            return k - 1;
        }

        return Math.Max(0.0, Math.Round(lambda + Math.Sqrt(lambda) * rng.NextGaussian()));
    }

    private static Estimate Summarize(double[] samples)
    {
        double mean = samples.Average();
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        int n = sorted.Length;
        return new Estimate(mean, sorted[(int)(0.05 * (n - 1))], sorted[(int)(0.95 * (n - 1))]);
    }
}
