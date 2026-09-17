using ArchWalk.RhinoPlugin.Camera;
using Rhino.Display;

namespace ArchWalk.RhinoPlugin.Placement;

sealed class PlacementConduit : DisplayConduit
{
    protected override void DrawOverlay(DrawEventArgs e)
    {
        var draft = PlacementController.Draft;
        if (draft.Phase == PlacementPhase.Idle || !draft.HasFoot)
            return;
        if (e.RhinoDoc is null || !RhinoUnits.TryFromDoc(e.RhinoDoc, out var units, out _))
            return;

        var foot = new Rhino.Geometry.Point3d(draft.FootXDocument, draft.FootYDocument, draft.FootZDocument);
        PlacementMarker.Draw(e.Display, foot, draft, units, draft.HasValidSupport);
    }
}
