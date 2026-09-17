namespace ArchWalk.Core.Math;

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero => new(0, 0, 0);
    public static Vec3 UnitX => new(1, 0, 0);
    public static Vec3 UnitY => new(0, 1, 0);
    public static Vec3 UnitZ => new(0, 0, 1);

    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);
    public double Length => System.Math.Sqrt(LengthSquared);

    public Vec3 Normalized()
    {
        var len = Length;
        return len <= 1e-15 ? Zero : new Vec3(X / len, Y / len, Z / len);
    }

    public Vec3 WithZ(double z) => new(X, Y, z);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public static double Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));

    public double HorizontalLength => System.Math.Sqrt((X * X) + (Y * Y));
}
