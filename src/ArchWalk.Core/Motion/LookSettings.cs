namespace ArchWalk.Core.Motion;

public static class LookSettings
{
    public static double YawRadiansFromPixels(double logicalPixels)
        => logicalPixels * MotionDefaults.LookRadiansPerLogicalPixel;

    public static double PitchRadiansFromPixels(double logicalPixelsYDown, bool invertVertical)
    {
        var pixels = invertVertical ? logicalPixelsYDown : -logicalPixelsYDown;
        return pixels * MotionDefaults.LookRadiansPerLogicalPixel;
    }
}

public enum MouseLookProfile
{
    FreeLook = 0,
    RightButton = 1
}

public enum SessionState
{
    Idle = 0,
    EnterPending = 1,
    Captured = 2,
    Paused = 3,
    Ending = 4
}

public enum WalkExitKind
{
    KeepView = 0,
    RestoreSnapshot = 1
}
