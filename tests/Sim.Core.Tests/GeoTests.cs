using System;
using Sim.Core.Geo;
using Xunit;

namespace Sim.Core.Tests;

public class GeoTests
{
    public static TheoryData<double, double, double> RoundTripPoints => new()
    {
        { 44.4268, 26.1025, 90.0 },
        { 0.0, 0.0, 0.0 },
        { 89.9, 10.0, 5000.0 },
        { -89.9, -170.0, 0.0 },
        { 90.0, 0.0, 100.0 },
        { -45.0, 179.9, 35000.0 },
        { 60.0, -120.0, -100.0 },
        { 10.0, 10.0, 400000.0 },
    };

    [Theory]
    [MemberData(nameof(RoundTripPoints))]
    public void Ecef_roundtrip_is_accurate(double lat, double lon, double alt)
    {
        var original = new GeoPoint(lat, lon, alt);
        var back = Wgs84.FromEcef(Wgs84.ToEcef(original));

        Assert.InRange(Math.Abs(back.LatDeg - original.LatDeg), 0.0, 1e-8);
        Assert.InRange(Math.Abs(back.AltM - original.AltM), 0.0, 1e-3);

        // la poli longitudinea e nedefinita
        if (Math.Abs(lat) < 89.999)
        {
            double dLon = ((back.LonDeg - original.LonDeg + 540.0) % 360.0) - 180.0;
            Assert.InRange(Math.Abs(dLon), 0.0, 1e-8);
        }
    }

    [Fact]
    public void Equator_prime_meridian_maps_to_semi_major_axis()
    {
        var v = Wgs84.ToEcef(new GeoPoint(0, 0, 0));
        Assert.Equal(Wgs84.A, v.X, 6);
        Assert.Equal(0.0, v.Y, 6);
        Assert.Equal(0.0, v.Z, 6);
    }

    [Fact]
    public void Great_circle_one_degree_on_equator()
    {
        double expected = Wgs84.MeanRadiusM * Math.PI / 180.0;
        double d = Wgs84.GreatCircleDistanceM(new GeoPoint(0, 0), new GeoPoint(0, 1));
        Assert.Equal(expected, d, 3);
    }

    [Fact]
    public void Great_circle_quarter_of_meridian()
    {
        double expected = Wgs84.MeanRadiusM * Math.PI / 2.0;
        double d = Wgs84.GreatCircleDistanceM(new GeoPoint(0, 0), new GeoPoint(90, 0));
        Assert.Equal(expected, d, 3);
    }

    [Fact]
    public void Great_circle_is_symmetric_and_zero_for_same_point()
    {
        var a = new GeoPoint(44.43, 26.10);
        var b = new GeoPoint(46.77, 23.62);
        Assert.Equal(Wgs84.GreatCircleDistanceM(a, b), Wgs84.GreatCircleDistanceM(b, a), 6);
        Assert.Equal(0.0, Wgs84.GreatCircleDistanceM(a, a), 6);
    }

    [Fact]
    public void Slant_range_includes_altitude_difference()
    {
        var ground = new GeoPoint(45, 25, 0);
        var above = new GeoPoint(45, 25, 10000);
        Assert.Equal(10000.0, Wgs84.SlantRangeM(ground, above), 3);
    }

    [Fact]
    public void Bearing_cardinal_directions()
    {
        var origin = new GeoPoint(0, 0);
        Assert.Equal(0.0, Wgs84.InitialBearingDeg(origin, new GeoPoint(1, 0)), 6);
        Assert.Equal(90.0, Wgs84.InitialBearingDeg(origin, new GeoPoint(0, 1)), 6);
        Assert.Equal(180.0, Wgs84.InitialBearingDeg(origin, new GeoPoint(-1, 0)), 6);
        Assert.Equal(270.0, Wgs84.InitialBearingDeg(origin, new GeoPoint(0, -1)), 6);
    }

    [Fact]
    public void Destination_is_inverse_of_bearing_and_distance()
    {
        var a = new GeoPoint(44.43, 26.10, 500);
        var b = new GeoPoint(47.0, 21.0, 500);

        double brg = Wgs84.InitialBearingDeg(a, b);
        double dist = Wgs84.GreatCircleDistanceM(a, b);
        var reached = Wgs84.Destination(a, brg, dist);

        Assert.Equal(b.LatDeg, reached.LatDeg, 6);
        Assert.Equal(b.LonDeg, reached.LonDeg, 6);
        Assert.Equal(500.0, reached.AltM, 6);
    }
}
