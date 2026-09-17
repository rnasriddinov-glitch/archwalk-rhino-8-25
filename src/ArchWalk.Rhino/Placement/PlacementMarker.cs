using ArchWalk.Core.Motion;
using ArchWalk.Core.Placement;
using ArchWalk.Core.Units;
using Rhino.Display;
using Rhino.Geometry;
using System.Drawing;

namespace ArchWalk.RhinoPlugin.Placement;

public static class PlacementMarker
{
    public static void Draw(DisplayPipeline display, Point3d foot, PlacementDraft draft, DocumentUnits units, bool valid)
    {
        var ok = valid ? Color.FromArgb(220, 80, 180, 80) : Color.FromArgb(220, 200, 60, 40);
        var accent = valid ? Color.Gold : Color.OrangeRed;
        var eyeHeight = units.ToDocument(draft.EyeHeightMeters);
        var eye = new Point3d(foot.X, foot.Y, foot.Z + eyeHeight);
        var ringR = units.ToDocument(0.18);
        display.DrawCircle(new Circle(new Plane(foot, Vector3d.ZAxis), ringR), accent, 2);
        display.DrawPoint(foot, PointStyle.RoundSimple, 4, accent);

        display.DrawLine(foot, eye, ok, 2);
        var shoulder = units.ToDocument(0.22);
        var yaw = draft.HasFoot ? draft.YawRadians : 0;
        var right = new Vector3d(System.Math.Cos(yaw), -System.Math.Sin(yaw), 0);
        if (!right.Unitize())
            right = Vector3d.XAxis;
        var shoulderZ = foot.Z + (eyeHeight * 0.72);
        var shoulderCenter = new Point3d(foot.X, foot.Y, shoulderZ);
        display.DrawLine(shoulderCenter - (right * shoulder), shoulderCenter + (right * shoulder), ok, 2);
        display.DrawPoint(eye, PointStyle.RoundSimple, 3, accent);

        if (!draft.HasFoot)
            return;

        var arrowLen = units.ToDocument(MotionDefaults.ArrowLengthMeters);
        var sectorLen = units.ToDocument(MotionDefaults.FrustumGizmoLengthMeters);
        var pose = draft.ToPose(units);
        var basis = pose.Basis();
        var forward = new Vector3d(basis.Horizontal.X, basis.Horizontal.Y, basis.Horizontal.Z);
        if (!forward.Unitize())
            return;
        var tip = foot + (forward * arrowLen);
        display.DrawArrow(new Line(foot, tip), accent, 12, 0);

        var aspect = draft.AspectWidthOverHeight > 0.05 ? draft.AspectWidthOverHeight : 16.0 / 9.0;
        var hfov = PlacementMath.HorizontalFovForAspect(draft.VerticalFovRadians, aspect);
        PlacementMath.SectorEndpoints(
            new ArchWalk.Core.Math.Vec3(units.ToMeters(foot.X), units.ToMeters(foot.Y), units.ToMeters(foot.Z)),
            draft.YawRadians,
            hfov,
            MotionDefaults.FrustumGizmoLengthMeters,
            out var leftM,
            out var rightM,
            out var centerM);

        var left = ToPoint(leftM, units);
        var rightPt = ToPoint(rightM, units);
        var center = ToPoint(centerM, units);
        display.DrawLine(foot, left, Color.FromArgb(160, 100, 180, 255), 1);
        display.DrawLine(foot, rightPt, Color.FromArgb(160, 100, 180, 255), 1);
        display.DrawLine(left, center, Color.FromArgb(120, 100, 180, 255), 1);
        display.DrawLine(rightPt, center, Color.FromArgb(120, 100, 180, 255), 1);

        if (draft.LookAt3D && System.Math.Abs(draft.PitchRadians) > 1e-4)
        {
            var look = new Vector3d(basis.Forward.X, basis.Forward.Y, basis.Forward.Z);
            if (look.Unitize())
                display.DrawLine(eye, eye + (look * sectorLen), Color.Cyan, 1);
        }

        var label = (draft.FootSource == FootSourceKind.Level ? "По отметке" : "На поверхности")
            + "  H=" + draft.EyeHeightMeters.ToString("0.000") + " м"
            + "  Z=" + foot.Z.ToString("0.###");
        display.Draw2dText(label, accent, new Point2d(16, 28), false, 12);
    }

    static Point3d ToPoint(ArchWalk.Core.Math.Vec3 meters, DocumentUnits units) =>
        new(units.ToDocument(meters.X), units.ToDocument(meters.Y), units.ToDocument(meters.Z));
}
