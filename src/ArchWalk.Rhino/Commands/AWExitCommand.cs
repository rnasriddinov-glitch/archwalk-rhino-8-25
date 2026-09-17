using System.Runtime.InteropServices;
using ArchWalk.Core.Motion;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("b52d9f81-3c0a-4e66-8d2b-7f1e4c8a3b22")]
public sealed class AWExitCommand : Command
{
    public override string EnglishName => "AWExit";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (!SessionController.IsActive)
        {
            RhinoApp.WriteLine("ARCHWALK: нет активной прогулки.");
            return Result.Nothing;
        }

        SessionController.Exit(WalkExitKind.KeepView, "AWExit");
        return Result.Success;
    }
}
