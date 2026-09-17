using ArchWalk.Core.Camera;
using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;

namespace ArchWalk.Core.Placement;

/// <summary>Deterministic aim helpers for the placement draft (document 01 §4, 02 FOV).</summary>
public static class PlacementMath
{
    public const double MinAimHorizontalMeters = 0.05;

    /// <summary>
    /// Horizontal aim on the foot elevation plane. Pitch stays 0.
    /// Returns false when the aim point is too close in XY (keep previous yaw).
    /// </summary>
    public static bool TryYawFromFootPlaneAim(
        Vec3 footMeters,
        Vec3 aimOnFootPlaneMeters,
        out double yawRadians)
    {
        var dx = aimOnFootPlaneMeters.X - footMeters.X;
        var dy = aimOnFootPlaneMeters.Y - footMeters.Y;
        var horiz = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (horiz < MinAimHorizontalMeters)
        {
            yawRadians = 0;
            return false;
        }

        yawRadians = CameraPose.NormalizeYaw(System.Math.Atan2(dx, dy));
        return true;
    }

    /// <summary>Look-at a 3D point from the eye; clamps pitch to product limits.</summary>
    public static bool TryLookAtPoint(
        Vec3 eyeMeters,
        Vec3 targetMeters,
        out double yawRadians,
        out double pitchRadians)
    {
        var d = targetMeters - eyeMeters;
        var horiz = System.Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
        if (horiz < MinAimHorizontalMeters && System.Math.Abs(d.Z) < MinAimHorizontalMeters)
        {
            yawRadians = 0;
            pitchRadians = 0;
            return false;
        }

        yawRadians = CameraPose.NormalizeYaw(System.Math.Atan2(d.X, d.Y));
        pitchRadians = CameraPose.ClampPitch(System.Math.Atan2(d.Z, System.Math.Max(horiz, 1e-12)));
        return true;
    }

    public static double HorizontalFovForAspect(double verticalFovRadians, double aspectWidthOverHeight)
        => CameraOptics.HorizontalFovRadians(verticalFovRadians, aspectWidthOverHeight);

    /// <summary>Plan-sector wedge endpoints at a fixed readable distance on the foot plane.</summary>
    public static void SectorEndpoints(
        Vec3 footMeters,
        double yawRadians,
        double horizontalFovRadians,
        double lengthMeters,
        out Vec3 left,
        out Vec3 right,
        out Vec3 center)
    {
        var half = horizontalFovRadians * 0.5;
        center = FootOffset(footMeters, yawRadians, lengthMeters);
        left = FootOffset(footMeters, yawRadians - half, lengthMeters);
        right = FootOffset(footMeters, yawRadians + half, lengthMeters);
    }

    public static Vec3 FootOffset(Vec3 footMeters, double yawRadians, double lengthMeters)
    {
        var x = footMeters.X + (System.Math.Sin(yawRadians) * lengthMeters);
        var y = footMeters.Y + (System.Math.Cos(yawRadians) * lengthMeters);
        return new Vec3(x, y, footMeters.Z);
    }

    public static CameraPose PoseFromDraft(
        Vec3 footMeters,
        double yawRadians,
        double pitchRadians,
        double eyeHeightMeters = MotionDefaults.EyeHeightMeters,
        double verticalFovRadians = -1)
    {
        var fov = verticalFovRadians > 0 ? verticalFovRadians : MotionDefaults.VerticalFovRadians;
        return new CameraPose(
            footMeters.X,
            footMeters.Y,
            footMeters.Z,
            eyeHeightMeters,
            CameraPose.NormalizeYaw(yawRadians),
            CameraPose.ClampPitch(pitchRadians),
            fov);
    }
}
