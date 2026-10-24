using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Sim.Core.Airspace;
using Sim.Server.Models;

namespace Sim.Server.Services;

/// <summary>
/// Incarca hartile de tari din data/countries/&lt;nume&gt;.geojson si tine in cache partea scumpa (parsare + raster),
/// partajata intre rulari. Cheia de cache include data ultimei modificari: daca editezi fisierul, se reincarca.
/// </summary>
public sealed class AirspaceCatalog
{
    /// <summary>Marimea celulei rasterului in grade (0.05 ~ 5 km); pentru hartile mari (lume) 0.1, ca sa nu ajunga la zeci de MB.</summary>
    private const double CellDeg = 0.05;
    private const double CoarseCellDeg = 0.1;
    private const double CoarseAboveSpanDeg = 120.0;

    private readonly SimPaths _paths;
    private readonly ConcurrentDictionary<string, Lazy<(CountryMap Map, CountryRaster Raster)>> _cache = new(StringComparer.Ordinal);

    public AirspaceCatalog(SimPaths paths) => _paths = paths;

    public string PathFor(string map)
    {
        if (!ScenarioCatalog.IsSafeId(map))
            throw new ArgumentException("Nume de harta invalid.");
        return Path.Combine(_paths.CountriesDir, map + ".geojson");
    }

    public AirspaceModel Create(string map, IReadOnlyDictionary<string, CountryRole> roles)
    {
        string path = PathFor(map);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Harta de tari '{map}' nu exista ({path}).");

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
            _cache.TryRemove(key, out _); // nu pastram o incarcare esuata (Lazy memoreaza exceptiile)
            throw;
        }
    }
}
