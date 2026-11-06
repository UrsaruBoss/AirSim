using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Sim.Core.Scenarios;
using Sim.Core.Sun;
using Sim.Server.Hubs;
using Sim.Server.Models;
using Sim.Server.Services;

// command-line mode: dotnet run --project src/Sim.Server -- batch scenario=<id> runs=30
if (args.Length > 0 && args[0].Equals("batch", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = BatchCli.Run(args[1..]);
    return;
}

// generates the precomputed runs for public mode: dotnet run -c Release --project src/Sim.Server -- precompute
if (args.Length > 0 && args[0].Equals("precompute", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = PrecomputeCli.Run(args[1..]);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Sim:Mode=public (environment variable Sim__Mode=public): no live simulation, no custom scenarios, no disk writes.
// The simulation routes do not even exist in this mode (not merely hidden), so they cannot be called directly.
bool isPublic = string.Equals(builder.Configuration["Sim:Mode"], "public", StringComparison.OrdinalIgnoreCase);

// enums (e.g. UnitKind) are written as camelCase strings, like in the scenario files
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// JSON (test page) and MessagePack (the real frontend) work side by side; the client chooses
if (!isPublic)
    builder.Services.AddSignalR().AddMessagePackProtocol();

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/octet-stream", "application/geo+json" });
});

if (isPublic)
{
    // per-IP limit (behind a reverse proxy the real IP comes from X-Forwarded-For)
    builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = 429;
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "anon", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    });
}

string[] origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                   ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// Default: the "scenarios" and "runs" folders in the solution root (found by walking up to a *.sln / *.slnx).
// Can be overridden with Sim:ScenariosDir / Sim:RunsDir (path relative to the project folder, or absolute).
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
builder.Services.AddSingleton(new SimOptions(isPublic, ResolveDir("Sim:PrecomputedDir", "precomputed")));
builder.Services.AddSingleton<AirspaceCatalog>();
builder.Services.AddSingleton<CatalogProvider>();
builder.Services.AddSingleton<PrecomputedStore>();

if (!isPublic)
{
    builder.Services.AddSingleton<CompareService>();
    builder.Services.AddSingleton<SimulationHost>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationHost>());
}

var app = builder.Build();
_ = app.Services.GetRequiredService<PrecomputedStore>(); // valideaza rularile precalculate la pornire, nu la prima cerere

if (isPublic)
{
    app.UseForwardedHeaders();
    app.UseRateLimiter();
}
app.UseResponseCompression();
app.UseCors("frontend");
app.UseDefaultFiles();
app.UseStaticFiles();

if (!isPublic)
    app.MapHub<SimHub>("/hubs/sim");

var api = app.MapGroup("/api");

api.MapGet("/mode", () => new { mode = isPublic ? "public" : "local" });

// in public mode only scenarios with a valid precomputed run are listed
api.MapGet("/scenarios", (SimPaths paths, CatalogProvider catalog, PrecomputedStore store) =>
    ScenarioCatalog.List(paths.ScenariosDir, catalog.Get).Where(i => !isPublic || store.HasAny(i.Id)));

api.MapGet("/precomputed", (PrecomputedStore store) => store.List());
api.MapGet("/precomputed/{id}/data", (string id, string? v, HttpContext http, PrecomputedStore store) => Precomputed(id, v, http, store, data: true));
api.MapGet("/precomputed/{id}/bin", (string id, string? v, HttpContext http, PrecomputedStore store) => Precomputed(id, v, http, store, data: false));

// the type catalog (for the UI selector): { offense: [...], defense: [...] }
api.MapGet("/catalog", (CatalogProvider catalog) => Run(() =>
{
    var c = catalog.Get();
    return new { offense = c.OffenseTypes, defense = c.DefenseTypes };
}));

api.MapGet("/scenarios/{id}", (string id, SimPaths paths, CatalogProvider catalog, PrecomputedStore store) =>
{
    if (!ScenarioCatalog.IsSafeId(id))
        return Results.BadRequest(new { error = "Invalid scenario id." });
    if (isPublic && !store.Has(id))
        return Results.NotFound();

    // "id.night" = the scenario "id" started at night (precomputed runs in public mode)
    var (baseId, variant) = DayNight.SplitVariantId(id);
    string file = ScenarioCatalog.PathFor(paths.ScenariosDir, File.Exists(ScenarioCatalog.PathFor(paths.ScenariosDir, id)) ? id : baseId);
    if (!File.Exists(file))
        return Results.NotFound();

    try
    {
        var loaded = ScenarioLoader.FromFile(file, catalog.Get());
        bool isVariant = variant.Length > 0 && !File.Exists(ScenarioCatalog.PathFor(paths.ScenariosDir, id));
        return Results.Ok(isVariant ? DayNight.WithVariant(loaded, variant) : loaded);
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// alliances (NATO, EU...) for quick role selection in the editor
api.MapGet("/alliances", (SimPaths paths) =>
{
    string file = Path.GetFullPath(Path.Combine(paths.CatalogDir, "..", "alliances.json"));
    return File.Exists(file) ? Results.Text(File.ReadAllText(file), "application/json", System.Text.Encoding.UTF8) : Results.NotFound();
});

// GeoJSON of the country map, for rendering in Cesium
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

// live simulation, custom scenarios, comparisons and saved runs exist only in local mode
if (!isPublic)
{
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
}

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

static IResult Precomputed(string id, string? v, HttpContext http, PrecomputedStore store, bool data)
{
    var entry = ScenarioCatalog.IsSafeId(id) ? store.Get(id) : null;
    if (entry is null)
        return Results.NotFound();

    // the ?v= key changes the URL when the run is regenerated, so the file can be cached "forever"
    http.Response.Headers.CacheControl = v == entry.Key ? "public, max-age=31536000, immutable" : "no-cache";
    return data
        ? Results.File(entry.DataPath, "application/json")
        : Results.File(entry.BinPath, "application/octet-stream");
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
