using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Airspace;
using Sim.Core.Defenses;
using Sim.Core.Entities;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Scenarios;

namespace Sim.Core;

/// <summary>
/// The simulation core: deterministic (scenario + seed + engine version => same event log).
/// Uses no system clock, no Guids and no collections with undefined ordering.
/// </summary>
public sealed class Simulation
{
    private readonly Drone[] _drones;
    private readonly long _maxTicks;
    private readonly AirspaceModel? _airspace;
    private readonly DefenseEngine? _defense;
    private readonly long _defenseEveryTicks;
    private int _remaining;
    private readonly int[] _targetNeeds;
    private readonly int[] _targetImpacts;
    private readonly int[]?[] _retargetCandidates;
    private readonly Rng _retargetRng;

    public Scenario Scenario { get; }
    public ulong Seed { get; }
    public SimClock Clock { get; }
    public EventLog Events { get; } = new();

    /// <summary>Current state of the drones (read). Useful for snapshots sent to the frontend.</summary>
    public ReadOnlySpan<Drone> Drones => _drones;

    public bool IsFinished => _remaining == 0 || Clock.Tick >= _maxTicks;

    /// <summary>Total number of drones in the scenario.</summary>
    public int DroneCount => _drones.Length;

    /// <summary>Drones that have not hit yet (waiting or flying).</summary>
    public int RemainingCount => _remaining;

    /// <summary>Number of airspace violations so far.</summary>
    public int ViolationCount { get; private set; }

    /// <summary>Drones detected by radars.</summary>
    public int DetectedCount => _defense?.DetectedCount ?? 0;

    /// <summary>Drones shot down by the defense.</summary>
    public int InterceptedCount => _defense?.InterceptedCount ?? 0;

    /// <summary>Shots fired by defensive systems (hit or miss).</summary>
    public int ShotsFired => _defense?.ShotsFired ?? 0;

    /// <summary>Missiles / projectiles left in the defense.</summary>
    public int MagazineLeft => _defense?.MagazineLeft ?? 0;

    /// <summary>Interceptors launched (interceptor drones), regardless of outcome.</summary>
    public int InterceptorsLaunched => _defense?.InterceptorsLaunched ?? 0;

    /// <summary>Interceptors lost: the target vanished or their flight ended before reaching it.</summary>
    public int InterceptorsLost => _defense?.LostCount ?? 0;
    public int DefensesDestroyed => _defense?.DestroyedCount ?? 0;

    /// <summary>Shots fired at decoys (defense) and decoys shot down.</summary>
    public int DecoyShots => _defense?.DecoyShots ?? 0;
    public int DecoysIntercepted => _defense?.DecoysIntercepted ?? 0;

    /// <summary>Cost (USD): the defense's shots, of which at decoys, and the attacker's units (sum of the waves' CostUsd).</summary>
    public double DefenseSpentUsd => _defense?.SpentUsd ?? 0.0;
    public double DecoySpentUsd => _defense?.SpentOnDecoysUsd ?? 0.0;
    public double AttackCostUsd { get; private set; }

    /// <summary>Jamming saturation: active jammer cycles, how many exceeded capacity, peak load (x capacity).</summary>
    public int EwActiveCycles => _defense?.EwActiveCycles ?? 0;
    public int EwSaturatedCycles => _defense?.EwSaturatedCycles ?? 0;
    public double EwPeakLoad => _defense?.EwPeakLoad ?? 0.0;

    /// <summary>Units that changed their target in flight.</summary>
    public int RetargetedCount { get; private set; }

    /// <summary>Defensive units currently in the air (patrol aircraft, interceptor drones), for display.</summary>
    public IReadOnlyList<Friendly> GetFriendlies() => _defense?.GetFriendlies(Clock.Time) ?? Array.Empty<Friendly>();

    /// <summary>Drones that reached their target.</summary>
    public int CrashedCount { get; private set; }
    public int JammedCount => _defense?.JammedCount ?? 0;
    public int ImpactCount => _drones.Length - _remaining - InterceptedCount - CrashedCount - JammedCount;

