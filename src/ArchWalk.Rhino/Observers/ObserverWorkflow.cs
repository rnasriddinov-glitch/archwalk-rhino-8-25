using ArchWalk.Core.Motion;
using ArchWalk.Core.Observers;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Data;
using ArchWalk.RhinoPlugin.Placement;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace ArchWalk.RhinoPlugin.Observers;

/// <summary>Document-facing observer actions: save draft, enter record, update from walk.</summary>
public static class ObserverWorkflow
{
    static Guid _selectedId;
    static Guid _sessionRecordId;
    static ObserverMarkerConduit? _marker;

    public static event Action? UiChanged;

    public static Guid SelectedId
    {
        get => _selectedId;
        set
        {
            _selectedId = value;
            RaiseUi();
        }
    }

    public static Guid SessionRecordId => _sessionRecordId;

    public static void Select(Guid id)
    {
        _selectedId = id;
        EnsureMarkers(RhinoDoc.ActiveDoc, true);
        RaiseUi();
    }

    public static ObserverRecord? CommitDraft(RhinoDoc doc, PlacementDraft draft, bool thenEnter)
    {
        if (!RhinoUnits.TryFromDoc(doc, out var units, out var error))
        {
            if (error is not null)
                RhinoApp.WriteLine(error);
            return null;
        }

        var record = ObserverRepository.FromPose(
            draft.ToPose(units),
            units,
            ObserverRepository.NextAutomaticName(doc),
            draft.MovementMode,
            MotionDefaults.BaseSpeedMetersPerSecond);
        ObserverRepository.Add(doc, record);
        _selectedId = record.Id;
        EnsureMarkers(doc, true);
        RaiseUi();

        if (thenEnter)
            EnterRecord(doc, record.Id, deferCapture: true);
        return record;
    }

    public static bool EnterRecord(RhinoDoc doc, Guid id, bool deferCapture)
    {
        if (!ObserverRepository.TryGet(doc, id, out var record))
        {
            RhinoApp.WriteLine("ARCHWALK: запись не найдена.");
            return false;
        }

        if (!RhinoUnits.TryFromDoc(doc, out var units, out var error))
        {
            if (error is not null)
                RhinoApp.WriteLine(error);
            return false;
        }

        var source = doc.Views.ActiveView;
        if (source is null)
        {
            RhinoApp.WriteLine("ARCHWALK: нет активного вида.");
            return false;
        }

        var choice = TargetViewResolver.ResolveDefault(doc, source);
        var shell = new PlacementDraft
        {
            SourceViewId = source.MainViewport.Id,
            TargetViewId = choice.ViewId,
            CreateNewTargetView = choice.CreateNew,
            TargetLabel = choice.Label,
            AspectWidthOverHeight = choice.AspectWidthOverHeight
        };
        var view = TargetViewResolver.EnsureTargetView(doc, shell, source);
        var pose = ObserverRepository.ToPose(record, units);
        var mode = record.InitialMovementMode;
        if (!SessionController.Enter(doc, view, pose, mode, MouseLookProfile.FreeLook, deferCapture))
            return false;

        _sessionRecordId = record.Id;
        _selectedId = record.Id;
        EnsureMarkers(doc, false);
        RaiseUi();
        return true;
    }

    public static bool UpdateSelectedFromWalk(RhinoDoc doc)
    {
        var id = _sessionRecordId != Guid.Empty ? _sessionRecordId : _selectedId;
        if (id == Guid.Empty)
        {
            RhinoApp.WriteLine("ARCHWALK: нет выбранной записи для обновления.");
            return false;
        }

        if (SessionController.Core is null)
        {
            RhinoApp.WriteLine("ARCHWALK: нет активной позы прогулки.");
            return false;
        }

        if (!RhinoUnits.TryFromDoc(doc, out var units, out _))
            return false;

        if (!ObserverRepository.TryGet(doc, id, out var record))
            return false;

        var pose = SessionController.Core.Pose;
        record.FootXDocument = units.ToDocument(pose.FootXMeters);
        record.FootYDocument = units.ToDocument(pose.FootYMeters);
        record.FootZDocument = units.ToDocument(pose.FootZMeters);
        record.EyeHeightMeters = pose.EyeHeightMeters;
        record.YawRadians = pose.YawRadians;
        record.PitchRadians = pose.PitchRadians;
        record.VerticalFovRadians = pose.VerticalFovRadians;
        record.InitialMovementMode = PersistMode(SessionController.Core.Mode);
        record.BaseSpeedMetersPerSecond = SessionController.Core.BaseSpeedMetersPerSecond;
        var ok = ObserverRepository.Update(doc, record);
        if (ok)
            RhinoApp.WriteLine("ARCHWALK: наблюдатель обновлён — " + record.Name);
        RaiseUi();
        return ok;
    }

    public static ObserverRecord? NewObserverHere(RhinoDoc doc)
    {
        if (SessionController.Core is null || !RhinoUnits.TryFromDoc(doc, out var units, out _))
            return null;
        var record = ObserverRepository.FromPose(
            SessionController.Core.Pose,
            units,
            ObserverRepository.NextAutomaticName(doc),
            PersistMode(SessionController.Core.Mode),
            SessionController.Core.BaseSpeedMetersPerSecond);
        ObserverRepository.Add(doc, record);
        _selectedId = record.Id;
        EnsureMarkers(doc, true);
        RaiseUi();
        return record;
    }

    public static void ClearSessionRecord()
    {
        _sessionRecordId = Guid.Empty;
        RaiseUi();
    }

    static MovementMode PersistMode(MovementMode mode) => mode switch
    {
        MovementMode.Fly => MovementMode.Fly,
        MovementMode.Surface => MovementMode.Surface,
        _ => MovementMode.Level
    };

    public static void EnsureMarkers(RhinoDoc? doc, bool show)
    {
        if (!show || doc is null)
        {
            if (_marker is not null)
                _marker.Enabled = false;
            return;
        }

        _marker ??= new ObserverMarkerConduit();
        _marker.Enabled = true;
        doc.Views.Redraw();
    }

    static void RaiseUi()
    {
        try { UiChanged?.Invoke(); }
        catch { /* ignore */ }
    }
}

sealed class ObserverMarkerConduit : DisplayConduit
{
    protected override void DrawOverlay(DrawEventArgs e)
    {
        if (e.RhinoDoc is null || !RhinoUnits.TryFromDoc(e.RhinoDoc, out var units, out _))
            return;
        foreach (var record in ObserverRepository.List(e.RhinoDoc))
        {
            var foot = new Point3d(record.FootXDocument, record.FootYDocument, record.FootZDocument);
            var eye = new Point3d(foot.X, foot.Y, foot.Z + units.ToDocument(record.EyeHeightMeters));
            var selected = record.Id == ObserverWorkflow.SelectedId;
            var color = selected ? System.Drawing.Color.Gold : System.Drawing.Color.FromArgb(180, 120, 160, 255);
            e.Display.DrawPoint(foot, PointStyle.RoundSimple, selected ? 5 : 3, color);
            e.Display.DrawLine(foot, eye, color, selected ? 2 : 1);
            var dir = new Vector3d(Math.Sin(record.YawRadians), Math.Cos(record.YawRadians), 0);
            if (dir.Unitize())
                e.Display.DrawArrow(new Line(foot, foot + (dir * units.ToDocument(1.0))), color, 10, 0);
            e.Display.DrawDot(foot + (Vector3d.ZAxis * units.ToDocument(0.05)), record.Name, color, System.Drawing.Color.Black);
        }
    }
}
