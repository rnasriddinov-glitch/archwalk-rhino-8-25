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
        if (IsUsable(_view))
        {
            KeepDetached(_view!);
            return true;
        }

        foreach (var existing in doc.Views)
        {
            if (existing.MainViewport.Name == ViewTitle)
            {
                _view = existing;
                _owned = false;
                KeepDetached(_view);
                return true;
            }
        }

        var working = doc.Views.ActiveView;
        var redraw = doc.Views.RedrawEnabled;
        try
        {
            // Views.Add makes the new view ActiveView and, in Rhino 8 tabs, briefly
            // presents it in the working window. Hold redraw until it is floating
            // and the previous ActiveView is restored.
            doc.Views.RedrawEnabled = false;
            var rect = new Rectangle(40, 40, Math.Max(160, width), Math.Max(100, height));
            _view = doc.Views.Add(ViewTitle, DefinedViewportProjection.Perspective, rect, true);
            _owned = _view is not null;
            if (_view is not null)
            {
                KeepDetached(_view);
                var shaded = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.ShadedId);
                if (shaded is not null)
                    _view.MainViewport.DisplayMode = shaded;
            }
        }
        finally
        {
            RestoreActiveView(doc, working);
            doc.Views.RedrawEnabled = redraw;
        }

        return _view is not null;
    }

    public Bitmap? CaptureFrame(RhinoDoc doc, CameraPose pose, DocumentUnits units, int width, int height)
    {
        var working = doc.Views.ActiveView;
        try
        {
            if (!EnsureView(doc, width, height))
                return null;
            var vp = _view!.MainViewport;
            if (!CameraAdapter.ApplyPose(vp, pose, units))
                return null;

            RestoreActiveView(doc, working);
            var bitmap = DisplayPipeline.DrawToBitmap(vp, width, height);
            RestoreActiveView(doc, working);
            // Present only on the owned floating view after ActiveView is restored.
            if (_view.Floating)
                _view.Redraw();
            return bitmap;
        }
        finally
        {
            RestoreActiveView(doc, working);
        }
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

    static bool IsUsable(RhinoView? view) =>
        view is not null && view.MainViewport is not null;

    static void KeepDetached(RhinoView view)
    {
        try { view.Floating = true; }
        catch { /* view may not support floating */ }
    }

    static void RestoreActiveView(RhinoDoc doc, RhinoView? working)
    {
        if (working is null || working.MainViewport is null)
            return;
        if (ReferenceEquals(doc.Views.ActiveView, working))
            return;
        try { doc.Views.ActiveView = working; }
        catch { /* view may already be gone */ }
    }
}
