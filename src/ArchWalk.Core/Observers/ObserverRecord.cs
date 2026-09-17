using ArchWalk.Core.Motion;

namespace ArchWalk.Core.Observers;

public sealed class ObserverRecord
{
    public const int CurrentSchemaVersion = 1;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Наблюдатель 01";
    public double FootXDocument { get; set; }
    public double FootYDocument { get; set; }
    public double FootZDocument { get; set; }
    public double EyeHeightMeters { get; set; } = MotionDefaults.EyeHeightMeters;
    public double YawRadians { get; set; }
    public double PitchRadians { get; set; }
    public double VerticalFovRadians { get; set; } = MotionDefaults.VerticalFovRadians;
    public MovementMode InitialMovementMode { get; set; } = MovementMode.Level;
    public double BaseSpeedMetersPerSecond { get; set; } = MotionDefaults.BaseSpeedMetersPerSecond;
    public int Revision { get; set; } = 1;

    public ObserverRecord Clone() => new()
    {
        Id = Id,
        Name = Name,
        FootXDocument = FootXDocument,
        FootYDocument = FootYDocument,
        FootZDocument = FootZDocument,
        EyeHeightMeters = EyeHeightMeters,
        YawRadians = YawRadians,
        PitchRadians = PitchRadians,
        VerticalFovRadians = VerticalFovRadians,
        InitialMovementMode = InitialMovementMode,
        BaseSpeedMetersPerSecond = BaseSpeedMetersPerSecond,
        Revision = Revision
    };
}

public sealed class ObserverDocumentState
{
    public int SchemaVersion { get; set; } = ObserverRecord.CurrentSchemaVersion;
    public double MetersPerDocumentUnit { get; set; } = 0.001;
    public List<ObserverRecord> Records { get; set; } = [];

    public ObserverDocumentState Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        MetersPerDocumentUnit = MetersPerDocumentUnit,
        Records = Records.Select(r => r.Clone()).ToList()
    };
}
