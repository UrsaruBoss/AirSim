using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sim.Core;
using Sim.Core.Airspace;
using Sim.Core.Analytics;
using Sim.Core.Entities;
using Sim.Core.Events;
using Sim.Core.Runs;
using Sim.Core.Scenarios;
using Sim.Server.Hubs;
using Sim.Server.Models;

namespace Sim.Server.Services;

public enum SimRunState
{
    Idle,
    Running,
    Paused,
    Finished
}

/// <summary>
/// Tine simularea curenta si o avanseaza in timp real x viteza.
/// Numarul de pasi pe iteratie nu influenteaza rezultatul: fiecare Step() e determinist,
/// deci pauza, viteza si ritmul de publicare nu schimba log-ul de evenimente.
/// </summary>
public sealed class SimulationHost : BackgroundService
{
    private const int TickIntervalMs = 20;
    private const int PublishIntervalMs = 100;
    private const int MaxStepsPerIteration = 20000;
    private const double MaxIterationMs = 40.0;
    private const double MinSpeed = 0.1;
    private const double MaxSpeed = 1000.0;

    private sealed record Publication(byte[]? Snapshot, EventDto[] Events, StatusDto Status);
    private sealed record FinishedRun(Scenario Scenario, RunMeta Meta, Simulation Sim, int Epoch);

    private readonly object _gate = new();
    private string _replayRunId = "";
    private string _replayExpectedHash = "";
    private readonly IHubContext<SimHub> _hub;
    private readonly SimPaths _paths;
    private readonly AirspaceCatalog _airspace;
    private readonly CatalogProvider _catalog;
    private readonly ILogger<SimulationHost> _log;

    private Simulation? _sim;
    private RunMeta? _meta;
    private SimRunState _state = SimRunState.Idle;
    private string _scenarioId = "";
    private int _epoch;
    private double _speed = 1.0;
    private double _budget;
    private int _eventsSent;
    private long _lastPublishedTick = -1;
    private bool _publishNow;
    private string _savedRunId = "";

    public SimulationHost(IHubContext<SimHub> hub, SimPaths paths, AirspaceCatalog airspace, CatalogProvider catalog, ILogger<SimulationHost> log)
    {
        _hub = hub;
        _paths = paths;
        _airspace = airspace;
        _catalog = catalog;
        _log = log;
    }

    // ---------- comenzi (apelate din API) ----------

