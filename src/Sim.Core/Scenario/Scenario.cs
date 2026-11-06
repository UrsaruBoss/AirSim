using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sim.Core.Airspace;

namespace Sim.Core.Scenarios;

public enum UnitKind
{
    Drone,
    BallisticMissile,
    Aircraft,
    /// <summary>Cruise missile: flies like a drone (constant altitude), but is treated separately by the defense.</summary>
    CruiseMissile
}

/// <summary>Launch point (offensive). Location in WGS84 degrees, altitude in meters.</summary>
public sealed record LaunchSite
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double LatDeg { get; init; }
    public double LonDeg { get; init; }
    public double AltM { get; init; }
}

/// <summary>Target. Kind is free-form for now ("city", "military_base", "field"); it becomes the terrain class later.</summary>
public sealed record Target
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "field";
    public double LatDeg { get; init; }
    public double LonDeg { get; init; }
    public double AltM { get; init; }

    /// <summary>Radius of the target's area (m): impacts inside take the target's class, the rest land in the field. 0 = default by type (city 3000, base 600).</summary>
    public double RadiusM { get; init; }

    /// <summary>How many impacts (that reach the target) destroy it. 0 = never destroyed. A destroyed target may be abandoned by waves with Retarget.</summary>
    public int ImpactsToDestroy { get; init; }
}

/// <summary>
/// Launch wave: Count units from SiteId toward TargetId, launched every IntervalSeconds
/// starting at StartTimeSeconds. SpreadMeters = dispersion of the aim point (standard deviation).
/// </summary>
public sealed record Wave
{
    public string Id { get; init; } = "";
    public string SiteId { get; init; } = "";
    public string TargetId { get; init; } = "";

    /// <summary>
    /// Several launch points: each unit picks one at random (order matters for reproducibility;
    /// an id repeated several times = higher weight). If the list is not empty, it replaces SiteId.
    /// </summary>
    public List<string>? SiteIds { get; init; }

    /// <summary>Same for targets: each unit picks one from the list (e.g. several points in a city). Replaces TargetId.</summary>
    public List<string>? TargetIds { get; init; }

    /// <summary>Probability that a unit does not reach its target (malfunction, out of fuel): it falls at a random point of the route (5%-95%).</summary>
    public double FailureRate { get; init; }

    /// <summary>Jamming resistance (0-1): 0 = fully susceptible (civil GPS), 1 = immune (inertial guidance / CRPA antennas / ballistic missile).</summary>
    public double EwResistance { get; init; }

    /// <summary>Decoy: does no real damage, but the defense cannot tell it from a weapon. Used only for statistics (shots wasted on decoys).</summary>
    public bool Decoy { get; init; }

    /// <summary>The cost the defense BELIEVES the unit has (USD): a cheap decoy that looks like an expensive weapon draws expensive shots. 0 = the real cost.</summary>
    public double PerceivedCostUsd { get; init; }

    /// <summary>
    /// In-flight retargeting: when the unit is at most RetargetAtKm from its target and the target is already destroyed (Target.ImpactsToDestroy),
    /// it picks another non-destroyed target at random from the wave's list, at most RetargetRangeKm from its position. Once per unit.
    /// </summary>
    public bool Retarget { get; init; }
    public double RetargetAtKm { get; init; } = 40.0;
    public double RetargetRangeKm { get; init; } = 150.0;

    public UnitKind Kind { get; init; } = UnitKind.Drone;
    public int Count { get; init; } = 1;

    /// <summary>Id of the catalog type the wave comes from (informational; the values are already resolved into the fields).</summary>
    public string UnitType { get; init; } = "";

    /// <summary>Cost of one unit (USD), for the cost ratio.</summary>
    public double CostUsd { get; init; }

    /// <summary>Operational range of the type (km). 0 = unknown / unlimited; otherwise the scenario cannot send the unit farther.</summary>
    public double RangeKm { get; init; }
    public double StartTimeSeconds { get; init; }
    public double IntervalSeconds { get; init; }
    public double SpeedMps { get; init; } = 50.0;
    public double AltitudeM { get; init; } = 500.0;
    public double SpreadMeters { get; init; }

