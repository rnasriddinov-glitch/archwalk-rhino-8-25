using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P0;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("3e9d5b82-1a6c-4d07-b4f9-2c8e7d1f0b55")]
public sealed class AWP0CameraCommand : Command
{
    public override string EnglishName => "AWP0Camera";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        RhinoApp.WriteLine(HostTestRunner.RunCamera(doc));
        return Result.Success;
    }
}
