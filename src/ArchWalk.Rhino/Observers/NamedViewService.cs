using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ArchWalk.RhinoPlugin.Observers;

public static class NamedViewService
{
    public static bool TrySaveCurrentView(RhinoDoc doc, string? preferredName, bool allowReplace, out string message)
    {
        message = string.Empty;
        var view = doc.Views.ActiveView;
        if (view is null)
        {
            message = "Нет активного вида.";
            return false;
        }

        // Prefer the walk target if a session is active.
        if (SessionController.IsActive && SessionController.ViewId != Guid.Empty)
        {
            foreach (var v in doc.Views)
            {
                if (v.MainViewport.Id == SessionController.ViewId)
                {
                    view = v;
                    break;
                }
            }
        }

        var name = string.IsNullOrWhiteSpace(preferredName)
            ? SuggestName(doc)
            : preferredName.Trim();

        var existing = doc.NamedViews.FindByName(name);
        if (existing >= 0)
        {
            if (!allowReplace)
            {
                message = "Имя уже занято: " + name + ". Выберите другое или подтвердите замену.";
                return false;
            }

            doc.NamedViews.Delete(existing);
        }

        var index = doc.NamedViews.Add(name, view.MainViewport.Id);
        if (index < 0)
        {
            message = "Не удалось создать Named View.";
            return false;
        }

        message = "Named View сохранён: " + name;
        return true;
    }

    public static string SuggestName(RhinoDoc doc)
    {
        for (var i = 1; i < 10000; i++)
        {
            var name = "ARCHWALK " + i.ToString("00");
            if (doc.NamedViews.FindByName(name) < 0)
                return name;
        }
        return "ARCHWALK " + Guid.NewGuid().ToString("N")[..6];
    }

    public static bool PromptAndSave(RhinoDoc doc)
    {
        var gs = new GetString();
        gs.SetCommandPrompt("Имя Named View");
        gs.SetDefaultString(SuggestName(doc));
        var replaceOpt = gs.AddOption("ReplaceExisting");
        var allowReplace = false;
        while (true)
        {
            var result = gs.Get();
            if (result == GetResult.Cancel)
                return false;
            if (result == GetResult.Option && gs.Option() is not null && gs.Option()!.Index == replaceOpt)
            {
                allowReplace = !allowReplace;
                RhinoApp.WriteLine(allowReplace
                    ? "ARCHWALK: замена существующего имени разрешена."
                    : "ARCHWALK: замена существующего имени запрещена.");
                continue;
            }

            if (result != GetResult.String)
                return false;

            if (TrySaveCurrentView(doc, gs.StringResult(), allowReplace, out var message))
            {
                RhinoApp.WriteLine("ARCHWALK: " + message);
                return true;
            }

            RhinoApp.WriteLine("ARCHWALK: " + message);
            if (!allowReplace)
                RhinoApp.WriteLine("ARCHWALK: включите опцию ReplaceExisting или введите другое имя.");
        }
    }
}
