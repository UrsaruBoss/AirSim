namespace Sim.Core.Geo;

/// <summary>Punct geodezic WGS84: latitudine/longitudine in grade, altitudine elipsoidala in metri.</summary>
public readonly record struct GeoPoint(double LatDeg, double LonDeg, double AltM = 0.0);
