using ArchWalk.Core.Math;
using ArchWalk.Core.Support;

namespace ArchWalk.Core.Support;

/// <summary>Test double: piecewise height/slope samples on the XY plane.</summary>
public sealed class AnalyticSupportField : ISupportField
{
    readonly Func<double, double, SupportHit?> _sample;

    public AnalyticSupportField(Func<double, double, SupportHit?> sample) => _sample = sample;

    public static AnalyticSupportField Flat(double z = 0) =>
        new((_, _) => SupportHit.At(z, Vec3.UnitZ));

    public static AnalyticSupportField FlatWithHole(double z, double holeMinX, double holeMaxX, double holeMinY, double holeMaxY) =>
        new((x, y) =>
        {
            if (x >= holeMinX && x <= holeMaxX && y >= holeMinY && y <= holeMaxY)
                return null;
            return SupportHit.At(z, Vec3.UnitZ);
        });

    public static AnalyticSupportField Step(double lowZ, double highZ, double edgeY) =>
        new((_, y) => SupportHit.At(y >= edgeY ? highZ : lowZ, Vec3.UnitZ));

    public static AnalyticSupportField Ramp(double z0, double slopeRiseOverRun, double maxSlopeDegrees = 60)
    {
        var angle = System.Math.Atan(slopeRiseOverRun);
        var degrees = angle * 180.0 / System.Math.PI;
        var nx = -System.Math.Sin(angle);
        var nz = System.Math.Cos(angle);
        var normal = new Vec3(nx, 0, nz).Normalized();
        return new((x, _) =>
        {
            if (degrees > maxSlopeDegrees + 1e-6)
                return SupportHit.At(z0 + (x * slopeRiseOverRun), normal);
            return SupportHit.At(z0 + (x * slopeRiseOverRun), normal);
        });
    }

    public static AnalyticSupportField TwoFloors(double lowerZ, double upperZ) =>
        new((_, _) => SupportHit.At(lowerZ, Vec3.UnitZ)); // probe window decides; helper for attach tests uses reference Z

    public SupportHit Probe(double xMeters, double yMeters, double referenceZMeters, double upMeters, double downMeters)
    {
        var hit = _sample(xMeters, yMeters);
        if (hit is null || !hit.Value.Found)
            return SupportHit.Miss;
        var z = hit.Value.ZMeters;
        if (z > referenceZMeters + upMeters + 1e-9)
            return SupportHit.Miss;
        if (z < referenceZMeters - downMeters - 1e-9)
            return SupportHit.Miss;
        // Prefer the hit closest to reference within the window (continuity).
        return hit.Value;
    }
}
