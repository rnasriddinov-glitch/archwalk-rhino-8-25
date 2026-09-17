using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Input;
using ArchWalk.RhinoPlugin.Session;
using ArchWalk.WindowsInput;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using System.Text;

namespace ArchWalk.RhinoPlugin.P1;

public static class HostTestRunner
{
    public static string LastReport { get; private set; } = "not run";

    public static string RunAll(RhinoDoc uiDoc)
    {
        var log = new StringBuilder();
        log.AppendLine("ARCHWALK P1 host tests");
        log.AppendLine("Rhino " + RhinoApp.Version);
        log.AppendLine("Runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        log.AppendLine("Plugin " + typeof(ArchWalkPlugIn).Assembly.GetName().Version);
        log.AppendLine("UI document left as host only: " + (uiDoc.Path ?? uiDoc.Name));
        SessionController.InstallHostHooks();
        log.AppendLine();
        log.AppendLine(RunCase("P1-level", () => TestLevelWalk(uiDoc)));
        log.AppendLine(RunCase("P1-restore", () => TestBackspaceRestore(uiDoc)));
        log.AppendLine(RunCase("P1-pause", () => TestTabPause(uiDoc)));
        log.AppendLine(RunCase("P1-fly", () => TestFly(uiDoc)));
        log.AppendLine(RunCase("P1-command", () => TestForeignCommand(uiDoc)));
        log.AppendLine(RunCase("P1-rmb", () => TestRightButtonLook(uiDoc)));
        log.AppendLine(RunCase("P1-hitch", () => TestHitchPause(uiDoc)));
        log.AppendLine(RunCase("P1-chars", () => TestRussianCharsEaten(uiDoc)));
        log.AppendLine(RunCase("P1-close", () => TestViewClose(uiDoc)));
        LastReport = log.ToString();
        RhinoApp.WriteLine(LastReport);
        return LastReport;
    }

    static string RunCase(string id, Func<string> body)
    {
        try
        {
            var detail = body();
            return id + " PASSED  " + detail;
        }
        catch (Exception ex)
        {
            SessionController.SuppressHostScripts = false;
            SessionController.Reset("p1-test-failed");
            InputSession.Release("p1-test-failed");
            return id + " FAILED  " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    static string TestLevelWalk(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_LEVEL");
            if (!SessionController.EnterAtDocumentPoint(uiDoc, view, Point3d.Origin, 0, MovementMode.Level, MouseLookProfile.FreeLook, deferCapture: false))
                throw new InvalidOperationException("Enter failed.");
            if (SessionController.State != SessionState.Captured)
                throw new InvalidOperationException("Expected Captured, got " + SessionController.State);
            var loc = view.MainViewport.CameraLocation;
            if (!RhinoUnits.TryFromDoc(uiDoc, out var units, out _))
                throw new InvalidOperationException("Units.");
            var expectedZ = units.ToDocument(MotionDefaults.EyeHeightMeters);
            if (Math.Abs(loc.Z - expectedZ) > 0.05)
                throw new InvalidOperationException($"Eye Z {loc.Z} expected {expectedZ}.");

            HoldAndTick(ScanCodes.W, 1.0);
            var moved = SessionController.Core ?? throw new InvalidOperationException("No core.");
            if (moved.Pose.FootYMeters < 0.4)
                throw new InvalidOperationException("W did not advance +Y, footY=" + moved.Pose.FootYMeters);
            if (Math.Abs(moved.Pose.FootZMeters) > 1e-9)
                throw new InvalidOperationException("Level walk changed Z.");
            var afterWalk = view.MainViewport.CameraLocation;
            SessionController.Exit(WalkExitKind.KeepView, "p1-level");
            var kept = view.MainViewport.CameraLocation;
            if (kept.DistanceTo(afterWalk) > 1e-6)
                throw new InvalidOperationException("Esc/keep moved the walk camera.");
            if (SessionController.State != SessionState.Idle)
                throw new InvalidOperationException("Not Idle after keep-exit.");
            return $"eyeZ={expectedZ:0.###}; footY={moved.Pose.FootYMeters:0.000} m after 1s W; keep-view";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static string TestBackspaceRestore(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_RESTORE");
            view.MainViewport.ChangeToTwoPointPerspectiveProjection(50);
            var before = CameraAdapter.Capture(view.MainViewport);
            if (!SessionController.EnterAtDocumentPoint(uiDoc, view, new Point3d(100, 0, 0), 0, MovementMode.Level, MouseLookProfile.FreeLook, false))
                throw new InvalidOperationException("Enter failed.");
            HoldAndTick(ScanCodes.W, 0.5);
            if (view.MainViewport.IsTwoPointPerspectiveProjection)
                throw new InvalidOperationException("Walk stayed two-point.");
            SessionController.Exit(WalkExitKind.RestoreSnapshot, "p1-restore");
            if (!view.MainViewport.IsTwoPointPerspectiveProjection)
                throw new InvalidOperationException("Two-point was not restored.");
            var after = CameraAdapter.Capture(view.MainViewport);
            if (after.CameraLocation.DistanceTo(before.CameraLocation) > 0.1)
                throw new InvalidOperationException("Restore location drifted.");
            return "two-point snapshot restored after walk";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static string TestTabPause(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_PAUSE");
            SessionController.EnterAtDocumentPoint(uiDoc, view, Point3d.Origin, 0, MovementMode.Level, MouseLookProfile.FreeLook, false);
            var y = SessionController.Core!.Pose.FootYMeters;
            SessionController.Pause("p1-tab");
            if (SessionController.State != SessionState.Paused)
                throw new InvalidOperationException("Expected Paused.");
            if (SessionController.Bridge is not null)
                throw new InvalidOperationException("Bridge still captured on pause.");
            SessionController.TickForTest(1.0);
            if (Math.Abs(SessionController.Core!.Pose.FootYMeters - y) > 1e-12)
                throw new InvalidOperationException("Paused session still moved.");
            if (!SessionController.TryResume())
                throw new InvalidOperationException("Resume failed.");
            if (SessionController.State != SessionState.Captured)
                throw new InvalidOperationException("Expected Captured after resume.");
            SessionController.Exit(WalkExitKind.KeepView, "p1-pause");
            return "Tab pause stops motion; resume recaptures; keys must be pressed again";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static string TestFly(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_FLY");
            var pose = new CameraPose(0, 0, 0, MotionDefaults.EyeHeightMeters, 0, Math.PI / 4.0, MotionDefaults.VerticalFovRadians);
            if (!SessionController.Enter(uiDoc, view, pose, MovementMode.Fly, MouseLookProfile.FreeLook, false))
                throw new InvalidOperationException("Enter fly failed.");
            HoldAndTick(ScanCodes.W, 1.0);
            var core = SessionController.Core!;
            if (core.Mode != MovementMode.Fly)
                throw new InvalidOperationException("Mode not Fly.");
            if (core.Pose.EyeMeters.Z <= MotionDefaults.EyeHeightMeters + 0.2)
                throw new InvalidOperationException("Fly W did not follow pitched look.");
            Inject(ScanCodes.F, true);
            Inject(ScanCodes.F, false);
            SessionController.TickForTest(0);
            if (SessionController.Core!.Mode != MovementMode.Level)
                throw new InvalidOperationException("F did not return to Level.");
            SessionController.Exit(WalkExitKind.KeepView, "p1-fly");
            return "Fly follows look; F returns to Level";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static string TestForeignCommand(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_CMD");
            SessionController.EnterAtDocumentPoint(uiDoc, view, Point3d.Origin, 0, MovementMode.Level, MouseLookProfile.FreeLook, false);
            var loc = view.MainViewport.CameraLocation;
            SessionController.NotifyForeignCommand("Circle");
            if (SessionController.State != SessionState.Idle)
                throw new InvalidOperationException("Foreign command did not end session.");
            if (view.MainViewport.CameraLocation.DistanceTo(loc) > 1e-6)
                throw new InvalidOperationException("Foreign command exit moved camera.");
            return "begin-command ends walk and keeps view";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static string TestRightButtonLook(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_RMB");
            SessionController.EnterAtDocumentPoint(uiDoc, view, Point3d.Origin, 0, MovementMode.Level, MouseLookProfile.RightButton, false);
            var bridge = SessionController.Bridge ?? throw new InvalidOperationException("No bridge.");
            if (bridge.IsLooking)
                throw new InvalidOperationException("RMB profile looked without button.");
            bridge.AddLookPixels(100, 0);
            SessionController.TickForTest(0.1);
            if (Math.Abs(SessionController.Core!.Pose.YawRadians) > 1e-12)
                throw new InvalidOperationException("Look applied without RMB.");
            bridge.NotifyRightButton(true);
            if (!bridge.IsLooking)
                throw new InvalidOperationException("RMB down did not start look.");
            bridge.AddLookPixels(100, 0);
            SessionController.TickForTest(0);
            if (SessionController.Core.Pose.YawRadians <= 0)
                throw new InvalidOperationException("RMB look did not yaw.");
            bridge.NotifyRightButton(false);
            if (bridge.IsLooking)
                throw new InvalidOperationException("RMB up did not stop look.");
            SessionController.Exit(WalkExitKind.KeepView, "p1-rmb");
            return "look only while right button held";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static string TestHitchPause(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_HITCH");
            SessionController.EnterAtDocumentPoint(uiDoc, view, Point3d.Origin, 0, MovementMode.Level, MouseLookProfile.FreeLook, false);
            HoldAndTick(ScanCodes.W, 0.3);
            var y = SessionController.Core!.Pose.FootYMeters;
            SessionController.TickForTest(0.30, simulateHitch: true);
            if (SessionController.State != SessionState.Paused)
                throw new InvalidOperationException("Hitch did not pause, state=" + SessionController.State);
            if (Math.Abs(SessionController.Core.Pose.FootYMeters - y) > 1e-9)
                throw new InvalidOperationException("Hitch still advanced.");
            SessionController.Exit(WalkExitKind.KeepView, "p1-hitch");
            return "dt>250ms pauses and drops catch-up";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static string TestRussianCharsEaten(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_RU");
            SessionController.EnterAtDocumentPoint(uiDoc, view, Point3d.Origin, 0, MovementMode.Level, MouseLookProfile.FreeLook, false);
            var bridge = SessionController.Bridge!;
            bridge.InjectScanKey(ScanCodes.W, true);
            bridge.InjectChar('ц');
            bridge.InjectChar('w');
            bridge.PumpPostedMessages();
            if (!bridge.IsDown(PhysicalKey.W))
                throw new InvalidOperationException("Physical W missing.");
            if (bridge.EatenCharMessages < 2)
                throw new InvalidOperationException("WM_CHAR leak risk.");
            bridge.InjectScanKey(ScanCodes.W, false);
            bridge.PumpPostedMessages();
            SessionController.Exit(WalkExitKind.KeepView, "p1-ru");
            return $"scan W + eatenChars={bridge.EatenCharMessages}";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static string TestViewClose(RhinoDoc uiDoc)
    {
        var working = HostView(uiDoc);
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        SessionController.SuppressHostScripts = true;
        try
        {
            view = AddTestView(uiDoc, "ARCHWALK_P1_CLOSE");
            SessionController.EnterAtDocumentPoint(uiDoc, view, Point3d.Origin, 0, MovementMode.Level, MouseLookProfile.FreeLook, false);
            view.Close();
            view = null;
            if (SessionController.State != SessionState.Idle)
                throw new InvalidOperationException("Closing the walk view left state " + SessionController.State);
            return "view destroy releases capture";
        }
        finally
        {
            Cleanup(view, working, workingSnap);
        }
    }

    static RhinoView HostView(RhinoDoc doc)
    {
        if (doc.Views.ActiveView is { } active)
            return active;
        var listed = doc.Views.GetViewList(ViewTypeFilter.Model);
        if (listed is { Length: > 0 })
            return listed[0];
        return doc.Views.Add("ARCHWALK_P1_HOST", DefinedViewportProjection.Perspective, new System.Drawing.Rectangle(20, 20, 400, 300), false)
            ?? throw new InvalidOperationException("No host view.");
    }

    static RhinoView AddTestView(RhinoDoc doc, string name)
    {
        return doc.Views.Add(name, DefinedViewportProjection.Perspective, new System.Drawing.Rectangle(40, 40, 360, 240), true)
            ?? throw new InvalidOperationException("Could not create " + name);
    }

    static void HoldAndTick(int scan, double seconds)
    {
        var bridge = SessionController.Bridge ?? throw new InvalidOperationException("No bridge.");
        bridge.InjectScanKey(scan, true);
        bridge.PumpPostedMessages();
        if (!bridge.IsDown(PhysicalKey.W) && scan == ScanCodes.W)
            throw new InvalidOperationException("Physical W was not captured before tick.");
        SessionController.TickForTest(seconds);
        bridge.InjectScanKey(scan, false);
        bridge.PumpPostedMessages();
    }

    static void Inject(int scan, bool down)
    {
        var bridge = SessionController.Bridge ?? throw new InvalidOperationException("No bridge.");
        bridge.InjectScanKey(scan, down);
        bridge.PumpPostedMessages();
        SessionController.TickForTest(0);
    }

    static void Cleanup(RhinoView? view, RhinoView working, ViewportSnapshot workingSnap)
    {
        SessionController.Reset("p1-cleanup");
        InputSession.Release("p1-cleanup");
        SessionController.SuppressHostScripts = false;
        try { view?.Close(); } catch { /* already closed */ }
        if (working.Handle != IntPtr.Zero)
            CameraAdapter.Restore(working.MainViewport, workingSnap);
    }
}
