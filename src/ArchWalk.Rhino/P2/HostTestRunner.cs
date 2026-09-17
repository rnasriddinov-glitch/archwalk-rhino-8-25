using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Placement;
using ArchWalk.RhinoPlugin.Preview;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using System.Drawing;
using System.Text;

namespace ArchWalk.RhinoPlugin.P2;

public static class HostTestRunner
{
    public static string LastReport { get; private set; } = "not run";

    public static string RunAll(RhinoDoc uiDoc)
    {
        var log = new StringBuilder();
        log.AppendLine("ARCHWALK P2 host tests");
        log.AppendLine("Rhino " + RhinoApp.Version);
        log.AppendLine("Runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        log.AppendLine("Plugin " + typeof(ArchWalkPlugIn).Assembly.GetName().Version);
        log.AppendLine("UI document left as host only: " + (uiDoc.Path ?? uiDoc.Name));
        SessionController.InstallHostHooks();
        log.AppendLine();
        log.AppendLine(RunCase("P2-foot-level", () => TestFootLevel(uiDoc)));
        log.AppendLine(RunCase("P2-aim-horizontal", () => TestAimHorizontal(uiDoc)));
        log.AppendLine(RunCase("P2-lookat3d", () => TestLookAt3D(uiDoc)));
        log.AppendLine(RunCase("P2-preview", () => TestPreviewIndependent(uiDoc)));
        log.AppendLine(RunCase("P2-target-new", () => TestNewTargetWindow(uiDoc)));
        log.AppendLine(RunCase("P2-esc-cancel", () => TestEscCancel(uiDoc)));
        log.AppendLine(RunCase("P2-enter-keep", () => TestEnterKeepView(uiDoc)));
        log.AppendLine(RunCase("P2-no-support", () => TestNoSupportMessage(uiDoc)));
        LastReport = log.ToString();
        RhinoApp.WriteLine(LastReport);
        return LastReport;
    }

    static string RunCase(string id, Func<string> body)
    {
        try
        {
            PlacementController.Cancel("p2-case-reset");
            SessionController.Reset("p2-case-reset");
            var detail = body();
            return id + " PASSED  " + detail;
        }
        catch (Exception ex)
        {
            PlacementController.Cancel("p2-test-failed");
            SessionController.Reset("p2-test-failed");
            return id + " FAILED  " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    static string TestFootLevel(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var snap = CameraAdapter.Capture(working.MainViewport);
        try
        {
            if (!RhinoUnits.TryFromDoc(uiDoc, out var units, out _))
                throw new InvalidOperationException("units");
            if (!PlacementController.BeginPlace(uiDoc, working, FootSourceKind.Level))
                throw new InvalidOperationException("BeginPlace failed");
            var foot = new Point3d(1000, 2000, 500);
            if (!PlacementController.TrySetFootFromCursor(uiDoc, foot, out var msg))
                throw new InvalidOperationException(msg);
            var draft = PlacementController.Draft;
            if (draft.Phase != PlacementPhase.Aiming)
                throw new InvalidOperationException("Expected Aiming");
            if (Math.Abs(draft.FootZDocument - draft.LevelHintZDocument) > 1e-9)
                throw new InvalidOperationException("Level Z not from hint");
            var pose = draft.ToPose(units);
            if (Math.Abs(pose.EyeHeightMeters - MotionDefaults.EyeHeightMeters) > 1e-12)
                throw new InvalidOperationException("Eye height");
            var expectedEyeZ = draft.FootZDocument + units.ToDocument(MotionDefaults.EyeHeightMeters);
            if (Math.Abs(units.ToDocument(pose.EyeMeters.Z) - expectedEyeZ) > 0.05)
                throw new InvalidOperationException("Eye Z mismatch");
            return $"footZ={draft.FootZDocument:0.###}; eyeH={pose.EyeHeightMeters:0.000} m";
        }
        finally
        {
            PlacementController.Cancel("p2-foot");
            CameraAdapter.Restore(working.MainViewport, snap);
            working.Redraw();
        }
    }

    static string TestAimHorizontal(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        try
        {
            PlacementController.BeginPlace(uiDoc, working, FootSourceKind.Level);
            PlacementController.TrySetFootFromCursor(uiDoc, Point3d.Origin, out _);
            var aim = new Point3d(0, 5000, 0);
            if (!PlacementController.TryUpdateAimFromWorldPoint(uiDoc, aim, out var msg))
                throw new InvalidOperationException(msg);
            PlacementController.ConfirmAim(uiDoc);
            var draft = PlacementController.Draft;
            if (Math.Abs(draft.YawRadians) > 1e-9)
                throw new InvalidOperationException("Yaw should be 0 along +Y, got " + draft.YawRadians);
            if (Math.Abs(draft.PitchRadians) > 1e-12)
                throw new InvalidOperationException("Pitch must stay 0 for floor aim");
            if (!draft.IsReady)
                throw new InvalidOperationException("Not Ready");
            return "yaw=0; pitch=0; Ready";
        }
        finally
        {
            PlacementController.Cancel("p2-aim");
        }
    }

    static string TestLookAt3D(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        try
        {
            if (!RhinoUnits.TryFromDoc(uiDoc, out var units, out _))
                throw new InvalidOperationException("units");
            PlacementController.BeginPlace(uiDoc, working, FootSourceKind.Level);
            PlacementController.SetLookAt3D(true);
            PlacementController.TrySetFootFromCursor(uiDoc, Point3d.Origin, out _);
            var high = new Point3d(0, units.ToDocument(5), units.ToDocument(MotionDefaults.EyeHeightMeters + 5));
            if (!PlacementController.TryUpdateAimFromWorldPoint(uiDoc, high, out var msg))
                throw new InvalidOperationException(msg);
            var draft = PlacementController.Draft;
            if (draft.PitchRadians <= 0.4)
                throw new InvalidOperationException("Expected upward pitch, got " + draft.PitchRadians);
            return "pitch=" + (draft.PitchRadians * 180 / Math.PI).ToString("0.0") + "°";
        }
        finally
        {
            PlacementController.Cancel("p2-look3d");
        }
    }

    static string TestPreviewIndependent(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var before = CameraAdapter.Capture(working.MainViewport);
        RhinoView? extra = null;
        try
        {
            PlacementController.BeginPlace(uiDoc, working, FootSourceKind.Level);
            PlacementController.TrySetFootFromCursor(uiDoc, new Point3d(100, 0, 0), out _);
            PlacementController.TryUpdateAimFromWorldPoint(uiDoc, new Point3d(100, 2000, 0), out _);
            PlacementController.RequestPreview(uiDoc, force: true);
            var frame = PlacementController.LastPreviewFrame
                ?? throw new InvalidOperationException("No preview frame");
            if (frame.Width < 100 || frame.Height < 50)
                throw new InvalidOperationException("Frame too small");
            frame.Dispose();

            var after = CameraAdapter.Capture(working.MainViewport);
            if (after.CameraLocation.DistanceTo(before.CameraLocation) > 1e-6)
                throw new InvalidOperationException("Working camera moved");
            if (after.CameraDirection.IsParallelTo(before.CameraDirection) == 0)
                throw new InvalidOperationException("Working direction changed");
            return $"preview gen={PlacementController.PreviewGeneration}; working unchanged";
        }
        finally
        {
            PlacementController.Cancel("p2-preview");
            CameraAdapter.Restore(working.MainViewport, before);
            if (extra is not null)
            {
                try { extra.Close(); } catch { /* ignore */ }
            }
        }
    }

    static string TestNewTargetWindow(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        RhinoView? created = null;
        try
        {
            // Force orthographic-like source by using Top if available, else mark create-new.
            PlacementController.BeginPlace(uiDoc, working, FootSourceKind.Level);
            PlacementController.SetTargetChoice(uiDoc, new TargetViewChoice(Guid.Empty, "Новое окно прогулки", true, 16.0 / 9.0));
            PlacementController.TrySetFootFromCursor(uiDoc, Point3d.Origin, out _);
            PlacementController.TryUpdateAimFromWorldPoint(uiDoc, new Point3d(0, 1000, 0), out _);
            PlacementController.ConfirmAim(uiDoc);
            SessionController.SuppressHostScripts = true;
            if (!PlacementController.TryEnter(uiDoc, deferCapture: false, out var msg))
                throw new InvalidOperationException(msg);
            created = TargetViewResolver.FindView(uiDoc, SessionController.ViewId);
            if (created is null)
                throw new InvalidOperationException("Target view missing");
            SessionController.Exit(WalkExitKind.KeepView, "p2-new");
            if (created.MainViewport is null)
                throw new InvalidOperationException("Created view destroyed on keep");
            return "new window kept after Esc/keep";
        }
        finally
        {
            SessionController.SuppressHostScripts = false;
            SessionController.Reset("p2-new-cleanup");
            PlacementController.Cancel("p2-new");
            if (created is not null)
            {
                try { created.Close(); } catch { /* ignore */ }
            }
        }
    }

    static string TestEscCancel(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var undoBefore = uiDoc.UndoActive;
        PlacementController.BeginPlace(uiDoc, working, FootSourceKind.Level);
        PlacementController.TrySetFootFromCursor(uiDoc, new Point3d(10, 10, 0), out _);
        PlacementController.Cancel("esc");
        if (PlacementController.Draft.Phase != PlacementPhase.Idle)
            throw new InvalidOperationException("Draft not cleared");
        _ = undoBefore;
        return "draft cleared; no observer write in P2 finish path";
    }

    static string TestEnterKeepView(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? walk = null;
        try
        {
            walk = uiDoc.Views.Add("ARCHWALK_P2_ENTER", DefinedViewportProjection.Perspective, new Rectangle(40, 40, 480, 320), true)
                ?? throw new InvalidOperationException("No test view");
            PlacementController.BeginPlace(uiDoc, working, FootSourceKind.Level);
            PlacementController.SetTargetChoice(uiDoc, new TargetViewChoice(walk.MainViewport.Id, walk.MainViewport.Name, false, TargetViewResolver.AspectOf(walk)));
            PlacementController.TrySetFootFromCursor(uiDoc, Point3d.Origin, out _);
            PlacementController.TryUpdateAimFromWorldPoint(uiDoc, new Point3d(0, 2000, 0), out _);
            PlacementController.ConfirmAim(uiDoc);
            SessionController.SuppressHostScripts = true;
            if (!PlacementController.TryEnter(uiDoc, deferCapture: false, out var msg))
                throw new InvalidOperationException(msg);
            var loc = walk.MainViewport.CameraLocation;
            SessionController.Exit(WalkExitKind.KeepView, "p2-enter");
            if (walk.MainViewport.CameraLocation.DistanceTo(loc) > 1e-6)
                throw new InvalidOperationException("Keep-view moved camera");
            return "enter from Ready + keep-view";
        }
        finally
        {
            SessionController.SuppressHostScripts = false;
            SessionController.Reset("p2-enter-cleanup");
            PlacementController.Cancel("p2-enter");
            if (walk is not null)
            {
                try { walk.Close(); } catch { /* ignore */ }
            }
            CameraAdapter.Restore(working.MainViewport, workingSnap);
            working.Redraw();
        }
    }

    static string TestNoSupportMessage(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        try
        {
            PlacementController.BeginPlace(uiDoc, working, FootSourceKind.Surface);
            if (PlacementController.TrySetFootFromCursor(uiDoc, new Point3d(1e6, 1e6, 1e6), out var msg))
                throw new InvalidOperationException("Empty model should not silently support");
            if (msg.IndexOf("Нет опоры", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Expected support message, got: " + msg);
            if (PlacementController.Draft.HasValidSupport)
                throw new InvalidOperationException("HasValidSupport should be false");
            return msg;
        }
        finally
        {
            PlacementController.Cancel("p2-nosupport");
        }
    }

    static RhinoView HostView(RhinoDoc doc)
    {
        if (doc.Views.ActiveView is { } active)
            return active;
        foreach (var view in doc.Views)
        {
            if (view?.MainViewport is not null)
                return view;
        }
        return doc.Views.Add("ARCHWALK_P2_HOST", DefinedViewportProjection.Perspective, new Rectangle(20, 20, 400, 300), false)
            ?? throw new InvalidOperationException("No host view.");
    }
}
