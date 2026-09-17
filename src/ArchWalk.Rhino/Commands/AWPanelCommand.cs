using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.UI;
using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("c91f5d22-8b66-4a21-ae3f-2d9e1b7c4e55")]
public sealed class AWPanelCommand : Command
{
    public override string EnglishName => "AWPanel";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        _ = doc;
        Panels.OpenPanel(ObserverPanel.PanelId);
        return Result.Success;
    }
}
