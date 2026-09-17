using ArchWalk.Core.Math;

namespace ArchWalk.Core.Camera;

public readonly record struct CameraBasis(Vec3 Forward, Vec3 Right, Vec3 Up, Vec3 Horizontal);

public readonly record struct CameraPose(
    double FootXMeters,
    double FootYMeters,
    double FootZMeters,
    double EyeHeightMeters,
    double YawRadians,
    double PitchRadians,
    double VerticalFovRadians)
{
    public Vec3 FootMeters => new(FootXMeters, FootYMeters, FootZMeters);

    public Vec3 EyeMeters => new(FootXMeters, FootYMeters, FootZMeters + EyeHeightMeters);

    public static CameraPose CreateDefault() => new(
        0, 0, 0,
        Motion.MotionDefaults.EyeHeightMeters,
        0,
        0,
        Motion.MotionDefaults.VerticalFovRadians);

    public CameraPose WithFoot(Vec3 foot) => this with
    {
        FootXMeters = foot.X,
        FootYMeters = foot.Y,
        FootZMeters = foot.Z
    };

    public CameraPose WithLook(double yawRadians, double pitchRadians) => this with
    {
        YawRadians = NormalizeYaw(yawRadians),
        PitchRadians = ClampPitch(pitchRadians)
    };

    public CameraBasis Basis()
    {
        var yaw = YawRadians;
        var pitch = ClampPitch(PitchRadians);
        var h = new Vec3(System.Math.Sin(yaw), System.Math.Cos(yaw), 0);
        var r = new Vec3(System.Math.Cos(yaw), -System.Math.Sin(yaw), 0);
        var f = (h * System.Math.Cos(pitch)) + (Vec3.UnitZ * System.Math.Sin(pitch));
        var u = Vec3.Cross(r, f).Normalized();
        f = f.Normalized();
        r = Vec3.Cross(f, u).Normalized();
        u = Vec3.Cross(r, f).Normalized();
        return new CameraBasis(f, r, u, h);
    }

    public static double ClampPitch(double pitchRadians)
    {
        var min = Motion.MotionDefaults.MinPitchRadians;
        var max = Motion.MotionDefaults.MaxPitchRadians;
        if (pitchRadians < min) return min;
        if (pitchRadians > max) return max;
        return pitchRadians;
    }

    public static double NormalizeYaw(double yawRadians)
    {
        var twoPi = System.Math.PI * 2.0;
        var wrapped = yawRadians % twoPi;
        if (wrapped > System.Math.PI) wrapped -= twoPi;
        if (wrapped <= -System.Math.PI) wrapped += twoPi;
        return wrapped;
    }
}
