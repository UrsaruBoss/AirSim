using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Sim.Server.Services;

namespace Sim.Server.Hubs;

/// <summary>
/// Server -> client only hub. Messages emitted:
///   "snapshot" (byte[], see SnapshotWriter), "events" (EventDto[]), "status" (StatusDto).
/// The simulation is controlled over REST (/api/sim/...).
/// </summary>
public sealed class SimHub : Hub
{
    private readonly SimulationHost _host;

    public SimHub(SimulationHost host) => _host = host;

    public override Task OnConnectedAsync()
    {
        // a new client immediately receives status + snapshot (also useful while the simulation is paused)
        _host.RequestPublish();
        return base.OnConnectedAsync();
    }
}
