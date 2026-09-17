using ArchWalk.Core.Camera;
using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Support;
using Xunit;

namespace ArchWalk.Core.Tests;

public sealed class SurfaceNavigatorTests
{
    [Fact]
    public void FlatFloor_AcceptsForwardStep()
    {
        var field = AnalyticSupportField.Flat(0);
        var from = new Vec3(0, 0, 0);
        var result = SurfaceNavigator.TryMove(field, from, 0, 0.05, 0.001);
        Assert.Equal(SurfaceStepStatus.Accepted, result.Status);
        Assert.Equal(0.05, result.FootMeters.Y, 9);
        Assert.Equal(0, result.FootMeters.Z, 9);
    }

    [Fact]
    public void Hole_BlocksCenter()
    {
        var field = AnalyticSupportField.FlatWithHole(0, -0.05, 0.05, 0.9, 1.1);
        var from = new Vec3(0, 0.8, 0);
        var result = SurfaceNavigator.TryMove(field, from, 0, 1.0, 0.001);
        Assert.Equal(SurfaceStepStatus.Blocked, result.Status);
        Assert.Equal(0.8, result.FootMeters.Y, 9);
    }

    [Fact]
    public void Edge_SlidesAlongAllowedAxis()
    {
        // Floor only for y <= 1; move diagonally toward void.
        var field = new AnalyticSupportField((x, y) => y <= 1.0 ? SupportHit.At(0, Vec3.UnitZ) : null);
        var from = new Vec3(0, 0.95, 0);
        var result = SurfaceNavigator.TryMove(field, from, 0.2, 1.2, 0.001);
        Assert.Equal(SurfaceStepStatus.Slid, result.Status);
        Assert.True(result.FootMeters.Y <= 1.0 + 1e-9);
        Assert.True(result.FootMeters.X > 0.05);
    }

    [Fact]
    public void StepUp_Within220mm_Accepted()
    {
        var field = AnalyticSupportField.Step(0, 0.20, 1.0);
        var from = new Vec3(0, 0.9, 0);
        Assert.True(SurfaceNavigator.TryResolveFoot(field, from, 0, 1.05, 0.001, out var foot, out _));
        Assert.Equal(0.20, foot.Z, 9);
    }

    [Fact]
    public void StepUp_230mm_OutsideLocalWindow()
    {
        var field = AnalyticSupportField.Step(0, 0.23, 1.0);
        var from = new Vec3(0, 0.9, 0);
        Assert.False(SurfaceNavigator.TryResolveFoot(field, from, 0, 1.05, 0.001, out _, out var reason));
        Assert.Equal("нет центральной опоры", reason);
    }

    [Fact]
    public void StepUp_ExactlyAtLimit_Accepted()
    {
        var field = AnalyticSupportField.Step(0, MotionDefaults.MaxStepUpMeters, 1.0);
        var from = new Vec3(0, 0.9, 0);
        Assert.True(SurfaceNavigator.TryResolveFoot(field, from, 0, 1.05, 0.001, out var foot, out _));
        Assert.Equal(MotionDefaults.MaxStepUpMeters, foot.Z, 9);
    }

    [Fact]
    public void SteepRamp_BlockedBySlope()
    {
        // 50° slope
        var rise = System.Math.Tan(50.0 * System.Math.PI / 180.0);
        var field = AnalyticSupportField.Ramp(0, rise);
        var from = new Vec3(0, 0, 0);
        Assert.False(SurfaceNavigator.TryResolveFoot(field, from, 0.05, 0, 0.001, out _, out var reason));
        Assert.Equal("уклон", reason);
    }

    [Fact]
    public void MildRamp_Accepted()
    {
        var rise = System.Math.Tan(30.0 * System.Math.PI / 180.0);
        var field = AnalyticSupportField.Ramp(0, rise);
        var from = new Vec3(0, 0, 0);
        Assert.True(SurfaceNavigator.TryResolveFoot(field, from, 0.05, 0, 0.001, out var foot, out _));
        Assert.InRange(foot.Z, 0.02, 0.04);
    }

    [Fact]
    public void LocalWindow_DoesNotGrabUpperFloor()
    {
        var field = new MultiLevelSupportField([0.0, 3.0]);
        var from = new Vec3(0, 0, 0);
        Assert.True(SurfaceNavigator.TryResolveFoot(field, from, 0.1, 0, 0.001, out var foot, out _));
        Assert.Equal(0.0, foot.Z, 9);
    }