    public StatusDto Start(string scenarioId, ulong? seed)
    {
        if (!ScenarioCatalog.IsSafeId(scenarioId))
            throw new ArgumentException("Id de scenariu invalid.");

        string path = ScenarioCatalog.PathFor(_paths.ScenariosDir, scenarioId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Scenariul '{scenarioId}' nu exista.");

        return StartScenario(ScenarioLoader.FromFile(path, _catalog.Get()), scenarioId, seed);
    }

    /// <summary>Porneste un scenariu construit in editor (JSON cu tipuri din catalog, rezolvat si validat ca orice scenariu).</summary>
    public StatusDto StartCustom(string scenarioJson, ulong? seed) =>
        StartScenario(ScenarioLoader.FromJson(scenarioJson, _catalog.Get()), "custom", seed);

    /// <summary>
    /// Replay: porneste din nou scenariul si seed-ul unei rulari salvate. Motorul e determinist, deci desfasurarea e identica
    /// (daca versiunea motorului e aceeasi); la final, <see cref="ReplayCheck"/> compara hash-ul log-ului cu cel salvat.
    /// </summary>
    public object StartReplay(string runId)
    {
        var (json, meta) = RunLibrary.Load(_paths.RunsDir, runId);
        var scenario = ScenarioLoader.FromJson(json, _catalog.Get());
        var status = StartScenario(scenario, "replay:" + runId, meta.Seed);
        bool scenarioMatches = ScenarioLoader.Hash(scenario) == meta.ScenarioHash;
        bool engineMatches = meta.EngineVersion == EngineInfo.Version;
        lock (_gate)
        {
            _replayRunId = runId;
            _replayExpectedHash = meta.EventLogHash;
        }
        return new { status, scenarioMatches, engineMatches, savedEngineVersion = meta.EngineVersion };
    }

    public ReplayCheckDto ReplayCheck()
    {
        lock (_gate)
        {
            if (_replayRunId == "" || _sim is null)
                return new ReplayCheckDto(false, "", false, false, "", "");
            bool finished = _state == SimRunState.Finished;
            string actual = finished ? _sim.Events.Hash() : "";
            return new ReplayCheckDto(true, _replayRunId, finished, finished && actual == _replayExpectedHash, _replayExpectedHash, actual);
        }
    }

    private StatusDto StartScenario(Scenario scenario, string scenarioId, ulong? seed)
    {
        ulong actualSeed = seed ?? (ulong)Random.Shared.NextInt64(1L << 53);
        AirspaceModel? airspace = string.IsNullOrEmpty(scenario.AirspaceMap)
            ? null
            : _airspace.Create(scenario.AirspaceMap, scenario.Countries);
        var sim = new Simulation(scenario, actualSeed, airspace); // poate arunca (scenariu invalid / harta lipsa / tip neimplementat)

        lock (_gate)
        {
            _sim = sim;
            _meta = RunMeta.Create(scenario, actualSeed);
            _scenarioId = scenarioId;
            _epoch++;
            _state = SimRunState.Running;
            _budget = 0;
            _eventsSent = 0;
            _lastPublishedTick = -1;
            _savedRunId = "";
            _replayRunId = "";
            _replayExpectedHash = "";
            _publishNow = true;
            return BuildStatusLocked(CountFlyingLocked());
        }
    }

    public StatusDto Pause()
    {
        lock (_gate)
        {
            if (_state != SimRunState.Running)
                throw new InvalidOperationException("Simularea nu ruleaza.");
            _state = SimRunState.Paused;
            _publishNow = true;
            return BuildStatusLocked(CountFlyingLocked());
        }
    }

    public StatusDto Resume()
    {
        lock (_gate)
        {
            if (_state != SimRunState.Paused)
                throw new InvalidOperationException("Simularea nu e in pauza.");
            _state = SimRunState.Running;
            _publishNow = true;
            return BuildStatusLocked(CountFlyingLocked());
        }
    }

    public StatusDto Stop()
    {
        lock (_gate)
        {
            _sim = null;
            _meta = null;
            _state = SimRunState.Idle;
            _publishNow = true;
            return BuildStatusLocked(0);
        }
    }

    public StatusDto SetSpeed(double speed)
    {
        if (!(speed >= MinSpeed && speed <= MaxSpeed))
            throw new ArgumentException($"Viteza trebuie sa fie intre {MinSpeed} si {MaxSpeed}.");

        lock (_gate)
        {
            _speed = speed;
            _publishNow = true;
            return BuildStatusLocked(CountFlyingLocked());
        }
    }

    /// <summary>Salveaza rularea curenta in runs/. Doar in pauza sau dupa final (log-ul nu se modifica atunci).</summary>
    public string SaveRun()
    {
        lock (_gate)
        {
            if (_sim is null || _meta is null)
                throw new InvalidOperationException("Nu exista nicio rulare.");
            if (_state == SimRunState.Running)
                throw new InvalidOperationException("Pune simularea in pauza sau asteapta sa se termine.");

            RunStore.Save(_paths.RunsDir, _sim.Scenario, _meta, _sim.Events);
            _savedRunId = _meta.RunId;
            _publishNow = true;
            return _meta.RunId;
        }
    }

    public StatusDto GetStatus()
    {
        lock (_gate)
            return BuildStatusLocked(CountFlyingLocked());
    }

    /// <summary>Statistici ale rularii curente (partiale cat timp ruleaza, finale dupa terminare).</summary>
    public RunSummary GetSummary()
    {
        lock (_gate)
        {
            if (_sim is null)
                throw new InvalidOperationException("Nu exista nicio rulare.");
            return RunAnalyzer.Analyze(_sim.Scenario, _sim.Events.Events, _sim.Seed);
        }
    }

    public void RequestPublish()
    {
        lock (_gate)
            _publishNow = true;
    }

    // ---------- bucla de timp real ----------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        long last = Stopwatch.GetTimestamp();
        long lastPublish = last;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                long now = Stopwatch.GetTimestamp();
                double realDt = Stopwatch.GetElapsedTime(last, now).TotalSeconds;
                last = now;
                bool publishDue = Stopwatch.GetElapsedTime(lastPublish, now).TotalMilliseconds >= PublishIntervalMs;

                Publication? publication = null;
                FinishedRun? finished = null;

                lock (_gate)
                {
                    Advance(realDt, out finished);

                    bool progressed = _sim is not null && _sim.Clock.Tick != _lastPublishedTick;
                    if (_publishNow || finished is not null || (publishDue && progressed && _state == SimRunState.Running))
                    {
                        publication = BuildPublicationLocked();
                        _publishNow = false;
                        lastPublish = now;
                    }
                }

                if (publication is not null)
                    await SendAsync(publication, stoppingToken);

                if (finished is not null)
                    SaveFinishedRun(finished);
            }
        }
        catch (OperationCanceledException)
        {
            // oprire normala
        }
    }

    private void Advance(double realDt, out FinishedRun? finished)
    {
        finished = null;
        if (_sim is null || _state != SimRunState.Running)
            return;

        double dt = _sim.Clock.Dt;
        _budget += realDt * _speed;

        // Cat timp se avanseaza simularea, _gate e tinut (pauza, status si publicarea asteapta).
        // Daca un pas e scump (mii de drone), 20000 de pasi ar bloca interfata minute intregi,
        // deci iteratia e limitata si in timp real; ce nu apuca se pierde (simularea ruleaza atunci la viteza maxima posibila).
        long started = Stopwatch.GetTimestamp();
        int steps = 0;
        bool behind = false;
        while (_budget >= dt && !_sim.IsFinished)
        {
            if (steps >= MaxStepsPerIteration || (steps > 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= MaxIterationMs))
            {
                behind = true;
                break;
            }
            _sim.Step();
            _budget -= dt;
            steps++;
        }

        // nu mai tine pasul cu timpul real: renunta la restanta ca sa nu intre intr-o spirala
        if (behind)
            _budget = 0;

        if (_sim.IsFinished)
        {
            _state = SimRunState.Finished;
            _budget = 0;
            finished = new FinishedRun(_sim.Scenario, _meta!, _sim, _epoch);
        }
    }

    private Publication BuildPublicationLocked()
    {
        byte[]? snapshot = null;
        var events = Array.Empty<EventDto>();
        int flying = 0;

        if (_sim is not null)
        {
            flying = CountFlyingLocked();
            snapshot = SnapshotWriter.Write(_sim.Clock.Tick, _sim.Clock.Time, _sim.Drones, flying, _sim.GetFriendlies());
            _lastPublishedTick = _sim.Clock.Tick;

            var log = _sim.Events.Events;
            int count = log.Count - _eventsSent;
            if (count > 0)
            {
                events = new EventDto[count];
                double dt = _sim.Clock.Dt;
                for (int i = 0; i < count; i++)
                {
                    var e = log[_eventsSent + i];
                    events[i] = new EventDto(
                        e.Tick, e.Tick * dt, e.Type.WireName(), e.EntityId,
                        e.Position.LatDeg, e.Position.LonDeg, e.Position.AltM, e.Code);
                }
                _eventsSent = log.Count;
            }
        }

        return new Publication(snapshot, events, BuildStatusLocked(flying));
    }

    private async Task SendAsync(Publication p, CancellationToken ct)
    {
        try
        {
            if (p.Snapshot is not null)
                await _hub.Clients.All.SendAsync("snapshot", p.Snapshot, ct);
            if (p.Events.Length > 0)
                await _hub.Clients.All.SendAsync("events", p.Events, ct);
            await _hub.Clients.All.SendAsync("status", p.Status, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Trimiterea catre clienti a esuat.");
        }
    }

    private void SaveFinishedRun(FinishedRun run)
    {
        try
        {
            RunStore.Save(_paths.RunsDir, run.Scenario, run.Meta, run.Sim.Events);
            lock (_gate)
            {
                if (_epoch == run.Epoch)
                {
                    _savedRunId = run.Meta.RunId;
                    _publishNow = true;
                }
            }
            _log.LogInformation("Rulare salvata: {RunId}", run.Meta.RunId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Salvarea rularii {RunId} a esuat.", run.Meta.RunId);
        }
    }

    private int CountFlyingLocked()
    {
        if (_sim is null)
            return 0;

        var drones = _sim.Drones;
        int flying = 0;
        for (int i = 0; i < drones.Length; i++)
            if (drones[i].Status == EntityStatus.Flying)
                flying++;
        return flying;
    }

    private StatusDto BuildStatusLocked(int flying)
    {
        int total = _sim?.DroneCount ?? 0;
        int impacted = _sim?.ImpactCount ?? 0;

        return new StatusDto(
            State: _state.ToString().ToLowerInvariant(),
            ScenarioId: _scenarioId,
            ScenarioName: _sim?.Scenario.Name ?? "",
            Seed: _sim is null ? "" : _sim.Seed.ToString(),
            RunId: _meta?.RunId ?? "",
            Epoch: _epoch,
            Speed: _speed,
            SimTimeS: _sim?.Clock.Time ?? 0.0,
            Tick: _sim?.Clock.Tick ?? 0,
            TotalDrones: total,
            Flying: flying,
            Impacted: impacted,
            Violations: _sim?.ViolationCount ?? 0,
            Detected: _sim?.DetectedCount ?? 0,
            Intercepted: _sim?.InterceptedCount ?? 0,
            ShotsFired: _sim?.ShotsFired ?? 0,
            MagazineLeft: _sim?.MagazineLeft ?? 0,
            InterceptorsLaunched: _sim?.InterceptorsLaunched ?? 0,
            InterceptorsLost: _sim?.InterceptorsLost ?? 0,
            Crashed: _sim?.CrashedCount ?? 0,
            Jammed: _sim?.JammedCount ?? 0,
            SavedRunId: _savedRunId);
    }
}
