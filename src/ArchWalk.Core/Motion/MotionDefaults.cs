namespace ArchWalk.Core.Motion;

/// <summary>Product defaults from 02_CAMERA_AND_MOVEMENT.md. SI units.</summary>
public static class MotionDefaults
{
    public const double EyeHeightMeters = 1.550;
    public const double MinEyeHeightMeters = 0.300;
    public const double MaxEyeHeightMeters = 2.500;

    public const double BaseSpeedMetersPerSecond = 1.3;
    public const double MinBaseSpeedMetersPerSecond = 0.1;
    public const double MaxBaseSpeedMetersPerSecond = 6.0;

    public const double SprintMultiplier = 2.0;
    public const double PreciseMultiplier = 0.2;
    public const double PreciseLookMultiplier = 0.5;

    public const double VerticalSpeedMetersPerSecond = 0.6;

    public const double WheelSpeedStep = 1.2;

    public const double LookDegreesPerLogicalPixel = 0.10;
    public const double MinPitchDegrees = -85.0;
    public const double MaxPitchDegrees = 85.0;

    public const double VerticalFovDegrees = 45.0;
    public const double MinVerticalFovDegrees = 30.0;
    public const double MaxVerticalFovDegrees = 75.0;

    public const double AccelTauSeconds = 0.060;
    public const double BrakeTauSeconds = 0.025;

    public const double TickHz = 120.0;
    public const double TickSeconds = 1.0 / TickHz;
    public const int MaxTicksPerUpdate = 12;
    public const double HitchCatchUpCapSeconds = 0.100;
    public const double HitchPauseSeconds = 0.250;
    public const double StopSpeedMetersPerSecond = 0.001;

    public const double CameraTargetDistanceMeters = 5.0;
    public const double ArrowLengthMeters = 1.0;
    public const double FrustumGizmoLengthMeters = 3.0;

    public static double LookRadiansPerLogicalPixel => LookDegreesPerLogicalPixel * System.Math.PI / 180.0;
    public static double MinPitchRadians => MinPitchDegrees * System.Math.PI / 180.0;
    public static double MaxPitchRadians => MaxPitchDegrees * System.Math.PI / 180.0;
    public static double VerticalFovRadians => VerticalFovDegrees * System.Math.PI / 180.0;
}
