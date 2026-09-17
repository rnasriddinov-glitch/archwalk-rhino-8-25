using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Units;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ArchWalk.RhinoPlugin.Camera;

public sealed class ViewportSnapshot
{
    public ViewportSnapshot(ViewportInfo projection, Guid displayModeId, string viewName, Point3d cameraLocation, Vector3d cameraDirection, Vector3d cameraUp, Point3d cameraTarget)
    {
        Projection = projection;
        DisplayModeId = displayModeId;
        ViewName = viewName;
        CameraLocation = cameraLocation;
        CameraDirection = cameraDirection;
        CameraUp = cameraUp;
        CameraTarget = cameraTarget;
    }

    public ViewportInfo Projection { get; }
    public Guid DisplayModeId { get; }
    public string ViewName { get; }
    public Point3d CameraLocation { get; }
    public Vector3d CameraDirection { get; }
    public Vector3d CameraUp { get; }
    public Point3d CameraTarget { get; }
}

public static class CameraAdapter
{
    public static ViewportSnapshot Capture(RhinoViewport vp)
    {
        var modeId = vp.DisplayMode?.Id ?? Guid.Empty;
        return new ViewportSnapshot(
            new ViewportInfo(vp),
            modeId,
            vp.Name,
            vp.CameraLocation,
            vp.CameraDirection,
            vp.CameraUp,
            vp.CameraTarget);
    }

    public static bool Restore(RhinoViewport vp, ViewportSnapshot snapshot)
    {
        var ok = vp.SetViewProjection(snapshot.Projection, true);
        if (snapshot.DisplayModeId != Guid.Empty)
        {
            var mode = DisplayModeDescription.GetDisplayMode(snapshot.DisplayModeId);
            if (mode is not null)
                vp.DisplayMode = mode;
        }
        return ok;
    }

    public static bool ApplyPose(RhinoViewport vp, CameraPose pose, DocumentUnits units, double? aspectOverride = null)
    {
        var basis = pose.Basis();
        var eye = pose.EyeMeters;
        var loc = new Point3d(units.ToDocument(eye.X), units.ToDocument(eye.Y), units.ToDocument(eye.Z));
        var dir = new Vector3d(basis.Forward.X, basis.Forward.Y, basis.Forward.Z);
        var up = new Vector3d(basis.Up.X, basis.Up.Y, basis.Up.Z);
        if (!dir.Unitize() || !up.Unitize())
            return false;

        var targetDistance = units.ToDocument(MotionDefaults.CameraTargetDistanceMeters);
        var info = new ViewportInfo(vp);
        if (!info.IsPerspectiveProjection || info.IsTwoPointPerspectiveProjection)
            info.ChangeToPerspectiveProjection(targetDistance, true, 50);

        info.SetCameraLocation(loc);
        info.SetCameraDirection(dir);
        info.SetCameraUp(up);

        info.GetFrustum(out _, out _, out _, out _, out var nearDistance, out var farDistance);
        if (nearDistance <= 0)
            nearDistance = 0.1;
        var aspect = aspectOverride
            ?? (vp.Size.Height > 0 ? vp.Size.Width / (double)vp.Size.Height : 0);
        if (aspect <= 1e-9)
            aspect = info.FrustumAspect > 1e-9 ? info.FrustumAspect : 16.0 / 9.0;
        CameraOptics.PerspectiveFrustum(pose.VerticalFovRadians, aspect, nearDistance, out var left, out var right, out var bottom, out var top);
        if (!info.SetFrustum(left, right, bottom, top, nearDistance, farDistance))
            return false;

        if (!vp.SetViewProjection(info, true))
            return false;

        var target = loc + (dir * targetDistance);
        var locBeforeTarget = vp.CameraLocation;
        vp.SetCameraTarget(target, false);
        var locAfterTarget = vp.CameraLocation;
        return locBeforeTarget.DistanceTo(locAfterTarget) < 1e-6;
    }

    public static FovSample ReadFov(RhinoViewport vp)
    {
        vp.GetCameraAngle(out var halfDiag, out var halfVertical, out var halfHorizontal);
        return new FovSample(halfDiag, halfVertical, halfHorizontal, vp.FrustumAspect);
    }
}

public readonly record struct FovSample(
    double HalfDiagonalRadians,
    double HalfVerticalRadians,
    double HalfHorizontalRadians,
    double Aspect);
