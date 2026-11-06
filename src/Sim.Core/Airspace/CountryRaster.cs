using System;
using System.Collections.Generic;

namespace Sim.Core.Airspace;

/// <summary>
/// Lat/lon grid where each cell stores the index of its country. Built once (scanline, even-odd rule),
/// after which "which country is point (lat, lon) in" is a direct array lookup: O(1), no allocations.
/// Border precision is on the order of the cell size (0.05 degrees ~ 5 km), enough for airspace.
/// </summary>
public sealed class CountryRaster
{
    private const long MaxCells = 64_000_000;

    private readonly ushort[] _cells; // 0 = no country, k = country with index k - 1

    public double West { get; }
    public double North { get; }
    public double CellDeg { get; }
    public int Width { get; }
    public int Height { get; }

    private CountryRaster(double west, double north, double cellDeg, int width, int height)
    {
        West = west;
        North = north;
        CellDeg = cellDeg;
        Width = width;
        Height = height;
        _cells = new ushort[(long)width * height];
    }

    /// <summary>Index of the country at the given point, or -1 (outside the map, at sea, etc.).</summary>
    public int IndexAt(double latDeg, double lonDeg)
    {
        int col = (int)Math.Floor((lonDeg - West) / CellDeg);
        int row = (int)Math.Floor((North - latDeg) / CellDeg);
        if ((uint)col >= (uint)Width || (uint)row >= (uint)Height)
            return -1;

        return _cells[(long)row * Width + col] - 1;
    }

    public static CountryRaster Build(CountryMap map, double cellDeg = 0.05)
    {
        if (!(cellDeg > 0.0))
            throw new ArgumentOutOfRangeException(nameof(cellDeg), "The cell size must be positive.");

        int width = (int)Math.Ceiling((map.East - map.West) / cellDeg);
        int height = (int)Math.Ceiling((map.North - map.South) / cellDeg);
        if (width <= 0 || height <= 0)
            throw new ArgumentException("The map has zero extent.");
        if ((long)width * height > MaxCells)
            throw new ArgumentException($"Raster too large ({width} x {height}); increase the cell size.");

        var raster = new CountryRaster(map.West, map.North, cellDeg, width, height);

        // edge intersections with each row center; lists are reused from one country to the next
        var rowXs = new List<double>?[height];

        for (int k = 0; k < map.Countries.Count; k++)
        {
            var country = map.Countries[k];
            int firstRow = height, lastRow = -1;

            foreach (var ring in country.Rings)
            {
                int n = ring.Lon.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    double x0 = ring.Lon[i], y0 = ring.Lat[i];
                    double x1 = ring.Lon[j], y1 = ring.Lat[j];
                    if (y0 == y1)
                        continue;

                    double yLo = Math.Min(y0, y1), yHi = Math.Max(y0, y1);

                    // rows whose center c satisfies yLo <= c < yHi (half-open interval:
                    // a vertex shared by two edges is not counted twice)
                    int rMin = (int)Math.Floor((map.North - yHi) / cellDeg - 0.5) + 1;
                    int rMax = (int)Math.Floor((map.North - yLo) / cellDeg - 0.5);
                    if (rMin < 0) rMin = 0;
                    if (rMax >= height) rMax = height - 1;

                    for (int r = rMin; r <= rMax; r++)
                    {
                        double centerLat = map.North - (r + 0.5) * cellDeg;
                        double t = (centerLat - y0) / (y1 - y0);
                        (rowXs[r] ??= new List<double>()).Add(x0 + t * (x1 - x0));
                    }

                    if (rMin <= rMax)
                    {
                        if (rMin < firstRow) firstRow = rMin;
                        if (rMax > lastRow) lastRow = rMax;
                    }
                }
            }

            ushort value = (ushort)(k + 1);
            for (int r = firstRow; r <= lastRow; r++)
            {
                var xs = rowXs[r];
                if (xs is null || xs.Count == 0)
                    continue;

                xs.Sort();
                for (int p = 0; p + 1 < xs.Count; p += 2)
                {
                    // cells whose center lies between the two intersections
                    int colStart = (int)Math.Ceiling((xs[p] - map.West) / cellDeg - 0.5);
                    int colEnd = (int)Math.Floor((xs[p + 1] - map.West) / cellDeg - 0.5);
                    if (colStart < 0) colStart = 0;
                    if (colEnd >= width) colEnd = width - 1;

                    long rowOffset = (long)r * width;
                    for (int c = colStart; c <= colEnd; c++)
                        raster._cells[rowOffset + c] = value;
                }
                xs.Clear();
            }
        }

        return raster;
    }
}
