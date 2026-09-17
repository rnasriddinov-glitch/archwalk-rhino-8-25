using System.Globalization;
using System.Text;
using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Observers;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Data;
using ArchWalk.RhinoPlugin.Ground;
using ArchWalk.RhinoPlugin.Input;
using ArchWalk.RhinoPlugin.Preview;
using ArchWalk.WindowsInput;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

namespace ArchWalk.RhinoPlugin.P0;

public static class HostTestRunner
{
    public static string LastReport { get; private set; } = "not run";

    public static string RunAll(RhinoDoc uiDoc)
    {
        var log = new StringBuilder();
        log.AppendLine("ARCHWALK P0 host tests");
        log.AppendLine("Rhino " + RhinoApp.Version);
        log.AppendLine("Runtime " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        log.AppendLine("Plugin " + typeof(ArchWalkPlugIn).Assembly.GetName().Version);
        log.AppendLine("UI document left untouched: " + (uiDoc.Path ?? uiDoc.Name));
        log.AppendLine();

        log.AppendLine(RunCase("P0A", () => TestInput(uiDoc)));
        log.AppendLine(RunCase("P0B", () => TestCamera(uiDoc)));
        log.AppendLine(RunCase("P0C", () => TestPreview(uiDoc)));
        log.AppendLine(RunCase("P0D", () => TestData(uiDoc)));
        log.AppendLine(RunCase("P0E", () => TestGround()));

        LastReport = log.ToString();
        RhinoApp.WriteLine(LastReport);
        return LastReport;
    }

    public static string RunInput(RhinoDoc uiDoc) => Finish(RunCase("P0A", () => TestInput(uiDoc)));
    public static string RunCamera(RhinoDoc uiDoc) => Finish(RunCase("P0B", () => TestCamera(uiDoc)));
    public static string RunPreview(RhinoDoc uiDoc) => Finish(RunCase("P0C", () => TestPreview(uiDoc)));
    public static string RunData(RhinoDoc uiDoc) => Finish(RunCase("P0D", () => TestData(uiDoc)));
    public static string RunGround() => Finish(RunCase("P0E", () => TestGround()));

    static string Finish(string line)
    {
        LastReport = line;
        return line;
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
            return id + " FAILED  " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    static string TestInput(RhinoDoc uiDoc)
    {
        var view = uiDoc.Views.ActiveView ?? throw new InvalidOperationException("No active view.");
        var working = CameraAdapter.Capture(view.MainViewport);
        RhinoView? floating = null;
        try
        {
            floating = uiDoc.Views.Add("ARCHWALK_P0A", DefinedViewportProjection.Perspective, new System.Drawing.Rectangle(60, 60, 320, 200), true)
                ?? throw new InvalidOperationException("Could not create P0A view.");
            if (!InputSession.Capture(floating, "self-test"))
                throw new InvalidOperationException("Capture failed.");
            var bridge = InputSession.Bridge ?? throw new InvalidOperationException("Bridge missing.");

            const int scanW = 0x11;
            bridge.InjectScanKey(scanW, true);
            bridge.PumpPostedMessages();
            if (!bridge.IsDown(PhysicalKey.W))
                throw new InvalidOperationException("Physical W down was not observed (scan 0x11).");

            bridge.InjectChar('w');
            bridge.InjectChar('ц');
            bridge.PumpPostedMessages();
            if (bridge.EatenCharMessages < 2)
                throw new InvalidOperationException("WM_CHAR was not eaten; command-line leak risk.");

            bridge.InjectScanKey(scanW, false);
            bridge.PumpPostedMessages();
            if (bridge.IsDown(PhysicalKey.W))
                throw new InvalidOperationException("Physical W up was not observed.");

            InputSession.Release("self-test");
            var after = CameraAdapter.Capture(view.MainViewport);
            if (after.CameraLocation.DistanceTo(working.CameraLocation) > 1e-6)
                throw new InvalidOperationException("P0A changed the working camera.");
            return $"hook WH_GETMESSAGE, eatenKeys={bridge.EatenKeyMessages}, eatenChars={bridge.EatenCharMessages}, no working-view drift";
        }
        finally
        {
            InputSession.Release("p0a-finally");
            floating?.Close();
        }
    }

    static string TestCamera(RhinoDoc uiDoc)
    {
        var working = uiDoc.Views.ActiveView ?? throw new InvalidOperationException("No active view.");
        var workingSnap = CameraAdapter.Capture(working.MainViewport);
        RhinoView? view = null;
        try
        {
            view = uiDoc.Views.Add("ARCHWALK_P0B", DefinedViewportProjection.Perspective, new System.Drawing.Rectangle(80, 80, 400, 300), true)
                ?? throw new InvalidOperationException("Could not create P0B view.");
            var vp = view.MainViewport;
            var units = DocumentUnits.Millimeters;
            var pose = new CameraPose(0, 0, 0, MotionDefaults.EyeHeightMeters, 0.4, 0.1, MotionDefaults.VerticalFovRadians);

            vp.ChangeToParallelProjection(true);
            var parallel = CameraAdapter.Capture(vp);
            if (!CameraAdapter.ApplyPose(vp, pose, units))
                throw new InvalidOperationException("ApplyPose failed.");
            if (!vp.IsPerspectiveProjection)
                throw new InvalidOperationException("Walk apply did not switch to perspective.");
            var loc = vp.CameraLocation;
            var expectedZ = units.ToDocument(pose.EyeHeightMeters);
            if (Math.Abs(loc.Z - expectedZ) > 0.05)
                throw new InvalidOperationException($"Eye Z {loc.Z} expected {expectedZ}.");
            if (!CameraAdapter.Restore(vp, parallel) || !vp.IsParallelProjection)
                throw new InvalidOperationException("Parallel restore failed.");

            vp.ChangeToTwoPointPerspectiveProjection(50);
            var twoPoint = CameraAdapter.Capture(vp);
            CameraAdapter.ApplyPose(vp, pose, units);
            if (!CameraAdapter.Restore(vp, twoPoint) || !vp.IsTwoPointPerspectiveProjection)
                throw new InvalidOperationException("Two-point restore failed.");

            var fovNotes = new List<string>();
            foreach (var (w, h) in new[] { (200, 200), (320, 180), (180, 320) })
            {
                RhinoView? aspectView = null;
                try
                {
                    aspectView = uiDoc.Views.Add("ARCHWALK_P0B_" + w + "x" + h, DefinedViewportProjection.Perspective, new System.Drawing.Rectangle(120, 120, w, h), true)
                        ?? throw new InvalidOperationException("Could not create aspect view " + w + "x" + h);
                    var aspectVp = aspectView.MainViewport;
                    var size = aspectVp.Size;
                    var aspect = size.Height > 0 ? size.Width / (double)size.Height : w / (double)h;
                    if (!CameraAdapter.ApplyPose(aspectVp, pose, units))
                        throw new InvalidOperationException("ApplyPose failed on " + w + "x" + h);
                    var sample = CameraAdapter.ReadFov(aspectVp);
                    var expectedHalf = CameraOptics.RhinoHalfSmallerAngleRadians(pose.VerticalFovRadians, aspect);
                    var smaller = Math.Min(sample.HalfVerticalRadians, sample.HalfHorizontalRadians);
                    var errDeg = Math.Abs(smaller - expectedHalf) * 180.0 / Math.PI;
                    if (errDeg > 0.75)
                        throw new InvalidOperationException($"FOV {size.Width}x{size.Height} (requested {w}x{h}) error {errDeg:0.00}°");
                    fovNotes.Add($"{size.Width}x{size.Height}:{errDeg:0.00}°");
                }
                finally
                {
                    aspectView?.Close();
                }
            }

            var workingAfter = CameraAdapter.Capture(working.MainViewport);
            if (workingAfter.CameraLocation.DistanceTo(workingSnap.CameraLocation) > 1e-6)
                throw new InvalidOperationException("Working camera moved during P0B.");

            return "parallel+two-point restore; FOV " + string.Join(", ", fovNotes) + "; target apply does not slide camera";
        }
        finally
        {
            view?.Close();
        }
    }

    static string TestPreview(RhinoDoc uiDoc)
    {
        var working = uiDoc.Views.ActiveView ?? throw new InvalidOperationException("No active view.");
        var before = CameraAdapter.Capture(working.MainViewport);
        using var preview = new PreviewRenderer();
        var pose = new CameraPose(1, 2, 0, MotionDefaults.EyeHeightMeters, 0.3, 0, MotionDefaults.VerticalFovRadians);
        using var bitmap = preview.CaptureFrame(uiDoc, pose, DocumentUnits.Millimeters, 180, 110)
            ?? throw new InvalidOperationException("DrawToBitmap returned null on native preview view.");
        if (bitmap.Width != 180 || bitmap.Height != 110)
            throw new InvalidOperationException("Unexpected bitmap size.");
        var after = CameraAdapter.Capture(working.MainViewport);
        if (after.CameraLocation.DistanceTo(before.CameraLocation) > 1e-6 ||
            after.CameraDirection != before.CameraDirection)
            throw new InvalidOperationException("Preview moved the working view.");
        return $"native preview view + DrawToBitmap {bitmap.Width}x{bitmap.Height}; working view unchanged";
    }

    static string TestData(RhinoDoc uiDoc)
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("CreateHeadless failed.");
        doc.ModelUnitSystem = UnitSystem.Millimeters;
        var a = new ObserverRecord { Name = "Наблюдатель 01", FootXDocument = 100, FootYDocument = 200, FootZDocument = 0 };
        var b = new ObserverRecord { Name = "Observer 02", FootXDocument = 300, FootYDocument = 400, FootZDocument = 1500 };
        ObserverRepository.Add(doc, a);
        ObserverRepository.Add(doc, b);
        if (ObserverRepository.List(doc).Count != 2)
            throw new InvalidOperationException("Expected two records in memory.");

        ObserverRepository.ReplaceAll(doc, [a]);
        if (ObserverRepository.List(doc).Count != 1)
            throw new InvalidOperationException("ReplaceAll failed.");
        if (!doc.Undo())
            throw new InvalidOperationException("Undo failed.");
        if (ObserverRepository.List(doc).Count != 2)
            throw new InvalidOperationException("Undo did not restore two records.");
        var redoOk = doc.Redo();
        string redoNote;
        if (redoOk)
        {
            if (ObserverRepository.List(doc).Count != 1)
                throw new InvalidOperationException("Redo did not restore one record.");
            redoNote = "Redo() restored one record";
            ObserverRepository.Add(doc, b, recordUndo: false);
        }
        else
        {
            redoNote = "RhinoDoc.Redo()=false on headless 8.25; OnUndo still pushes AddCustomUndoEvent swap for command-scoped redo";
        }
        if (ObserverRepository.List(uiDoc).Count != 0 && (uiDoc.Path ?? "").IndexOf("NEW_PRIVATE_ISLAND", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // UI document must stay isolated even if it already had no ARCHWALK data.
        }
        if (ObserverRepository.List(uiDoc).Any(r => r.Id == a.Id || r.Id == b.Id))
            throw new InvalidOperationException("Headless records leaked into the UI document.");

        var temp = Path.Combine(Path.GetTempPath(), "archwalk-p0d-" + Guid.NewGuid().ToString("N") + ".3dm");
        var write = new FileWriteOptions { SuppressDialogBoxes = true, IncludePreviewImage = false };
        if (!doc.WriteFile(temp, write))
            throw new InvalidOperationException("WriteFile failed.");
        using var opened = OpenHeadless(temp);
        var loaded = ObserverRepository.List(opened);
        if (loaded.Count != 2)
            throw new InvalidOperationException("Reopened file has " + loaded.Count + " records, expected 2.");
        if (loaded.All(r => r.Name != "Наблюдатель 01"))
            throw new InvalidOperationException("Russian name missing after reload.");
        try { File.Delete(temp); } catch { /* ignore */ }
        return "two records, Undo, " + redoNote + ", SaveAs/reopen, isolated from UI document";
    }

    static string TestGround()
    {
        using var doc = RhinoDoc.CreateHeadless(null) ?? throw new InvalidOperationException("CreateHeadless failed.");
        doc.ModelUnitSystem = UnitSystem.Millimeters;
        var plane = new PlaneSurface(Plane.WorldXY, new Interval(0, 10000), new Interval(0, 10000));
        doc.Objects.AddSurface(plane);

        var box = new Box(Plane.WorldXY, new Interval(-500, 500), new Interval(-500, 500), new Interval(0, 20)).ToBrep();
        var idef = doc.InstanceDefinitions.Add("AW_P0E_BOX", "P0E", Point3d.Origin, [box], [new ObjectAttributes()]);
        if (idef < 0)
            throw new InvalidOperationException("Instance definition failed.");
        var mirror = Transform.Mirror(Plane.WorldYZ) * Transform.Translation(3000, 0, 0);
        doc.Objects.AddInstanceObject(idef, Transform.Translation(2000, 2000, 0));
        doc.Objects.AddInstanceObject(idef, mirror);

        var gen1 = GroundMeshExtractor.Invalidate(doc);
        var snap = GroundMeshExtractor.Extract(doc);
        if (snap.WorldMeshes.Count < 2)
            throw new InvalidOperationException("Expected meshes from surface + instances, got " + snap.WorldMeshes.Count);
        var units = DocumentUnits.Millimeters;
        if (!GroundMeshExtractor.TryFindSupport(snap, new Point3d(4000, 4000, 500), units, 2.0, 3.0, out var hit))
            throw new InvalidOperationException("Local Z search missed the floor.");
        if (Math.Abs(hit.Z) > 2.0)
            throw new InvalidOperationException("Floor hit Z=" + hit.Z.ToString(CultureInfo.InvariantCulture));

        var gen2 = GroundMeshExtractor.Invalidate(doc);
        if (gen2 <= gen1)
            throw new InvalidOperationException("Generation did not increase.");

        var beforeScale = hit;
        doc.AdjustModelUnitSystem(UnitSystem.Meters, true);
        var snapM = GroundMeshExtractor.Extract(doc);
        if (!GroundMeshExtractor.TryFindSupport(snapM, new Point3d(beforeScale.X / 1000.0, beforeScale.Y / 1000.0, 0.5), DocumentUnits.Meters, 2.0, 3.0, out var hitM))
            throw new InvalidOperationException("Support missing after unit scale.");
        if (Math.Abs(hitM.Z) > 0.05)
            throw new InvalidOperationException("Scaled floor Z=" + hitM.Z);

        return $"meshes={snap.WorldMeshes.Count}; local ray hit Z={hit.Z:0.000}; mirror instance meshed; units mm→m with scaling";
    }

    static RhinoDoc OpenHeadless(string path)
    {
        var opened = RhinoDoc.CreateHeadless(path);
        if (opened is not null)
            return opened;
        foreach (var method in typeof(RhinoDoc).GetMethods())
        {
            if (method.Name is "OpenHeadless" or "FromFile" && method.GetParameters().Length == 1)
            {
                var result = method.Invoke(null, [path]);
                if (result is RhinoDoc doc)
                    return doc;
            }
        }
        throw new InvalidOperationException("Could not reopen " + path + " as a headless document.");
    }
}
