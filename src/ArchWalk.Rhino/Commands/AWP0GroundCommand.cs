using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P0;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("61208eb5-4d9f-403a-e72c-5f1ba0423e88")]
public sealed class AWP0GroundCommand : Command
{
    public override string EnglishName => "AWP0Ground";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine(HostTestRunner.RunGround());
        return Result.Success;
    }
}
