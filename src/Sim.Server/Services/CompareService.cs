using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core.Analytics;
using Sim.Core.Scenarios;
using Sim.Server.Models;

namespace Sim.Server.Services;

/// <param name="NoRetarget">Dezactiveaza redirectionarea in zbor in toate valurile (pentru a testa efectul ei).</param>
public sealed record CompareSide(string? Id, JsonElement? Scenario, bool NoRetarget = false);

public sealed record CompareRequest(CompareSide A, CompareSide B, int Runs, ulong? Seed);

public sealed record CompareJobDto(
    string Id, string State, int Done, int Total, string Error,
    string NameA, string NameB, int Runs, string Seed, string EngineVersion,
    IReadOnlyList<ComparisonRow>? Rows, IReadOnlyList<MetricStat>? MetricsA, IReadOnlyList<MetricStat>? MetricsB);

/// <summary>
/// Ruleaza in fundal doua loturi (A si B) cu aceleasi seed-uri si le compara pe perechi.
/// Un singur job la un moment dat, ca sa nu sufoce simularea live.
/// </summary>
public sealed class CompareService
{
    public const int MaxRuns = 100;
    private const int KeepJobs = 8;

    private sealed class Job
    {
        public string Id = "";
        public string State = "running";
        public int Done;
        public int Total;
        public string Error = "";
        public string NameA = "", NameB = "";
        public int Runs;
        public ulong Seed;
        public IReadOnlyList<ComparisonRow>? Rows;
        public BatchReport? A, B;
    }

    private readonly SimPaths _paths;
    private readonly CatalogProvider _catalog;
    private readonly AirspaceCatalog _airspace;
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly ConcurrentQueue<string> _order = new();
    private readonly object _gate = new();
    private string _activeId = "";

    public CompareService(SimPaths paths, CatalogProvider catalog, AirspaceCatalog airspace)
    {
        _paths = paths;
        _catalog = catalog;
        _airspace = airspace;
    }

    private Scenario Resolve(CompareSide side)
    {
        var scenario = ResolveBase(side);
        if (side.NoRetarget)
            scenario = scenario with { Name = scenario.Name + " (fara redirectionare)", Waves = scenario.Waves.Select(w => w with { Retarget = false }).ToList() };
        return scenario;
    }

    private Scenario ResolveBase(CompareSide side)
    {
        if (side.Scenario is { } json && json.ValueKind == JsonValueKind.Object)
            return ScenarioLoader.FromJson(json.GetRawText(), _catalog.Get());
        if (string.IsNullOrWhiteSpace(side.Id))
            throw new ArgumentException("Fiecare parte a comparatiei cere un scenariu (id sau JSON).");
        string path = Path.Combine(_paths.ScenariosDir, side.Id + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Scenariul '{side.Id}' nu exista.");
        return ScenarioLoader.FromFile(path, _catalog.Get());
    }

    public CompareJobDto Start(CompareRequest req)
    {
        if (req.Runs < 1 || req.Runs > MaxRuns)
            throw new ArgumentException($"Numarul de rulari trebuie sa fie intre 1 si {MaxRuns}.");

        var a = Resolve(req.A);
        var b = Resolve(req.B);
        var errA = ScenarioLoader.Validate(a);
        var errB = ScenarioLoader.Validate(b);
        if (errA.Count > 0) throw new ArgumentException("Scenariul A invalid: " + string.Join("; ", errA));
        if (errB.Count > 0) throw new ArgumentException("Scenariul B invalid: " + string.Join("; ", errB));

        ulong seed = req.Seed ?? (ulong)Random.Shared.NextInt64(1L << 40);
        var job = new Job { Id = Guid.NewGuid().ToString("N")[..10], Total = req.Runs * 2, NameA = a.Name, NameB = b.Name, Runs = req.Runs, Seed = seed };

        lock (_gate)
        {
            if (_activeId != "" && _jobs.TryGetValue(_activeId, out var running) && running.State == "running")
                throw new InvalidOperationException("O comparatie ruleaza deja; asteapta sa se termine.");
            _activeId = job.Id;
            _jobs[job.Id] = job;
            _order.Enqueue(job.Id);
            while (_order.Count > KeepJobs && _order.TryDequeue(out var old))
                _jobs.TryRemove(old, out _);
        }

        // jumatate din fire, ca simularea live sa ramana fluida
        int threads = Math.Max(1, Environment.ProcessorCount / 2);
        Func<Sim.Core.Airspace.AirspaceModel?>? air(Scenario s)
        {
            if (string.IsNullOrEmpty(s.AirspaceMap))
                return null;
            return () => _airspace.Create(s.AirspaceMap, s.Countries);
        }

        _ = Task.Run(() =>
        {
            try
            {
                int offset = 0;
                job.A = BatchRunner.Run(a, seed, req.Runs, air(a), threads, d => job.Done = offset + d);
                offset = req.Runs;
                job.Done = offset;
                job.B = BatchRunner.Run(b, seed, req.Runs, air(b), threads, d => job.Done = offset + d);
                job.Rows = BatchCompare.Compare(job.A, job.B);
                job.Done = job.Total;
                job.State = "done";
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                job.State = "error";
            }
        });

        return ToDto(job);
    }

    public CompareJobDto Get(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
            throw new FileNotFoundException("Comparatia nu exista (poate a expirat).");
        return ToDto(job);
    }

    private static CompareJobDto ToDto(Job j) => new(
        j.Id, j.State, j.Done, j.Total, j.Error, j.NameA, j.NameB, j.Runs, j.Seed.ToString(),
        Sim.Core.EngineInfo.Version, j.Rows, j.A?.Metrics, j.B?.Metrics);
}
