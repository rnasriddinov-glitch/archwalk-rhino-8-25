using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Support;
using ArchWalk.Core.Units;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace ArchWalk.RhinoPlugin.Ground;

public sealed class GroundMeshEntry
{
    public GroundMeshEntry(Mesh mesh, BoundingBox worldBox)
    {
        Mesh = mesh;
        WorldBox = worldBox;
    }

    public Mesh Mesh { get; }
    public BoundingBox WorldBox { get; }
}

public sealed class GroundCache
{
    readonly List<GroundMeshEntry> _entries;
    readonly int _cellSizeDocument;
    readonly Dictionary<(int, int), List<int>> _grid;

    public GroundCache(
        uint documentSerial,
        int generation,
        DocumentUnits units,
        IReadOnlyList<GroundMeshEntry> entries,
        string notes)
    {
        DocumentSerial = documentSerial;
        Generation = generation;
        Units = units;
        Notes = notes;
        _entries = entries.ToList();
        var cell = units.ToDocument(2.0);
        _cellSizeDocument = cell < 1 ? 1 : (int)System.Math.Ceiling(cell);
        _grid = BuildGrid(_entries, _cellSizeDocument);
    }

    public uint DocumentSerial { get; }
    public int Generation { get; }
    public DocumentUnits Units { get; }
    public string Notes { get; }
    public int MeshCount => _entries.Count;

    public ISupportField AsSupportField() => new GroundSupportField(this);

    public SupportHit ProbeMeters(double xMeters, double yMeters, double referenceZMeters, double upMeters, double downMeters)
    {
        var x = Units.ToDocument(xMeters);
        var y = Units.ToDocument(yMeters);
        var refZ = Units.ToDocument(referenceZMeters);
        var up = Units.ToDocument(upMeters);
        var down = Units.ToDocument(downMeters);
        if (!TryProbeDocument(x, y, refZ, up, down, out var zDoc, out var normal))
            return SupportHit.Miss;
        return SupportHit.At(Units.ToMeters(zDoc), new Vec3(normal.X, normal.Y, normal.Z));
    }

    public bool TryProbeDocument(
        double xDocument,
        double yDocument,
        double referenceZDocument,
        double upDocument,
        double downDocument,
        out double hitZDocument,
        out Vector3d normal)
    {
        hitZDocument = 0;
        normal = Vector3d.ZAxis;
        var origin = new Point3d(xDocument, yDocument, referenceZDocument + upDocument);
        var ray = new Ray3d(origin, -Vector3d.ZAxis);
        var maxDistance = upDocument + downDocument;
        if (maxDistance <= 0)
            return false;

        var bestT = double.PositiveInfinity;
        Mesh? bestMesh = null;
        var found = false;

        foreach (var index in CandidateIndices(xDocument, yDocument))
        {
            var entry = _entries[index];
            var box = entry.WorldBox;
            if (xDocument < box.Min.X - 1e-6 || xDocument > box.Max.X + 1e-6
                || yDocument < box.Min.Y - 1e-6 || yDocument > box.Max.Y + 1e-6)
                continue;
            if (box.Max.Z < referenceZDocument - downDocument - 1e-6)
                continue;
            if (box.Min.Z > referenceZDocument + upDocument + 1e-6)
                continue;

            var t = Intersection.MeshRay(entry.Mesh, ray);
            if (t < 0 || t > maxDistance || t >= bestT)
                continue;
            bestT = t;
            bestMesh = entry.Mesh;
            found = true;
        }

        if (!found || bestMesh is null)
            return false;

        var hit = ray.PointAt(bestT);
        hitZDocument = hit.Z;
        normal = EstimateNormal(bestMesh, hit);
        return true;
    }