    /// <summary>Maximum altitude of the ballistic trajectory (m). 0 = automatic: 25% of the distance, between 20 and 300 km. Ignored for other types.</summary>
    public double ApogeeM { get; init; }

    /// <summary>Mass of the explosive payload (kg); determines the lethal radius when estimating casualties.</summary>
    public double WarheadKg { get; init; } = 40.0;
}

public enum DefenseKind
{
    /// <summary>Detects (limited by the radar horizon); does not fire.</summary>
    Radar,
    /// <summary>Surface-to-air missile: fires at drones / aircraft detected by a radar.</summary>
    Sam,
    /// <summary>Anti-aircraft gun: sees its targets within range on its own (no radar), default ceiling 4000 m.</summary>
    AaGun,
    /// <summary>Anti-ballistic defense: fires at ballistic missiles detected by a radar.</summary>
    Abm,
    /// <summary>
    /// Fighter aircraft. Waits on a circle (center = Lat/Lon, radius PatrolRadiusKm, altitude AltM), is sent against detected targets
    /// within ScrambleRangeKm, fires within weapon range (RangeKm), and when it runs out of ammunition or fuel it returns to the nearest
    /// airbase, rearms (TurnaroundSeconds) and comes back. Requires at least one Airbase unit in the scenario.
    /// </summary>
    Patrol,
    /// <summary>Interceptor drone base: launches (Magazine) drones that chase detected targets within RangeKm.</summary>
    InterceptorBase,
    /// <summary>Airbase: position only. Patrol aircraft return here to rearm.</summary>
    Airbase,
    /// <summary>
    /// Electronic warfare (jamming / GPS spoofing): every unit within range (RangeKm) is lost at a rate of JamRatePerMinute
    /// (multiplied by its susceptibility, see Wave.EwResistance). Needs no radar and uses no ammunition. By default it affects drones and cruise missiles.
    /// </summary>
    Ew
}

