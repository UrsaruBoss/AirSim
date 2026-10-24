namespace Sim.Core.Airspace;

/// <summary>
/// Rolul unei tari in scenariu. In JSON apare camelCase: "neutral", "defender", "defenderAlly", "attacker", "attackerAlly".
/// Tarile nementionate in scenariu sunt tratate ca Neutral.
/// </summary>
public enum CountryRole
{
    Neutral,
    Defender,
    DefenderAlly,
    Attacker,
    AttackerAlly
}

/// <summary>Regulile de survol. Pastrate intr-un singur loc ca sa se poata schimba/parametriza usor.</summary>
public static class AirspacePolicy
{
    /// <summary>
    /// Pentru unitatile ofensive: intrarea in spatiul aerian al unei tari cu acest rol e o incalcare?
    /// Libere: tara atacatoare, aliatii ei si tara aparatoare (zona de razboi).
    /// Incalcari: tari neutre si aliatii aparatorului (nu sunt in razboi, dar sunt de partea cealalta).
    /// </summary>
    public static bool IsViolationForAttackers(CountryRole role) =>
        role is CountryRole.Neutral or CountryRole.DefenderAlly;
}
