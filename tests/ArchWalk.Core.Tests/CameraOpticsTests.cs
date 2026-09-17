using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using Xunit;

namespace ArchWalk.Core.Tests;

public sealed class CameraOpticsTests
{
    const double Deg = System.Math.PI / 180.0;

    [Fact]
    public void YawZero_LooksAlongPositiveY()
    {
        var pose = CameraPose.CreateDefault();
        var basis = pose.Basis();
        Assert.Equal(0, basis.Forward.X, 12);
        Assert.Equal(1, basis.Forward.Y, 12);
        Assert.Equal(0, basis.Forward.Z, 12);
        Assert.Equal(1, basis.Right.X, 12);
        Assert.Equal(0, basis.Right.Y, 12);
        Assert.Equal(0, basis.Up.X, 12);
        Assert.Equal(0, basis.Up.Y, 12);
        Assert.Equal(1, basis.Up.Z, 12);
    }

    [Fact]
    public void FiveFullTurns_KeepZeroRollAndSameEye()
    {
        var pose = CameraPose.CreateDefault();
        pose = pose.WithLook(pose.YawRadians + (10 * System.Math.PI), 0);
        var basis = pose.Basis();
        Assert.Equal(0, basis.Up.X, 10);
        Assert.Equal(0, basis.Up.Y, 10);
        Assert.Equal(1, basis.Up.Z, 10);
        Assert.Equal(0, CameraPose.NormalizeYaw(10 * System.Math.PI), 12);
        Assert.Equal(CameraPose.CreateDefault().EyeMeters, pose.EyeMeters);
    }

    [Fact]
    public void PitchIsClamped()
    {
        var pose = CameraPose.CreateDefault().WithLook(0, 2);
        Assert.Equal(MotionDefaults.MaxPitchRadians, pose.PitchRadians, 12);
        pose = pose.WithLook(0, -2);
        Assert.Equal(MotionDefaults.MinPitchRadians, pose.PitchRadians, 12);
    }

    [Fact]
    public void WideAspect_HorizontalFovMatchesExample()
    {
        var v = 45 * Deg;
        var h = CameraOptics.HorizontalFovRadians(v, 16.0 / 9.0);
        Assert.Equal(72.73, h / Deg, 1);
    }

    [Fact]
    public void RhinoCameraAngle_UsesHalfOfSmallerAngle()
    {
        var v = 45 * Deg;
        Assert.Equal(v / 2.0, CameraOptics.RhinoHalfSmallerAngleRadians(v, 16.0 / 9.0), 12);
        var tall = CameraOptics.RhinoHalfSmallerAngleRadians(v, 9.0 / 16.0);
        var h = CameraOptics.HorizontalFovRadians(v, 9.0 / 16.0);
        Assert.Equal(h / 2.0, tall, 12);
        Assert.True(tall < v / 2.0);
    }

    [Fact]
    public void LookingStraightUp_StillHasHorizontalWalkDirection()
    {
        var pose = CameraPose.CreateDefault().WithLook(0, MotionDefaults.MaxPitchRadians);
        var basis = pose.Basis();
        Assert.Equal(0, basis.Horizontal.X, 12);
        Assert.Equal(1, basis.Horizontal.Y, 12);
        Assert.Equal(0, basis.Horizontal.Z, 12);
        Assert.True(basis.Forward.Z > 0.99);
    }
}
