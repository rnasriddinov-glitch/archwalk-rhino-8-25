using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Observers;
using ArchWalk.Core.Units;
using Rhino;
using Rhino.Commands;
using Rhino.FileIO;

namespace ArchWalk.RhinoPlugin.Data;

public static class ObserverRepository
{
    static readonly Dictionary<uint, Store> Stores = [];

    public static event Action<RhinoDoc>? Changed;

    public static ObserverDocumentState Get(RhinoDoc doc) => Ensure(doc).State.Clone();

    public static IReadOnlyList<ObserverRecord> List(RhinoDoc doc) => Ensure(doc).State.Records.Select(r => r.Clone()).ToList();

    public static bool TryGet(RhinoDoc doc, Guid id, out ObserverRecord record)
    {
        record = null!;
        var found = Ensure(doc).State.Records.FirstOrDefault(r => r.Id == id);
        if (found is null)
            return false;
        record = found.Clone();
        return true;
    }

    public static bool ShouldWrite(RhinoDoc doc)
    {
        var store = Ensure(doc);
        return !store.ForbidWrite && store.State.Records.Count > 0;
    }

    public static bool IsWriteForbidden(RhinoDoc doc) => Ensure(doc).ForbidWrite;

    public static ObserverRecord Add(RhinoDoc doc, ObserverRecord record, bool recordUndo = true)
    {
        Mutate(doc, "ARCHWALK add observer", recordUndo, store => store.State.Records.Add(record.Clone()));
        return record;
    }

    public static bool Update(RhinoDoc doc, ObserverRecord record, bool recordUndo = true)
    {
        var ok = false;
        Mutate(doc, "ARCHWALK update observer", recordUndo, store =>
        {
            var idx = store.State.Records.FindIndex(r => r.Id == record.Id);
            if (idx < 0)
                return;
            var next = record.Clone();
            next.Revision = store.State.Records[idx].Revision + 1;
            store.State.Records[idx] = next;
            ok = true;
        });
        return ok;
    }

    public static bool Delete(RhinoDoc doc, Guid id, bool recordUndo = true)
    {
        var ok = false;
        Mutate(doc, "ARCHWALK delete observer", recordUndo, store =>
        {
            ok = store.State.Records.RemoveAll(r => r.Id == id) > 0;
        });
        return ok;
    }

    public static ObserverRecord? Duplicate(RhinoDoc doc, Guid id, bool recordUndo = true)
    {
        if (!TryGet(doc, id, out var source))
            return null;
        var copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = NextAutomaticName(doc);
        copy.Revision = 1;
        Add(doc, copy, recordUndo);
        return copy;
    }

    public static bool Rename(RhinoDoc doc, Guid id, string name, bool recordUndo = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (!TryGet(doc, id, out var record))
            return false;
        record.Name = name.Trim();
        return Update(doc, record, recordUndo);
    }

    public static bool ReplaceAll(RhinoDoc doc, IEnumerable<ObserverRecord> records, bool recordUndo = true)
    {
        Mutate(doc, "ARCHWALK observers", recordUndo, store =>
        {
            store.State.Records = records.Select(r => r.Clone()).ToList();
        });
        return true;
    }

    public static string NextAutomaticName(RhinoDoc doc)
    {
        var existing = new HashSet<string>(List(doc).Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < 10000; i++)
        {
            var name = "Наблюдатель " + i.ToString("00");
            if (!existing.Contains(name))
                return name;
        }
        return "Наблюдатель " + Guid.NewGuid().ToString("N")[..4];
    }