    /// <param name="airspace">
    /// Country map with the scenario's roles. Required if the scenario declares AirspaceMap; without it there are no airspace events.
    /// </param>
    public Simulation(Scenario scenario, ulong seed, AirspaceModel? airspace = null)
    {
        var errors = ScenarioLoader.Validate(scenario);
        if (errors.Count > 0)
            throw new ArgumentException("Invalid scenario:" + Environment.NewLine + string.Join(Environment.NewLine, errors), nameof(scenario));

        if (!string.IsNullOrEmpty(scenario.AirspaceMap) && airspace is null)
            throw new ArgumentException($"The scenario requires the airspace map '{scenario.AirspaceMap}', but it was not provided.", nameof(airspace));

        _airspace = airspace;

        Scenario = scenario;
        Seed = seed;
        Clock = new SimClock(scenario.DtSeconds);
        _maxTicks = Clock.TicksFor(scenario.DurationSeconds);

        var root = new Rng(seed);
        var sites = scenario.LaunchSites.ToDictionary(s => s.Id);
        var targets = scenario.Targets.ToDictionary(t => t.Id);
        var drones = new List<Drone>();

        var targetIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < scenario.Targets.Count; i++)
            targetIndex[scenario.Targets[i].Id] = i;
        _targetNeeds = scenario.Targets.Select(t => t.ImpactsToDestroy).ToArray();
        _targetImpacts = new int[scenario.Targets.Count];
        _retargetRng = root.Derive(1UL << 43);
        _retargetCandidates = new int[]?[scenario.Waves.Count];
        for (int wi = 0; wi < scenario.Waves.Count; wi++)
        {
            var w = scenario.Waves[wi];
            if (!w.Retarget)
                continue;
            var ids = w.TargetIds is { Count: > 0 } ? w.TargetIds : new List<string> { w.TargetId };
            _retargetCandidates[wi] = ids.Distinct().Select(id => targetIndex[id]).ToArray();
        }

        for (int wi = 0; wi < scenario.Waves.Count; wi++)
        {
            var wave = scenario.Waves[wi];
            var siteChoices = wave.SiteIds is { Count: > 0 } ? wave.SiteIds : null;
            var targetChoices = wave.TargetIds is { Count: > 0 } ? wave.TargetIds : null;

            for (int i = 0; i < wave.Count; i++)
            {
                int id = drones.Count;
                var rng = root.Derive((ulong)id); // own stream per drone

                // choices consume from the stream only when lists exist (old scenarios stay identical)
                var site = sites[siteChoices is null ? wave.SiteId : siteChoices[rng.NextInt(siteChoices.Count)]];
                string targetId = targetChoices is null ? wave.TargetId : targetChoices[rng.NextInt(targetChoices.Count)];
                var target = targets[targetId];
                var start = new GeoPoint(site.LatDeg, site.LonDeg, site.AltM);
                var targetPoint = new GeoPoint(target.LatDeg, target.LonDeg, target.AltM);

                var aim = Scatter(targetPoint, wave.SpreadMeters, rng);
                double groundDist = Wgs84.GreatCircleDistanceM(start, aim);

                double failRate = wave.Kind == UnitKind.Drone
                    ? Math.Min(1.0, wave.FailureRate + Sim.Core.Climate.WeatherModel.DroneFailureAdd(scenario.Weather))
                    : wave.FailureRate;
                bool failed = failRate > 0.0 && rng.Chance(failRate);
                if (failed)
                {
                    groundDist *= rng.NextDouble(0.05, 0.95);
                    var fell = Wgs84.Destination(start, Wgs84.InitialBearingDeg(start, aim), groundDist);
                    aim = new GeoPoint(fell.LatDeg, fell.LonDeg, targetPoint.AltM);
                }

                double apogee = wave.Kind != UnitKind.BallisticMissile ? 0.0
                    : wave.ApogeeM > 0.0 ? wave.ApogeeM
                    : Math.Clamp(0.25 * groundDist, 20_000.0, 300_000.0);

                drones.Add(new Drone
                {
                    Id = id,
                    WaveIndex = wi,
                    Kind = wave.Kind,
                    ApogeeM = apogee,
                    CostUsd = wave.CostUsd,
                    WarheadKg = wave.WarheadKg,
                    Failed = failed,
                    JamFactor = 1.0 - wave.EwResistance,
                    Decoy = wave.Decoy,
                    PerceivedCostUsd = wave.PerceivedCostUsd,
                    TargetIndex = targetIndex[targetId],
                    CanRetarget = wave.Retarget && !failed && wave.Kind != UnitKind.BallisticMissile,
                    RetargetAtM = wave.RetargetAtKm * 1000.0,
                    Status = EntityStatus.Pending,
                    LaunchTimeS = wave.StartTimeSeconds + i * wave.IntervalSeconds,
                    SpeedMps = wave.SpeedMps,
                    CruiseAltM = wave.AltitudeM,
                    Start = start,
                    Aim = aim,
                    BearingDeg = Wgs84.InitialBearingDeg(start, aim),
                    TotalDistM = groundDist,
                    Position = start,
                    CountryIndex = -1
                });
            }
        }

