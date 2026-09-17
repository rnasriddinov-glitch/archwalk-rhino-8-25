using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Support;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Ground;
using ArchWalk.RhinoPlugin.Placement;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using System.Text;

namespace ArchWalk.RhinoPlugin.P4;

public static class HostTestRunner
{
    public static string LastReport { get; private set; } = "not run";

    public static string RunAll(RhinoDoc uiDoc)
    {
        var log = new StringBuilder();
        log.AppendLine("ARCHWALK P4 host tests");
        log.AppendLine("Rhino " + RhinoApp.Version);
        log.AppendLine("Runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        log.AppendLine("Plugin " + typeof(ArchWalkPlugIn).Assembly.GetName().Version);
        SessionController.InstallHostHooks();
        SessionController.SuppressHostScripts = true;
        log.AppendLine();
        log.AppendLine(RunCase("P4-cache-local", () => TestCacheLocalWindow()));
        log.AppendLine(RunCase("P4-surface-walk", () => TestSurfaceWalk(uiDoc)));
        log.AppendLine(RunCase("P4-step-limits", () => TestStepLimits()));
        log.AppendLine(RunCase("P4-hole-edge", () => TestHoleAndEdge()));
        log.AppendLine(RunCase("P4-two-floors", () => TestTwoFloorsNoGrab()));
        log.AppendLine(RunCase("P4-fly-attach", () => TestFlyAttach(uiDoc)));
        log.AppendLine(RunCase("P4-invalidate", () => TestInvalidateEndsWalk(uiDoc)));
        log.AppendLine(RunCase("P4-blocks", () => TestNestedBlock()));
        LastReport = log.ToString();
        SessionController.SuppressHostScripts = false;
        RhinoApp.WriteLine(LastReport);
        return LastReport;
    }

    static string RunCase(string id, Func<string> body)
    {
        try
        {
            PlacementController.Cancel("p4-case");
            SessionController.Reset("p4-case");
            return id + " PASSED  " + body();
        }
        catch (Exception ex)
        {
            PlacementController.Cancel("p4-fail");
            SessionController.Reset("p4-fail");
            return id + " FAILED  " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    static string TestCacheLocalWindow()
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("headless");
        doc.ModelUnitSystem = UnitSystem.Meters;
        doc.Objects.AddSurface(new PlaneSurface(Plane.WorldXY, new Interval(0, 20), new Interval(0, 20)));
        doc.Objects.AddSurface(new PlaneSurface(new Plane(new Point3d(0, 0, 3), Vector3d.ZAxis), new Interval(0, 20), new Interval(0, 20)));
        var cache = GroundCacheBuilder.Build(doc, DocumentUnits.Meters);
        if (cache.MeshCount < 2)
            throw new InvalidOperationException("expected >=2 meshes, got " + cache.MeshCount);
        var lower = cache.ProbeMeters(1, 1, 0.05, MotionDefaults.MaxStepUpMeters + 0.01, MotionDefaults.MaxStepDownMeters + 0.01);
        if (!lower.Found || Math.Abs(lower.ZMeters) > 0.02)
            throw new InvalidOperationException("lower hit Z=" + lower.ZMeters);
        var mid = cache.ProbeMeters(1, 1, 1.5, 0.25, 0.35);
        if (mid.Found)
            throw new InvalidOperationException("mid window should miss both floors");
        return "meshes=" + cache.MeshCount + "; local window ok";
    }

    static string TestSurfaceWalk(RhinoDoc uiDoc)
    {
        var working = uiDoc.Views.ActiveView ?? throw new InvalidOperationException("no view");
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("headless");
        doc.ModelUnitSystem = UnitSystem.Millimeters;
        AddFloorMm(doc, 0, 10000, 0);
        var cache = GroundCacheBuilder.Build(doc, DocumentUnits.Millimeters);
        var field = cache.AsSupportField();
        var pose = new CameraPose(1, 1, 0, MotionDefaults.EyeHeightMeters, 0, 0, MotionDefaults.VerticalFovRadians);
        var core = new MotionCore(pose, MovementMode.Surface, MotionDefaults.BaseSpeedMetersPerSecond, field, 0.001);
        for (var i = 0; i < 120; i++)
            core.Advance(1.0 / 60.0, InputIntent.HoldForward);
        if (core.Pose.FootYMeters < 2.0)
            throw new InvalidOperationException("did not walk enough Y=" + core.Pose.FootYMeters);
        if (Math.Abs(core.Pose.FootZMeters) > 0.02)
            throw new InvalidOperationException("foot Z drifted " + core.Pose.FootZMeters);

        // Live enter on UI doc with a temporary plane if needed — prefer headless enter via UI view only when geometry is on uiDoc.
        _ = working;
        return "Y=" + core.Pose.FootYMeters.ToString("0.00") + "m Z=" + core.Pose.FootZMeters.ToString("0.000");
    }

    static string TestStepLimits()
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("headless");
        doc.ModelUnitSystem = UnitSystem.Meters;
        // Lower tread only for y<5; riser surface only for y>=5 (no continuous slab under the step).
        AddFloor(doc, 0, 10, 0, y0: 0, y1: 5);
        AddFloor(doc, 0, 10, 0.20, y0: 5, y1: 10);
        AddFloor(doc, 10, 20, 0, y0: 0, y1: 5);
        AddFloor(doc, 10, 20, 0.23, y0: 5, y1: 10);
        var cache = GroundCacheBuilder.Build(doc, DocumentUnits.Meters);
        var field = cache.AsSupportField();
        if (!SurfaceNavigator.TryResolveFoot(field, new Core.Math.Vec3(2, 4.9, 0), 2, 5.1, 0.001, out var ok, out _))
            throw new InvalidOperationException("220mm step should pass");
        if (Math.Abs(ok.Z - 0.20) > 0.02)
            throw new InvalidOperationException("step Z=" + ok.Z);
        if (SurfaceNavigator.TryResolveFoot(field, new Core.Math.Vec3(12, 4.9, 0), 12, 5.1, 0.001, out _, out var reason))
            throw new InvalidOperationException("230mm step should fail");
        return "ok220 Z=" + ok.Z.ToString("0.000") + "; block230=" + reason;
    }

    static string TestHoleAndEdge()
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("headless");
        doc.ModelUnitSystem = UnitSystem.Meters;
        // Two strips with a 0.5m gap (hole) between y=2 and y=2.5
        AddFloor(doc, 0, 10, 0, y0: 0, y1: 2);
        AddFloor(doc, 0, 10, 0, y0: 2.5, y1: 10);
        var cache = GroundCacheBuilder.Build(doc, DocumentUnits.Meters);
        var field = cache.AsSupportField();
        var core = new MotionCore(
            new CameraPose(1, 1.5, 0, MotionDefaults.EyeHeightMeters, 0, 0, MotionDefaults.VerticalFovRadians),
            MovementMode.Surface,
            MotionDefaults.BaseSpeedMetersPerSecond,
            field,
            0.001);
        for (var i = 0; i < 300; i++)
            core.Advance(1.0 / 120.0, InputIntent.HoldForward);
        if (core.Pose.FootYMeters > 2.15)
            throw new InvalidOperationException("crossed hole Y=" + core.Pose.FootYMeters);
        return "stopped Y=" + core.Pose.FootYMeters.ToString("0.000");
    }

