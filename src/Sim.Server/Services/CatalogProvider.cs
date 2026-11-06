using System;
using System.IO;
using System.Linq;
using Sim.Core.Catalog;
using Sim.Server.Models;

namespace Sim.Server.Services;

/// <summary>
/// Loads the type catalog (data/catalog) and caches it until the files change
/// (key = last-modified time), so catalog edits show up without restarting the server.
/// </summary>
public sealed class CatalogProvider
{
    private readonly SimPaths _paths;
    private readonly object _gate = new();
    private (long Stamp, UnitCatalog Catalog)? _cached;

    public CatalogProvider(SimPaths paths) => _paths = paths;

    /// <summary>May throw InvalidDataException if the catalog files are invalid.</summary>
    public UnitCatalog Get()
    {
        long stamp = Stamp();
        lock (_gate)
        {
            if (_cached is { } cached && cached.Stamp == stamp)
                return cached.Catalog;

            var catalog = UnitCatalog.FromDirectory(_paths.CatalogDir);
            _cached = (stamp, catalog);
            return catalog;
        }
    }

    private long Stamp() =>
        new[] { "offense.json", "defense.json" }
            .Select(name => Path.Combine(_paths.CatalogDir, name))
            .Select(path => File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L)
            .Aggregate(17L, (acc, ticks) => unchecked(acc * 31 + ticks));
}
