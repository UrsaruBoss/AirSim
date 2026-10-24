using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core.Entities;
using Sim.Core.Events;
using Sim.Core.Geo;
using Sim.Core.Scenarios;

namespace Sim.Core.Defenses;

/// <summary>O unitate defensiva aflata in aer (pentru afisare): avion de patrula sau dronă interceptoare.</summary>
public readonly record struct Friendly(int Id, GeoPoint Position);

/// <summary>
/// Aparare anti-aeriana. Radarele detecteaza (cu limita orizontului), SAM / tunuri / ABM / avioane de patrula trag
/// (rezolvare instantanee, cu zar), iar bazele de interceptoare lanseaza drone care urmaresc efectiv tinta.
/// Determinist: unitatile si dronele se proceseaza mereu in ordine fixa, iar fiecare unitate are propriul flux de numere aleatoare.
/// </summary>
public sealed class DefenseEngine
{
    /// <summary>Un grad de latitudine are cel putin ~110,5 km; 110 km da o limita sigura pentru respingerea rapida.</summary>
    private const double MetersPerLatDegreeLowerBound = 110_000.0;

    /// <summary>Fluxurile defensivelor incep dupa orice id de drona realist.</summary>
    private const ulong StreamBase = 1UL << 40;

    /// <summary>Orizont radar: d(m) = 4120 * (sqrt(h1) + sqrt(h2)), inaltimile in metri (refractie standard 4/3).</summary>
    private const double HorizonMetersPerSqrtMeter = 4120.0;

    private const double RadarAntennaHeightM = 10.0;
    private const double AaGunDefaultCeilingM = 4000.0;
    private const double EwDefaultCeilingM = 5000.0;
    private const double InterceptorClimbMps = 40.0;

    /// <summary>Id-urile interceptoarelor incep de aici (id-urile avioanelor de patrula sunt indexul unitatii).</summary>
    public const int InterceptorIdBase = 1_000_000;

    private struct Unit
    {
        public Defense Config;
        public GeoPoint Position;
        public double RangeM;
        public double RangeLatDeg;
        public int Magazine;
        public double NextReadyS;
        public Rng Rng;
        public int EngageMask;
        public int PrimaryMask;
        public double[] PkByKind; // indexat dupa UnitKind
        public double MinAltM;
        public double MaxAltM;
        public bool Destroyed;
        public double HardnessKg;
        public double SizeM;
        public int[]? Links; // indexuri de radare (doar cu LocalTracking)
        public double SelfTrackM;

        // doar pentru patrule
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
        /// <summary>In asteptare, pe cercul de patrulare (sau in drum spre el).</summary>
        Cap,
        /// <summary>Urmareste o tinta alocata.</summary>
        Intercept,
        /// <summary>Se intoarce la baza.</summary>
        Return,
        /// <summary>La sol, se reincarca.</summary>
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
    private int[] _active = Array.Empty<int>(); // indicii dronelor in zbor la inceputul ciclului, crescator
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

    /// <summary>Tiruri (si lansari de interceptoare) trase in momeli, si cate momeli au fost doborate.</summary>
    public int DecoyShots { get; private set; }
    public int DecoysIntercepted { get; private set; }

    /// <summary>Costul tirurilor (USD, dupa ShotCostUsd al fiecarui sistem), total si cel tras in momeli.</summary>
    public double SpentUsd { get; private set; }
    public double SpentOnDecoysUsd { get; private set; }

    /// <summary>
    /// Saturarea bruiajului (doar sistemele cu JamCapacity): cicluri-bruior cu tinte in raza, cate dintre ele au depasit capacitatea
    /// si cea mai mare incarcare (unitati in raza / capacitate) vazuta vreodata.
    /// </summary>
    public int EwActiveCycles { get; private set; }
    public int EwSaturatedCycles { get; private set; }
    public double EwPeakLoad { get; private set; }

    /// <summary>Rachete / proiectile / interceptoare ramase, insumate pe toate unitatile.</summary>
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