    IEnumerable<int> CandidateIndices(double x, double y)
    {
        var cx = (int)System.Math.Floor(x / _cellSizeDocument);
        var cy = (int)System.Math.Floor(y / _cellSizeDocument);
        var seen = new HashSet<int>();
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (!_grid.TryGetValue((cx + dx, cy + dy), out var list))
                continue;
            foreach (var i in list)
            {
                if (seen.Add(i))
                    yield return i;
            }
        }
    }

    static Dictionary<(int, int), List<int>> BuildGrid(IReadOnlyList<GroundMeshEntry> entries, int cell)
    {
        var grid = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < entries.Count; i++)
        {
            var box = entries[i].WorldBox;
            if (!box.IsValid)
                continue;
            var minX = (int)System.Math.Floor(box.Min.X / cell);
            var maxX = (int)System.Math.Floor(box.Max.X / cell);
            var minY = (int)System.Math.Floor(box.Min.Y / cell);
            var maxY = (int)System.Math.Floor(box.Max.Y / cell);
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            {
                var key = (x, y);
                if (!grid.TryGetValue(key, out var list))
                {
                    list = [];
                    grid[key] = list;
                }
                list.Add(i);
            }
        }
        return grid;
    }

    static Vector3d EstimateNormal(Mesh mesh, Point3d hit)
    {
        var mp = mesh.ClosestMeshPoint(hit, System.Math.Max(mesh.GetBoundingBox(true).Diagonal.Length * 0.01, 1e-6));
        if (mp is not null)
        {
            var n = mesh.NormalAt(mp);
            if (n.IsValid && n.Unitize())
            {
                if (n.Z < 0)
                    n = -n;
                return n;
            }
        }

        return Vector3d.ZAxis;
    }
}

sealed class GroundSupportField(GroundCache cache) : ISupportField
{
    public SupportHit Probe(double xMeters, double yMeters, double referenceZMeters, double upMeters, double downMeters) =>
        cache.ProbeMeters(xMeters, yMeters, referenceZMeters, upMeters, downMeters);
}

public static class GroundCacheBuilder
{
    static readonly Dictionary<uint, int> Generations = [];
    static readonly Dictionary<uint, GroundCache> Caches = [];

    public static int Invalidate(RhinoDoc doc)
    {
        var next = Generations.TryGetValue(doc.RuntimeSerialNumber, out var g) ? g + 1 : 1;
        Generations[doc.RuntimeSerialNumber] = next;
        Caches.Remove(doc.RuntimeSerialNumber);
        GroundMeshExtractor.Invalidate(doc);
        return next;
    }

    public static int CurrentGeneration(RhinoDoc doc) =>
        Generations.TryGetValue(doc.RuntimeSerialNumber, out var g) ? g : 0;

    public static void Clear(RhinoDoc doc)
    {
        Caches.Remove(doc.RuntimeSerialNumber);
        Generations.Remove(doc.RuntimeSerialNumber);
    }

    public static GroundCache GetOrBuild(RhinoDoc doc, DocumentUnits units, GroundSupportFilter? filter = null)
    {
        var serial = doc.RuntimeSerialNumber;
        var gen = CurrentGeneration(doc);
        if (Caches.TryGetValue(serial, out var cached)
            && cached.Generation == gen
            && System.Math.Abs(cached.Units.MetersPerDocumentUnit - units.MetersPerDocumentUnit) < 1e-15)
            return cached;

        var built = Build(doc, units, filter);
        Caches[serial] = built;
        return built;
    }

    public static GroundCache Build(RhinoDoc doc, DocumentUnits units, GroundSupportFilter? filter = null)
    {
        filter ??= GroundSupportFilter.Automatic;
        var entries = new List<GroundMeshEntry>();
        var notes = new List<string>();
        foreach (var obj in doc.Objects)
        {
            if (obj is null || obj.IsDeleted)
                continue;
            if (!filter.Includes(obj))
                continue;
            if (obj is InstanceObject instance)
                ExtractInstance(instance, Transform.Identity, entries, notes, filter);
            else
                ExtractObject(obj, Transform.Identity, entries, notes);
        }

        return new GroundCache(
            doc.RuntimeSerialNumber,
            CurrentGeneration(doc),
            units,
            entries,
            string.Join("; ", notes));
    }

