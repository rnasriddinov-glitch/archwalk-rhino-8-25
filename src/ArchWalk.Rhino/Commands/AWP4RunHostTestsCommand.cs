using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P4;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("c4e8f1a2-7b3d-4c91-9e50-1a2b3c4d5e6f")]
public sealed class AWP4RunHostTestsCommand : Command
{
    public override string EnglishName => "AWP4RunHostTests";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine(HostTestRunner.RunAll(doc));
        return Result.Success;
    }
}
