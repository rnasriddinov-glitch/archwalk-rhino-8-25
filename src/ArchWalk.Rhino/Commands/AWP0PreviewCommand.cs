using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P0;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("4f0e6c93-2b7d-4e18-c50a-3d9f8e201c66")]
public sealed class AWP0PreviewCommand : Command
{
    public override string EnglishName => "AWP0Preview";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine(HostTestRunner.RunPreview(doc));
        return Result.Success;
    }
}
