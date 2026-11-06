using Sim.Core.Geo;
using Sim.Core.Scenarios;

namespace Sim.Core.Entities;

public enum EntityStatus : byte
{
    Pending,
    Flying,
    Impacted,
    /// <summary>Shot down by the defense before hitting.</summary>
    Intercepted
}

/// <summary>
/// Flat state of one drone (a struct in an array, not an object): public fields so it can be modified in place.
/// Great-circle flight from Start to Aim at constant ground speed. Altitude: constant (drone, aircraft) or parabolic (ballistic missile).
/// </summary>
public struct Drone
{
    public int Id;
    public int WaveIndex;
    public UnitKind Kind;

    /// <summary>Maximum trajectory altitude (ballistic missiles only), in meters.</summary>
    public double ApogeeM;

    /// <summary>How many interceptors are currently chasing the drone (a base does not launch a second one at the same target).</summary>
    public int ChaseCount;
    public EntityStatus Status;

    /// <summary>Unit cost (USD), from the wave; 0 = unknown. Used by the engagement doctrine.</summary>
    public double CostUsd;

    /// <summary>Explosive payload (kg), from the wave; decides whether an impact destroys a defensive system.</summary>
    public double WarheadKg;

    /// <summary>The unit never reaches the target: Aim is where it falls (along the route), and a Crashed event is emitted on arrival, not Impact.</summary>
    public bool Failed;

    /// <summary>Susceptibility to jamming (1 - EwResistance).</summary>
    public double JamFactor;

    /// <summary>Decoy (statistics only).</summary>
    public bool Decoy;

    /// <summary>Cost as perceived by the defense (USD); 0 = the real one.</summary>
    public double PerceivedCostUsd;

    /// <summary>Index of the current target in Scenario.Targets.</summary>
    public int TargetIndex;

    /// <summary>May be redirected (a single check, at RetargetAtM from the target).</summary>
    public bool CanRetarget;
    public bool RetargetChecked;
    public double RetargetAtM;

    public double LaunchTimeS;
    public double SpeedMps;
    public double CruiseAltM;

    public GeoPoint Start;
    public GeoPoint Aim;
    public double BearingDeg;
    public double TotalDistM;

    /// <summary>Current position (updated on every Step while it flies).</summary>
    public GeoPoint Position;

    /// <summary>Index of the country it is in (in the airspace map), -1 = no country / no map.</summary>
    public int CountryIndex;

    /// <summary>A radar has detected the drone (it stays tracked until the end).</summary>
    public bool Detected;
}
