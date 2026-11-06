using System;

namespace Sim.Core.Scenarios;

/// <summary>
/// The identity of a run: scenario (by hash) + seed + engine version.
/// CreatedUtc is informational only and never enters the simulation logic or the hash.
/// </summary>
public sealed record RunMeta
{
    public string RunId { get; init; } = "";
    public ulong Seed { get; init; }
    public string EngineVersion { get; init; } = "";
    public string ScenarioHash { get; init; } = "";

    /// <summary>Hash of the event log. Empty until the run finishes.</summary>
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
