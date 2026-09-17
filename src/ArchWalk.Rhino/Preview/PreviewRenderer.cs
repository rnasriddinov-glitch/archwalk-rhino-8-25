using System.Drawing;
using ArchWalk.Core.Camera;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Camera;
using Rhino;
using Rhino.Display;

namespace ArchWalk.RhinoPlugin.Preview;

public sealed class PreviewRenderer : IDisposable
{
    public const string ViewTitle = "ARCHWALK_PREVIEW";

    RhinoView? _view;
    bool _owned;

    public RhinoView? View => _view;

    public bool EnsureView(RhinoDoc doc, int width = 360, int height = 220)
    {
        if (_view is not null && _view.MainViewport is not null)
            return true;

        foreach (var existing in doc.Views)
        {
            if (existing.MainViewport.Name == ViewTitle)
            {
                _view = existing;
                _owned = false;
                return true;
            }
        }

        var rect = new Rectangle(40, 40, Math.Max(160, width), Math.Max(100, height));
        _view = doc.Views.Add(ViewTitle, DefinedViewportProjection.Perspective, rect, true);
        _owned = _view is not null;
        if (_view is not null)
        {
            var shaded = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.ShadedId);
            if (shaded is not null)
                _view.MainViewport.DisplayMode = shaded;
        }
        return _view is not null;
    }

    public Bitmap? CaptureFrame(RhinoDoc doc, CameraPose pose, DocumentUnits units, int width, int height)
    {
        if (!EnsureView(doc, width, height))
            return null;
        var vp = _view!.MainViewport;
        if (!CameraAdapter.ApplyPose(vp, pose, units))
            return null;
        _view.Redraw();
        return DisplayPipeline.DrawToBitmap(vp, width, height);
    }

    public void Dispose()
    {
        if (_owned && _view is not null)
        {
            try { _view.Close(); }
            catch { /* view may already be gone */ }
        }
        _view = null;
        _owned = false;
    }
}
