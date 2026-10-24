using System.Collections.Generic;

namespace Sim.Core.Analytics;

/// <summary>Estimare cu incertitudine: media si intervalul 5%-95% din trialurile Monte Carlo.</summary>
public sealed record Estimate(double Mean, double P5, double P95);

public sealed record ClassStat(string Class, int Impacts, Estimate Killed, Estimate Injured);

/// <summary>
/// Statistici pe unitate defensiva, deduse din evenimente (Code = id-ul unitatii).
/// Shots = tiruri (la baza de interceptoare: lansari); Hits / Misses = rezultatul lor.
/// Returns = cate ori o patrula s-a intors la baza.
/// </summary>
public sealed record DefenseStat(
    string Id, string Name, string Kind,
    int Detected, int Shots, int Hits, int Misses, int Lost, int Returns, int MagazineStart, bool Destroyed = false);

public sealed record CountryViolationCount(string Code, int Count);

/// <summary>Rezultatul unei rulari (partiale sau terminate). Se deduce exclusiv din scenariu + log de evenimente + seed.</summary>
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
    int Jammed = 0);
