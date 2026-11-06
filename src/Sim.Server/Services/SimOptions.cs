namespace Sim.Server.Services;

/// <summary>
/// The server mode. Public (Sim:Mode=public, e.g. environment variable Sim__Mode=public): no live simulation, no custom
/// scenarios, no disk writes; serves only the precomputed runs. Local (default): everything, as before.
/// </summary>
public sealed record SimOptions(bool IsPublic, string PrecomputedDir);