    [Fact]
    public void CanAttach_RequiresLocalSupport()
    {
        var field = new MultiLevelSupportField([0.0, 3.0]);
        Assert.True(SurfaceNavigator.CanAttach(field, new Vec3(0, 0, 0), 0.001));
        Assert.False(SurfaceNavigator.CanAttach(field, new Vec3(0, 0, 1.5), 0.001));
    }

    [Fact]
    public void EyeSmoothing_SettlesUnder2mmWithin250ms()
    {
        var display = 1.55;
        var target = 1.55 + 0.20;
        var t = 0.0;
        while (t < MotionDefaults.EyeSmoothSettleSeconds)
        {
            display = SurfaceNavigator.SmoothEyeZ(display, target, MotionDefaults.TickSeconds, true);
            t += MotionDefaults.TickSeconds;
        }
        Assert.True(System.Math.Abs(display - target) < MotionDefaults.EyeSmoothSettleMeters);
    }

    [Fact]
    public void EyeSmoothing_LagClampedTo80mm()
    {
        var target = 1.55 + 0.5;
        var display = SurfaceNavigator.SmoothEyeZ(1.55, target, MotionDefaults.TickSeconds, true);
        Assert.Equal(target - MotionDefaults.EyeSmoothMaxLagMeters, display, 6);
    }
}

public sealed class SurfaceMotionCoreTests
{
    [Fact]
    public void SurfaceWalk_FollowsFlatFloor()
    {
        var core = MotionCore.CreateDefaultSurface(AnalyticSupportField.Flat(0));
        Simulate(core, 60, 2.0, InputIntent.HoldForward);
        Assert.InRange(core.Pose.FootYMeters, 2.0, 2.7);
        Assert.Equal(0, core.Pose.FootZMeters, 6);
    }

    [Fact]
    public void Surface_QE_DoesNotChangeFootZ()
    {
        var core = MotionCore.CreateDefaultSurface(AnalyticSupportField.Flat(0));
        Simulate(core, 120, 1.0, InputIntent.None with { Up = true });
        Assert.Equal(0, core.Pose.FootZMeters, 9);
        Assert.Equal("F — полёт для смены уровня", core.HudHint);
    }

    [Fact]
    public void Surface_HoleStopsWithoutTeleport()
    {
        var field = AnalyticSupportField.FlatWithHole(0, -1, 1, 1.0, 3.0);
        var core = MotionCore.CreateDefaultSurface(field);
        Simulate(core, 120, 3.0, InputIntent.HoldForward);
        Assert.True(core.Pose.FootYMeters < 1.05);
        Assert.Equal(0, core.Pose.FootZMeters, 6);
    }

    [Fact]
    public void FlyReturn_RequiresLocalSupport()
    {
        var field = new MultiLevelSupportField([0.0, 3.0]);
        var pose = CameraPose.CreateDefault().WithFoot(new Vec3(0, 0, 1.5));
        var core = new MotionCore(pose, MovementMode.Fly, MotionDefaults.BaseSpeedMetersPerSecond, field);
        Assert.False(core.TryAttachSurface());
        Assert.Equal(MovementMode.Fly, core.Mode);

        core.SetFootMeters(new Vec3(0, 0, 0.05));
        Assert.True(core.TryAttachSurface());
        Assert.Equal(MovementMode.Surface, core.Mode);
        Assert.Equal(0, core.Pose.FootZMeters, 6);
    }

    [Fact]
    public void RenderPose_LagsPhysicalEyeOnStep()
    {
        var field = AnalyticSupportField.Step(0, 0.20, 0.5);
        var core = new MotionCore(
            CameraPose.CreateDefault(),
            MovementMode.Surface,
            MotionDefaults.BaseSpeedMetersPerSecond,
            field,
            eyeSmoothing: true);
        // Jump foot onto step in one resolve via set+advance small move
        core.SetFootMeters(new Vec3(0, 0.4, 0));
        Simulate(core, 120, 0.5, InputIntent.HoldForward);
        Assert.True(core.Pose.FootZMeters > 0.15);
        Assert.True(core.DisplayEyeZMeters < core.Pose.EyeMeters.Z + 1e-9);
        Assert.True(core.Pose.EyeMeters.Z - core.DisplayEyeZMeters <= MotionDefaults.EyeSmoothMaxLagMeters + 1e-9);
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
