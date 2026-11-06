using MessagePack;

namespace Sim.Server.Models;

/// <summary>Absolute paths of the folders for scenarios, saved runs, country maps and the type catalog.</summary>
public sealed record SimPaths(string ScenariosDir, string RunsDir, string CountriesDir, string CatalogDir);

public sealed record StartRequest(string Scenario, ulong? Seed);

/// <summary>The scenario is raw JSON (the scenario object), resolved and validated on the server.</summary>
public sealed record CustomStartRequest(System.Text.Json.JsonElement Scenario, ulong? Seed);

public sealed record SpeedRequest(double Value);

public sealed record ScenarioInfo(string Id, string Name, bool Valid, string? Error, int TotalUnits);

// StatusDto and EventDto also travel over SignalR with MessagePack, where the keys would come out in PascalCase.
// [Key] pins them explicitly to camelCase, identical to the JSON variant (test page) and to the types in web/src/types.ts.

/// <summary>The seed is a string so it does not lose precision in JavaScript (numbers above 2^53).</summary>
[MessagePackObject]
public sealed record StatusDto(
    [property: Key("state")] string State,
    [property: Key("scenarioId")] string ScenarioId,
    [property: Key("scenarioName")] string ScenarioName,
    [property: Key("seed")] string Seed,
    [property: Key("runId")] string RunId,
    [property: Key("epoch")] int Epoch,
    [property: Key("speed")] double Speed,
    [property: Key("simTimeS")] double SimTimeS,
    [property: Key("tick")] long Tick,
    [property: Key("totalDrones")] int TotalDrones,
    [property: Key("flying")] int Flying,
    [property: Key("impacted")] int Impacted,
    [property: Key("violations")] int Violations,
    [property: Key("detected")] int Detected,
    [property: Key("intercepted")] int Intercepted,
    [property: Key("shotsFired")] int ShotsFired,
    [property: Key("magazineLeft")] int MagazineLeft,
    [property: Key("interceptorsLaunched")] int InterceptorsLaunched,
    [property: Key("interceptorsLost")] int InterceptorsLost,
    [property: Key("crashed")] int Crashed,
    [property: Key("jammed")] int Jammed,
    [property: Key("savedRunId")] string SavedRunId);

[MessagePackObject]
public sealed record EventDto(
    [property: Key("tick")] long Tick,
    [property: Key("timeS")] double TimeS,
    [property: Key("type")] string Type,
    [property: Key("id")] int Id,
    [property: Key("latDeg")] double LatDeg,
    [property: Key("lonDeg")] double LonDeg,
    [property: Key("altM")] double AltM,
    [property: Key("code")] string? Code);
