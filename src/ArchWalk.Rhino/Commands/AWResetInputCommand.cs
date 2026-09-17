using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.Input;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("9a11b022-7c3d-4e8f-b1a4-55d0e9c31f20")]
public sealed class AWResetInputCommand : Command
{
    public override string EnglishName => "AWResetInput";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        SessionController.Reset("AWResetInput");
        InputSession.Release("AWResetInput");
        return Result.Success;
    }
}
