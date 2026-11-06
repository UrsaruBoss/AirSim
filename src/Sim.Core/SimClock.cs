using System;

namespace Sim.Core;

/// <summary>
/// Fixed-step simulation clock. The only source of time for simulation logic
/// (never DateTime.Now). Time is computed from the tick count so errors do not accumulate.
/// </summary>
public sealed class SimClock
{
    public double Dt { get; }
    public long Tick { get; private set; }

    public double Time => Tick * Dt;

    public SimClock(double dtSeconds)
    {
        if (!(dtSeconds > 0.0) || double.IsInfinity(dtSeconds))
            throw new ArgumentOutOfRangeException(nameof(dtSeconds), "The time step must be positive and finite.");
        Dt = dtSeconds;
    }

    public void Advance() => Tick++;

    /// <summary>Number of ticks needed to cover the given duration.</summary>
    public long TicksFor(double seconds) => (long)Math.Ceiling(seconds / Dt);
}