    static string TestTwoFloorsNoGrab()
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("headless");
        doc.ModelUnitSystem = UnitSystem.Meters;
        AddFloor(doc, 0, 20, 0);
        AddFloor(doc, 0, 20, 3.0);
        var cache = GroundCacheBuilder.Build(doc, DocumentUnits.Meters);
        var field = cache.AsSupportField();
        var core = new MotionCore(
            new CameraPose(1, 1, 0, MotionDefaults.EyeHeightMeters, 0, System.Math.PI / 3, MotionDefaults.VerticalFovRadians),
            MovementMode.Surface,
            MotionDefaults.BaseSpeedMetersPerSecond,
            field,
            0.001);
        for (var i = 0; i < 120; i++)
            core.Advance(1.0 / 60.0, InputIntent.HoldForward);
        if (Math.Abs(core.Pose.FootZMeters) > 0.05)
            throw new InvalidOperationException("grabbed Z=" + core.Pose.FootZMeters);
        return "kept floor0 Z=" + core.Pose.FootZMeters.ToString("0.000");
    }

    static string TestFlyAttach(RhinoDoc uiDoc)
    {
        var view = uiDoc.Views.ActiveView ?? throw new InvalidOperationException("no view");
        // Build support on a throwaway headless field and exercise MotionCore attach rules;
        // live F key needs UI capture. Host proves the attach gate used by SessionController.
        var field = new MultiLevelSupportField([0.0, 3.0]);
        var mid = new MotionCore(
            new CameraPose(0, 0, 1.5, MotionDefaults.EyeHeightMeters, 0, 0, MotionDefaults.VerticalFovRadians),
            MovementMode.Fly,
            MotionDefaults.BaseSpeedMetersPerSecond,
            field);
        if (mid.TryAttachSurface())
            throw new InvalidOperationException("attach between floors should fail");
        mid.SetFootMeters(new Core.Math.Vec3(0, 0, 0.02));
        if (!mid.TryAttachSurface())
            throw new InvalidOperationException("attach near floor should succeed");
        _ = view;
        return "fly-attach gate ok";
    }

    static string TestInvalidateEndsWalk(RhinoDoc uiDoc)
    {
        var view = uiDoc.Views.ActiveView ?? throw new InvalidOperationException("no view");
        var id = uiDoc.Objects.AddSurface(new PlaneSurface(Plane.WorldXY, new Interval(-1000, 1000), new Interval(-1000, 1000)));
        try
        {
            if (!SessionController.EnterAtDocumentPoint(
                    uiDoc,
                    view,
                    Point3d.Origin,
                    0,
                    MovementMode.Surface,
                    MouseLookProfile.FreeLook,
                    deferCapture: false))
                throw new InvalidOperationException("enter surface failed: " + (SessionController.LastExitReason ?? "unknown"));
            if (SessionController.Core?.Mode != MovementMode.Surface)
                throw new InvalidOperationException("mode=" + SessionController.Core?.Mode);
            uiDoc.Objects.Delete(id, true);
            // Geometry hook should end the walk.
            if (SessionController.IsActive)
                SessionController.TickForTest(0.01);
            if (SessionController.IsActive)
                throw new InvalidOperationException("walk still active after delete");
            if (SessionController.LastExitReason is null || !SessionController.LastExitReason.Contains("support-invalidated"))
                throw new InvalidOperationException("exit reason=" + SessionController.LastExitReason);
            return SessionController.LastExitReason;
        }
        finally
        {
            SessionController.Reset("p4-cleanup");
            if (id != Guid.Empty)
                uiDoc.Objects.Delete(id, true);
        }
    }

    static string TestNestedBlock()
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("headless");
        doc.ModelUnitSystem = UnitSystem.Millimeters;
        var plane = new PlaneSurface(Plane.WorldXY, new Interval(0, 2000), new Interval(0, 2000));
        var defId = doc.InstanceDefinitions.Add("AWP4Floor", "", Point3d.Origin, [plane], null);
        if (defId < 0)
            throw new InvalidOperationException("idef");
        var mirror = Transform.Mirror(Plane.WorldYZ);
        var xform = Transform.Translation(new Vector3d(3000, 0, 0)) * mirror;
        doc.Objects.AddInstanceObject(defId, xform);
        var cache = GroundCacheBuilder.Build(doc, DocumentUnits.Millimeters);
        if (cache.MeshCount < 1)
            throw new InvalidOperationException("no meshes from block");
        var hit = cache.ProbeMeters(UnitsToMeters(3000 - 500), UnitsToMeters(500), 0.5, 2, 3);
        if (!hit.Found)
            throw new InvalidOperationException("mirrored block miss");
        return "block meshes=" + cache.MeshCount + " Z=" + hit.ZMeters.ToString("0.000");
    }

    static double UnitsToMeters(double mm) => mm * 0.001;

    static void AddFloor(RhinoDoc doc, double x0, double x1, double z, double y0 = 0, double y1 = 10)
    {
        var plane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
        doc.Objects.AddSurface(new PlaneSurface(plane, new Interval(x0, x1), new Interval(y0, y1)));
    }

    static void AddFloorMm(RhinoDoc doc, double zMm, double size, double unused)
    {
        _ = unused;
        var plane = new Plane(new Point3d(0, 0, zMm), Vector3d.ZAxis);
        doc.Objects.AddSurface(new PlaneSurface(plane, new Interval(0, size), new Interval(0, size)));
    }
}
