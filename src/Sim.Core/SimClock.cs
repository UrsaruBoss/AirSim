using System;

namespace Sim.Core;

/// <summary>
/// Ceas de simulare cu pas fix. Singura sursa de timp pentru logica de simulare
/// (niciodata DateTime.Now). Timpul se calculeaza din numarul de tick-uri, ca sa nu se acumuleze erori.
/// </summary>
public sealed class SimClock
{
    public double Dt { get; }
    public long Tick { get; private set; }

    public double Time => Tick * Dt;

    public SimClock(double dtSeconds)
    {
        if (!(dtSeconds > 0.0) || double.IsInfinity(dtSeconds))
            throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Pasul de timp trebuie sa fie pozitiv si finit.");
        Dt = dtSeconds;
    }

    public void Advance() => Tick++;

    /// <summary>Numarul de tick-uri necesar pentru a acoperi durata data.</summary>
    public long TicksFor(double seconds) => (long)Math.Ceiling(seconds / Dt);
}