/// <summary>
/// Defensive system. RangeKm = detection range (radar), engagement range (SAM / gun / ABM / patrol) or launch range (interceptor base).
/// Magazine = missiles / projectiles / interceptors; Pk = probability of a hit per shot;
/// ReloadSeconds = minimum time between two shots / launches of the same unit.
/// </summary>
public sealed record Defense
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public DefenseKind Kind { get; init; } = DefenseKind.Sam;
    public double LatDeg { get; init; }
    public double LonDeg { get; init; }
    public double AltM { get; init; }
    public double RangeKm { get; init; } = 20.0;
    public int Magazine { get; init; }
    public double Pk { get; init; } = 0.7;
    public double ReloadSeconds { get; init; } = 2.0;

    /// <summary>Id of the catalog type the unit comes from (informational; the values are already resolved into the fields).</summary>
    public string TypeId { get; init; } = "";

    /// <summary>
    /// Pk as a function of the target's altitude: points (altitude m, factor 0..1), ascending by altitude, linear in between and constant beyond the ends.
    /// The Pk of every shot is multiplied by the factor at the target's altitude (e.g. a gun group hits low targets best; a long-range SAM struggles against very low ones).
    /// Absent = no effect (the property is left out of the canonical form, so existing scenarios keep their hash).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AltitudePoint>? PkAltitude { get; init; }

    /// <summary>Cost of one shot / launch (USD), for the cost ratio.</summary>
    public double ShotCostUsd { get; init; }

    /// <summary>Acquisition cost of the system (USD, illustrative), used in budget mode. 0 = unknown.</summary>
    public double CostUsd { get; init; }

    /// <summary>Pk per target type (key: the type name, e.g. "ballisticMissile"); types not listed use Pk.</summary>
    public SortedDictionary<string, double>? PkByKind { get; init; }

    /// <summary>Target types it can engage. Empty / missing = default by type (SAM, gun, patrol, interceptors: drones + aircraft; ABM: ballistic missiles).</summary>
    public List<UnitKind>? Engages { get; init; }

    /// <summary>Altitude band of engageable targets (m). MaxAltM = 0: unlimited (for guns: 4000 m).</summary>
    public double MinAltM { get; init; }
    public double MaxAltM { get; init; }

    /// <summary>Patrol: radius of the patrol circle (km, 0 = stationary) and aircraft speed (m/s).</summary>
    public double PatrolRadiusKm { get; init; }
    public double PatrolSpeedMps { get; init; } = 200.0;

    /// <summary>Patrol: how long it flies (s) before returning to base, how long it stays on the ground rearming (s), and how far (km from the center) it will chase a detected target.</summary>
    public double EnduranceSeconds { get; init; } = 3600.0;
    public double TurnaroundSeconds { get; init; } = 600.0;
    public double ScrambleRangeKm { get; init; } = 100.0;

    /// <summary>Interceptor base: speed of the interceptor drones (m/s) and maximum flight time (s).</summary>
    public double InterceptorSpeedMps { get; init; } = 120.0;
    public double MaxFlightSeconds { get; init; } = 600.0;

    /// <summary>Electronic warfare: loss rate per minute (λ) for a fully susceptible unit in range. Probability of loss within one minute = 1 - exp(-λ).</summary>
    public double JamRatePerMinute { get; init; }

    /// <summary>Electronic warfare: efficiency drop with distance. Effective rate = JamRatePerMinute * (1 - JamFalloff * distance / RangeKm); 0 = constant across the range, 1 = zero at the edge of the range.</summary>
    public double JamFalloff { get; init; }

    /// <summary>Electronic warfare: how many units it can jam at once. Above this value, efficiency is shared out (capacity / units in range). 0 = unlimited.</summary>
    public int JamCapacity { get; init; }

    // ---- Radar network (only when Scenario.LocalTracking = true) ----

    /// <summary>Maximum distance (km) to a radar from which the unit receives tracks (data link). Default 150 km.</summary>
    public double LinkRangeKm { get; init; } = 150.0;

    /// <summary>Radius (km) within which the unit sees targets on its own, with its own radar, without the network (Pantsir, Tor, mobile groups). 0 = no own radar.</summary>
    public double SelfTrackKm { get; init; }

    // ---- Vulnerability (the attacker's counterplay): a nearby impact can destroy the system ----

    /// <summary>
    /// How much explosive (kg) it takes, on average, to destroy the system: a strike of W kg destroys it with probability 1 - exp(-W / HardnessKg).
    /// 0 = default by type (radar 40, SAM 80, gun 60, ABM 100, interceptor base 50, airbase 5000); patrol aircraft cannot be destroyed.
    /// </summary>
    public double HardnessKg { get; init; }

    /// <summary>Size of the system (m): an impact with lethal radius R hits if it is at most SizeM + R away. 0 = default (airbase 600, ABM 40, the rest 30).</summary>
    public double SizeM { get; init; }

    // ---- Engagement doctrine (all optional; defaults = the simple behavior: fire at everything it can) ----

    /// <summary>
    /// The system's "primary" targets: always engaged (regardless of cost), may also use the reserved ammunition, and receive
    /// up to MaxRoundsPerTarget shots. The rest are "opportunity targets", subject to the rules below.
    /// Empty / missing = default: ABM = ballistic missiles; the rest = everything that is not a drone (aircraft, cruise missile, ballistic missile).
    /// </summary>
    public List<UnitKind>? PrimaryKinds { get; init; }

    /// <summary>
    /// Maximum accepted cost: a shot of ShotCostUsd is used on an opportunity target only if ShotCostUsd &lt;= MaxCostRatio * the target's cost.
    /// 0 = no limit. (Not applied if ShotCostUsd or the target's cost is unknown, i.e. 0.)
    /// </summary>
    public double MaxCostRatio { get; init; }

    /// <summary>"Leakers": an opportunity target that is too cheap is still engaged if it has at most this many km left to impact. 0 = never.</summary>
    public double LeakerDistanceKm { get; init; }

    /// <summary>The last ReserveRounds shots are kept for primary targets only.</summary>
    public int ReserveRounds { get; init; }

    /// <summary>How many shots (at most) are fired at one primary target: shots are fired in turn, stopping at the first hit (shoot-look-shoot).</summary>
    public int MaxRoundsPerTarget { get; init; } = 1;

    /// <summary>How it picks a target: the one closest to impact, or the most dangerous type first (ballistic &gt; cruise &gt; aircraft &gt; drone), then the one closest to impact.</summary>
    public TargetPriority Priority { get; init; } = TargetPriority.Imminent;
}

