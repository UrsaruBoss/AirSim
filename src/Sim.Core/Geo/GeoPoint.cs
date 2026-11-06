namespace Sim.Core.Geo;

/// <summary>WGS84 geodetic point: latitude/longitude in degrees, ellipsoidal altitude in meters.</summary>
public readonly record struct GeoPoint(double LatDeg, double LonDeg, double AltM = 0.0);
