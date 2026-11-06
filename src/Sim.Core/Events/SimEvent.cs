using Sim.Core.Geo;

namespace Sim.Core.Events;

/// <summary>The numeric values go into the hash: do not renumber, only append at the end.</summary>
public enum SimEventType
{
    Launched = 1,
    Impact = 2,
    AirspaceViolation = 3,
    /// <summary>Radar Code detected the drone.</summary>
    Detected = 4,
    /// <summary>The drone was shot down by system Code.</summary>
    Intercepted = 5,
    /// <summary>System Code fired and missed.</summary>
    Missed = 6,
    /// <summary>Base Code launched an interceptor (EntityId = the interceptor id).</summary>
    InterceptorLaunched = 7,
    /// <summary>The interceptor lost its target or ran out of flight time.</summary>
    InterceptorLost = 8,
    /// <summary>Patrol Code is returning to base (out of ammunition or fuel).</summary>
    PatrolReturning = 9,
    /// <summary>Patrol Code was rearmed and is back on station.</summary>
    PatrolRearmed = 10,
    /// <summary>An impact by drone EntityId destroyed defensive system Code.</summary>
    DefenseDestroyed = 11,
    /// <summary>Unit EntityId fell along its route (malfunction) without reaching the target.</summary>
    Crashed = 12,
    /// <summary>Electronic warfare system Code made unit EntityId lose guidance (it falls on the spot).</summary>
    Jammed = 13,
    /// <summary>Unit EntityId changed its target in flight (the original target was already destroyed); Code = the id of the new target.</summary>
    Retargeted = 14
}

/// <summary>Code: the ISO country code for AirspaceViolation, the defensive system id for Detected/Intercepted/Missed, otherwise null. Part of the hash.</summary>
public readonly record struct SimEvent(long Tick, SimEventType Type, int EntityId, GeoPoint Position, string? Code = null);

public static class SimEventTypeExtensions
{
    /// <summary>Name in files and on the wire: camelCase ("launched", "impact", "airspaceViolation").</summary>
    public static string WireName(this SimEventType type)
    {
        string name = type.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
