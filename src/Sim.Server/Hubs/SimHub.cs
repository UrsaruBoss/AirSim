using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Sim.Server.Services;

namespace Sim.Server.Hubs;

/// <summary>
/// Hub doar pentru server -> client. Mesaje emise:
///   "snapshot" (byte[], vezi SnapshotWriter), "events" (EventDto[]), "status" (StatusDto).
/// Controlul simularii se face prin REST (/api/sim/...).
/// </summary>
public sealed class SimHub : Hub
{
    private readonly SimulationHost _host;

    public SimHub(SimulationHost host) => _host = host;

    public override Task OnConnectedAsync()
    {
        // clientul nou primeste imediat status + snapshot (util si cand simularea e in pauza)
        _host.RequestPublish();
        return base.OnConnectedAsync();
    }
}
