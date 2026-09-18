using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using Xunit;

namespace ArchWalk.Core.Tests;

public sealed class WalkSettingsTests
{
    [Theory]
    [InlineData("1550", 1.55)]
    [InlineData("1.55", 1.55)]
    [InlineData("1,55", 1.55)]
    [InlineData("1.55 м", 1.55)]
    [InlineData("1,55 м", 1.55)]
    [InlineData("1.55m", 1.55)]
    [InlineData("1550 мм", 1.55)]
    [InlineData("1550mm", 1.55)]
    [InlineData("300", 0.3)]
    [InlineData("0.3", 0.3)]
    public void TryParseEyeHeight_AcceptsCommonForms(string text, double expectedMeters)
    {
        Assert.True(WalkSettings.TryParseEyeHeight(text, out var meters));
        Assert.Equal(expectedMeters, meters, 9);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("abc")]
    public void TryParseEyeHeight_RejectsInvalid(string text)
    {
        Assert.False(WalkSettings.TryParseEyeHeight(text, out _));
    }

    [Fact]
    public void TryParseEyeHeight_BareSmallNumberIsMetresThenClamped()
    {
        Assert.True(WalkSettings.TryParseEyeHeight("0.1", out var meters));
        Assert.Equal(MotionDefaults.MinEyeHeightMeters, meters, 9);
    }

    [Fact]
    public void TryParseEyeHeight_ClampsToRange()
    {
        Assert.True(WalkSettings.TryParseEyeHeight("100", out var low));
        Assert.Equal(MotionDefaults.MinEyeHeightMeters, low, 9);
        Assert.True(WalkSettings.TryParseEyeHeight("5", out var high));
        Assert.Equal(MotionDefaults.MaxEyeHeightMeters, high, 9);
        Assert.True(WalkSettings.TryParseEyeHeight("5000", out var highMm));
        Assert.Equal(MotionDefaults.MaxEyeHeightMeters, highMm, 9);
    }

    [Theory]
    [InlineData("1.3", 1.3)]
    [InlineData("1,3", 1.3)]
    [InlineData("2 м/с", 2.0)]
    [InlineData("0.5 m/s", 0.5)]
    public void TryParseBaseSpeed_AcceptsCommonForms(string text, double expected)
    {
        Assert.True(WalkSettings.TryParseBaseSpeed(text, out var speed));
        Assert.Equal(expected, speed, 9);
    }

    [Fact]
    public void ClampBaseSpeed_RespectsLimits()
    {
        Assert.Equal(MotionDefaults.MinBaseSpeedMetersPerSecond, WalkSettings.ClampBaseSpeed(0.01), 12);
        Assert.Equal(MotionDefaults.MaxBaseSpeedMetersPerSecond, WalkSettings.ClampBaseSpeed(99), 12);
        Assert.Equal(1.3, WalkSettings.ClampBaseSpeed(1.3), 12);
    }

    [Fact]
    public void SetEyeHeight_KeepsFeetFixed()
    {
        var pose = new CameraPose(1, 2, 3, MotionDefaults.EyeHeightMeters, 0.2, 0.1, MotionDefaults.VerticalFovRadians);
        var core = new MotionCore(pose, MovementMode.Level, MotionDefaults.BaseSpeedMetersPerSecond);
        core.SetEyeHeight(1.8);
        Assert.Equal(1.0, core.Pose.FootXMeters, 12);
        Assert.Equal(2.0, core.Pose.FootYMeters, 12);
        Assert.Equal(3.0, core.Pose.FootZMeters, 12);
        Assert.Equal(1.8, core.Pose.EyeHeightMeters, 12);
        Assert.Equal(4.8, core.Pose.EyeMeters.Z, 12);
        Assert.Equal(4.8, core.DisplayEyeZMeters, 12);
    }

    [Fact]
    public void Home_RestoresHeightAndSpeedAfterLiveChange()
    {
        var core = MotionCore.CreateDefaultLevel();
        core.SetEyeHeight(1.9);
        core.SetBaseSpeed(3.0);
        core.ResetToSessionStart();
        Assert.Equal(MotionDefaults.EyeHeightMeters, core.Pose.EyeHeightMeters, 12);
        Assert.Equal(MotionDefaults.BaseSpeedMetersPerSecond, core.BaseSpeedMetersPerSecond, 9);
    }

    [Fact]
    public void Home_KeepsLiveHeightIfRememberedAsSessionStart()
    {
        var core = MotionCore.CreateDefaultLevel();
        core.SetEyeHeight(1.7);
        core.SetBaseSpeed(2.0);
        core.RememberSessionStart();
        core.SetEyeHeight(2.0);
        core.SetBaseSpeed(4.0);
        core.ResetToSessionStart();
        Assert.Equal(1.7, core.Pose.EyeHeightMeters, 12);
        Assert.Equal(2.0, core.BaseSpeedMetersPerSecond, 9);
    }
}
