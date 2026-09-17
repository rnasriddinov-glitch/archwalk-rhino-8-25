using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;

namespace ArchWalk.Core.Support;

public enum SurfaceStepStatus
{
    Accepted = 0,
    Blocked = 1,
    Slid = 2
}

public readonly record struct SurfaceStepResult(SurfaceStepStatus Status, Vec3 FootMeters, string? Hint);

/// <summary>Deterministic surface-follow rules from 02_CAMERA_AND_MOVEMENT.md §8.</summary>
public static class SurfaceNavigator
{
    static readonly (double Dx, double Dy)[] AuxiliaryOffsets =
    [
        (1, 0),
        (-1, 0),
        (0, 1),
        (0, -1)
    ];

    public static double ClampSupportTolerance(double absoluteToleranceMeters)
    {
        if (absoluteToleranceMeters < MotionDefaults.SupportToleranceMinMeters)
            return MotionDefaults.SupportToleranceMinMeters;
        if (absoluteToleranceMeters > MotionDefaults.SupportToleranceMaxMeters)
            return MotionDefaults.SupportToleranceMaxMeters;
        return absoluteToleranceMeters;
    }

    public static bool CanAttach(ISupportField field, Vec3 footMeters, double toleranceMeters)
    {
        var tol = ClampSupportTolerance(toleranceMeters);
        return TryResolveFoot(field, footMeters, footMeters.X, footMeters.Y, tol, out _, out _);
    }

    public static SurfaceStepResult TryMove(
        ISupportField field,
        Vec3 fromFootMeters,
        double proposedXMeters,
        double proposedYMeters,
        double toleranceMeters)
    {
        var tol = ClampSupportTolerance(toleranceMeters);
        if (TryResolveFoot(field, fromFootMeters, proposedXMeters, proposedYMeters, tol, out var full, out _))
            return new SurfaceStepResult(SurfaceStepStatus.Accepted, full, null);

        var slidX = TryResolveFoot(field, fromFootMeters, proposedXMeters, fromFootMeters.Y, tol, out var alongX, out _)
            && HorizontalMoved(fromFootMeters, alongX);
        var slidY = TryResolveFoot(field, fromFootMeters, fromFootMeters.X, proposedYMeters, tol, out var alongY, out _)
            && HorizontalMoved(fromFootMeters, alongY);
        if (slidX && !slidY)
            return new SurfaceStepResult(SurfaceStepStatus.Slid, alongX, null);
        if (slidY && !slidX)
            return new SurfaceStepResult(SurfaceStepStatus.Slid, alongY, null);
        if (slidX && slidY)
        {
            var dx = alongX - fromFootMeters;
            var dy = alongY - fromFootMeters;
            return dx.HorizontalLength >= dy.HorizontalLength
                ? new SurfaceStepResult(SurfaceStepStatus.Slid, alongX, null)
                : new SurfaceStepResult(SurfaceStepStatus.Slid, alongY, null);
        }

        return new SurfaceStepResult(SurfaceStepStatus.Blocked, fromFootMeters, "край / нет опоры");
    }

    static bool HorizontalMoved(Vec3 from, Vec3 to) =>
        ((to.X - from.X) * (to.X - from.X)) + ((to.Y - from.Y) * (to.Y - from.Y)) > 1e-12;

    public static bool TryResolveFoot(
        ISupportField field,
        Vec3 fromFootMeters,
        double xMeters,
        double yMeters,
        double toleranceMeters,
        out Vec3 footMeters,
        out string? reason)
    {
        footMeters = fromFootMeters;
        reason = null;
        var tol = ClampSupportTolerance(toleranceMeters);
        var up = MotionDefaults.MaxStepUpMeters + tol;
        var down = MotionDefaults.MaxStepDownMeters + tol;

        var center = field.Probe(xMeters, yMeters, fromFootMeters.Z, up, down);
        if (!center.Found)
        {
            reason = "нет центральной опоры";
            return false;
        }

        if (!IsSlopeAllowed(center.Normal))
        {
            reason = "уклон";
            return false;
        }

        if (!IsStepAllowed(fromFootMeters.Z, center.ZMeters, tol))
        {
            reason = "ступень";
            return false;
        }

        var radius = MotionDefaults.SupportProbeRadiusMeters;
        var auxOk = 0;
        foreach (var (dx, dy) in AuxiliaryOffsets)
        {
            var aux = field.Probe(
                xMeters + (dx * radius),
                yMeters + (dy * radius),
                fromFootMeters.Z,
                up,
                down);
            if (!aux.Found || !IsSlopeAllowed(aux.Normal))
                continue;
            if (!IsStepAllowed(center.ZMeters, aux.ZMeters, tol) && !IsStepAllowed(fromFootMeters.Z, aux.ZMeters, tol))
                continue;
            auxOk++;
        }

        if (auxOk < MotionDefaults.MinAuxiliarySupportProbes)
        {
            reason = "край";
            return false;
        }

        footMeters = new Vec3(xMeters, yMeters, center.ZMeters);
        return true;
    }

    public static bool IsSlopeAllowed(Vec3 normal)
    {
        var n = normal.Normalized();
        var up = System.Math.Abs(n.Z);
        if (up <= 1e-12)
            return false;
        var degrees = System.Math.Acos(System.Math.Min(1.0, up)) * 180.0 / System.Math.PI;
        return degrees <= MotionDefaults.MaxSupportSlopeDegrees + 1e-6;
    }

    public static bool IsStepAllowed(double fromZ, double toZ, double toleranceMeters)
    {
        var dz = toZ - fromZ;
        var tol = ClampSupportTolerance(toleranceMeters);
        if (dz > MotionDefaults.MaxStepUpMeters + tol)
            return false;
        if (dz < -(MotionDefaults.MaxStepDownMeters + tol))
            return false;
        return true;
    }

    public static double SmoothEyeZ(
        double currentDisplayEyeZ,
        double targetEyeZ,
        double dtSeconds,
        bool enabled)
    {
        if (!enabled || dtSeconds <= 0)
            return targetEyeZ;

        var tau = MotionDefaults.EyeSmoothTauSeconds;
        var alpha = tau <= 0 ? 1.0 : 1.0 - System.Math.Exp(-dtSeconds / tau);
        var next = currentDisplayEyeZ + ((targetEyeZ - currentDisplayEyeZ) * alpha);
        var lag = next - targetEyeZ;
        var maxLag = MotionDefaults.EyeSmoothMaxLagMeters;
        if (lag > maxLag)
            next = targetEyeZ + maxLag;
        else if (lag < -maxLag)
            next = targetEyeZ - maxLag;
        return next;
    }
}
