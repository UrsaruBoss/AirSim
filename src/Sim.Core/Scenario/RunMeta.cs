using System;

namespace Sim.Core.Scenarios;

/// <summary>
/// Identitatea unei rulari: scenariu (prin hash) + seed + versiune de motor.
/// CreatedUtc e doar informativ si nu intra niciodata in logica de simulare sau in hash.
/// </summary>
public sealed record RunMeta
{
    public string RunId { get; init; } = "";
    public ulong Seed { get; init; }
    public string EngineVersion { get; init; } = "";
    public string ScenarioHash { get; init; } = "";

    /// <summary>Hash-ul log-ului de evenimente. Gol pana se termina rularea.</summary>
    public string EventLogHash { get; init; } = "";

    public DateTimeOffset CreatedUtc { get; init; }

    public static RunMeta Create(Scenario scenario, ulong seed)
    {
        string hash = ScenarioLoader.Hash(scenario);
        return new RunMeta
        {
            RunId = $"{hash[..8]}-{seed}",
            Seed = seed,
            EngineVersion = EngineInfo.Version,
            ScenarioHash = hash,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }
}
