using System.Diagnostics;
using ArchWalk.Core.Camera;
using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Support;
using Xunit;

namespace ArchWalk.Core.Tests;

public sealed class MotionPerfTests
{
    [Fact]
    public void MotionTick_P95UnderTwoMilliseconds()
    {
        var core = MotionCore.CreateDefaultLevel();
        for (var i = 0; i < 60; i++)
            core.Advance(1.0 / 60.0, InputIntent.HoldForward);

        var samples = new double[500];
        for (var i = 0; i < samples.Length; i++)
        {
            var sw = Stopwatch.StartNew();
            core.Advance(1.0 / 60.0, InputIntent.HoldForward);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var p95 = samples[(int)(samples.Length * 0.95)];
        Assert.True(p95 <= 2.0, "motion p95=" + p95 + "ms");
    }

    [Fact]
    public void AnalyticSupport_P95UnderThreeMilliseconds()
    {
        var field = AnalyticSupportField.Flat();
        for (var i = 0; i < 50; i++)
            SurfaceNavigator.TryMove(field, new Vec3(0, 0, 0), i * 0.01, 0, 0.001);

        var samples = new double[400];
        for (var i = 0; i < samples.Length; i++)
        {
            var sw = Stopwatch.StartNew();
            SurfaceNavigator.TryMove(field, new Vec3(i * 0.01, 0, 0), i * 0.01 + 0.1, 0, 0.001);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var p95 = samples[(int)(samples.Length * 0.95)];
        Assert.True(p95 <= 3.0, "support p95=" + p95 + "ms");
    }

    [Fact]
    public void LargeWorldCoordinates_NoHorizontalDrift()
    {
        var pose = new CameraPose(1_000_000, 2_000_000, 0, MotionDefaults.EyeHeightMeters, 0, 0, MotionDefaults.VerticalFovRadians);
        var core = new MotionCore(pose, MovementMode.Level, MotionDefaults.BaseSpeedMetersPerSecond, null, 0.001);
        for (var i = 0; i < 600; i++)
            core.Advance(1.0 / 60.0, InputIntent.HoldForward);
        Assert.InRange(core.Pose.FootYMeters - 2_000_000, 12.87, 13.13);
        Assert.Equal(1_000_000, core.Pose.FootXMeters, 6);
    }
}
