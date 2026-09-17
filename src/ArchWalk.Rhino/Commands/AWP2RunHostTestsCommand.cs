using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P2;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("e1b7f844-0d88-4c43-8051-4fbc3d9e6a77")]
public sealed class AWP2RunHostTestsCommand : Command
{
    public override string EnglishName => "AWP2RunHostTests";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine(HostTestRunner.RunAll(doc));
        return Result.Success;
    }
}
