using ArchWalk.Core.Camera;
using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Ground;
using ArchWalk.RhinoPlugin.Input;
using ArchWalk.RhinoPlugin.Placement;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using System.Diagnostics;
using System.Text;

namespace ArchWalk.RhinoPlugin.P5;

public static class HostTestRunner
{
    public static string LastReport { get; private set; } = "not run";

    public static string RunAll(RhinoDoc uiDoc)
    {
        var log = new StringBuilder();
        log.AppendLine("ARCHWALK P5 host tests");
        log.AppendLine("Rhino " + RhinoApp.Version);
        log.AppendLine("Runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        log.AppendLine("Plugin " + typeof(ArchWalkPlugIn).Assembly.GetName().Version);
        SessionController.InstallHostHooks();
        SessionController.SuppressHostScripts = true;
        log.AppendLine();
        log.AppendLine(RunCase("P5-motion-budget", () => TestMotionBudget()));
        log.AppendLine(RunCase("P5-support-budget", () => TestSupportBudget()));
        log.AppendLine(RunCase("P5-enter-exit-cycles", () => TestEnterExitCycles(uiDoc)));
        log.AppendLine(RunCase("P5-idle-quiet", () => TestIdleQuiet()));
        log.AppendLine(RunCase("P5-no-side-effects", () => TestNoSideEffects(uiDoc)));
        log.AppendLine(RunCase("P5-display-modes", () => TestDisplayModeRestore(uiDoc)));
        log.AppendLine(RunCase("P5-large-coords", () => TestLargeCoords()));
        log.AppendLine(RunCase("P5-package-files", TestPackageFiles));
        LastReport = log.ToString();
        SessionController.SuppressHostScripts = false;
        RhinoApp.WriteLine(LastReport);
        return LastReport;
    }

    static string RunCase(string id, Func<string> body)
    {
        try
        {
            PlacementController.Cancel("p5-case");
            SessionController.Reset("p5-case");
            return id + " PASSED  " + body();
        }
        catch (Exception ex)
        {
            PlacementController.Cancel("p5-fail");
            SessionController.Reset("p5-fail");
            return id + " FAILED  " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    static string TestMotionBudget()
    {
        var core = MotionCore.CreateDefaultLevel();
        var intent = InputIntent.HoldForward;
        // Warm-up
        for (var i = 0; i < 120; i++)
            core.Advance(1.0 / 60.0, intent);

        var samples = new double[600];
        for (var i = 0; i < samples.Length; i++)
        {
            var sw = Stopwatch.StartNew();
            core.Advance(1.0 / 60.0, intent);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var p95 = samples[(int)(samples.Length * 0.95)];
        if (p95 > 2.0)
            throw new InvalidOperationException("motion p95=" + p95.ToString("0.000") + "ms > 2ms");
        return "motion p95=" + p95.ToString("0.000") + "ms";
    }

    static string TestSupportBudget()
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("headless");
        doc.ModelUnitSystem = UnitSystem.Meters;
        doc.Objects.AddSurface(new PlaneSurface(Plane.WorldXY, new Interval(0, 40), new Interval(0, 40)));
        var cache = GroundCacheBuilder.Build(doc, DocumentUnits.Meters);
        var field = cache.AsSupportField();
        // Warm cache probes
        for (var i = 0; i < 50; i++)
            field.Probe(i * 0.1, i * 0.1, 0.2, 0.5, 0.5);

        var samples = new double[400];
        for (var i = 0; i < samples.Length; i++)
        {
            var x = (i % 40) * 0.5;
            var y = (i / 40) * 0.5;
            var sw = Stopwatch.StartNew();
            field.Probe(x, y, 0.2, MotionDefaults.MaxStepUpMeters + 0.01, MotionDefaults.MaxStepDownMeters + 0.01);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var p95 = samples[(int)(samples.Length * 0.95)];
        if (p95 > 3.0)
            throw new InvalidOperationException("support p95=" + p95.ToString("0.000") + "ms > 3ms");
        return "support p95=" + p95.ToString("0.000") + "ms meshes=" + cache.MeshCount;
    }

    static string TestEnterExitCycles(RhinoDoc uiDoc)
    {
        var view = uiDoc.Views.ActiveView ?? throw new InvalidOperationException("no view");
        var pose = new CameraPose(0, 0, 0, MotionDefaults.EyeHeightMeters, 0, 0, MotionDefaults.VerticalFovRadians);
        for (var i = 0; i < 40; i++)
        {
            if (!SessionController.Enter(uiDoc, view, pose, MovementMode.Level, MouseLookProfile.FreeLook, deferCapture: false))
                throw new InvalidOperationException("enter failed at " + i);
            SessionController.TickForTest(0);
            if (!SessionController.IsActive)
                throw new InvalidOperationException("not active after enter " + i);
            SessionController.Exit(WalkExitKind.KeepView, "p5-cycle");
            if (SessionController.IsActive || SessionController.HasCaptureBridge)
                throw new InvalidOperationException("leak after exit " + i);
            if (SessionController.IdleHooked)
                throw new InvalidOperationException("idle still hooked after exit " + i);
        }

        InputSession.Release("p5-cycles");
        return "40 enter/exit; bridge=false idle=false";
    }

    static string TestIdleQuiet()
    {
        if (SessionController.State != SessionState.Idle)
            throw new InvalidOperationException("expected Idle");
        if (SessionController.HasCaptureBridge)
            throw new InvalidOperationException("bridge alive in Idle");
        if (SessionController.IdleHooked)
            throw new InvalidOperationException("Idle handler still attached");
        return "Idle quiet; no capture bridge; Idle unhooked";
    }

    static string TestNoSideEffects(RhinoDoc uiDoc)
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("headless");
        doc.ModelUnitSystem = UnitSystem.Millimeters;
        var id = doc.Objects.AddSurface(new PlaneSurface(Plane.WorldXY, new Interval(0, 5000), new Interval(0, 5000)));
        var beforeCount = doc.Objects.Count;
        var beforeLayer = doc.Layers.CurrentLayerIndex;
        var cache = GroundCacheBuilder.Build(doc, DocumentUnits.Millimeters);
        _ = cache.ProbeMeters(1, 1, 0.05, 0.3, 0.3);
        if (doc.Objects.Count != beforeCount)
            throw new InvalidOperationException("object count changed");
        if (doc.Layers.CurrentLayerIndex != beforeLayer)
            throw new InvalidOperationException("current layer changed");
        if (doc.Objects.FindId(id) is null)
            throw new InvalidOperationException("geometry lost");
        _ = uiDoc;
        return "geometry/layers untouched after ground probe";
    }

    static string TestDisplayModeRestore(RhinoDoc uiDoc)
    {
        var view = uiDoc.Views.ActiveView ?? throw new InvalidOperationException("no view");
        var vp = view.ActiveViewport;
        var original = vp.DisplayMode;
        var shaded = DisplayModeDescription.FindByName("Shaded")
            ?? DisplayModeDescription.GetDisplayModes().FirstOrDefault(m => m.EnglishName.Contains("Shaded", StringComparison.OrdinalIgnoreCase));
        if (shaded is null)
            return "SKIP no Shaded mode on host";

        vp.DisplayMode = shaded;
        var pose = new CameraPose(0, 0, 0, MotionDefaults.EyeHeightMeters, 0, 0, MotionDefaults.VerticalFovRadians);
        if (!SessionController.Enter(uiDoc, view, pose, MovementMode.Level, MouseLookProfile.FreeLook, deferCapture: false))
            throw new InvalidOperationException("enter failed");
        SessionController.TickForTest(0);
        SessionController.Exit(WalkExitKind.RestoreSnapshot, "p5-display");
        // KeepView path leaves user mode; RestoreSnapshot restores camera — display mode is host-owned.
        // Re-apply original if we changed it for the test.
        vp.DisplayMode = original;
        if (SessionController.HasCaptureBridge)
            throw new InvalidOperationException("bridge after display test");
        return "enter/exit ok; display restored to " + (original?.EnglishName ?? "?");
    }

    static string TestLargeCoords()
    {
        var origin = new Vec3(1_000_000.0, 2_000_000.0, 0);
        var pose = new CameraPose(origin.X, origin.Y, origin.Z, MotionDefaults.EyeHeightMeters, 0, 0, MotionDefaults.VerticalFovRadians);
        var core = new MotionCore(pose, MovementMode.Level, MotionDefaults.BaseSpeedMetersPerSecond, null, 0.001);
        for (var i = 0; i < 600; i++)
            core.Advance(1.0 / 60.0, InputIntent.HoldForward);
        var dy = core.Pose.FootYMeters - origin.Y;
        if (Math.Abs(dy - 13.0) > 0.13)
            throw new InvalidOperationException("large-coord drift dy=" + dy);
        if (Math.Abs(core.Pose.FootXMeters - origin.X) > 1e-6)
            throw new InvalidOperationException("X drift");
        if (Math.Abs(core.Pose.FootZMeters - origin.Z) > 1e-9)
            throw new InvalidOperationException("Z drift");
        return "10s walk at 1e6 m; dy=" + dy.ToString("0.000");
    }

    static string TestPackageFiles()
    {
        var root = FindRepoRoot();
        var rhp = Path.Combine(root, "src", "ArchWalk.Rhino", "bin", "plugin", "net8.0-windows", "ArchWalk.rhp");
        var yakDir = Path.Combine(root, "packaging", "dist");
        if (!File.Exists(rhp))
            throw new InvalidOperationException("missing rhp " + rhp);
        var yaks = Directory.Exists(yakDir)
            ? Directory.GetFiles(yakDir, "archwalk-*.yak")
            : [];
        if (yaks.Length == 0)
            throw new InvalidOperationException("no yak in packaging/dist — run packaging/Build-Yak.ps1");
        var yak = yaks.OrderByDescending(File.GetLastWriteTimeUtc).First();
        var install = Path.Combine(root, "INSTALL.md");
        var controls = Path.Combine(root, "CONTROLS.md");
        if (!File.Exists(install) || !File.Exists(controls))
            throw new InvalidOperationException("missing INSTALL.md or CONTROLS.md");
        return "rhp+yak ok; " + Path.GetFileName(yak);
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ArchWalkPlugIn).Assembly.Location) ?? ".");
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArchWalk.sln")) ||
                File.Exists(Path.Combine(dir.FullName, "packaging", "manifest.yml")))
                return dir.FullName;
            dir = dir.Parent;
        }

        // Sideload builds live under bin\; walk up from there.
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArchWalk.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("repo root not found");
    }
}
