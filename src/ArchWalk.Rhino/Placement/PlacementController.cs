using ArchWalk.Core.Camera;
using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Placement;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Ground;
using ArchWalk.RhinoPlugin.Preview;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using System.Drawing;

namespace ArchWalk.RhinoPlugin.Placement;

public static class PlacementController
{
    static readonly object Gate = new();
    static PlacementDraft _draft = new();
    static PlacementConduit? _conduit;
    static PreviewRenderer? _preview;
    static Bitmap? _lastFrame;
    static int _previewGeneration;
    static DateTime _lastPreviewUtc = DateTime.MinValue;
    static double _lastLevelZDocument;
    static bool _hasLevelHint;

    public static event Action? Changed;

    public static PlacementDraft Draft
    {
        get { lock (Gate) return _draft.Clone(); }
    }

    public static Bitmap? LastPreviewFrame
    {
        get { lock (Gate) return _lastFrame is null ? null : (Bitmap)_lastFrame.Clone(); }
    }

    public static int PreviewGeneration
    {
        get { lock (Gate) return _previewGeneration; }
    }

    public static void Cancel(string reason = "esc")
    {
        lock (Gate)
        {
            DisposePreview_NoLock();
            _draft = new PlacementDraft();
            EnsureConduit(false);
        }
        RhinoApp.WriteLine("ARCHWALK: установка отменена (" + reason + ").");
        RaiseChanged();
    }

    public static bool BeginPlace(RhinoDoc doc, RhinoView sourceView, FootSourceKind footSource = FootSourceKind.Level)
    {
        if (SessionController.IsActive)
        {
            RhinoApp.WriteLine("ARCHWALK: сначала завершите прогулку.");
            return false;
        }

        if (!RhinoUnits.TryFromDoc(doc, out _, out var error))
        {
            if (error is not null)
                RhinoApp.WriteLine(error);
            return false;
        }

        var target = TargetViewResolver.ResolveDefault(doc, sourceView);
        lock (Gate)
        {
            _draft = new PlacementDraft
            {
                Phase = PlacementPhase.PlacingFoot,
                FootSource = footSource,
                DocumentSerial = doc.RuntimeSerialNumber,
                SourceViewId = sourceView.MainViewport.Id,
                TargetViewId = target.ViewId,
                CreateNewTargetView = target.CreateNew,
                TargetLabel = target.Label,
                AspectWidthOverHeight = target.AspectWidthOverHeight,
                LevelHintZDocument = _hasLevelHint ? _lastLevelZDocument : sourceView.MainViewport.ConstructionPlane().OriginZ,
                StatusMessage = footSource == FootSourceKind.Level
                    ? "Кликните место у ног (По отметке)"
                    : "Кликните опору под ногами"
            };
            EnsureConduit(true);
        }
        RaiseChanged();
        return true;
    }

    public static void SetTargetChoice(RhinoDoc doc, TargetViewChoice choice)
    {
        lock (Gate)
        {
            if (_draft.Phase == PlacementPhase.Idle)
                return;
            _draft.TargetViewId = choice.ViewId;
            _draft.CreateNewTargetView = choice.CreateNew;
            _draft.TargetLabel = choice.Label;
            _draft.AspectWidthOverHeight = choice.AspectWidthOverHeight;
            if (!choice.CreateNew)
            {
                var view = TargetViewResolver.FindView(doc, choice.ViewId);
                if (view is not null)
                    _draft.AspectWidthOverHeight = TargetViewResolver.AspectOf(view);
            }
        }
        RequestPreview(doc, force: true);
        RaiseChanged();
    }

    public static void SetLookAt3D(bool enabled)
    {
        lock (Gate)
        {
            _draft.LookAt3D = enabled;
            if (!enabled)
                _draft.PitchRadians = 0;
        }
        RaiseChanged();
    }

