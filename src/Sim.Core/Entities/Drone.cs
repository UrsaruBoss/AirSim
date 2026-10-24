using Sim.Core.Geo;
using Sim.Core.Scenarios;

namespace Sim.Core.Entities;

public enum EntityStatus : byte
{
    Pending,
    Flying,
    Impacted,
    /// <summary>Doborata de aparare, inainte sa loveasca.</summary>
    Intercepted
}

/// <summary>
/// Stare plata a unei drone (struct intr-un array, nu obiect): campuri publice ca sa se poata modifica pe loc.
/// Zbor pe cerc mare de la Start la Aim, viteza constanta la sol. Altitudine: constanta (drona, avion) sau parabolica (racheta balistica).
/// </summary>
public struct Drone
{
    public int Id;
    public int WaveIndex;
    public UnitKind Kind;

    /// <summary>Altitudinea maxima a traiectoriei (doar rachete balistice), in metri.</summary>
    public double ApogeeM;

    /// <summary>Cate interceptoare urmaresc drona acum (o baza nu lanseaza al doilea pe aceeasi tinta).</summary>
    public int ChaseCount;
    public EntityStatus Status;

    /// <summary>Costul unitatii (USD), din val; 0 = necunoscut. Folosit de doctrina de angajare.</summary>
    public double CostUsd;

    /// <summary>Sarcina explozivă (kg), din val; determina daca un impact distruge un sistem defensiv.</summary>
    public double WarheadKg;

    /// <summary>Unitatea nu ajunge la tinta: Aim e punctul unde cade (pe traseu), iar la sosire se emite Crashed, nu Impact.</summary>
    public bool Failed;

    /// <summary>Sensibilitatea la bruiaj (1 - EwResistance).</summary>
    public double JamFactor;

    /// <summary>Momeala (doar statistici).</summary>
    public bool Decoy;

    /// <summary>Costul perceput de aparare (USD); 0 = cel real.</summary>
    public double PerceivedCostUsd;

    /// <summary>Indexul tintei curente in Scenario.Targets.</summary>
    public int TargetIndex;

    /// <summary>Poate fi redirectionata (o singura verificare, la RetargetAtM de tinta).</summary>
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

    /// <summary>Pozitia curenta (actualizata la fiecare Step cat timp zboara).</summary>
    public GeoPoint Position;

    /// <summary>Indexul tarii in care se afla (in harta de spatiu aerian), -1 = nicio tara / fara harta.</summary>
    public int CountryIndex;

    /// <summary>Un radar a detectat drona (ramane urmarita pana la final).</summary>
    public bool Detected;
}
