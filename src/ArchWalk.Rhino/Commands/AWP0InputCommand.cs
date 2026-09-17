using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.Input;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("2d8c4a71-0f5b-4c96-a3e8-1b7d6c0e9a44")]
public sealed class AWP0InputCommand : Command
{
    public override string EnglishName => "AWP0Input";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var go = new GetOption();
        go.SetCommandPrompt("ARCHWALK P0A input capture");
        var live = go.AddOption("Live");
        var stop = go.AddOption("Stop");
        go.AcceptNothing(true);
        var result = go.Get();
        if (result == GetResult.Option && go.Option() is not null && go.Option().Index == stop)
        {
            InputSession.Release("AWP0Input Stop");
            return Result.Success;
        }

        var view = doc.Views.ActiveView;
        if (view is null)
            return Result.Failure;
        return InputSession.Capture(view, result == GetResult.Option && go.Option()?.Index == live ? "live" : "default")
            ? Result.Success
            : Result.Failure;
    }
}
