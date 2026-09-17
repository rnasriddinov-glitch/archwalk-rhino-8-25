using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P5;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("a1b2c3d4-e5f6-4789-a012-3456789abcde")]
public sealed class AWP5RunHostTestsCommand : Command
{
    public override string EnglishName => "AWP5RunHostTests";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine(HostTestRunner.RunAll(doc));
        return Result.Success;
    }
}
