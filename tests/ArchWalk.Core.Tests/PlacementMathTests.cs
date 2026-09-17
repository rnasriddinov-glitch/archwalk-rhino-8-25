using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Placement;
using Xunit;

namespace ArchWalk.Core.Tests;

public sealed class PlacementMathTests
{
    [Fact]
    public void FootPlaneAim_KeepsPitchZeroAndYawAlongPlusY()
    {
        var foot = new Vec3(0, 0, 0);
        Assert.True(PlacementMath.TryYawFromFootPlaneAim(foot, new Vec3(0, 2, 0), out var yaw));
        Assert.Equal(0, yaw, 12);
        var pose = PlacementMath.PoseFromDraft(foot, yaw, pitchRadians: 0);
        Assert.Equal(0, pose.PitchRadians, 12);
        Assert.Equal(MotionDefaults.EyeHeightMeters, pose.EyeHeightMeters);
    }

    [Fact]
    public void FootPlaneAim_RejectsNearlyCoincidentPoint()
    {
        Assert.False(PlacementMath.TryYawFromFootPlaneAim(new Vec3(1, 1, 0), new Vec3(1.01, 1.01, 0), out _));
    }

    [Fact]
    public void LookAtPoint_SetsPitchTowardRaisedTarget()
    {
        var eye = new Vec3(0, 0, MotionDefaults.EyeHeightMeters);
        Assert.True(PlacementMath.TryLookAtPoint(eye, new Vec3(0, 5, MotionDefaults.EyeHeightMeters + 5), out var yaw, out var pitch));
        Assert.Equal(0, yaw, 10);
        Assert.True(pitch > 0.5);
        Assert.True(pitch <= MotionDefaults.MaxPitchRadians + 1e-12);
    }

    [Fact]
    public void SectorEndpoints_AreSymmetricAboutYaw()
    {
        PlacementMath.SectorEndpoints(
            new Vec3(0, 0, 0),
            yawRadians: 0,
            horizontalFovRadians: System.Math.PI / 2,
            lengthMeters: 3,
            out var left,
            out var right,
            out var center);
        Assert.Equal(3, center.Y, 10);
        Assert.Equal(0, center.X, 10);
        Assert.Equal(-left.X, right.X, 10);
        Assert.Equal(left.Y, right.Y, 10);
    }
}
