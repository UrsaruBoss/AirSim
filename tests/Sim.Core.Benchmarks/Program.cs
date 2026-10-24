using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Sim.Core;
using Sim.Core.Scenarios;

// Rulare (obligatoriu Release):
//   dotnet run -c Release --project tests/Sim.Core.Benchmarks
BenchmarkRunner.Run<TickBenchmark>();

/// <summary>Costul unui singur Step() cu N drone aflate toate in zbor.</summary>
[MemoryDiagnoser]
public class TickBenchmark
{
    [Params(2000, 20000)]
    public int Drones;

    private Simulation _sim = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Distanta mare (~8000 km) la 50 m/s: dronele nu ating tinta cat dureaza benchmark-ul.
        var scenario = new Scenario
        {
            Name = "bench",
            DtSeconds = 0.1,
            DurationSeconds = 1e8,
            LaunchSites = new List<LaunchSite> { new() { Id = "L", Name = "L", LatDeg = 60, LonDeg = 60 } },
            Targets = new List<Target> { new() { Id = "T", Name = "T", LatDeg = 0, LonDeg = 0 } },
            Waves = new List<Wave>
            {
                new()
                {
                    Id = "W", SiteId = "L", TargetId = "T", Kind = UnitKind.Drone,
                    Count = Drones, IntervalSeconds = 0, SpeedMps = 50, AltitudeM = 500, SpreadMeters = 500
                }
            }
        };

        _sim = new Simulation(scenario, 1);
        _sim.Step(); // toate dronele se lanseaza in primul tick
    }

    [Benchmark]
    public void Tick() => _sim.Step();
}