/// <summary>A point of an altitude curve: the factor applied at that altitude (m).</summary>
public sealed record AltitudePoint(double AltM, double Factor);

public enum TargetPriority
{
    /// <summary>The target that reaches impact first.</summary>
    Imminent,
    /// <summary>The most dangerous type first; on a tie, the one closest to impact.</summary>
    Threat
}

public sealed record Scenario
{
    public string Name { get; init; } = "";
    public double DtSeconds { get; init; } = 0.1;
    public double DurationSeconds { get; init; } = 3600.0;

    /// <summary>
    /// Instant at which the scenario starts (ISO 8601, e.g. "2026-03-01T22:30:00Z"). Sets the time of day, hence the sun position at each place:
    /// optical defenses and the people exposed to impacts depend on it (see Sim.Core.Sun.DayNight). Absent = no time of day (the model stays neutral;
    /// the property is left out of the canonical form, so existing scenarios keep their hash).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartTimeUtc { get; init; }

    /// <summary>
    /// Weather preset: "clear", "overcast", "rain", "fog" or "storm" (see Sim.Core.Climate.WeatherModel). Optics, radars and small drones are affected differently.
    /// Absent = neutral, same as "clear" (the property is left out of the canonical form, so existing scenarios keep their hash).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Weather { get; init; }

    /// <summary>
    /// Public warning system (sirens / phone alerts triggered by the first detection of a threat): people who have time to reach shelter before an impact
    /// are less likely to die (see CasualtyModel). Absent = on; false = no warning at all. Only the casualty estimate depends on it.
    /// (Left out of the canonical form when absent, so existing scenarios keep their hash.)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PublicWarning { get; init; }

    /// <summary>Name of the country map (file data/countries/&lt;name&gt;.geojson). Empty = no airspace.</summary>
    public string AirspaceMap { get; init; } = "";

    /// <summary>
    /// ISO2 code -> role. SortedDictionary (ordinal order) so the canonical form and the hash do not depend on the order in the file.
    /// Countries not mentioned are Neutral.
    /// </summary>
    public SortedDictionary<string, CountryRole> Countries { get; init; } = new(StringComparer.Ordinal);

    public List<LaunchSite> LaunchSites { get; init; } = new();
    public List<Target> Targets { get; init; } = new();
    public List<Wave> Waves { get; init; } = new();

    public List<Defense> Defenses { get; init; } = new();

    /// <summary>How often (in simulated seconds) the defensive systems detect and fire. Each unit fires at most once per cycle.</summary>
    public double DefenseIntervalSeconds { get; init; } = 1.0;

    /// <summary>
    /// Order in which units fire in a cycle: false = list order; true = cheapest first (cost per shot ascending,
    /// unknown cost last), so cheap layers try before expensive ones.
    /// </summary>
    public bool CheapestFirst { get; init; }

    /// <summary>
    /// false = detection is global and permanent (any radar that has seen a target shows it to everyone, forever).
    /// true = network: a system that needs a track fires only if an INTACT radar within its link range (LinkRangeKm) sees the target right now,
    /// or if the target is within its own radar range (SelfTrackKm). Destroyed radars or radars the target has flown past no longer count.
    /// </summary>
    public bool LocalTracking { get; init; }
}
