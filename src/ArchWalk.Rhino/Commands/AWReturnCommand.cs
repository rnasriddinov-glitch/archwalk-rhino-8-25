using System.Runtime.InteropServices;
using ArchWalk.Core.Motion;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("c63e0a92-4d1b-4f77-9e3c-8a2f5d9b4c33")]
public sealed class AWReturnCommand : Command
{
    public override string EnglishName => "AWReturn";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (!SessionController.IsActive)
        {
            RhinoApp.WriteLine("ARCHWALK: нет активной прогулки.");
            return Result.Nothing;
        }

        SessionController.Exit(WalkExitKind.RestoreSnapshot, "AWReturn");
        return Result.Success;
    }
}
