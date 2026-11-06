namespace Sim.Core;

public static class EngineInfo
{
    /// <summary>
    /// Bump whenever any logic that affects simulation results changes.
    /// Part of a run's identity: scenario + seed + engine version.
    /// </summary>
    public const string Version = "0.6.0";
}
