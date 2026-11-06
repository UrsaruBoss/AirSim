using System.Collections.Generic;

namespace Sim.Core.Analytics;

/// <summary>Estimate with uncertainty: the mean and the 5%-95% interval from the Monte Carlo trials.</summary>
public sealed record Estimate(double Mean, double P5, double P95);

public sealed record ClassStat(string Class, int Impacts, Estimate Killed, Estimate Injured);

/// <summary>
/// Statistics per defensive unit, derived from events (Code = the unit id).
/// Shots = shots fired (for interceptor bases: launches); Hits / Misses = their outcome.
/// Returns = how many times a patrol returned to base.
/// </summary>
public sealed record DefenseStat(
    string Id, string Name, string Kind,
    int Detected, int Shots, int Hits, int Misses, int Lost, int Returns, int MagazineStart, bool Destroyed = false);

public sealed record CountryViolationCount(string Code, int Count);

/// <summary>
/// Time series of the run: cumulative counts at the end of each bin of <c>BinSeconds</c> (bin i covers up to (i + 1) * BinSeconds).
/// Shots = shots fired by shooters (an interceptor base counts its launches); MagazineTotal = the starting magazines of all shooters.
/// </summary>
public sealed record RunSeries(
    double BinSeconds,
    IReadOnlyList<int> Launched, IReadOnlyList<int> Detected, IReadOnlyList<int> Intercepted, IReadOnlyList<int> Impacts,
    IReadOnlyList<int> Jammed, IReadOnlyList<int> Crashed, IReadOnlyList<int> Shots,
    int MagazineTotal);

/// <summary>What happened to each wave: how many were launched and how they ended (the rest were still in flight).</summary>
public sealed record WaveStat(string Id, string Kind, int Count, int Launched, int Intercepted, int Jammed, int Crashed, int Impacted);

/// <summary>Distribution of the casualty estimate over the Monte Carlo trials: bin i covers [i * BinWidth, (i + 1) * BinWidth); values are numbers of trials.</summary>
public sealed record CasualtyHistogram(double BinWidth, IReadOnlyList<int> Killed, IReadOnlyList<int> Injured);

/// <summary>Cost of the two sides (USD): the attack units launched against what the defense spent on shots. Unknown costs count as 0.</summary>
public sealed record CostStat(double AttackUsd, double DefenseUsd);

/// <summary>
/// Public warning: for the impacts, how long before them the threat was first detected (0 if it never was) and how many of the exposed people were sheltered.
/// Enabled = false when the scenario turns the warning system off (then nothing is sheltered).
/// </summary>
public sealed record WarningStat(bool Enabled, int Impacts, int Undetected, double MeanWarningS, double MeanShelteredPct);

/// <summary>The result of a run (partial or finished). Derived solely from scenario + event log + seed.</summary>
public sealed record RunSummary(
    int Total,
    int Launched,
    int Detected,
    int Intercepted,
    int Missed,
    int Impacted,
    int Violations,
    IReadOnlyList<ClassStat> Classes,
    Estimate Killed,
    Estimate Injured,
    IReadOnlyList<CountryViolationCount> ViolationsByCountry,
    IReadOnlyList<DefenseStat> Defenses,
    int Trials,
    int Failed = 0,
    int Jammed = 0,
    RunSeries? Series = null,
    IReadOnlyList<WaveStat>? Waves = null,
    CasualtyHistogram? CasualtyHistogram = null,
    CostStat? Cost = null,
    WarningStat? Warning = null,
    SensitivityReport? Sensitivity = null);
