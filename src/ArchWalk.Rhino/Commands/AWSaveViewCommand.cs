using System.Runtime.InteropServices;
using ArchWalk.RhinoPlugin.Observers;
using Rhino;
using Rhino.Commands;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("f2c8a955-1e99-4d54-9162-5acd4e0f7b88")]
public sealed class AWSaveViewCommand : Command
{
    public override string EnglishName => "AWSaveView";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        return NamedViewService.PromptAndSave(doc) ? Result.Success : Result.Cancel;
    }
}
