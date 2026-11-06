namespace Sim.Core.Airspace;

/// <summary>
/// The role of a country in a scenario. In JSON it is camelCase: "neutral", "defender", "defenderAlly", "attacker", "attackerAlly".
/// Countries not mentioned in the scenario are treated as Neutral.
/// </summary>
public enum CountryRole
{
    Neutral,
    Defender,
    DefenderAlly,
    Attacker,
    AttackerAlly
}

/// <summary>Overflight rules. Kept in one place so they are easy to change or parameterize.</summary>
public static class AirspacePolicy
{
    /// <summary>
    /// For offensive units: is entering the airspace of a country with this role a violation?
    /// Free: the attacking country, its allies and the defending country (the war zone).
    /// Violations: neutral countries and the defender's allies (not at war, but on the other side).
    /// </summary>
    public static bool IsViolationForAttackers(CountryRole role) =>
        role is CountryRole.Neutral or CountryRole.DefenderAlly;
}
