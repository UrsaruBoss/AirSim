using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Entities;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Scenarios;
using Sim.Core.Sun;

namespace Sim.Core.Defenses;

/// <summary>A defensive unit currently in the air (for display): a patrol aircraft or an interceptor drone.</summary>
public readonly record struct Friendly(int Id, GeoPoint Position);

/// <summary>
/// Anti-air defense. Radars detect (limited by the horizon), SAMs / guns / ABMs / patrol aircraft fire
/// (instant resolution, with a die roll), and interceptor bases launch drones that actually chase the target.
/// Deterministic: units and drones are always processed in a fixed order, and each unit has its own random-number stream.
/// </summary>
public sealed class DefenseEngine
{
    /// <summary>One degree of latitude is at least ~110.5 km; 110 km is a safe bound for fast rejection.</summary>
    private const double MetersPerLatDegreeLowerBound = 110_000.0;

    /// <summary>The defenses' streams start after any realistic drone id.</summary>
    private const ulong StreamBase = 1UL << 40;

    /// <summary>Radar horizon: d(m) = 4120 * (sqrt(h1) + sqrt(h2)), heights in meters (standard 4/3 refraction).</summary>
    private const double HorizonMetersPerSqrtMeter = 4120.0;

    private const double RadarAntennaHeightM = 10.0;
    private const double AaGunDefaultCeilingM = 4000.0;
    private const double EwDefaultCeilingM = 5000.0;
    private const double InterceptorClimbMps = 40.0;

    /// <summary>Interceptor ids start here (patrol aircraft ids are the unit index).</summary>
    public const int InterceptorIdBase = 1_000_000;

    private struct Unit
    {
        public Defense Config;
        public GeoPoint Position;
        public double RangeM;
        public double RangeLatDeg;
        // time of day (only when the scenario has a start time): range and Pk are scaled by the daylight at the unit's position
        public double BaseRangeM;
        public double BaseRangeLatDeg;
        public DayNight.UnitNight Night;
        public double PkScale;
        public double WeatherPk;
        public double[]? AltAlt;    // altitude curve (see Defense.PkAltitude)
        public double[]? AltFactor;
        public double NextLightS;
        public int Magazine;
        public double NextReadyS;
        public Rng Rng;
        public int EngageMask;
        public int PrimaryMask;
        public double[] PkByKind; // indexed by UnitKind
        public double MinAltM;
        public double MaxAltM;
        public bool Destroyed;
        public double HardnessKg;
        public double SizeM;
        public int[]? Links; // radar indices (only with LocalTracking)
        public double SelfTrackM;

        // patrols only
        public PatrolState State;
        public bool Orbiting;
        public double OrbitAngleDeg;
        public int TargetId;
        public double FuelEndS;
        public double RearmUntilS;
        public int InitialMagazine;
        public GeoPoint Home;
        public int HomeUnit;
    }

    private enum PatrolState : byte
    {
        /// <summary>Waiting on the patrol circle (or on the way to it).</summary>
        Cap,
        /// <summary>Chasing an assigned target.</summary>
        Intercept,
        /// <summary>Returning to base.</summary>
        Return,
        /// <summary>On the ground, rearming.</summary>
        Rearm
    }

    private struct Interceptor
    {
        public int Unit;
        public int Target;
        public GeoPoint Position;
        public double ExpiresAtS;
        public bool Active;
    }

    private readonly Unit[] _units;
    private readonly int[] _fireOrder;
    private readonly double _cycleSeconds;
    private readonly bool _local;
    private readonly int[] _airbaseUnits;
    private int[] _active = Array.Empty<int>(); // indices of drones in flight at the start of the cycle, ascending
    private int _activeCount;
    private Interceptor[] _interceptors = new Interceptor[16];
    private int _interceptorCount;

    public int DetectedCount { get; private set; }
    public int ShotsFired { get; private set; }
    public int InterceptedCount { get; private set; }
    public int MissedCount { get; private set; }
    public int InterceptorsLaunched { get; private set; }
    public int LostCount { get; private set; }
    public int DestroyedCount { get; private set; }
    public int JammedCount { get; private set; }

    /// <summary>Shots (and interceptor launches) fired at decoys, and how many decoys were shot down.</summary>
    public int DecoyShots { get; private set; }
    public int DecoysIntercepted { get; private set; }

    /// <summary>Cost of the shots (USD, from each system's ShotCostUsd), total and the part fired at decoys.</summary>
    public double SpentUsd { get; private set; }
    public double SpentOnDecoysUsd { get; private set; }

    /// <summary>
    /// Jamming saturation (only systems with JamCapacity): jammer cycles with targets in range, how many of them exceeded capacity,
    /// and the highest load (units in range / capacity) ever seen.
    /// </summary>
    public int EwActiveCycles { get; private set; }
    public int EwSaturatedCycles { get; private set; }
    public double EwPeakLoad { get; private set; }

