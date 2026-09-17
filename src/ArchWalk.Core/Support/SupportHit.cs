using ArchWalk.Core.Math;

namespace ArchWalk.Core.Support;

public readonly record struct SupportHit(bool Found, double ZMeters, Vec3 Normal)
{
    public static SupportHit Miss { get; } = new(false, 0, Vec3.UnitZ);

    public static SupportHit At(double zMeters, Vec3 normal) => new(true, zMeters, normal.Normalized());
}