    static void ExtractInstance(
        InstanceObject instance,
        Transform parent,
        List<GroundMeshEntry> entries,
        List<string> notes,
        GroundSupportFilter filter)
    {
        if (!filter.Includes(instance))
            return;
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
                ExtractInstance(nested, xform, entries, notes, filter);
            else if (pieces[i] is not null)
                ExtractObject(pieces[i], xform, entries, notes);
        }
    }

    static void ExtractObject(RhinoObject obj, Transform xform, List<GroundMeshEntry> entries, List<string> notes)
    {
        if (obj.IsHidden)
            return;
        var layer = obj.Document?.Layers[obj.Attributes.LayerIndex];
        if (layer is not null && !layer.IsVisible)
            return;

        var local = obj.GetMeshes(MeshType.Render);
        if (local is null || local.Length == 0)
        {
            obj.CreateMeshes(MeshType.Render, MeshingParameters.QualityRenderMesh, true);
            local = obj.GetMeshes(MeshType.Render);
        }
        if (local is null || local.Length == 0)
            local = CreateMeshesFromGeometry(obj.Geometry);

        foreach (var mesh in local)
        {
            if (mesh is null || !mesh.IsValid)
                continue;
            var copy = mesh.DuplicateMesh();
            if (!xform.Equals(Transform.Identity))
                copy.Transform(xform);
            copy.Normals.ComputeNormals();
            copy.FaceNormals.ComputeFaceNormals();
            var box = copy.GetBoundingBox(true);
            if (!box.IsValid)
                continue;
            entries.Add(new GroundMeshEntry(copy, box));
        }
    }

    static Mesh[] CreateMeshesFromGeometry(GeometryBase? geo)
    {
        switch (geo)
        {
            case Mesh mesh:
                return [mesh.DuplicateMesh()];
            case Brep brep:
                return Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh) ?? [];
            case Extrusion extrusion:
            {
                var brep = extrusion.ToBrep();
                return brep is null ? [] : Mesh.CreateFromBrep(brep, MeshingParameters.QualityRenderMesh) ?? [];
            }
            case Surface surface:
            {
                var one = Mesh.CreateFromSurface(surface, MeshingParameters.QualityRenderMesh);
                return one is null ? [] : [one];
            }
            case SubD subd:
            {
                var mesh = Mesh.CreateFromSubD(subd, 3);
                return mesh is null ? [] : [mesh];
            }
            default:
                return [];
        }
    }
}

/// <summary>Automatic visible geometry, or an explicit object/layer allow-list.</summary>
public sealed class GroundSupportFilter
{
    public static GroundSupportFilter Automatic { get; } = new(true, null, null);

    GroundSupportFilter(bool automatic, HashSet<Guid>? objectIds, HashSet<int>? layerIndices)
    {
        AutomaticMode = automatic;
        ObjectIds = objectIds;
        LayerIndices = layerIndices;
    }

    public bool AutomaticMode { get; }
    public HashSet<Guid>? ObjectIds { get; }
    public HashSet<int>? LayerIndices { get; }

    public static GroundSupportFilter FromSelection(IEnumerable<Guid> objectIds, IEnumerable<int> layerIndices) =>
        new(false, objectIds.ToHashSet(), layerIndices.ToHashSet());

    public bool Includes(RhinoObject obj)
    {
        if (obj.IsDeleted)
            return false;
        if (AutomaticMode)
            return !obj.IsHidden;
        if (ObjectIds is not null && ObjectIds.Contains(obj.Id))
            return true;
        if (LayerIndices is not null && LayerIndices.Contains(obj.Attributes.LayerIndex))
            return true;
        return false;
    }
}
