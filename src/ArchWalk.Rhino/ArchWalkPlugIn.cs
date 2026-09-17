using System.Reflection;
using ArchWalk.RhinoPlugin.Data;
using ArchWalk.RhinoPlugin.Input;
using ArchWalk.RhinoPlugin.Placement;
using ArchWalk.RhinoPlugin.Session;
using ArchWalk.RhinoPlugin.UI;
using Rhino;
using Rhino.FileIO;
using Rhino.PlugIns;
using Rhino.UI;

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
        ObserverRepository.InstallUnitHooks();
        RhinoDoc.CloseDocument += (_, e) =>
        {
            PlacementController.Cancel("document-close");
            ArchWalk.RhinoPlugin.Observers.ObserverWorkflow.EnsureMarkers(null, false);
            ObserverRepository.Remove(e.Document);
            InputSession.Release("document-close");
        };
        RhinoApp.Closing += (_, _) =>
        {
            PlacementController.Cancel("rhino-closing");
            InputSession.Release("rhino-closing");
        };
    }

    public static ArchWalkPlugIn Instance { get; private set; } = null!;

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        EnsurePlugInId();
        if (Id == Guid.Empty)
        {
            errorMessage =
                "ARCHWALK: PlugIn.Id is empty. Reinstall from dist\\install\\ArchWalk.rhp " +
                "(assembly must have [assembly: Guid(\"" + IdString + "\")]). Loaded from: " +
                (Assembly?.Location ?? "?");
            return LoadReturnCode.ErrorShowDialog;
        }

        RhinoApp.WriteLine("ARCHWALK loaded Id=" + Id + " from " + (Assembly?.Location ?? "?"));
        Panels.RegisterPanel(this, typeof(ObserverPanel), "Наблюдатель", null);
        return LoadReturnCode.Success;
    }

    /// <summary>
    /// Rhino sets <see cref="PlugIn.Id"/> from the assembly GuidAttribute in PlugIn.Create.
    /// If an older build without that attribute still constructs, force the known Id before RegisterPanel.
    /// </summary>
    void EnsurePlugInId()
    {
        if (Id != Guid.Empty)
            return;

        var expected = new Guid(IdString);
        try
        {
            var attrs = GetType().Assembly.GetCustomAttributes(typeof(System.Runtime.InteropServices.GuidAttribute), false);
            if (attrs.Length > 0 && attrs[0] is System.Runtime.InteropServices.GuidAttribute ga &&
                Guid.TryParse(ga.Value, out var fromAsm) && fromAsm != Guid.Empty)
                expected = fromAsm;
        }
        catch
        {
            // keep IdString
        }

        var field = typeof(PlugIn).GetField("m_id", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(this, expected);
    }

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
