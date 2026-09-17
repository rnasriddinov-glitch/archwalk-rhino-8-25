using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.P0;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("72319fc6-5e0a-414b-f83d-602cb1534f99")]
public sealed class AWP0RunHostTestsCommand : Command
{
    public override string EnglishName => "AWP0RunHostTests";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        HostTestRunner.RunAll(doc);
        return Result.Success;
    }
}
