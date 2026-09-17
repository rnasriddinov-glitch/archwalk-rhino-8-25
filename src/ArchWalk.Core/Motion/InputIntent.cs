namespace ArchWalk.Core.Motion;

public readonly record struct InputIntent(
    bool Forward,
    bool Back,
    bool Left,
    bool Right,
    bool Up,
    bool Down,
    bool Sprint,
    bool Precise,
    double YawDeltaRadians,
    double PitchDeltaRadians,
    int SpeedWheelSteps)
{
    public static InputIntent None { get; } = new(false, false, false, false, false, false, false, false, 0, 0, 0);

    public static InputIntent HoldForward => None with { Forward = true };

    public double HorizontalSpeedMultiplier
    {
        get
        {
            if (Precise)
                return MotionDefaults.PreciseMultiplier;
            if (Sprint)
                return MotionDefaults.SprintMultiplier;
            return 1.0;
        }
    }

    public double LookMultiplier => Precise ? MotionDefaults.PreciseLookMultiplier : 1.0;
}