    public static void SetFootSource(FootSourceKind kind)
    {
        lock (Gate)
        {
            _draft.FootSource = kind;
            _draft.StatusMessage = kind == FootSourceKind.Level
                ? "Кликните место у ног (По отметке)"
                : "Кликните опору под ногами";
        }
        RaiseChanged();
    }

    public static bool TrySetFootFromCursor(RhinoDoc doc, Point3d cursorDocument, out string message)
    {
        message = string.Empty;
        if (!RhinoUnits.TryFromDoc(doc, out var units, out var error))
        {
            message = error ?? "Нет единиц.";
            return false;
        }

        lock (Gate)
        {
            if (_draft.Phase is not PlacementPhase.PlacingFoot and not PlacementPhase.Aiming and not PlacementPhase.Ready)
            {
                message = "Установка не активна.";
                return false;
            }

            if (_draft.FootSource == FootSourceKind.Surface)
            {
                var snap = GroundMeshExtractor.Extract(doc);
                if (!GroundMeshExtractor.TryFindSupport(snap, cursorDocument, units, 2.0, 4.0, out var hit))
                {
                    _draft.HasValidSupport = false;
                    _draft.StatusMessage = "Нет опоры — выберите «По отметке»";
                    message = _draft.StatusMessage;
                    EnsureConduit(true);
                    RaiseChanged();
                    return false;
                }

                cursorDocument = hit;
                _draft.HasValidSupport = true;
            }
            else
            {
                // Level: keep Z from hint (last level / CPlane), XY from cursor.
                cursorDocument = new Point3d(cursorDocument.X, cursorDocument.Y, _draft.LevelHintZDocument);
                _draft.HasValidSupport = true;
            }

            _draft.FootXDocument = cursorDocument.X;
            _draft.FootYDocument = cursorDocument.Y;
            _draft.FootZDocument = cursorDocument.Z;
            _draft.Phase = PlacementPhase.Aiming;
            _draft.StatusMessage = _draft.LookAt3D
                ? "Укажите точку взгляда 3D"
                : "Укажите направление взгляда";
            _lastLevelZDocument = cursorDocument.Z;
            _hasLevelHint = true;
            EnsureConduit(true);
        }

        RequestPreview(doc, force: true);
        RaiseChanged();
        return true;
    }

    public static bool TryUpdateAimFromWorldPoint(RhinoDoc doc, Point3d worldPoint, out string message)
    {
        message = string.Empty;
        if (!RhinoUnits.TryFromDoc(doc, out var units, out var error))
        {
            message = error ?? "Нет единиц.";
            return false;
        }

        lock (Gate)
        {
            if (_draft.Phase is not PlacementPhase.Aiming and not PlacementPhase.Ready)
            {
                message = "Сначала укажите ноги.";
                return false;
            }

            var foot = new Point3d(_draft.FootXDocument, _draft.FootYDocument, _draft.FootZDocument);
            if (_draft.LookAt3D)
            {
                var eye = new Point3d(foot.X, foot.Y, foot.Z + units.ToDocument(_draft.EyeHeightMeters));
                var eyeM = new Vec3(units.ToMeters(eye.X), units.ToMeters(eye.Y), units.ToMeters(eye.Z));
                var targetM = new Vec3(units.ToMeters(worldPoint.X), units.ToMeters(worldPoint.Y), units.ToMeters(worldPoint.Z));
                if (!PlacementMath.TryLookAtPoint(eyeM, targetM, out var yaw, out var pitch))
                {
                    message = "Цель слишком близко.";
                    return false;
                }

                _draft.YawRadians = yaw;
                _draft.PitchRadians = pitch;
            }
            else
            {
                var aimOnPlane = new Point3d(worldPoint.X, worldPoint.Y, foot.Z);
                var footM = _draft.FootMeters(units);
                var aimM = new Vec3(units.ToMeters(aimOnPlane.X), units.ToMeters(aimOnPlane.Y), footM.Z);
                if (!PlacementMath.TryYawFromFootPlaneAim(footM, aimM, out var yaw))
                {
                    message = "Цель слишком близко — направление не меняется.";
                    return false;
                }

                _draft.YawRadians = yaw;
                _draft.PitchRadians = 0;
            }

            _draft.StatusMessage = "Направление обновлено";
            EnsureConduit(true);
        }

        RequestPreview(doc, force: false);
        RaiseChanged();
        return true;
    }

