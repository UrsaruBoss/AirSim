using System;

namespace Sim.Core.Geo;

/// <summary>WGS84 constants and geodetic conversions.</summary>
public static class Wgs84
{
    /// <summary>Semi-major axis (m).</summary>
    public const double A = 6378137.0;

    /// <summary>Flattening.</summary>
    public const double F = 1.0 / 298.257223563;

    /// <summary>Eccentricity squared.</summary>
    public const double E2 = F * (2.0 - F);

    /// <summary>Mean radius (IUGG) used for spherical calculations (m).</summary>
    public const double MeanRadiusM = 6371008.8;

    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;

    public static Vec3 ToEcef(GeoPoint p)
    {
        double lat = p.LatDeg * Deg2Rad;
        double lon = p.LonDeg * Deg2Rad;
        double sinLat = Math.Sin(lat);
        double cosLat = Math.Cos(lat);

        double n = A / Math.Sqrt(1.0 - E2 * sinLat * sinLat);
        double x = (n + p.AltM) * cosLat * Math.Cos(lon);
        double y = (n + p.AltM) * cosLat * Math.Sin(lon);
        double z = (n * (1.0 - E2) + p.AltM) * sinLat;
        return new Vec3(x, y, z);
    }

    /// <summary>ECEF -> geodetic. Fixed number of iterations (deterministic, converges to sub-millimeter).</summary>
    public static GeoPoint FromEcef(Vec3 v)
    {
        double lon = Math.Atan2(v.Y, v.X);
        double p = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        double lat = Math.Atan2(v.Z, p * (1.0 - E2));
        double h = 0.0;

        for (int i = 0; i < 10; i++)
        {
            double sinLat = Math.Sin(lat);
            double n = A / Math.Sqrt(1.0 - E2 * sinLat * sinLat);

            // near the poles cos(lat) -> 0, so use the z-based formula
            h = Math.Abs(lat) < 1.0
                ? p / Math.Cos(lat) - n
                : v.Z / sinLat - n * (1.0 - E2);

            lat = Math.Atan2(v.Z, p * (1.0 - E2 * n / (n + h)));
        }

        return new GeoPoint(lat * Rad2Deg, lon * Rad2Deg, h);
    }

    /// <summary>Great-circle distance (haversine, mean-radius sphere), at sea level, in meters.</summary>
    public static double GreatCircleDistanceM(GeoPoint a, GeoPoint b)
    {
        double lat1 = a.LatDeg * Deg2Rad;
        double lat2 = b.LatDeg * Deg2Rad;
        double dLat = lat2 - lat1;
        double dLon = (b.LonDeg - a.LonDeg) * Deg2Rad;

        double sLat = Math.Sin(dLat / 2.0);
        double sLon = Math.Sin(dLon / 2.0);
        double h = sLat * sLat + Math.Cos(lat1) * Math.Cos(lat2) * sLon * sLon;
        return 2.0 * MeanRadiusM * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    /// <summary>Straight-line (slant range) distance between two 3D points, in meters. Used for radar/engagement.</summary>
    public static double SlantRangeM(GeoPoint a, GeoPoint b) => (ToEcef(a) - ToEcef(b)).Length;

    /// <summary>Initial bearing from a to b, in degrees [0, 360), on the sphere.</summary>
    public static double InitialBearingDeg(GeoPoint a, GeoPoint b)
    {
        double lat1 = a.LatDeg * Deg2Rad;
        double lat2 = b.LatDeg * Deg2Rad;
        double dLon = (b.LonDeg - a.LonDeg) * Deg2Rad;

        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        double brg = Math.Atan2(y, x) * Rad2Deg;
        return (brg + 360.0) % 360.0;
    }

    /// <summary>The point reached by leaving 'from' on the given bearing, at the given distance on the sphere. Altitude is preserved.</summary>
    public static GeoPoint Destination(GeoPoint from, double bearingDeg, double distanceM)
    {
        double lat1 = from.LatDeg * Deg2Rad;
        double lon1 = from.LonDeg * Deg2Rad;
        double brg = bearingDeg * Deg2Rad;
        double ang = distanceM / MeanRadiusM;

        double sinLat2 = Math.Sin(lat1) * Math.Cos(ang) + Math.Cos(lat1) * Math.Sin(ang) * Math.Cos(brg);
        double lat2 = Math.Asin(Math.Clamp(sinLat2, -1.0, 1.0));
        double lon2 = lon1 + Math.Atan2(
            Math.Sin(brg) * Math.Sin(ang) * Math.Cos(lat1),
            Math.Cos(ang) - Math.Sin(lat1) * sinLat2);

        double lonDeg = ((lon2 * Rad2Deg + 540.0) % 360.0) - 180.0;
        return new GeoPoint(lat2 * Rad2Deg, lonDeg, from.AltM);
    }
}
