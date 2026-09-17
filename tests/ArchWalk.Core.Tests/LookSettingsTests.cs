using ArchWalk.Core.Motion;
using Xunit;

namespace ArchWalk.Core.Tests;

public sealed class LookSettingsTests
{
    [Fact]
    public void MouseUp_LooksUp_WhenNotInverted()
    {
        var pitch = LookSettings.PitchRadiansFromPixels(-10, invertVertical: false);
        Assert.True(pitch > 0);
        Assert.Equal(10 * MotionDefaults.LookRadiansPerLogicalPixel, pitch, 12);
    }

    [Fact]
    public void Yaw_IsNotScaledByDeltaTime()
    {
        var a = LookSettings.YawRadiansFromPixels(25);
        var b = LookSettings.YawRadiansFromPixels(25);
        Assert.Equal(a, b, 12);
        Assert.Equal(25 * MotionDefaults.LookRadiansPerLogicalPixel, a, 12);
    }
}
