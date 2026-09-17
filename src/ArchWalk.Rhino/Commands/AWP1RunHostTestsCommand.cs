using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P1;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("d74f1b03-5e2c-4088-af4d-9b3e6e0c5d44")]
public sealed class AWP1RunHostTestsCommand : Command
{
    public override string EnglishName => "AWP1RunHostTests";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        HostTestRunner.RunAll(doc);
        return Result.Success;
    }
}