    public static bool TryUpdateAimFromRay(RhinoDoc doc, Line worldRay, out string message)
    {
        message = string.Empty;
        if (!RhinoUnits.TryFromDoc(doc, out var units, out var error))
        {
            message = error ?? "Нет единиц.";
            return false;
        }

        Point3d aimPoint;
        lock (Gate)
        {
            if (_draft.Phase is not PlacementPhase.Aiming and not PlacementPhase.Ready)
            {
                message = "Сначала укажите ноги.";
                return false;
            }

            var foot = new Point3d(_draft.FootXDocument, _draft.FootYDocument, _draft.FootZDocument);
            if (_draft.LookAt3D)
            {
                // Fallback: point along the ray at ~5 m from the eye.
                var eye = new Point3d(foot.X, foot.Y, foot.Z + units.ToDocument(_draft.EyeHeightMeters));
                if (!TryClosestPointOnRay(worldRay, eye + (worldRay.Direction * units.ToDocument(5.0)), out aimPoint))
                {
                    message = "Недопустимая цель взгляда.";
                    return false;
                }
            }
            else
            {
                var plane = new Plane(foot, Vector3d.ZAxis);
                if (!TryPlaneIntersection(worldRay, plane, out aimPoint))
                {
                    message = "Луч почти параллелен плоскости ног.";
                    return false;
                }
            }
        }

        return TryUpdateAimFromWorldPoint(doc, aimPoint, out message);
    }

    public static bool ConfirmAim(RhinoDoc doc)
    {
        lock (Gate)
        {
            if (_draft.Phase is not PlacementPhase.Aiming and not PlacementPhase.Ready)
                return false;
            if (!_draft.HasValidSupport)
                return false;
            _draft.Phase = PlacementPhase.Ready;
            _draft.StatusMessage = "Готово — Войти или Готово";
            EnsureConduit(true);
        }
        RequestPreview(doc, force: true);
        RaiseChanged();
        return true;
    }

    public static void EditFoot()
    {
        lock (Gate)
        {
            if (_draft.Phase == PlacementPhase.Idle)
                return;
            _draft.Phase = PlacementPhase.PlacingFoot;
            _draft.StatusMessage = _draft.FootSource == FootSourceKind.Level
                ? "Кликните место у ног (По отметке)"
                : "Кликните опору под ногами";
            EnsureConduit(true);
        }
        RaiseChanged();
    }

    public static void EditAim()
    {
        lock (Gate)
        {
            if (!_draft.HasFoot)
                return;
            _draft.Phase = PlacementPhase.Aiming;
            _draft.StatusMessage = _draft.LookAt3D
                ? "Укажите точку взгляда 3D"
                : "Укажите направление взгляда";
            EnsureConduit(true);
        }
        RaiseChanged();
    }

    public static bool TryEnter(RhinoDoc doc, bool deferCapture, out string message)
    {
        message = string.Empty;
        PlacementDraft draft;
        lock (Gate)
        {
            if (_draft.Phase != PlacementPhase.Ready)
            {
                message = "Сначала завершите установку (место и направление).";
                return false;
            }
            draft = _draft.Clone();
        }

        if (!RhinoUnits.TryFromDoc(doc, out var units, out var error))
        {
            message = error ?? "Нет единиц.";
            return false;
        }

        var source = TargetViewResolver.FindView(doc, draft.SourceViewId) ?? doc.Views.ActiveView;
        if (source is null)
        {
            message = "Нет исходного вида.";
            return false;
        }

        var target = TargetViewResolver.EnsureTargetView(doc, draft, source);
        var pose = draft.ToPose(units);
        if (!SessionController.Enter(doc, target, pose, draft.MovementMode, draft.LookProfile, deferCapture))
        {
            message = "Не удалось войти в прогулку.";
            return false;
        }

        lock (Gate)
        {
            DisposePreview_NoLock();
            _draft = new PlacementDraft();
            EnsureConduit(false);
        }
        RaiseChanged();
        return true;
    }