        _drones = drones.ToArray();
        AttackCostUsd = _drones.Sum(x => x.CostUsd);
        _remaining = _drones.Length;

        if (scenario.Defenses.Count > 0)
        {
            _defense = new DefenseEngine(scenario.Defenses, root, scenario.CheapestFirst, scenario.DefenseIntervalSeconds, scenario.LocalTracking,
                Sim.Core.Sun.DayNight.TryParseUtc(scenario.StartTimeUtc, out double startMs) ? startMs : null, scenario.Weather);
            _defenseEveryTicks = Math.Max(1L, (long)Math.Round(scenario.DefenseIntervalSeconds / scenario.DtSeconds));
        }
    }

    /// <summary>A single tick. Drones are always processed in id order => event order is deterministic.</summary>
    public void Step()
    {
        double t = Clock.Time;
        long tick = Clock.Tick;

        for (int i = 0; i < _drones.Length; i++)
        {
            ref Drone d = ref _drones[i];

            if (d.Status is EntityStatus.Impacted or EntityStatus.Intercepted)
                continue;

            if (d.Status == EntityStatus.Pending)
            {
                if (t < d.LaunchTimeS)
                    continue;

                d.Status = EntityStatus.Flying;
                d.Position = d.Start;
                // the launch country is not a violation: remember it without an event
                d.CountryIndex = _airspace?.CountryIndexAt(d.Start.LatDeg, d.Start.LonDeg) ?? -1;
                Events.Add(new SimEvent(tick, SimEventType.Launched, d.Id, d.Start));
            }

            double traveled = (t - d.LaunchTimeS) * d.SpeedMps;
            if (d.CanRetarget && !d.RetargetChecked && d.TotalDistM - traveled <= d.RetargetAtM)
            {
                d.RetargetChecked = true; // the decision is taken only once
                if (TryRetarget(ref d, tick, t))
                    traveled = 0.0;
            }

            if (traveled >= d.TotalDistM)
            {
                d.Status = EntityStatus.Impacted;
                d.Position = d.Aim;
                _remaining--;
                if (_airspace is not null)
                    CheckAirspace(ref d, tick, d.Aim);
                if (d.Failed)
                {
                    CrashedCount++;
                    Events.Add(new SimEvent(tick, SimEventType.Crashed, d.Id, d.Aim));
                }
                else
                {
                    Events.Add(new SimEvent(tick, SimEventType.Impact, d.Id, d.Aim));
                    _targetImpacts[d.TargetIndex]++;
                    _defense?.ApplyImpact(in d, tick, Events);
                }
            }
            else
            {
                var p = Wgs84.Destination(d.Start, d.BearingDeg, traveled);
                d.Position = new GeoPoint(p.LatDeg, p.LonDeg, AltitudeAt(in d, traveled));
                if (_airspace is not null)
                    CheckAirspace(ref d, tick, d.Position);
            }
        }

        if (_defense is not null)
        {
            if (tick % _defenseEveryTicks == 0)
                _remaining -= _defense.Update(_drones, tick, t, Events);
            _remaining -= _defense.MoveInterceptors(_drones, tick, t, Clock.Dt, Events);
            _defense.MovePatrols(_drones, tick, t, Clock.Dt, Events);
        }

        Clock.Advance();
    }

    /// <summary>Runs until all drones have hit or the scenario duration is reached.</summary>
    public void Run()
    {
        while (!IsFinished)
            Step();
    }

    private bool IsDestroyed(int target) => _targetNeeds[target] > 0 && _targetImpacts[target] >= _targetNeeds[target];

    /// <summary>If the current target is destroyed, picks another non-destroyed target at random from the wave's list (within RetargetRangeKm) and resumes the route from here.</summary>
    private bool TryRetarget(ref Drone d, long tick, double t)
    {
        int current = d.TargetIndex;
        if (!IsDestroyed(current))
            return false;

        var wave = Scenario.Waves[d.WaveIndex];
        double rangeM = wave.RetargetRangeKm * 1000.0;
        var options = new List<int>();
        foreach (int c in _retargetCandidates[d.WaveIndex]!)
        {
            if (c == current || IsDestroyed(c))
                continue;
            var tg = Scenario.Targets[c];
            if (Wgs84.GreatCircleDistanceM(d.Position, new GeoPoint(tg.LatDeg, tg.LonDeg)) <= rangeM)
                options.Add(c);
        }

        if (options.Count == 0)
            return false;

        int pick = options[_retargetRng.NextInt(options.Count)];
        var target = Scenario.Targets[pick];
        var aim = Scatter(new GeoPoint(target.LatDeg, target.LonDeg, target.AltM), wave.SpreadMeters, _retargetRng);

        d.Start = d.Position;
        d.LaunchTimeS = t;
        d.Aim = aim;
        d.BearingDeg = Wgs84.InitialBearingDeg(d.Start, aim);
        d.TotalDistM = Wgs84.GreatCircleDistanceM(d.Start, aim);
        d.TargetIndex = pick;

        RetargetedCount++;
        Events.Add(new SimEvent(tick, SimEventType.Retargeted, d.Id, d.Position, target.Id));
        return true;
    }

    /// <summary>
    /// On a country change: if the new country is forbidden to offensive units (neutral or allied to the defender),
    /// emits AirspaceViolation (once per entry, not on every tick).
    /// </summary>
    private void CheckAirspace(ref Drone d, long tick, GeoPoint position)
    {
        int index = _airspace!.CountryIndexAt(position.LatDeg, position.LonDeg);
        if (index == d.CountryIndex)
            return;

        d.CountryIndex = index;
        if (index >= 0 && _airspace.IsViolationForAttackers(index))
        {
            ViolationCount++;
            Events.Add(new SimEvent(tick, SimEventType.AirspaceViolation, d.Id, position, _airspace.CodeOf(index)));
        }
    }

    /// <summary>Ballistic missiles follow a parabola (apogee at mid-distance); everything else flies at constant altitude.</summary>
    private static double AltitudeAt(in Drone d, double traveledM)
    {
        if (d.Kind != UnitKind.BallisticMissile)
            return d.CruiseAltM;

        double s = Math.Clamp(traveledM / d.TotalDistM, 0.0, 1.0);
        return d.Start.AltM + (d.Aim.AltM - d.Start.AltM) * s + 4.0 * d.ApogeeM * s * (1.0 - s);
    }

    // Aim-point dispersion: two normals (east, north) with standard deviation sigmaM.
    // Two values are always consumed from the stream, even when sigmaM = 0.
    private static GeoPoint Scatter(GeoPoint center, double sigmaM, Rng rng)
    {
        double east = rng.NextGaussian() * sigmaM;
        double north = rng.NextGaussian() * sigmaM;
        double dist = Math.Sqrt(east * east + north * north);
        if (dist == 0.0)
            return center;

        double bearingDeg = Math.Atan2(east, north) * 180.0 / Math.PI;
        return Wgs84.Destination(center, bearingDeg, dist);
    }
}
