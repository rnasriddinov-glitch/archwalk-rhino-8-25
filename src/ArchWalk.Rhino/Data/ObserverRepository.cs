using ArchWalk.Core.Observers;
using Rhino;
using Rhino.Commands;
using Rhino.FileIO;

namespace ArchWalk.RhinoPlugin.Data;

public static class ObserverRepository
{
    static readonly Dictionary<uint, Store> Stores = [];

    public static ObserverDocumentState Get(RhinoDoc doc) => Ensure(doc).State.Clone();

    public static IReadOnlyList<ObserverRecord> List(RhinoDoc doc) => Ensure(doc).State.Records.Select(r => r.Clone()).ToList();

    public static bool ShouldWrite(RhinoDoc doc)
    {
        var store = Ensure(doc);
        return !store.ForbidWrite && store.State.Records.Count > 0;
    }

    public static ObserverRecord Add(RhinoDoc doc, ObserverRecord record, bool recordUndo = true)
    {
        Mutate(doc, "ARCHWALK add observer", recordUndo, store => store.State.Records.Add(record.Clone()));
        return record;
    }

    public static bool ReplaceAll(RhinoDoc doc, IEnumerable<ObserverRecord> records, bool recordUndo = true)
    {
        Mutate(doc, "ARCHWALK observers", recordUndo, store =>
        {
            store.State.Records = records.Select(r => r.Clone()).ToList();
        });
        return true;
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
                InitialMovementMode = (Core.Motion.MovementMode)archive.ReadInt(),
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
    }

    public static void Remove(RhinoDoc? doc)
    {
        if (doc is null)
            return;
        Stores.Remove(doc.RuntimeSerialNumber);
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
    }

    static double UnitScaleOf(RhinoDoc doc) =>
        RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);

    sealed class Store
    {
        public ObserverDocumentState State { get; set; } = new();
        public bool ForbidWrite { get; set; }
    }
}