    public static bool TryFinishWithoutEnter(RhinoDoc doc, out string message)
    {
        message = string.Empty;
        PlacementDraft draft;
        lock (Gate)
        {
            if (_draft.Phase != PlacementPhase.Ready)
            {
                message = "Черновик ещё не готов.";
                return false;
            }
            draft = _draft.Clone();
            DisposePreview_NoLock();
            _draft = new PlacementDraft();
            EnsureConduit(false);
        }

        // Persistence of observers is P3; P2 clears the draft without Undo pollution.
        _ = draft;
        _ = doc;
        RaiseChanged();
        return true;
    }

    public static void RequestPreview(RhinoDoc doc, bool force)
    {
        if (!RhinoUnits.TryFromDoc(doc, out var units, out _))
            return;

        CameraPose pose;
        double aspect;
        lock (Gate)
        {
            if (_draft.Phase is PlacementPhase.Idle or PlacementPhase.PlacingFoot)
                return;
            if (!force && (DateTime.UtcNow - _lastPreviewUtc).TotalMilliseconds < 100)
                return;
            pose = _draft.ToPose(units);
            aspect = _draft.AspectWidthOverHeight;
            _lastPreviewUtc = DateTime.UtcNow;
        }

        var width = 360;
        var height = Math.Max(100, (int)Math.Round(width / Math.Max(0.2, aspect)));
        _preview ??= new PreviewRenderer();
        var frame = _preview.CaptureFrame(doc, pose, units, width, height);
        lock (Gate)
        {
            _lastFrame?.Dispose();
            _lastFrame = frame;
            if (frame is not null)
                _previewGeneration++;
        }
        RaiseChanged();
    }

    public static void DrawDynamic(RhinoDoc doc, DisplayPipeline display, Point3d cursor)
    {
        if (!RhinoUnits.TryFromDoc(doc, out var units, out _))
            return;
        PlacementDraft draft;
        lock (Gate) draft = _draft.Clone();
        if (draft.Phase == PlacementPhase.Idle)
            return;

        var foot = draft.HasFoot
            ? new Point3d(draft.FootXDocument, draft.FootYDocument, draft.FootZDocument)
            : draft.FootSource == FootSourceKind.Level
                ? new Point3d(cursor.X, cursor.Y, draft.LevelHintZDocument)
                : cursor;

        PlacementMarker.Draw(display, foot, draft, units, draft.HasValidSupport);
    }

    static void EnsureConduit(bool enabled)
    {
        if (enabled)
        {
            _conduit ??= new PlacementConduit();
            _conduit.Enabled = true;
        }
        else if (_conduit is not null)
        {
            _conduit.Enabled = false;
        }
    }

    static void DisposePreview_NoLock()
    {
        _preview?.Dispose();
        _preview = null;
        _lastFrame?.Dispose();
        _lastFrame = null;
    }

    static bool TryPlaneIntersection(Line ray, Plane plane, out Point3d point)
    {
        point = Point3d.Unset;
        if (!Rhino.Geometry.Intersect.Intersection.LinePlane(ray, plane, out var t))
            return false;
        if (t < 1e-9)
            return false;
        point = ray.PointAt(t);
        return point.IsValid;
    }

    static bool TryClosestPointOnRay(Line ray, Point3d near, out Point3d point)
    {
        point = Point3d.Unset;
        var dir = ray.Direction;
        if (!dir.Unitize())
            return false;
        var t = (near - ray.From) * dir;
        if (t < 0.1)
            t = 5.0;
        point = ray.From + (dir * t);
        return true;
    }

    static void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch { /* panel refresh must not break placement */ }
    }
}