    public DefenseEngine(IReadOnlyList<Defense> defenses, Rng root, bool cheapestFirst = false, double cycleSeconds = 1.0, bool localTracking = false)
    {
        _cycleSeconds = cycleSeconds;
        _local = localTracking;
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
            double phase = (i * 137.5) % 360.0; // faze diferite => patrulele nu zboara suprapuse
            if (d.Kind == DefenseKind.Airbase)
                bases.Add(i);
            if (d.Kind == DefenseKind.Patrol && d.PatrolRadiusKm > 0.0)
                position = Wgs84.Destination(position, phase, d.PatrolRadiusKm * 1000.0);

            _units[i] = new Unit
            {
                Config = d,
                Position = position,
                RangeM = d.RangeKm * 1000.0,
                RangeLatDeg = d.RangeKm * 1000.0 / MetersPerLatDegreeLowerBound,
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

        // ordinea de tragere: lista, sau cele mai ieftine intai (sortare stabila; cost necunoscut = la urma)
        var order = Enumerable.Range(0, _units.Length);
        if (cheapestFirst)
            order = order.OrderBy(i => defenses[i].ShotCostUsd > 0.0 ? defenses[i].ShotCostUsd : double.MaxValue);
        _fireOrder = order.ToArray();
    }

    /// <summary>Pentru fiecare unitate care trage: radarele (indexuri) aflate in raza ei de legatura. Patrulele folosesc pozitia statiei.</summary>
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
    /// Are unitatea pista pe tinta? Fara LocalTracking: tinta a fost detectata vreodata. Cu LocalTracking: tinta e in raza radarului propriu
    /// sau un radar intact din legatura o vede acum (raza si orizont).
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
    /// Doctrina: o tinta principala se angajeaza mereu; una de oportunitate numai daca nu atinge rezerva de munitie
    /// si (daca exista limita de cost) tirul nu e prea scump fata de tinta - cu exceptia scapatoarelor aproape de impact.
    /// </summary>
    private static bool Allowed(in Unit unit, in Drone d, double timeS)
    {
        if ((unit.PrimaryMask & (1 << (int)d.Kind)) != 0)
            return true;

        var cfg = unit.Config;
        if (cfg.ReserveRounds > 0 && unit.Magazine <= cfg.ReserveRounds)
            return false;

        double believedCost = d.PerceivedCostUsd > 0.0 ? d.PerceivedCostUsd : d.CostUsd; // apararea nu stie costul real al unei momeli
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
            return drone | cruise; // ghidare prin satelit / inertiala: bruiajul nu afecteaza avioane cu pilot sau rachete balistice
        return d.Kind == DefenseKind.Abm ? ballistic : drone | aircraft | cruise;
    }

    /// <summary>Un ciclu de aparare. Intoarce cate drone au fost doborate (trebuie scazute din cele ramase).</summary>
    public int Update(Drone[] drones, long tick, double timeS, EventLog events)
    {
        // doar dronele in zbor conteaza in acest ciclu: lista (in ordinea id-urilor, ca rezultatul sa ramana identic) se face o data
        if (_active.Length < drones.Length)
            _active = new int[drones.Length];
        _activeCount = 0;
        for (int k = 0; k < drones.Length; k++)
            if (drones[k].Status == EntityStatus.Flying)
                _active[_activeCount++] = k;
        if (_activeCount == 0)
            return 0;

        // 1) radarele detecteaza dronele aflate in zbor (in limita razei si a orizontului)
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

        // 1b) razboiul electronic: fiecare unitate din raza are o sansa (exponentiala) sa-si piarda ghidarea in acest ciclu
        int jammed = 0;
        for (int u = 0; u < _units.Length; u++)
        {
            ref Unit unit = ref _units[u];
            if (unit.Config.Kind != DefenseKind.Ew || unit.Destroyed || unit.Config.JamRatePerMinute <= 0.0)
                continue;

            double ratePerCycle = unit.Config.JamRatePerMinute / 60.0 * _cycleSeconds;

            // saturare: peste JamCapacity unitati in raza, eficienta se imparte
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

                d.Status = EntityStatus.Impacted; // iese din joc fara sa loveasca tinta (ca o cadere)
                JammedCount++;
                jammed++;
                events.Add(new SimEvent(tick, SimEventType.Jammed, d.Id, d.Position, unit.Config.Id));
            }
        }

        // 2) patrulele in asteptare primesc o tinta detectata din raza lor de interventie
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

        // 3) SAM / tunuri / ABM / patrule trag, bazele de interceptoare lanseaza: o singura tinta per unitate per ciclu
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

            // tinta principala: pana la MaxRoundsPerTarget tiruri pe rand, oprire la prima lovitura
            bool primary = (unit.PrimaryMask & (1 << (int)target.Kind)) != 0;
            int rounds = primary ? Math.Clamp(unit.Config.MaxRoundsPerTarget, 1, unit.Magazine) : 1;
            double pk = unit.PkByKind[(int)target.Kind];

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

    private static bool EwEligible(in Unit unit, in Drone d) =>
        d.Status == EntityStatus.Flying && d.JamFactor > 0.0
        && (unit.EngageMask & (1 << (int)d.Kind)) != 0
        && d.Position.AltM >= unit.MinAltM && d.Position.AltM <= unit.MaxAltM
        && InRange(in unit, d.Position, unit.RangeM);

    /// <summary>Muta interceptoarele (la fiecare tick) si rezolva ajungerea din urma. Intoarce cate drone au fost doborate.</summary>
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

                if (unit.Rng.Chance(unit.PkByKind[(int)target.Kind]))
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

            // urmarire pura: spre pozitia curenta a tintei, urcand / coborand spre altitudinea ei
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

    /// <summary>Unitatile defensive aflate in aer la momentul dat: avioanele de patrula si interceptoarele active.</summary>
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
            if (weight > bestWeight || (weight == bestWeight && remaining < bestRemaining)) // egalitate => ramane id-ul mai mic
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

    /// <summary>Tinta detectata, inca nealocata, din raza de interventie a patrulei (distanta fata de centrul ei), cea mai apropiata de impact.</summary>
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
    /// Muta avioanele de patrula (la fiecare tick): asteptare pe cerc, urmarire, intoarcere la baza, reincarcare.
    /// Avioanele nu pot fi doborate (deocamdata).
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

    /// <summary>Spre punctul de pe cerc (daca nu orbiteaza inca), apoi pe cerc.</summary>
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
            StrandPatrol(ref unit); // nicio baza intreaga: avionul nu mai poate ateriza
        }