    public static ObserverRecord FromPose(
        CameraPose pose,
        DocumentUnits units,
        string name,
        MovementMode mode,
        double baseSpeed)
    {
        return new ObserverRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            FootXDocument = units.ToDocument(pose.FootXMeters),
            FootYDocument = units.ToDocument(pose.FootYMeters),
            FootZDocument = units.ToDocument(pose.FootZMeters),
            EyeHeightMeters = pose.EyeHeightMeters,
            YawRadians = pose.YawRadians,
            PitchRadians = pose.PitchRadians,
            VerticalFovRadians = pose.VerticalFovRadians,
            InitialMovementMode = mode,
            BaseSpeedMetersPerSecond = baseSpeed,
            Revision = 1
        };
    }

    public static CameraPose ToPose(ObserverRecord record, DocumentUnits units) => new(
        units.ToMeters(record.FootXDocument),
        units.ToMeters(record.FootYDocument),
        units.ToMeters(record.FootZDocument),
        record.EyeHeightMeters,
        record.YawRadians,
        record.PitchRadians,
        record.VerticalFovRadians);

    /// <summary>Scale foot coordinates once when Rhino scales geometry with the unit change.</summary>
    public static void ApplyGeometryScale(RhinoDoc doc, double scale, bool recordUndo = true)
    {
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            return;
        Mutate(doc, "ARCHWALK units scale", recordUndo, store =>
        {
            foreach (var record in store.State.Records)
            {
                record.FootXDocument *= scale;
                record.FootYDocument *= scale;
                record.FootZDocument *= scale;
                record.Revision++;
            }
            store.State.MetersPerDocumentUnit = UnitScaleOf(doc);
        });
    }

    /// <summary>Numeric foot coords unchanged; refresh stored meters-per-unit after a unit rename without scaling.</summary>
    public static void RefreshStoredUnitScale(RhinoDoc doc, bool recordUndo = false)
    {
        Mutate(doc, "ARCHWALK units refresh", recordUndo, store =>
        {
            store.State.MetersPerDocumentUnit = UnitScaleOf(doc);
        });
    }

    public static void InstallUnitHooks()
    {
        RhinoDoc.UnitsChangedWithScaling -= OnUnitsScaled;
        RhinoDoc.UnitsChangedWithScaling += OnUnitsScaled;
        RhinoDoc.DocumentPropertiesChanged -= OnDocProps;
        RhinoDoc.DocumentPropertiesChanged += OnDocProps;
    }

    public static void Write(RhinoDoc doc, BinaryArchiveWriter archive)
    {
        var store = Ensure(doc);
        archive.Write3dmChunkVersion(1, 0);
        archive.WriteInt(store.State.SchemaVersion);
        archive.WriteBool(store.ForbidWrite);
        archive.WriteDouble(store.State.MetersPerDocumentUnit);
        archive.WriteInt(store.State.Records.Count);
        foreach (var record in store.State.Records)
        {
            archive.WriteGuid(record.Id);
            archive.WriteString(record.Name);
            archive.WriteDouble(record.FootXDocument);
            archive.WriteDouble(record.FootYDocument);
            archive.WriteDouble(record.FootZDocument);
            archive.WriteDouble(record.EyeHeightMeters);
            archive.WriteDouble(record.YawRadians);
            archive.WriteDouble(record.PitchRadians);
            archive.WriteDouble(record.VerticalFovRadians);
            archive.WriteInt((int)record.InitialMovementMode);
            archive.WriteDouble(record.BaseSpeedMetersPerSecond);
            archive.WriteInt(record.Revision);
        }
    }

    public static void Read(RhinoDoc doc, BinaryArchiveReader archive, FileReadOptions options)
    {
        archive.Read3dmChunkVersion(out var major, out _);
        if (major > 1)
        {
            Ensure(doc).ForbidWrite = true;
            RhinoApp.WriteLine("ARCHWALK: unknown observer data version; leaving the original block untouched.");
            return;
        }

        var schema = archive.ReadInt();
        var forbid = archive.ReadBool();
        var meters = archive.ReadDouble();
        var count = archive.ReadInt();
        var records = new List<ObserverRecord>(count);
        for (var i = 0; i < count; i++)
        {
            records.Add(new ObserverRecord
            {
                Id = archive.ReadGuid(),
                Name = archive.ReadString(),
                FootXDocument = archive.ReadDouble(),
                FootYDocument = archive.ReadDouble(),
                FootZDocument = archive.ReadDouble(),
                EyeHeightMeters = archive.ReadDouble(),
                YawRadians = archive.ReadDouble(),
                PitchRadians = archive.ReadDouble(),
                VerticalFovRadians = archive.ReadDouble(),
                InitialMovementMode = (MovementMode)archive.ReadInt(),
                BaseSpeedMetersPerSecond = archive.ReadDouble(),
                Revision = archive.ReadInt()
            });
        }

        if (options.ImportMode || options.InsertMode || options.ImportReferenceMode)
            return;

        var store = Ensure(doc);
        store.ForbidWrite = forbid;
        store.State.SchemaVersion = schema;
        store.State.MetersPerDocumentUnit = meters;
        store.State.Records = records;
        RaiseChanged(doc);
    }

    public static void Remove(RhinoDoc? doc)
    {
        if (doc is null)
            return;
        Stores.Remove(doc.RuntimeSerialNumber);
    }

    static void OnUnitsScaled(object? sender, UnitsChangedWithScalingEventArgs e)
    {
        if (e.Document is null)
            return;
        ApplyGeometryScale(e.Document, e.Scale, recordUndo: true);
    }

    static void OnDocProps(object? sender, DocumentEventArgs e)
    {
        var doc = e.Document;
        if (doc is null)
            return;
        var store = Ensure(doc);
        var now = UnitScaleOf(doc);
        if (Math.Abs(store.State.MetersPerDocumentUnit - now) <= 1e-15)
            return;
        // Without scaling the numeric coordinates stay; only the stored scale tag updates.
        // Scale-with-geometry is handled by UnitsChangedWithScaling first.
        RefreshStoredUnitScale(doc, recordUndo: false);
    }

    static Store Ensure(RhinoDoc doc)
    {
        if (!Stores.TryGetValue(doc.RuntimeSerialNumber, out var store))
        {
            store = new Store
            {
                State = new ObserverDocumentState
                {
                    MetersPerDocumentUnit = UnitScaleOf(doc)
                }
            };
            Stores[doc.RuntimeSerialNumber] = store;
        }
        return store;
    }

    static void Mutate(RhinoDoc doc, string description, bool recordUndo, Action<Store> mutate)
    {
        var store = Ensure(doc);
        var before = store.State.Clone();
        uint serial = 0;
        var began = false;
        if (recordUndo)
        {
            if (!doc.UndoRecordingEnabled)
                doc.UndoRecordingEnabled = true;
            serial = doc.BeginUndoRecord(description);
            began = serial != 0;
        }
        try
        {
            mutate(store);
            store.State.MetersPerDocumentUnit = UnitScaleOf(doc);
            if (recordUndo)
                doc.AddCustomUndoEvent(description, OnUndo, before);
        }
        finally
        {
            if (began)
                doc.EndUndoRecord(serial);
        }
        RaiseChanged(doc);
    }

    static void OnUndo(object? sender, CustomUndoEventArgs e)
    {
        if (e.Tag is not ObserverDocumentState previous)
            return;
        var doc = e.Document;
        var store = Ensure(doc);
        var current = store.State.Clone();
        store.State = previous.Clone();
        doc.AddCustomUndoEvent(e.ActionDescription, OnUndo, current);
        RaiseChanged(doc);
    }

    static double UnitScaleOf(RhinoDoc doc) =>
        RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);

    static void RaiseChanged(RhinoDoc doc)
    {
        try { Changed?.Invoke(doc); }
        catch { /* UI refresh must not break data */ }
    }

    sealed class Store
    {
        public ObserverDocumentState State { get; set; } = new();
        public bool ForbidWrite { get; set; }
    }
}
