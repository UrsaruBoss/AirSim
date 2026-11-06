using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Scenarios;
using Sim.Core.Sun;

namespace Sim.Core.Analytics;

/// <summary>
/// End-of-run statistics: counters from the log + casualty estimate per terrain class (Monte Carlo).
/// Deterministic: same scenario + seed + log => same result; also works on a saved run.
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

        // drone ids are assigned in wave order => the wave is the first whose cumulative limit exceeds the id
        int total = scenario.Waves.Sum(w => w.Count);
        var waveEnd = new int[scenario.Waves.Count];
        int running = 0;
        for (int i = 0; i < waveEnd.Length; i++)
            waveEnd[i] = running += scenario.Waves[i].Count;

        int launched = 0, detected = 0, intercepted = 0, missed = 0, impacted = 0, violations = 0, failed = 0, jammed = 0;
        var impactsPerClass = new int[ClassCount];
        var areaPerClass = new double[ClassCount]; // sum of lethal areas (km2) per class, already weighted by the time-of-day population factor
        var fatalWeightPerClass = new double[ClassCount]; // the same, additionally weighted by the time-of-day fatality factor
        bool hasTime = DayNight.TryParseUtc(scenario.StartTimeUtc, out double startMs);
        bool warningOn = scenario.PublicWarning ?? true;
        var firstDetected = new Dictionary<int, long>(); // drone id -> tick of its first detection
        int warnImpacts = 0, warnUndetected = 0;
        double warnSeconds = 0.0, warnShelteredSum = 0.0;
        var violationsByCountry = new SortedDictionary<string, int>(StringComparer.Ordinal);

        // per defensive unit: [detected, launches, hits, misses, lost, returns, destroyed]
        var perDefense = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var def in scenario.Defenses)
            perDefense[def.Id] = new int[7];
        void Bump(SimEvent e, int slot)
        {
            if (e.Code is not null && perDefense.TryGetValue(e.Code, out var counters))
                counters[slot]++;
        }

        // time series (per bin, cumulated at the end) and per-wave outcomes
        long lastTick = 0;
        foreach (var e in events)
            if (e.Tick > lastTick) lastTick = e.Tick;
        double endS = lastTick * scenario.DtSeconds;
        double binS = SeriesBinSeconds(endS);
        int binCount = (int)Math.Floor(endS / binS) + 1;
        var sLaunched = new int[binCount];
        var sDetected = new int[binCount];
        var sIntercepted = new int[binCount];
        var sImpacts = new int[binCount];
        var sJammed = new int[binCount];
        var sCrashed = new int[binCount];
        var sShots = new int[binCount];
        int waveCount = scenario.Waves.Count;
        var wLaunched = new int[waveCount];
        var wIntercepted = new int[waveCount];
        var wJammed = new int[waveCount];
        var wCrashed = new int[waveCount];
        var wImpacted = new int[waveCount];
        var kindByCode = new Dictionary<string, DefenseKind>(StringComparer.Ordinal);
        foreach (var def in scenario.Defenses)
            kindByCode[def.Id] = def.Kind;

        void Track(SimEvent e)
        {
            int bin = Math.Min(binCount - 1, (int)Math.Floor(e.Tick * scenario.DtSeconds / binS));
            int wi = e.EntityId >= 0 && e.EntityId < total ? WaveOf(waveEnd, e.EntityId) : -1;
            bool shooter = e.Code is not null && kindByCode.TryGetValue(e.Code, out var codeKind) && codeKind != DefenseKind.InterceptorBase;
            switch (e.Type)
            {
                case SimEventType.Launched: sLaunched[bin]++; if (wi >= 0) wLaunched[wi]++; break;
                case SimEventType.Detected: sDetected[bin]++; break;
                case SimEventType.Intercepted: sIntercepted[bin]++; if (shooter) sShots[bin]++; if (wi >= 0) wIntercepted[wi]++; break;
                case SimEventType.Missed: if (shooter) sShots[bin]++; break;
                case SimEventType.InterceptorLaunched: sShots[bin]++; break;
                case SimEventType.Jammed: sJammed[bin]++; if (wi >= 0) wJammed[wi]++; break;
                case SimEventType.Crashed: sCrashed[bin]++; if (wi >= 0) wCrashed[wi]++; break;
                case SimEventType.Impact: sImpacts[bin]++; if (wi >= 0) wImpacted[wi]++; break;
            }
        }

        foreach (var e in events)
        {
            Track(e);
            switch (e.Type)
            {
                case SimEventType.Launched: launched++; break;
                case SimEventType.Crashed: failed++; break;
                case SimEventType.Jammed: jammed++; Bump(e, 2); break;
                case SimEventType.Detected: detected++; Bump(e, 0); firstDetected.TryAdd(e.EntityId, e.Tick); break;
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
                    double area = CasualtyModel.LethalAreaKm2(wave.WarheadKg);
                    double densityScale = 1.0, fatalScale = 1.0;
                    if (hasTime)
                    {
                        // time of day at the place and the moment of the impact
                        double daylight = DayNight.DaylightAt(e.Position.LatDeg, e.Position.LonDeg, startMs + e.Tick * scenario.DtSeconds * 1000.0);
                        var night = DayNight.ForTerrain(cls);
                        densityScale = DayNight.Blend(night.Density, daylight);
                        fatalScale = DayNight.Blend(night.Fatal, daylight);
                    }

                    // public warning: how long before the impact the threat was first seen, and how many exposed people reached shelter in that time
                    double warningS = firstDetected.TryGetValue(e.EntityId, out long seenTick) ? Math.Max(0.0, (e.Tick - seenTick) * scenario.DtSeconds) : 0.0;
                    double daylightNow = hasTime ? DayNight.DaylightAt(e.Position.LatDeg, e.Position.LonDeg, startMs + e.Tick * scenario.DtSeconds * 1000.0) : 1.0;
                    double sheltered = warningOn ? CasualtyModel.ShelteredShare(cls, warningS, daylightNow) : 0.0;
                    fatalScale *= 1.0 - CasualtyModel.ShelterProtection * sheltered;
                    warnImpacts++;
                    if (!firstDetected.ContainsKey(e.EntityId)) warnUndetected++;
                    warnSeconds += warningS;
                    warnShelteredSum += sheltered;

                    areaPerClass[(int)cls] += area * densityScale;
                    fatalWeightPerClass[(int)cls] += area * densityScale * fatalScale;
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
                // density is uncertain but shared by all impacts of the same class (correlation: if the city is denser, it is denser everywhere)
                double densityFactor = Math.Exp(rng.NextGaussian() * CasualtyModel.DensityLogSigma
                                                - 0.5 * CasualtyModel.DensityLogSigma * CasualtyModel.DensityLogSigma);

                if (impactsPerClass[c] == 0)
                    continue;

                var profile = CasualtyModel.Profile((TerrainClass)c);
                double exposed = profile.DensityPerKm2 * densityFactor * areaPerClass[c] * areaFactor;
                double fatalScale = areaPerClass[c] > 0.0 ? fatalWeightPerClass[c] / areaPerClass[c] : 1.0;
                double killed = Poisson(rng, exposed * Math.Min(1.0, profile.FatalFraction * fatalScale));
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

        // series: cumulative counts; magazines; waves; cost; casualty histogram
        static int[] Cumulate(int[] a)
        {
            var r = new int[a.Length];
            int sum = 0;
            for (int i = 0; i < a.Length; i++) r[i] = sum += a[i];
            return r;
        }
        int magazineTotal = scenario.Defenses.Where(d => d.Kind is not (DefenseKind.Radar or DefenseKind.Airbase or DefenseKind.Ew)).Sum(d => d.Magazine);
        var series = new RunSeries(binS, Cumulate(sLaunched), Cumulate(sDetected), Cumulate(sIntercepted), Cumulate(sImpacts),
            Cumulate(sJammed), Cumulate(sCrashed), Cumulate(sShots), magazineTotal);

        var waveStats = new List<WaveStat>();
        for (int w = 0; w < waveCount; w++)
        {
            var wave = scenario.Waves[w];
            string wk = wave.Kind.ToString();
            waveStats.Add(new WaveStat(wave.Id, char.ToLowerInvariant(wk[0]) + wk.Substring(1), wave.Count,
                wLaunched[w], wIntercepted[w], wJammed[w], wCrashed[w], wImpacted[w]));
        }

        double attackUsd = 0.0;
        for (int w = 0; w < waveCount; w++)
            attackUsd += wLaunched[w] * scenario.Waves[w].CostUsd;
        double defenseUsd = 0.0;
        for (int i = 0; i < scenario.Defenses.Count; i++)
            defenseUsd += defenseStats[i].Shots * scenario.Defenses[i].ShotCostUsd;

        var histogram = BuildHistogram(killedTotal, injuredTotal);

        return new RunSummary(
            total, launched, detected, intercepted, missed, impacted, violations,
            classes, Summarize(killedTotal), Summarize(injuredTotal),
            violationsByCountry.Select(kv => new CountryViolationCount(kv.Key, kv.Value)).ToList(),
            defenseStats,
            trials,
            failed,
            jammed,
            series,
            waveStats,
            histogram,
            new CostStat(attackUsd, defenseUsd),
            new WarningStat(warningOn, warnImpacts, warnUndetected,
                warnImpacts > 0 ? warnSeconds / warnImpacts : 0.0,
                warnImpacts > 0 ? 100.0 * warnShelteredSum / warnImpacts : 0.0));
    }

    /// <summary>Bin width of the time series: the smallest of a few round values that gives at most ~240 bins.</summary>
    private static double SeriesBinSeconds(double endS)
    {
        foreach (double step in new[] { 10.0, 30.0, 60.0, 120.0, 300.0, 600.0, 900.0, 1800.0, 3600.0 })
            if (endS / step <= 240.0)
                return step;
        return 7200.0;
    }

    /// <summary>Histogram of killed and injured over the trials: integer bin width, at most ~25 bins, the same bins for both.</summary>
    private static CasualtyHistogram BuildHistogram(double[] killed, double[] injured)
    {
        double max = 0.0;
        foreach (double v in killed) max = Math.Max(max, v);
        foreach (double v in injured) max = Math.Max(max, v);
        double width = Math.Max(1.0, Math.Ceiling(max / 24.0));
        int bins = (int)Math.Floor(max / width) + 1;
        var k = new int[bins];
        var i = new int[bins];
        foreach (double v in killed) k[Math.Min(bins - 1, (int)Math.Floor(v / width))]++;
        foreach (double v in injured) i[Math.Min(bins - 1, (int)Math.Floor(v / width))]++;
        return new CasualtyHistogram(width, k, i);
    }

    /// <summary>
    /// An impact within the area radius of a target (any target in the scenario, not only the wave's own) takes the class of the nearest one;
    /// otherwise it lands in the field. This handles both multiple targets in a city and collateral hits.
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

    /// <summary>Poisson(lambda): Knuth for small values, normal approximation for large ones.</summary>
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
