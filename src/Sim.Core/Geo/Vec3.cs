using System;

namespace Sim.Core.Geo;

/// <summary>Vector 3D in precizie double (System.Numerics.Vector3 e float, insuficient pentru ECEF).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
}
