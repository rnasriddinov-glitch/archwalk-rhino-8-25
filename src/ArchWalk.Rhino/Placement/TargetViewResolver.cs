using Rhino;
using Rhino.Display;

namespace ArchWalk.RhinoPlugin.Placement;

public readonly record struct TargetViewChoice(
    Guid ViewId,
    string Label,
    bool CreateNew,
    double AspectWidthOverHeight)
{
    public override string ToString() => Label;
}

public static class TargetViewResolver
{
    public const string NewWalkViewTitle = "ARCHWALK Walk";

    public static TargetViewChoice ResolveDefault(RhinoDoc doc, RhinoView sourceView)
    {
        if (IsUsablePerspective(sourceView))
        {
            var aspect = AspectOf(sourceView);
            return new TargetViewChoice(sourceView.MainViewport.Id, sourceView.MainViewport.Name, false, aspect);
        }

        foreach (var view in EnumerateModelViews(doc))
        {
            if (view.MainViewport.Id == sourceView.MainViewport.Id)
                continue;
            if (!IsUsablePerspective(view))
                continue;
            return new TargetViewChoice(view.MainViewport.Id, view.MainViewport.Name, false, AspectOf(view));
        }

        return new TargetViewChoice(Guid.Empty, "Новое окно прогулки", true, 16.0 / 9.0);
    }

    public static IReadOnlyList<TargetViewChoice> ListChoices(RhinoDoc doc, RhinoView sourceView)
    {
        var list = new List<TargetViewChoice>();
        foreach (var view in EnumerateModelViews(doc))
        {
            if (!IsUsablePerspective(view))
                continue;
            list.Add(new TargetViewChoice(view.MainViewport.Id, view.MainViewport.Name, false, AspectOf(view)));
        }

        list.Add(new TargetViewChoice(Guid.Empty, "Новое окно прогулки", true, 16.0 / 9.0));
        if (list.All(c => c.ViewId != sourceView.MainViewport.Id) && IsUsablePerspective(sourceView))
            list.Insert(0, new TargetViewChoice(sourceView.MainViewport.Id, sourceView.MainViewport.Name, false, AspectOf(sourceView)));
        return list;
    }

    public static RhinoView? FindView(RhinoDoc doc, Guid viewportId)
    {
        if (viewportId == Guid.Empty)
            return null;
        foreach (var view in doc.Views)
        {
            if (view.MainViewport.Id == viewportId)
                return view;
        }
        return null;
    }

    public static RhinoView EnsureTargetView(RhinoDoc doc, PlacementDraft draft, RhinoView sourceView)
    {
        if (!draft.CreateNewTargetView)
        {
            var existing = FindView(doc, draft.TargetViewId);
            if (existing is not null)
                return existing;
        }

        var rect = new System.Drawing.Rectangle(60, 60, 960, 540);
        var created = doc.Views.Add(NewWalkViewTitle, DefinedViewportProjection.Perspective, rect, true)
            ?? throw new InvalidOperationException("Не удалось создать окно прогулки.");
        draft.CreateNewTargetView = true;
        draft.TargetViewId = created.MainViewport.Id;
        draft.TargetLabel = created.MainViewport.Name;
        draft.AspectWidthOverHeight = AspectOf(created);
        _ = sourceView;
        return created;
    }

    public static bool IsUsablePerspective(RhinoView view)
    {
        var vp = view.MainViewport;
        if (vp is null)
            return false;
        if (!vp.IsPerspectiveProjection)
            return false;
        // Locked page/detail views are out of v1 scope; model floating/standard perspectives are OK.
        return true;
    }

    public static double AspectOf(RhinoView view)
    {
        var size = view.MainViewport.Size;
        if (size.Height <= 0 || size.Width <= 0)
            return 16.0 / 9.0;
        return size.Width / (double)size.Height;
    }

    static IEnumerable<RhinoView> EnumerateModelViews(RhinoDoc doc)
    {
        foreach (var view in doc.Views)
        {
            if (view?.MainViewport is null)
                continue;
            yield return view;
        }
    }
}
