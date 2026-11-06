using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Sim.Core.Airspace;
using Sim.Server.Models;

namespace Sim.Server.Services;

/// <summary>
/// Loads the country maps from data/countries/&lt;name&gt;.geojson and caches the expensive part (parsing + raster),
/// shared between runs. The cache key includes the last-modified time: if you edit the file, it is reloaded.
/// </summary>
public sealed class AirspaceCatalog
{
    /// <summary>Raster cell size in degrees (0.05 ~ 5 km); 0.1 for large maps (world), so it does not grow to tens of MB.</summary>
    private const double CellDeg = 0.05;
    private const double CoarseCellDeg = 0.1;
    private const double CoarseAboveSpanDeg = 120.0;

    private readonly SimPaths _paths;
    private readonly ConcurrentDictionary<string, Lazy<(CountryMap Map, CountryRaster Raster)>> _cache = new(StringComparer.Ordinal);

    public AirspaceCatalog(SimPaths paths) => _paths = paths;

    public string PathFor(string map)
    {
        if (!ScenarioCatalog.IsSafeId(map))
            throw new ArgumentException("Invalid map name.");
        return Path.Combine(_paths.CountriesDir, map + ".geojson");
    }

    public AirspaceModel Create(string map, IReadOnlyDictionary<string, CountryRole> roles)
    {
        string path = PathFor(map);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Country map '{map}' does not exist ({path}).");

        string key = $"{map}|{File.GetLastWriteTimeUtc(path).Ticks}";
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<(CountryMap, CountryRaster)>(() =>
        {
            var countryMap = CountryMap.FromFile(path);
            double cell = countryMap.East - countryMap.West > CoarseAboveSpanDeg ? CoarseCellDeg : CellDeg;
            return (countryMap, CountryRaster.Build(countryMap, cell));
        }));

        try
        {
            var geography = lazy.Value;
            return new AirspaceModel(geography.Map, geography.Raster, roles);
        }
        catch
        {
            _cache.TryRemove(key, out _); // do not keep a failed load (Lazy memoizes exceptions)
            throw;
        }
    }
}