        events.Add(new SimEvent(tick, SimEventType.PatrolReturning, index, unit.Position, unit.Config.Id));
    }

    /// <summary>Avion fara baza: ramane definitiv la sol (nu mai apare si nu mai trage).</summary>
    private static void StrandPatrol(ref Unit unit)
    {
        unit.State = PatrolState.Rearm;
        unit.RearmUntilS = double.MaxValue;
    }

    /// <summary>
    /// Un impact cu sarcina WarheadKg distruge fiecare sistem aflat la cel mult SizeM + raza letala de punctul de impact,
    /// cu probabilitatea 1 - exp(-WarheadKg / HardnessKg). Sistemele se parcurg in ordinea listei (determinist).
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

    /// <summary>Avioanele care se reincarcau la baza distrusa sunt pierdute; cele care se indreptau spre ea cauta alta baza.</summary>
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
        foreach (int b in _airbaseUnits) // egalitate => prima din lista
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

    /// <summary>Pas orizontal spre destinatie (fara depasire), la altitudine fixa.</summary>
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
        // respingere rapida pe latitudine fata de pozitia curenta a unitatii (rangeM <= RangeM, deci limita e sigura)
        if (Math.Abs(p.LatDeg - unit.Position.LatDeg) > unit.RangeLatDeg)
            return false;
        return Wgs84.SlantRangeM(unit.Position, p) <= rangeM;
    }
}
