using ArchWalk.Core.Camera;
using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;
using Xunit;

namespace ArchWalk.Core.Tests;

public sealed class MotionCoreTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void TenSecondsAtCruise_IsThirteenMetres(int fps)
    {
        var core = MotionCore.CreateDefaultLevel();
        ReachCruise(core, fps);
        var start = core.Pose.FootMeters;
        Simulate(core, fps, 10.0, InputIntent.HoldForward);
        var distance = (core.Pose.FootMeters - start).HorizontalLength;
        Assert.InRange(distance, 13.0 * 0.99, 13.0 * 1.01);
        Assert.Equal(0, core.Pose.FootZMeters, 9);
        Assert.Equal(MotionDefaults.EyeHeightMeters, core.Pose.EyeMeters.Z - core.Pose.FootZMeters, 9);
    }

    [Fact]
    public void Diagonal_DoesNotExceedForwardSpeed()
    {
        var forward = MotionCore.CreateDefaultLevel();
        var diagonal = MotionCore.CreateDefaultLevel();
        ReachCruise(forward, 60);
        Simulate(diagonal, 60, 2.0, InputIntent.None with { Forward = true, Left = true });
        Assert.InRange(diagonal.Velocity.Length, forward.Velocity.Length * 0.99, forward.Velocity.Length * 1.01);
    }

    [Fact]
    public void Strafe_DoesNotChangeYaw()
    {
        var core = MotionCore.CreateDefaultLevel();
        Simulate(core, 60, 1.0, InputIntent.None with { Right = true });
        Assert.Equal(0, core.Pose.YawRadians, 12);
        Assert.True(core.Pose.FootXMeters > 0.5);
        Assert.Equal(0, core.Pose.FootYMeters, 3);
    }

    [Fact]
    public void PitchUp_WalkStaysHorizontal()
    {
        var core = MotionCore.CreateDefaultLevel();
        core.Advance(1.0 / 60.0, InputIntent.None with { PitchDeltaRadians = MotionDefaults.MaxPitchRadians });
        ReachCruise(core, 60);
        Assert.Equal(0, core.Velocity.Z, 12);
        Assert.Equal(0, core.Pose.FootZMeters, 9);
    }

    [Fact]
    public void MouseDelta_IndependentOfFrameRate()
    {
        const double delta = 0.02;
        var slow = MotionCore.CreateDefaultLevel();
        var fast = MotionCore.CreateDefaultLevel();
        var intent = InputIntent.None with { YawDeltaRadians = delta };
        for (var i = 0; i < 30; i++)
            slow.Advance(1.0 / 30.0, intent);
        for (var i = 0; i < 30; i++)
            fast.Advance(1.0 / 120.0, intent);
        Assert.Equal(slow.Pose.YawRadians, fast.Pose.YawRadians, 12);
        var expected = CameraPose.NormalizeYaw(30 * delta);
        Assert.Equal(expected, slow.Pose.YawRadians, 10);
    }

    [Fact]
    public void ReleaseAtCruise_StopsWithinFiftyMillimetres()
    {
        var core = MotionCore.CreateDefaultLevel();
        ReachCruise(core, 120);
        var start = core.Pose.FootMeters;
        Simulate(core, 120, 0.25, InputIntent.None);
        var residual = (core.Pose.FootMeters - start).Length;
        Assert.InRange(residual, 0, 0.050);
        Simulate(core, 120, 0.10, InputIntent.None);
        Assert.True(core.Velocity.Length < 0.03);
    }

    [Fact]
    public void PreciseOverridesSprint()
    {
        var sprint = MotionCore.CreateDefaultLevel();
        var precise = MotionCore.CreateDefaultLevel();
        Simulate(sprint, 120, 2.0, InputIntent.None with { Forward = true, Sprint = true });
        Simulate(precise, 120, 2.0, InputIntent.None with { Forward = true, Sprint = true, Precise = true });
        Assert.InRange(sprint.Velocity.Length, 2.55, 2.65);
        Assert.InRange(precise.Velocity.Length, 0.25, 0.27);
    }

    [Fact]
    public void HitchOver250ms_PausesAndDropsMotion()
    {
        var core = MotionCore.CreateDefaultLevel();
        ReachCruise(core, 60);
        var foot = core.Pose.FootMeters;
        var result = core.Advance(0.30, InputIntent.HoldForward);
        Assert.Equal(MotionStepStatus.PausedDueToHitch, result.Status);
        Assert.Equal(0, core.Velocity.Length, 12);
        Assert.Equal(foot.X, core.Pose.FootXMeters, 12);
        Assert.Equal(foot.Y, core.Pose.FootYMeters, 12);
    }

    [Fact]
    public void QE_ChangesFootZ_NotEyeHeight()
    {
        var core = MotionCore.CreateDefaultLevel();
        Simulate(core, 120, 1.0, InputIntent.None with { Up = true });
        Assert.Equal(0.6, core.Pose.FootZMeters, 3);
        Assert.Equal(MotionDefaults.EyeHeightMeters, core.Pose.EyeHeightMeters, 12);
    }

    [Fact]
    public void Fly_ForwardFollowsPitchedLook()
    {
        var pose = CameraPose.CreateDefault().WithLook(0, System.Math.PI / 4.0);
        var core = new MotionCore(pose, MovementMode.Fly, MotionDefaults.BaseSpeedMetersPerSecond);
        Simulate(core, 120, 2.0, InputIntent.HoldForward);
        Assert.True(core.Pose.FootYMeters > 1.0);
        Assert.True(core.Pose.EyeMeters.Z > MotionDefaults.EyeHeightMeters + 1.0);
        Assert.InRange(core.Velocity.Length, 1.29, 1.31);
    }

    [Fact]
    public void Fly_WaeDiagonal_NormalizedToBaseSpeed()
    {
        var only = MotionCore.CreateDefaultFly();
        var combo = MotionCore.CreateDefaultFly();
        Simulate(only, 120, 2.0, InputIntent.HoldForward);
        Simulate(combo, 120, 2.0, InputIntent.None with { Forward = true, Right = true, Up = true });
        Assert.InRange(combo.Velocity.Length, only.Velocity.Length * 0.99, only.Velocity.Length * 1.01);
    }

    [Fact]
    public void SetMode_ZerosVelocityAndKeepsFoot()
    {
        var core = MotionCore.CreateDefaultLevel();
        ReachCruise(core, 120);
        var foot = core.Pose.FootMeters;
        core.SetMode(MovementMode.Fly);
        Assert.Equal(0, core.Velocity.Length, 12);
        Assert.Equal(MovementMode.Fly, core.Mode);
        Assert.Equal(foot.X, core.Pose.FootXMeters, 9);
        Assert.Equal(foot.Y, core.Pose.FootYMeters, 9);
        Assert.Equal(foot.Z, core.Pose.FootZMeters, 9);
    }

    [Fact]
    public void Home_RestoresStartPoseModeAndSpeed()
    {
        var core = MotionCore.CreateDefaultLevel();
        Simulate(core, 60, 1.0, InputIntent.HoldForward);
        core.SetMode(MovementMode.Fly);
        core.SetBaseSpeed(3);
        core.ResetToSessionStart();
        Assert.Equal(0, core.Pose.FootYMeters, 9);
        Assert.Equal(MovementMode.Level, core.Mode);
        Assert.Equal(MotionDefaults.BaseSpeedMetersPerSecond, core.BaseSpeedMetersPerSecond, 9);
        Assert.Equal(0, core.Velocity.Length, 12);
    }

    static void ReachCruise(MotionCore core, int fps)
    {
        Simulate(core, fps, 1.0, InputIntent.HoldForward);
        Assert.InRange(core.Velocity.Length, 1.29, 1.31);
    }

    static void Simulate(MotionCore core, int fps, double seconds, InputIntent intent)
    {
        var dt = 1.0 / fps;
        var elapsed = 0.0;
        while (elapsed + 1e-12 < seconds)
        {
            var step = System.Math.Min(dt, seconds - elapsed);
            core.Advance(step, intent);
            elapsed += step;
        }
    }
}
