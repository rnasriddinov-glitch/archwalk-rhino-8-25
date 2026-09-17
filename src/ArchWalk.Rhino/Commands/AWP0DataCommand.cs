using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P0;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("501f7da4-3c8e-4f29-d61b-4e0a9f312d77")]
public sealed class AWP0DataCommand : Command
{
    public override string EnglishName => "AWP0Data";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine(HostTestRunner.RunData(doc));
        return Result.Success;
    }
}
