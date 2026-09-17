using System.Reflection;
using ArchWalk.RhinoPlugin.Data;
using ArchWalk.RhinoPlugin.Input;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.FileIO;
using Rhino.PlugIns;

namespace ArchWalk.RhinoPlugin;

[System.Runtime.InteropServices.Guid(ArchWalkPlugIn.IdString)]
public sealed class ArchWalkPlugIn : PlugIn
{
    public const string IdString = "6f2e1c8a-3b47-4d9e-9a1c-7e4b2f90c8d1";

    static ArchWalkPlugIn()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSibling;
    }

    public ArchWalkPlugIn()
    {
        Instance = this;
        SessionController.InstallHostHooks();
        RhinoDoc.CloseDocument += (_, e) =>
        {
            ObserverRepository.Remove(e.Document);
            InputSession.Release("document-close");
        };
        RhinoApp.Closing += (_, _) => InputSession.Release("rhino-closing");
    }

    public static ArchWalkPlugIn Instance { get; private set; } = null!;

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override bool ShouldCallWriteDocument(FileWriteOptions options)
    {
        if (options.WriteSelectedObjectsOnly || options.WriteGeometryOnly)
            return false;
        var doc = options.RhinoDoc;
        if (doc is null)
            return false;
        return ObserverRepository.ShouldWrite(doc);
    }

    protected override void WriteDocument(RhinoDoc doc, BinaryArchiveWriter archive, FileWriteOptions options)
    {
        ObserverRepository.Write(doc, archive);
    }

    protected override void ReadDocument(RhinoDoc doc, BinaryArchiveReader archive, FileReadOptions options)
    {
        ObserverRepository.Read(doc, archive, options);
    }

    static Assembly? ResolveSibling(object? sender, ResolveEventArgs args)
    {
        var simple = new AssemblyName(args.Name).Name;
        if (string.IsNullOrEmpty(simple))
            return null;
        var dir = Path.GetDirectoryName(typeof(ArchWalkPlugIn).Assembly.Location);
        if (string.IsNullOrEmpty(dir))
            return null;
        var path = Path.Combine(dir, simple + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