    /// <summary>Missiles / projectiles / interceptors left, summed over all units.</summary>
    public int MagazineLeft
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < _units.Length; i++)
                sum += _units[i].Magazine;
            return sum;
        }
    }

    private readonly double? _startUnixMs;
    private readonly bool _timeOfDay;

    public DefenseEngine(IReadOnlyList<Defense> defenses, Rng root, bool cheapestFirst = false, double cycleSeconds = 1.0, bool localTracking = false, double? startUnixMs = null, string? weather = null)
    {
        _cycleSeconds = cycleSeconds;
        _local = localTracking;
        _startUnixMs = startUnixMs;
        _units = new Unit[defenses.Count];
        var bases = new List<int>();

        for (int i = 0; i < _units.Length; i++)
        {
            var d = defenses[i];
            double maxAlt = d.MaxAltM > 0.0 ? d.MaxAltM
                : d.Kind == DefenseKind.AaGun ? AaGunDefaultCeilingM
                : d.Kind == DefenseKind.Ew ? EwDefaultCeilingM
                : double.MaxValue;
            bool shooter = d.Kind is not (DefenseKind.Radar or DefenseKind.Airbase or DefenseKind.Ew);

            var position = new GeoPoint(d.LatDeg, d.LonDeg, d.AltM);
            double phase = (i * 137.5) % 360.0; // different phases => patrols do not fly on top of each other
            if (d.Kind == DefenseKind.Airbase)
                bases.Add(i);
            if (d.Kind == DefenseKind.Patrol && d.PatrolRadiusKm > 0.0)
                position = Wgs84.Destination(position, phase, d.PatrolRadiusKm * 1000.0);

            var wx = Sim.Core.Climate.WeatherModel.ForDefense(weather, d.Kind); // static for the whole run
            double rangeM = d.RangeKm * 1000.0 * wx.RangeScale;

            _units[i] = new Unit
            {
                Config = d,
                Position = position,
                RangeM = rangeM,
                RangeLatDeg = rangeM / MetersPerLatDegreeLowerBound,
                BaseRangeM = rangeM,
                BaseRangeLatDeg = rangeM / MetersPerLatDegreeLowerBound,
                Night = DayNight.ForDefense(d.Kind),
                PkScale = wx.PkScale,
                WeatherPk = wx.PkScale,
                AltAlt = d.PkAltitude?.Select(x => x.AltM).ToArray(),
                AltFactor = d.PkAltitude?.Select(x => x.Factor).ToArray(),
                NextLightS = 0.0,
                Magazine = shooter ? d.Magazine : 0,
                InitialMagazine = shooter ? d.Magazine : 0,
                NextReadyS = 0.0,
                Rng = root.Derive(StreamBase + (ulong)i),
                EngageMask = MaskFor(d),
                PrimaryMask = PrimaryMaskFor(d),
                PkByKind = BuildPkTable(d),
                MinAltM = d.MinAltM,
                MaxAltM = maxAlt,
                HardnessKg = d.Kind == DefenseKind.Patrol ? 0.0 : d.HardnessKg > 0.0 ? d.HardnessKg : DefaultHardnessKg(d.Kind),
                SizeM = d.SizeM > 0.0 ? d.SizeM : DefaultSizeM(d.Kind),
                HomeUnit = -1,
                State = PatrolState.Cap,
                Orbiting = true,
                OrbitAngleDeg = phase,
                TargetId = -1,
                FuelEndS = d.EnduranceSeconds,
                SelfTrackM = d.SelfTrackKm * 1000.0
            };
        }

        if (_local)
            BuildLinks();

        _airbaseUnits = bases.ToArray();
        _timeOfDay = startUnixMs.HasValue && _units.Any(u => !u.Night.IsNeutral);

        // firing order: the list order, or cheapest first (stable sort; unknown cost = last)
        var order = Enumerable.Range(0, _units.Length);
        if (cheapestFirst)
            order = order.OrderBy(i => defenses[i].ShotCostUsd > 0.0 ? defenses[i].ShotCostUsd : double.MaxValue);
        _fireOrder = order.ToArray();
    }

    /// <summary>For each firing unit: the radars (indices) within its link range. Patrols use the station position.</summary>
    private void BuildLinks()
    {
        var radars = new List<int>();
        for (int i = 0; i < _units.Length; i++)
            if (_units[i].Config.Kind == DefenseKind.Radar)
                radars.Add(i);

        for (int i = 0; i < _units.Length; i++)
        {
            ref Unit unit = ref _units[i];
            if (unit.Config.Kind is DefenseKind.Radar or DefenseKind.Airbase or DefenseKind.Ew)
                continue;

            var at = new GeoPoint(unit.Config.LatDeg, unit.Config.LonDeg);
            double linkM = unit.Config.LinkRangeKm * 1000.0;
            unit.Links = radars.Where(r => Wgs84.GreatCircleDistanceM(at, _units[r].Position) <= linkM).ToArray();
        }
    }

    /// <summary>
    /// Does the unit have a track on the target? Without LocalTracking: the target was ever detected. With LocalTracking: the target is within the unit's own radar range
    /// or an intact linked radar sees it now (range and horizon).
    /// </summary>
    private bool Tracked(in Unit unit, in Drone d)
    {
        if (!_local)
            return d.Detected;

        if (unit.SelfTrackM > 0.0 && InRange(in unit, d.Position, Math.Min(unit.SelfTrackM, unit.RangeM)))
            return true;

        foreach (int r in unit.Links ?? Array.Empty<int>())
        {
            ref readonly Unit radar = ref _units[r];
            if (radar.Destroyed)
                continue;

            double radarHeight = Math.Max(0.0, radar.Position.AltM) + RadarAntennaHeightM;
            double horizon = HorizonMetersPerSqrtMeter * (Math.Sqrt(radarHeight) + Math.Sqrt(Math.Max(0.0, d.Position.AltM)));
            if (InRange(in radar, d.Position, Math.Min(radar.RangeM, horizon)))
                return true;
        }

        return false;
    }

    private static double DefaultHardnessKg(DefenseKind kind) => kind switch
    {
        DefenseKind.Radar => 40.0,
        DefenseKind.Sam => 80.0,
        DefenseKind.AaGun => 60.0,
        DefenseKind.Abm => 100.0,
        DefenseKind.InterceptorBase => 50.0,
        DefenseKind.Ew => 30.0,
        DefenseKind.Airbase => 5000.0,
        _ => 0.0
    };

    private static double DefaultSizeM(DefenseKind kind) => kind switch
    {
        DefenseKind.Airbase => 600.0,
        DefenseKind.Abm => 40.0,
        _ => 30.0
    };

    private static int PrimaryMaskFor(Defense d)
    {
        if (d.PrimaryKinds is { Count: > 0 })
        {
            int mask = 0;
            foreach (var kind in d.PrimaryKinds)
                mask |= 1 << (int)kind;
            return mask;
        }

        const int ballistic = 1 << (int)UnitKind.BallisticMissile;
        const int nonDrone = ballistic | (1 << (int)UnitKind.Aircraft) | (1 << (int)UnitKind.CruiseMissile);
        return d.Kind == DefenseKind.Abm ? ballistic : nonDrone;
    }

    private static int ThreatWeight(UnitKind kind) => kind switch
    {
        UnitKind.BallisticMissile => 4,
        UnitKind.CruiseMissile => 3,
        UnitKind.Aircraft => 2,
        _ => 1
    };

    /// <summary>
    /// Doctrine: a primary target is always engaged; an opportunity target only if it does not touch the ammunition reserve
    /// and (if there is a cost limit) the shot is not too expensive for the target - except for leakers close to impact.
    /// </summary>
    private static bool Allowed(in Unit unit, in Drone d, double timeS)
    {
        if ((unit.PrimaryMask & (1 << (int)d.Kind)) != 0)
            return true;

        var cfg = unit.Config;
        if (cfg.ReserveRounds > 0 && unit.Magazine <= cfg.ReserveRounds)
            return false;

        double believedCost = d.PerceivedCostUsd > 0.0 ? d.PerceivedCostUsd : d.CostUsd; // the defense does not know the real cost of a decoy
        if (cfg.MaxCostRatio > 0.0 && cfg.ShotCostUsd > 0.0 && believedCost > 0.0
            && cfg.ShotCostUsd > cfg.MaxCostRatio * believedCost)
        {
            double leakerM = cfg.LeakerDistanceKm * 1000.0;
            if (leakerM <= 0.0)
                return false;
            double remaining = d.TotalDistM - (timeS - d.LaunchTimeS) * d.SpeedMps;
            return remaining <= leakerM;
        }

        return true;
    }

    private static double[] BuildPkTable(Defense d)
    {
        var table = new double[Enum.GetValues<UnitKind>().Length];
        Array.Fill(table, d.Pk);

        if (d.PkByKind is not null)
        {
            foreach (var (name, pk) in d.PkByKind)
            {
                if (Enum.TryParse<UnitKind>(name, ignoreCase: true, out var kind))
                    table[(int)kind] = pk;
            }
        }

        return table;
    }

    /// <summary>
    /// Time of day: once a minute (simulated) per optical unit, range and Pk are scaled by the daylight at the unit's position.
    /// A pure function of the position and the time, so it does not touch any random stream.
    /// </summary>
    private void RefreshLight(double timeS)
    {
        double startMs = _startUnixMs!.Value;
        for (int u = 0; u < _units.Length; u++)
        {
            ref Unit unit = ref _units[u];
            if (unit.Night.IsNeutral || timeS < unit.NextLightS)
                continue;

            double daylight = DayNight.DaylightAt(unit.Position.LatDeg, unit.Position.LonDeg, startMs + timeS * 1000.0);
            double range = DayNight.Blend(unit.Night.RangeScale, daylight);
            unit.RangeM = unit.BaseRangeM * range;
            unit.RangeLatDeg = unit.BaseRangeLatDeg * range;
            unit.PkScale = DayNight.Blend(unit.Night.PkScale, daylight) * unit.WeatherPk;
            unit.NextLightS = timeS + 60.0;
        }
    }

    private static int MaskFor(Defense d)
    {
        if (d.Engages is { Count: > 0 })
        {
            int mask = 0;
            foreach (var kind in d.Engages)
                mask |= 1 << (int)kind;
            return mask;
        }

        const int drone = 1 << (int)UnitKind.Drone;
        const int aircraft = 1 << (int)UnitKind.Aircraft;
        const int cruise = 1 << (int)UnitKind.CruiseMissile;
        const int ballistic = 1 << (int)UnitKind.BallisticMissile;
        if (d.Kind == DefenseKind.Ew)
            return drone | cruise; // satellite / inertial guidance: jamming does not affect piloted aircraft or ballistic missiles
        return d.Kind == DefenseKind.Abm ? ballistic : drone | aircraft | cruise;
    }

    /// <summary>One defense cycle. Returns how many drones were shot down (to be subtracted from the remaining ones).</summary>
    public int Update(Drone[] drones, long tick, double timeS, EventLog events)
    {
        // only drones in flight count in this cycle: the list (in id order, so the result stays identical) is built once
        if (_active.Length < drones.Length)
            _active = new int[drones.Length];
        _activeCount = 0;
        for (int k = 0; k < drones.Length; k++)
            if (drones[k].Status == EntityStatus.Flying)
                _active[_activeCount++] = k;
        if (_activeCount == 0)
            return 0;

        if (_timeOfDay)
            RefreshLight(timeS);

        // 1) radars detect the drones in flight (within range and horizon)
        for (int u = 0; u < _units.Length; u++)
        {
            ref Unit unit = ref _units[u];
            if (unit.Config.Kind != DefenseKind.Radar || unit.Destroyed)
                continue;

            double radarHeight = Math.Max(0.0, unit.Position.AltM) + RadarAntennaHeightM;
            for (int a = 0, i; a < _activeCount && (i = _active[a]) >= 0; a++)
            {
                ref Drone d = ref drones[i];
                if (d.Status != EntityStatus.Flying || d.Detected)
                    continue;

                double horizon = HorizonMetersPerSqrtMeter * (Math.Sqrt(radarHeight) + Math.Sqrt(Math.Max(0.0, d.Position.AltM)));
                if (!InRange(in unit, d.Position, Math.Min(unit.RangeM, horizon)))
                    continue;

                d.Detected = true;
                DetectedCount++;
                events.Add(new SimEvent(tick, SimEventType.Detected, d.Id, d.Position, unit.Config.Id));
            }
        }

        // 1b) electronic warfare: every unit in range has an (exponential) chance of losing guidance in this cycle
        int jammed = 0;
        for (int u = 0; u < _units.Length; u++)
        {
            ref Unit unit = ref _units[u];
            if (unit.Config.Kind != DefenseKind.Ew || unit.Destroyed || unit.Config.JamRatePerMinute <= 0.0)
                continue;

            double ratePerCycle = unit.Config.JamRatePerMinute / 60.0 * _cycleSeconds;

            // saturation: above JamCapacity units in range, efficiency is shared out
            double saturation = 1.0;
            if (unit.Config.JamCapacity > 0)
            {
                int inRange = 0;
                for (int a = 0, i; a < _activeCount && (i = _active[a]) >= 0; a++)
                    if (EwEligible(in unit, in drones[i]))
                        inRange++;
                if (inRange > 0)
                {
                    EwActiveCycles++;
                    EwPeakLoad = Math.Max(EwPeakLoad, (double)inRange / unit.Config.JamCapacity);
                }
                if (inRange > unit.Config.JamCapacity)
                {
                    saturation = (double)unit.Config.JamCapacity / inRange;
                    EwSaturatedCycles++;
                }
            }

            for (int a = 0, i; a < _activeCount && (i = _active[a]) >= 0; a++)
            {
                ref Drone d = ref drones[i];
                if (!EwEligible(in unit, in d))
                    continue;

                double strength = d.JamFactor * saturation;
                if (unit.Config.JamFalloff > 0.0)
                    strength *= 1.0 - unit.Config.JamFalloff * Math.Min(1.0, Wgs84.SlantRangeM(unit.Position, d.Position) / unit.RangeM);

                if (!unit.Rng.Chance(1.0 - Math.Exp(-ratePerCycle * strength)))
                    continue;

                d.Status = EntityStatus.Impacted; // leaves the game without hitting the target (like a crash)
                JammedCount++;
                jammed++;
                events.Add(new SimEvent(tick, SimEventType.Jammed, d.Id, d.Position, unit.Config.Id));
            }
        }

        // 2) waiting patrols are given a detected target from within their intervention range
        for (int u = 0; u < _units.Length; u++)
        {
            ref Unit unit = ref _units[u];
            if (unit.Config.Kind != DefenseKind.Patrol || unit.State != PatrolState.Cap || unit.Magazine <= 0 || timeS >= unit.FuelEndS)
                continue;

            int task = FindTaskTarget(drones, in unit, timeS);
            if (task < 0)
                continue;

            unit.TargetId = task;
            unit.State = PatrolState.Intercept;
            unit.Orbiting = false;
            drones[task].ChaseCount++;
        }

        // 3) SAMs / guns / ABMs / patrols fire, interceptor bases launch: one target per unit per cycle
        int intercepted = 0;
        foreach (int u in _fireOrder)
        {
            ref Unit unit = ref _units[u];
            var kind = unit.Config.Kind;
            if (kind is DefenseKind.Radar or DefenseKind.Airbase or DefenseKind.Ew || unit.Magazine <= 0 || timeS < unit.NextReadyS)
                continue;
            if (kind == DefenseKind.Patrol && unit.State is not (PatrolState.Cap or PatrolState.Intercept))
                continue;

            bool needsTrack = kind != DefenseKind.AaGun;
            bool launcher = kind == DefenseKind.InterceptorBase;
            int best = FindTarget(drones, in unit, timeS, needsTrack, skipChased: launcher);
            if (best < 0)
                continue;

            ref Drone target = ref drones[best];
            if (launcher)
            {
                unit.Magazine--;
                unit.NextReadyS = timeS + unit.Config.ReloadSeconds;
                ShotsFired++;
                SpentUsd += unit.Config.ShotCostUsd;
                if (target.Decoy)
                {
                    DecoyShots++;
                    SpentOnDecoysUsd += unit.Config.ShotCostUsd;
                }
                Launch(u, ref unit, ref target, tick, timeS, events);
                continue;
            }

            // primary target: up to MaxRoundsPerTarget shots in turn, stopping at the first hit
            bool primary = (unit.PrimaryMask & (1 << (int)target.Kind)) != 0;
            int rounds = primary ? Math.Clamp(unit.Config.MaxRoundsPerTarget, 1, unit.Magazine) : 1;
            double pk = unit.PkByKind[(int)target.Kind] * unit.PkScale * AltitudeFactor(in unit, target.Position.AltM);

            bool hit = false;
            int fired = 0;
            while (fired < rounds && !hit)
            {
                unit.Magazine--;
                ShotsFired++;
                fired++;
                if (unit.Rng.Chance(pk))
                {
                    hit = true;
                }
                else
                {
                    MissedCount++;
                    events.Add(new SimEvent(tick, SimEventType.Missed, target.Id, target.Position, unit.Config.Id));
                }
            }

            unit.NextReadyS = timeS + unit.Config.ReloadSeconds * fired;
            double spent = fired * unit.Config.ShotCostUsd;
            SpentUsd += spent;
            if (target.Decoy)
            {
                DecoyShots += fired;
                SpentOnDecoysUsd += spent;
            }

            if (hit)
            {
                if (target.Decoy) DecoysIntercepted++;
                target.Status = EntityStatus.Intercepted;
                InterceptedCount++;
                intercepted++;
                events.Add(new SimEvent(tick, SimEventType.Intercepted, target.Id, target.Position, unit.Config.Id));
            }
        }

        return intercepted + jammed;
    }

    /// <summary>Pk multiplier at a target altitude: linear interpolation on the unit's curve (1 when it has none).</summary>
    private static double AltitudeFactor(in Unit unit, double altM)
    {
        var alts = unit.AltAlt;
        if (alts is null)
            return 1.0;

        var f = unit.AltFactor!;
        int last = alts.Length - 1;
        if (altM <= alts[0]) return f[0];
        if (altM >= alts[last]) return f[last];

        int i = 1;
        while (alts[i] < altM) i++; // alts[i - 1] < altM <= alts[i]
        double t = (altM - alts[i - 1]) / (alts[i] - alts[i - 1]);
        return f[i - 1] + (f[i] - f[i - 1]) * t;
    }

    private static bool EwEligible(in Unit unit, in Drone d) =>
        d.Status == EntityStatus.Flying && d.JamFactor > 0.0
        && (unit.EngageMask & (1 << (int)d.Kind)) != 0
        && d.Position.AltM >= unit.MinAltM && d.Position.AltM <= unit.MaxAltM
        && InRange(in unit, d.Position, unit.RangeM);

    /// <summary>Moves the interceptors (every tick) and resolves catch-ups. Returns how many drones were shot down.</summary>
    public int MoveInterceptors(Drone[] drones, long tick, double timeS, double dt, EventLog events)
    {
        int intercepted = 0;

        for (int i = 0; i < _interceptorCount; i++)
        {
            ref Interceptor ic = ref _interceptors[i];
            if (!ic.Active)
                continue;

            ref Unit unit = ref _units[ic.Unit];
            ref Drone target = ref drones[ic.Target];
            int id = InterceptorIdBase + i;

            if (target.Status != EntityStatus.Flying || timeS >= ic.ExpiresAtS)
            {
                ic.Active = false;
                if (target.ChaseCount > 0) target.ChaseCount--;
                LostCount++;
                events.Add(new SimEvent(tick, SimEventType.InterceptorLost, id, ic.Position, unit.Config.Id));
                continue;
            }

            double step = unit.Config.InterceptorSpeedMps * dt;
            double dist = Wgs84.SlantRangeM(ic.Position, target.Position);

            if (dist <= step)
            {
                ic.Active = false;
                if (target.ChaseCount > 0) target.ChaseCount--;

                if (unit.Rng.Chance(unit.PkByKind[(int)target.Kind] * unit.PkScale * AltitudeFactor(in unit, target.Position.AltM)))
                {
                    target.Status = EntityStatus.Intercepted;
                    InterceptedCount++;
                    intercepted++;
                    if (target.Decoy) DecoysIntercepted++;
                    events.Add(new SimEvent(tick, SimEventType.Intercepted, target.Id, target.Position, unit.Config.Id));
                }
                else
                {
                    MissedCount++;
                    events.Add(new SimEvent(tick, SimEventType.Missed, target.Id, target.Position, unit.Config.Id));
                }
                continue;
            }

            // pure pursuit: toward the target's current position, climbing / descending toward its altitude
            double ground = Wgs84.GreatCircleDistanceM(ic.Position, target.Position);
            double move = Math.Min(step, ground);
            var moved = ground > 0.0
                ? Wgs84.Destination(ic.Position, Wgs84.InitialBearingDeg(ic.Position, target.Position), move)
                : ic.Position;

            double dAlt = target.Position.AltM - ic.Position.AltM;
            double maxClimb = InterceptorClimbMps * dt;
            double alt = ic.Position.AltM + Math.Clamp(dAlt, -maxClimb, maxClimb);
            ic.Position = new GeoPoint(moved.LatDeg, moved.LonDeg, alt);
        }

        return intercepted;
    }

    /// <summary>Defensive units in the air at the given time: patrol aircraft and active interceptors.</summary>
    public IReadOnlyList<Friendly> GetFriendlies(double timeS)
    {
        var list = new List<Friendly>();
        for (int u = 0; u < _units.Length; u++)
        {
            if (_units[u].Config.Kind == DefenseKind.Patrol && _units[u].State != PatrolState.Rearm)
                list.Add(new Friendly(u, _units[u].Position));
        }

        for (int i = 0; i < _interceptorCount; i++)
        {
            if (_interceptors[i].Active)
                list.Add(new Friendly(InterceptorIdBase + i, _interceptors[i].Position));
        }

        return list;
    }

    private int FindTarget(Drone[] drones, in Unit unit, double timeS, bool needsTrack, bool skipChased)
    {
        int best = -1;
        double bestRemaining = double.MaxValue;
        int bestWeight = -1;
        bool byThreat = unit.Config.Priority == TargetPriority.Threat;

        for (int a = 0, i; a < _activeCount && (i = _active[a]) >= 0; a++)
        {
            ref Drone d = ref drones[i];
            if (d.Status != EntityStatus.Flying
                || (needsTrack && !Tracked(in unit, in d))
                || (skipChased && d.ChaseCount > 0)
                || (unit.EngageMask & (1 << (int)d.Kind)) == 0
                || d.Position.AltM < unit.MinAltM
                || d.Position.AltM > unit.MaxAltM
                || !InRange(in unit, d.Position, unit.RangeM)
                || !Allowed(in unit, in d, timeS))
                continue;

            double remaining = d.TotalDistM - (timeS - d.LaunchTimeS) * d.SpeedMps;
            int weight = byThreat ? ThreatWeight(d.Kind) : 0;
            if (weight > bestWeight || (weight == bestWeight && remaining < bestRemaining)) // tie => the lower id stays
            {
                bestWeight = weight;
                bestRemaining = remaining;
                best = i;
            }
        }

        return best;
    }

    private void Launch(int unitIndex, ref Unit unit, ref Drone target, long tick, double timeS, EventLog events)
    {
        if (_interceptorCount == _interceptors.Length)
            Array.Resize(ref _interceptors, _interceptors.Length * 2);

        _interceptors[_interceptorCount] = new Interceptor
        {
            Unit = unitIndex,
            Target = target.Id,
            Position = unit.Position,
            ExpiresAtS = timeS + unit.Config.MaxFlightSeconds,
            Active = true
        };

        target.ChaseCount++;
        InterceptorsLaunched++;
        events.Add(new SimEvent(tick, SimEventType.InterceptorLaunched, InterceptorIdBase + _interceptorCount, unit.Position, unit.Config.Id));
        _interceptorCount++;
    }

    /// <summary>A detected, still unassigned target within the patrol's intervention range (distance from its center), the one closest to impact.</summary>
    private int FindTaskTarget(Drone[] drones, in Unit unit, double timeS)
    {
        var station = new GeoPoint(unit.Config.LatDeg, unit.Config.LonDeg);
        double scrambleM = unit.Config.ScrambleRangeKm * 1000.0;
        double scrambleLatDeg = scrambleM / MetersPerLatDegreeLowerBound;

        int best = -1;
        double bestRemaining = double.MaxValue;
        int bestWeight = -1;
        bool byThreat = unit.Config.Priority == TargetPriority.Threat;
        for (int a = 0, i; a < _activeCount && (i = _active[a]) >= 0; a++)
        {
            ref Drone d = ref drones[i];
            if (d.Status != EntityStatus.Flying || d.ChaseCount > 0
                || (unit.EngageMask & (1 << (int)d.Kind)) == 0
                || d.Position.AltM < unit.MinAltM || d.Position.AltM > unit.MaxAltM
                || Math.Abs(d.Position.LatDeg - station.LatDeg) > scrambleLatDeg
                || Wgs84.GreatCircleDistanceM(station, d.Position) > scrambleM
                || !Tracked(in unit, in d)
                || !Allowed(in unit, in d, timeS))
                continue;

            double remaining = d.TotalDistM - (timeS - d.LaunchTimeS) * d.SpeedMps;
            int weight = byThreat ? ThreatWeight(d.Kind) : 0;
            if (weight > bestWeight || (weight == bestWeight && remaining < bestRemaining))
            {
                bestWeight = weight;
                bestRemaining = remaining;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Moves the patrol aircraft (every tick): waiting on the circle, chasing, returning to base, rearming.
    /// Aircraft cannot be shot down (for now).
    /// </summary>
    public void MovePatrols(Drone[] drones, long tick, double timeS, double dt, EventLog events)
    {
        for (int u = 0; u < _units.Length; u++)
        {
            ref Unit unit = ref _units[u];
            if (unit.Config.Kind != DefenseKind.Patrol)
                continue;

            double step = unit.Config.PatrolSpeedMps * dt;

            switch (unit.State)
            {
                case PatrolState.Rearm:
                    if (timeS >= unit.RearmUntilS)
                    {
                        unit.Magazine = unit.InitialMagazine;
                        unit.FuelEndS = timeS + unit.Config.EnduranceSeconds;
                        unit.NextReadyS = timeS;
                        unit.State = PatrolState.Cap;
                        unit.Orbiting = false;
                        events.Add(new SimEvent(tick, SimEventType.PatrolRearmed, u, unit.Position, unit.Config.Id));
                    }
                    break;

                case PatrolState.Return:
                {
                    unit.Position = MoveToward(unit.Position, unit.Home, step, unit.Config.AltM);
                    if (Wgs84.GreatCircleDistanceM(unit.Position, unit.Home) <= 1.0)
                    {
                        unit.State = PatrolState.Rearm;
                        unit.RearmUntilS = timeS + unit.Config.TurnaroundSeconds;
                    }
                    break;
                }

                case PatrolState.Intercept:
                {
                    if (unit.Magazine <= 0 || timeS >= unit.FuelEndS)
                    {
                        StartReturn(u, ref unit, drones, tick, events);
                        break;
                    }

                    ref Drone target = ref drones[unit.TargetId];
                    if (target.Status != EntityStatus.Flying)
                    {
                        if (target.ChaseCount > 0) target.ChaseCount--;
                        unit.TargetId = -1;
                        unit.State = PatrolState.Cap;
                        unit.Orbiting = false;
                        break;
                    }

                    unit.Position = MoveToward(unit.Position, target.Position, step, unit.Config.AltM);
                    break;
                }

                default: // Cap
                    if (unit.Magazine <= 0 || timeS >= unit.FuelEndS)
                    {
                        StartReturn(u, ref unit, drones, tick, events);
                        break;
                    }
                    FlyStation(ref unit, step, dt);
                    break;
            }
        }
    }

    /// <summary>Toward the point on the circle (if not orbiting yet), then along the circle.</summary>
    private static void FlyStation(ref Unit unit, double step, double dt)
    {
        var station = new GeoPoint(unit.Config.LatDeg, unit.Config.LonDeg, unit.Config.AltM);
        double radiusM = unit.Config.PatrolRadiusKm * 1000.0;

        if (!unit.Orbiting)
        {
            var entry = radiusM > 0.0 ? Wgs84.Destination(station, unit.OrbitAngleDeg, radiusM) : station;
            unit.Position = MoveToward(unit.Position, entry, step, unit.Config.AltM);
            if (Wgs84.GreatCircleDistanceM(unit.Position, entry) <= 1.0)
                unit.Orbiting = true;
            return;
        }

        if (radiusM <= 0.0)
        {
            unit.Position = station;
            return;
        }

        unit.OrbitAngleDeg = (unit.OrbitAngleDeg + unit.Config.PatrolSpeedMps / radiusM * dt * (180.0 / Math.PI)) % 360.0;
        unit.Position = Wgs84.Destination(station, unit.OrbitAngleDeg, radiusM);
    }

    private void StartReturn(int index, ref Unit unit, Drone[] drones, long tick, EventLog events)
    {
        if (unit.State == PatrolState.Intercept && unit.TargetId >= 0 && drones[unit.TargetId].ChaseCount > 0)
            drones[unit.TargetId].ChaseCount--;

        unit.TargetId = -1;
        unit.Orbiting = false;
        unit.State = PatrolState.Return;
        if (TryNearestAirbase(unit.Position, out var home, out int homeUnit))
        {
            unit.Home = home;
            unit.HomeUnit = homeUnit;
        }
        else
        {
            StrandPatrol(ref unit); // no intact base: the aircraft can no longer land
        }

        events.Add(new SimEvent(tick, SimEventType.PatrolReturning, index, unit.Position, unit.Config.Id));
    }

    /// <summary>Aircraft without a base: stays on the ground for good (no longer shown, no longer fires).</summary>
    private static void StrandPatrol(ref Unit unit)
    {
        unit.State = PatrolState.Rearm;
        unit.RearmUntilS = double.MaxValue;
    }

    /// <summary>
    /// An impact with a WarheadKg payload destroys every system within SizeM + lethal radius of the impact point,
    /// with probability 1 - exp(-WarheadKg / HardnessKg). Systems are visited in list order (deterministic).
    /// </summary>
    public void ApplyImpact(in Drone d, long tick, EventLog events)
    {
        if (d.WarheadKg <= 0.0)
            return;

        double lethalM = 6.0 * Math.Cbrt(d.WarheadKg);
        for (int u = 0; u < _units.Length; u++)
        {
            ref Unit unit = ref _units[u];
            if (unit.Destroyed || unit.HardnessKg <= 0.0)
                continue;

            double radiusM = unit.SizeM + lethalM;
            if (Math.Abs(d.Position.LatDeg - unit.Config.LatDeg) * MetersPerLatDegreeLowerBound > radiusM)
                continue;
            if (Wgs84.GreatCircleDistanceM(new GeoPoint(unit.Config.LatDeg, unit.Config.LonDeg), d.Position) > radiusM)
                continue;
            if (!unit.Rng.Chance(1.0 - Math.Exp(-d.WarheadKg / unit.HardnessKg)))
                continue;

            unit.Destroyed = true;
            unit.Magazine = 0;
            DestroyedCount++;
            events.Add(new SimEvent(tick, SimEventType.DefenseDestroyed, d.Id, unit.Position, unit.Config.Id));

            if (unit.Config.Kind == DefenseKind.Airbase)
                OnAirbaseDestroyed(u);
        }
    }

    /// <summary>Aircraft that were rearming at the destroyed base are lost; those heading to it look for another base.</summary>
    private void OnAirbaseDestroyed(int baseUnit)
    {
        for (int u = 0; u < _units.Length; u++)
        {
            ref Unit unit = ref _units[u];
            if (unit.Config.Kind != DefenseKind.Patrol || unit.HomeUnit != baseUnit)
                continue;

            if (unit.State == PatrolState.Rearm)
            {
                unit.RearmUntilS = double.MaxValue;
            }
            else if (unit.State == PatrolState.Return)
            {
                if (TryNearestAirbase(unit.Position, out var home, out int homeUnit))
                {
                    unit.Home = home;
                    unit.HomeUnit = homeUnit;
                }
                else
                {
                    StrandPatrol(ref unit);
                }
            }
        }
    }

    private bool TryNearestAirbase(GeoPoint from, out GeoPoint home, out int homeUnit)
    {
        home = default;
        homeUnit = -1;
        double bestDist = double.MaxValue;
        foreach (int b in _airbaseUnits) // tie => the first in the list
        {
            if (_units[b].Destroyed)
                continue;

            double dist = Wgs84.GreatCircleDistanceM(from, _units[b].Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                home = _units[b].Position;
                homeUnit = b;
            }
        }
        return homeUnit >= 0;
    }

    /// <summary>Horizontal step toward the destination (no overshoot), at fixed altitude.</summary>
    private static GeoPoint MoveToward(GeoPoint from, GeoPoint to, double distanceM, double altM)
    {
        double ground = Wgs84.GreatCircleDistanceM(from, to);
        if (ground <= distanceM)
            return new GeoPoint(to.LatDeg, to.LonDeg, altM);

        var moved = Wgs84.Destination(from, Wgs84.InitialBearingDeg(from, to), distanceM);
        return new GeoPoint(moved.LatDeg, moved.LonDeg, altM);
    }

    private static bool InRange(in Unit unit, GeoPoint p, double rangeM)
    {
        // fast latitude rejection against the unit's current position (rangeM <= RangeM, so the bound is safe)
        if (Math.Abs(p.LatDeg - unit.Position.LatDeg) > unit.RangeLatDeg)
            return false;
        return Wgs84.SlantRangeM(unit.Position, p) <= rangeM;
    }
}
