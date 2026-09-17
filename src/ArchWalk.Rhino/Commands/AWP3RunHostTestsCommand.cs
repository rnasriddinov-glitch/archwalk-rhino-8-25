using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P3;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("a3d9c066-2f0a-4e65-a273-6bde5f1a8c99")]
public sealed class AWP3RunHostTestsCommand : Command
{
    public override string EnglishName => "AWP3RunHostTests";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine(HostTestRunner.RunAll(doc));
        return Result.Success;
    }
}
