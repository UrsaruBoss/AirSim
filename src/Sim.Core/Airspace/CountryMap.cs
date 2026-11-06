using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sim.Core.Airspace;

/// <summary>A polygon ring, coordinates in degrees (lon, lat). May be closed (first = last) or not.</summary>
public readonly record struct Ring(double[] Lon, double[] Lat);

/// <summary>
/// One country: all its rings (outer outlines and holes, from every part of a MultiPolygon).
/// Rasterization uses the even-odd rule, so outer rings and holes need not be told apart.
/// </summary>
public sealed class CountryGeometry
{
    public string Iso2 { get; }
    public string Name { get; }
    public IReadOnlyList<Ring> Rings { get; }

    public double MinLon { get; }
    public double MinLat { get; }
    public double MaxLon { get; }
    public double MaxLat { get; }

    public CountryGeometry(string iso2, string name, IReadOnlyList<Ring> rings)
    {
        Iso2 = iso2;
        Name = name;
        Rings = rings;

        double minLon = double.MaxValue, minLat = double.MaxValue, maxLon = double.MinValue, maxLat = double.MinValue;
        foreach (var ring in rings)
        {
            for (int i = 0; i < ring.Lon.Length; i++)
            {
                minLon = Math.Min(minLon, ring.Lon[i]);
                maxLon = Math.Max(maxLon, ring.Lon[i]);
                minLat = Math.Min(minLat, ring.Lat[i]);
                maxLat = Math.Max(maxLat, ring.Lat[i]);
            }
        }
        MinLon = minLon;
        MinLat = minLat;
        MaxLon = maxLon;
        MaxLat = maxLat;
    }
}

/// <summary>The set of countries in a GeoJSON file (FeatureCollection with "iso2" and "name" properties, Polygon/MultiPolygon geometries).</summary>
public sealed class CountryMap
{
    private readonly Dictionary<string, int> _indexByCode = new(StringComparer.Ordinal);

    public IReadOnlyList<CountryGeometry> Countries { get; }

    public double West { get; }
    public double South { get; }
    public double East { get; }
    public double North { get; }

    public CountryMap(IReadOnlyList<CountryGeometry> countries)
    {
        if (countries.Count == 0)
            throw new InvalidDataException("The country map contains no country.");
        if (countries.Count >= ushort.MaxValue)
            throw new InvalidDataException("Too many countries for the raster.");

        Countries = countries;

        double west = double.MaxValue, south = double.MaxValue, east = double.MinValue, north = double.MinValue;
        for (int i = 0; i < countries.Count; i++)
        {
            var c = countries[i];
            if (!_indexByCode.TryAdd(c.Iso2, i))
                throw new InvalidDataException($"Duplicate country code in the map: '{c.Iso2}'.");

            west = Math.Min(west, c.MinLon);
            south = Math.Min(south, c.MinLat);
            east = Math.Max(east, c.MaxLon);
            north = Math.Max(north, c.MaxLat);
        }
        West = west;
        South = south;
        East = east;
        North = north;
    }

    /// <summary>Index of the country with the given code, or -1.</summary>
    public int IndexOf(string iso2) => _indexByCode.TryGetValue(iso2, out int i) ? i : -1;

    public static CountryMap FromFile(string path) => FromGeoJson(File.ReadAllText(path));

    public static CountryMap FromGeoJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var type) || type.GetString() != "FeatureCollection")
                throw new InvalidDataException("Invalid GeoJSON: a FeatureCollection is expected.");

            var countries = new List<CountryGeometry>();
            foreach (var feature in root.GetProperty("features").EnumerateArray())
            {
                var props = feature.GetProperty("properties");
                string iso2 = props.GetProperty("iso2").GetString() ?? "";
                if (iso2.Length == 0)
                    throw new InvalidDataException("Invalid GeoJSON: country without the 'iso2' property.");

                string name = props.TryGetProperty("name", out var n) ? n.GetString() ?? iso2 : iso2;

                var geometry = feature.GetProperty("geometry");
                var coordinates = geometry.GetProperty("coordinates");
                var rings = new List<Ring>();

                switch (geometry.GetProperty("type").GetString())
                {
                    case "Polygon":
                        AddPolygon(coordinates, rings);
                        break;
                    case "MultiPolygon":
                        foreach (var polygon in coordinates.EnumerateArray())
                            AddPolygon(polygon, rings);
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported geometry at '{iso2}' (only Polygon and MultiPolygon).");
                }

                countries.Add(new CountryGeometry(iso2, name, rings));
            }

            return new CountryMap(countries);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"Geoinvalid JSON: {ex.Message}", ex);
        }
    }

    private static void AddPolygon(JsonElement polygon, List<Ring> rings)
    {
        foreach (var ring in polygon.EnumerateArray())
        {
            int count = ring.GetArrayLength();
            var lon = new double[count];
            var lat = new double[count];

            int i = 0;
            foreach (var point in ring.EnumerateArray())
            {
                lon[i] = point[0].GetDouble();
                lat[i] = point[1].GetDouble();
                i++;
            }
            rings.Add(new Ring(lon, lat));
        }
    }
}
