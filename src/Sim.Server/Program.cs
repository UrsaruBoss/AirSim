using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sim.Core.Scenarios;
using Sim.Server.Hubs;
using Sim.Server.Models;
using Sim.Server.Services;

// mod linie de comanda: dotnet run --project src/Sim.Server -- batch scenario=<id> runs=30
if (args.Length > 0 && args[0].Equals("batch", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = BatchCli.Run(args[1..]);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// enum-urile (ex. UnitKind) ies ca string camelCase, la fel ca in fisierele de scenariu
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// JSON (pagina de test) si MessagePack (frontend-ul real) functioneaza in paralel; clientul alege
builder.Services.AddSignalR().AddMessagePackProtocol();

string[] origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                   ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// Implicit: folderele "scenarios" si "runs" din radacina solutiei (gasita urcand dupa *.sln / *.slnx).
// Se pot suprascrie cu Sim:ScenariosDir / Sim:RunsDir (cale relativa la folderul proiectului sau absoluta).
string? solutionRoot = FindUp(builder.Environment.ContentRootPath) ?? FindUp(AppContext.BaseDirectory);

string ResolveDir(string key, string folder)
{
    string? configured = builder.Configuration[key];
    if (!string.IsNullOrWhiteSpace(configured))
        return Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configured));
    return Path.Combine(solutionRoot ?? builder.Environment.ContentRootPath, folder);
}

builder.Services.AddSingleton(new SimPaths(
    ResolveDir("Sim:ScenariosDir", "scenarios"),
    ResolveDir("Sim:RunsDir", "runs"),
    ResolveDir("Sim:CountriesDir", "data/countries"),
    ResolveDir("Sim:CatalogDir", "data/catalog")));
builder.Services.AddSingleton<AirspaceCatalog>();
builder.Services.AddSingleton<CatalogProvider>();
builder.Services.AddSingleton<CompareService>();

builder.Services.AddSingleton<SimulationHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationHost>());

var app = builder.Build();

app.UseCors("frontend");
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<SimHub>("/hubs/sim");

var api = app.MapGroup("/api");

api.MapGet("/scenarios", (SimPaths paths, CatalogProvider catalog) => ScenarioCatalog.List(paths.ScenariosDir, catalog.Get));

// catalogul de tipuri (pentru selectorul din UI): { offense: [...], defense: [...] }
api.MapGet("/catalog", (CatalogProvider catalog) => Run(() =>
{
    var c = catalog.Get();
    return new { offense = c.OffenseTypes, defense = c.DefenseTypes };
}));

api.MapGet("/scenarios/{id}", (string id, SimPaths paths, CatalogProvider catalog) =>
{
    if (!ScenarioCatalog.IsSafeId(id))
        return Results.BadRequest(new { error = "Id de scenariu invalid." });

    string file = ScenarioCatalog.PathFor(paths.ScenariosDir, id);
    if (!File.Exists(file))
        return Results.NotFound();

    try
    {
        return Results.Ok(ScenarioLoader.FromFile(file, catalog.Get()));
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// aliante (NATO, UE...) pentru selectia rapida a rolurilor din editor
api.MapGet("/alliances", (SimPaths paths) =>
{
    string file = Path.GetFullPath(Path.Combine(paths.CatalogDir, "..", "alliances.json"));
    return File.Exists(file) ? Results.Text(File.ReadAllText(file), "application/json", System.Text.Encoding.UTF8) : Results.NotFound();
});

// GeoJSON-ul hartii de tari, pentru randare in Cesium
api.MapGet("/airspace/{map}", (string map, AirspaceCatalog airspace) =>
{
    string path;
    try
    {
        path = airspace.PathFor(map);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    return File.Exists(path)
        ? Results.Text(File.ReadAllText(path), "application/geo+json", System.Text.Encoding.UTF8)
        : Results.NotFound();
});

api.MapGet("/sim/status", (SimulationHost host) => host.GetStatus());
api.MapGet("/sim/summary", (SimulationHost host) => Run(() => host.GetSummary()));
api.MapPost("/sim/start", (StartRequest req, SimulationHost host) => Run(() => host.Start(req.Scenario, req.Seed)));
api.MapPost("/sim/start-custom", (CustomStartRequest req, SimulationHost host) => Run(() => host.StartCustom(req.Scenario.GetRawText(), req.Seed)));
api.MapGet("/runs", (SimPaths paths) => Run(() => RunLibrary.List(paths.RunsDir)));
api.MapGet("/runs/{id}/scenario", (string id, SimPaths paths) =>
{
    try { return Results.Text(RunLibrary.Load(paths.RunsDir, id).ScenarioJson, "application/json"); }
    catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidDataException)
    { return Results.BadRequest(new { error = ex.Message }); }
});
api.MapPost("/runs/{id}/replay", (string id, SimulationHost host) => Run(() => host.StartReplay(id)));
api.MapGet("/sim/replay-check", (SimulationHost host) => host.ReplayCheck());
api.MapPost("/compare", (CompareRequest req, CompareService svc) => Run(() => svc.Start(req)));
api.MapGet("/compare/{id}", (string id, CompareService svc) => Run(() => svc.Get(id)));
api.MapPost("/sim/pause", (SimulationHost host) => Run(() => host.Pause()));
api.MapPost("/sim/resume", (SimulationHost host) => Run(() => host.Resume()));
api.MapPost("/sim/stop", (SimulationHost host) => Run(() => host.Stop()));
api.MapPost("/sim/speed", (SpeedRequest req, SimulationHost host) => Run(() => host.SetSpeed(req.Value)));
api.MapPost("/sim/save", (SimulationHost host) => Run(() => new { runId = host.SaveRun() }));

app.Run();

static string? FindUp(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (dir.EnumerateFiles("*.sln").Any() || dir.EnumerateFiles("*.slnx").Any())
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

static IResult Run(Func<object> action)
{
    try
    {
        return Results.Ok(action());
    }
    catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or ArgumentException
                                    or InvalidOperationException or NotSupportedException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}
