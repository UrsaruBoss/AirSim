using System;
using System.Linq;
using Sim.Core.Scenarios;

namespace Sim.Core.Climate;

/// <summary>
/// Weather presets: a small set of named conditions with illustrative effects on the model, in the same spirit as <see cref="Sim.Core.Sun.DayNight"/>.
/// No weather (null) = neutral, identical to "clear". A preset is static for the whole run (no forecast, no change over time).
/// ALL FACTORS BELOW ARE ILLUSTRATIVE ESTIMATES, not measured data: they exist to compare conditions, not to predict.
/// Radar-guided missile systems are left alone on purpose (the effects are modest and the sources are weak);
/// what the presets capture is the big picture: optics suffer in fog and rain, small drones suffer in storms, radars lose a little in rain.
/// </summary>
public static class WeatherModel
{
    public const string Clear = "clear";

    /// <summary>The valid preset ids, in the order shown to the user.</summary>
    public static readonly string[] Presets = { Clear, "overcast", "rain", "fog", "storm" };

    public static bool IsKnown(string? weather) => weather is not null && Presets.Contains(weather, StringComparer.Ordinal);

    /// <summary>Effect on a defensive unit: scale of the detection / engagement range and of the probability of a hit.</summary>
    public readonly record struct Effect(double RangeScale, double PkScale)
    {
        public static Effect Neutral => new(1.0, 1.0);
        public bool IsNeutral => RangeScale == 1.0 && PkScale == 1.0;
    }

    public static Effect ForDefense(string? weather, DefenseKind kind) => (weather, kind) switch
    {
        ("overcast", DefenseKind.AaGun) => new Effect(0.85, 0.95),
        ("overcast", DefenseKind.InterceptorBase) => new Effect(1.0, 0.97),

        ("rain", DefenseKind.Radar) => new Effect(0.92, 1.0),
        ("rain", DefenseKind.AaGun) => new Effect(0.7, 0.85),
        ("rain", DefenseKind.Patrol) => new Effect(1.0, 0.95),
        ("rain", DefenseKind.InterceptorBase) => new Effect(1.0, 0.9),

        ("fog", DefenseKind.AaGun) => new Effect(0.4, 0.7),
        ("fog", DefenseKind.Patrol) => new Effect(1.0, 0.9),
        ("fog", DefenseKind.InterceptorBase) => new Effect(1.0, 0.9),

        ("storm", DefenseKind.Radar) => new Effect(0.88, 1.0),
        ("storm", DefenseKind.AaGun) => new Effect(0.5, 0.7),
        ("storm", DefenseKind.Sam) => new Effect(1.0, 0.95),
        ("storm", DefenseKind.Patrol) => new Effect(1.0, 0.9),
        ("storm", DefenseKind.InterceptorBase) => new Effect(0.8, 0.7),

        _ => Effect.Neutral
    };

    /// <summary>
    /// Extra probability that a small drone does not reach its target (added to the wave's own FailureRate).
    /// Only drones: cruise and ballistic missiles are not affected.
    /// </summary>
    public static double DroneFailureAdd(string? weather) => weather switch
    {
        "rain" => 0.01,
        "fog" => 0.01,
        "storm" => 0.05,
        _ => 0.0
    };
}
