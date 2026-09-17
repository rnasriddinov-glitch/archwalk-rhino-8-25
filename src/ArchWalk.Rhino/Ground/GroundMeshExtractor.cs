using ArchWalk.Core.Units;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace ArchWalk.RhinoPlugin.Ground;

public sealed class GroundSnapshot
{
    public GroundSnapshot(uint documentSerial, int generation, IReadOnlyList<Mesh> worldMeshes, string notes)
    {
        DocumentSerial = documentSerial;
        Generation = generation;
        WorldMeshes = worldMeshes;
        Notes = notes;
    }

    public uint DocumentSerial { get; }
    public int Generation { get; }
    public IReadOnlyList<Mesh> WorldMeshes { get; }
    public string Notes { get; }
}

public static class GroundMeshExtractor
{
    static readonly Dictionary<uint, int> Generations = [];

    public static int Invalidate(RhinoDoc doc)
    {
        var next = Generations.TryGetValue(doc.RuntimeSerialNumber, out var g) ? g + 1 : 1;
        Generations[doc.RuntimeSerialNumber] = next;
        return next;
    }

    public static int CurrentGeneration(RhinoDoc doc) =>
        Generations.TryGetValue(doc.RuntimeSerialNumber, out var g) ? g : 0;

    public static GroundSnapshot Extract(RhinoDoc doc)
    {
        var meshes = new List<Mesh>();
        var notes = new List<string>();
        foreach (var obj in doc.Objects)
        {
            if (obj is null || obj.IsDeleted)
                continue;
            if (obj is InstanceObject instance)
                ExtractInstance(instance, Transform.Identity, meshes, notes);
            else
                ExtractObject(obj, Transform.Identity, meshes, notes);
        }

        return new GroundSnapshot(doc.RuntimeSerialNumber, CurrentGeneration(doc), meshes, string.Join("; ", notes));
    }

    public static bool TryFindSupport(
        GroundSnapshot snapshot,
        Point3d footGuess,
        DocumentUnits units,
        double searchUpMeters,
        double searchDownMeters,
        out Point3d hit)
    {
        hit = Point3d.Unset;
        var origin = new Point3d(footGuess.X, footGuess.Y, footGuess.Z + units.ToDocument(searchUpMeters));
        var ray = new Ray3d(origin, -Vector3d.ZAxis);
        var maxDistance = units.ToDocument(searchUpMeters + searchDownMeters);
        var best = double.PositiveInfinity;
        var found = false;
        foreach (var mesh in snapshot.WorldMeshes)
        {
            var t = Intersection.MeshRay(mesh, ray);
            if (t < 0 || t > maxDistance || t >= best)
                continue;
            best = t;
            hit = ray.PointAt(t);
            found = true;
        }
        return found;
    }

    static void ExtractInstance(InstanceObject instance, Transform parent, List<Mesh> meshes, List<string> notes)
    {
        var world = parent * instance.InstanceXform;
        instance.Explode(true, out var pieces, out _, out var xforms);
        if (pieces is null || pieces.Length == 0)
        {
            notes.Add("empty-instance");
            return;
        }

        notes.Add($"instance:{instance.Id:D}:{pieces.Length}");
        for (var i = 0; i < pieces.Length; i++)
        {
            var xform = i < xforms.Length ? parent * xforms[i] : world;
            if (pieces[i] is InstanceObject nested)
                ExtractInstance(nested, xform, meshes, notes);
            else
                ExtractObject(pieces[i], xform, meshes, notes);
        }
    }

    static void ExtractObject(RhinoObject obj, Transform xform, List<Mesh> meshes, List<string> notes)
    {
        var local = obj.GetMeshes(MeshType.Render);
        if (local is null || local.Length == 0)
        {
            obj.CreateMeshes(MeshType.Render, MeshingParameters.FastRenderMesh, true);
            local = obj.GetMeshes(MeshType.Render);
        }
        if (local is null || local.Length == 0)
        {
            var geo = obj.Geometry;
            Mesh[]? created = geo switch
            {
                Mesh mesh => [mesh.DuplicateMesh()],
                Brep brep => Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh) ?? [],
                Extrusion extrusion => Mesh.CreateFromExtrusion(extrusion, MeshingParameters.FastRenderMesh) is { } one ? [one] : [],
                Surface surface => Mesh.CreateFromSurface(surface, MeshingParameters.FastRenderMesh) is { } s ? [s] : [],
                _ => []
            };
            local = created;
        }

        foreach (var mesh in local)
        {
            if (mesh is null || !mesh.IsValid)
                continue;
            var copy = mesh.DuplicateMesh();
            if (!xform.Equals(Transform.Identity))
                copy.Transform(xform);
            meshes.Add(copy);
        }
    }
}
