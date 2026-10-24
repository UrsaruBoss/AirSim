using Sim.Core.Geo;

namespace Sim.Core.Events;

/// <summary>Valorile numerice intra in hash: nu le renumerota, adauga doar la sfarsit.</summary>
public enum SimEventType
{
    Launched = 1,
    Impact = 2,
    AirspaceViolation = 3,
    /// <summary>Radarul Code a detectat drona.</summary>
    Detected = 4,
    /// <summary>Drona a fost doborata de sistemul Code.</summary>
    Intercepted = 5,
    /// <summary>Sistemul Code a tras si a ratat.</summary>
    Missed = 6,
    /// <summary>Baza Code a lansat un interceptor (EntityId = id-ul interceptorului).</summary>
    InterceptorLaunched = 7,
    /// <summary>Interceptorul si-a pierdut tinta sau i s-a terminat zborul.</summary>
    InterceptorLost = 8,
    /// <summary>Patrula Code se intoarce la baza (fara munitie sau combustibil).</summary>
    PatrolReturning = 9,
    /// <summary>Patrula Code a fost reincarcata si revine in asteptare.</summary>
    PatrolRearmed = 10,
    /// <summary>Un impact al dronei EntityId a distrus sistemul defensiv Code.</summary>
    DefenseDestroyed = 11,
    /// <summary>Unitatea EntityId a cazut pe traseu (defectiune), fara sa ajunga la tinta.</summary>
    Crashed = 12,
    /// <summary>Sistemul de razboi electronic Code a facut unitatea EntityId sa-si piarda ghidarea (cade pe loc).</summary>
    Jammed = 13,
    /// <summary>Unitatea EntityId si-a schimbat tinta in zbor (tinta initiala era deja distrusa); Code = id-ul noii tinte.</summary>
    Retargeted = 14
}

/// <summary>Code: codul ISO al tarii la AirspaceViolation, id-ul sistemului defensiv la Detected/Intercepted/Missed, altfel null. Intra in hash.</summary>
public readonly record struct SimEvent(long Tick, SimEventType Type, int EntityId, GeoPoint Position, string? Code = null);

public static class SimEventTypeExtensions
{
    /// <summary>Numele din fisiere si de pe retea: camelCase ("launched", "impact", "airspaceViolation").</summary>
    public static string WireName(this SimEventType type)
    {
        string name = type.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
