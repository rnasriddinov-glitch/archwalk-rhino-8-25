using ArchWalk.Core.Camera;
using ArchWalk.Core.Math;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Placement;
using ArchWalk.Core.Units;

namespace ArchWalk.RhinoPlugin.Placement;

public enum PlacementPhase
{
    Idle = 0,
    PlacingFoot = 1,
    Aiming = 2,
    Ready = 3
}

public enum FootSourceKind
{
    Level = 0,
    Surface = 1
}

public sealed class PlacementDraft
{
    public PlacementPhase Phase { get; set; } = PlacementPhase.Idle;
    public FootSourceKind FootSource { get; set; } = FootSourceKind.Level;
    public bool LookAt3D { get; set; }
    public bool HasValidSupport { get; set; } = true;
    public string StatusMessage { get; set; } = string.Empty;

    public uint DocumentSerial { get; set; }
    public Guid SourceViewId { get; set; }
    public Guid TargetViewId { get; set; }
    public bool CreateNewTargetView { get; set; }
    public string TargetLabel { get; set; } = "Perspective";

    public double FootXDocument { get; set; }
    public double FootYDocument { get; set; }
    public double FootZDocument { get; set; }
    public double LevelHintZDocument { get; set; }

    public double YawRadians { get; set; }
    public double PitchRadians { get; set; }
    public double EyeHeightMeters { get; set; } = MotionDefaults.EyeHeightMeters;
    public double BaseSpeedMetersPerSecond { get; set; } = MotionDefaults.BaseSpeedMetersPerSecond;
    public double VerticalFovRadians { get; set; } = MotionDefaults.VerticalFovRadians;
    public double AspectWidthOverHeight { get; set; } = 16.0 / 9.0;

    public MovementMode MovementMode { get; set; } = MovementMode.Level;
    public MouseLookProfile LookProfile { get; set; } = MouseLookProfile.FreeLook;

    public bool HasFoot => Phase is PlacementPhase.Aiming or PlacementPhase.Ready;
    public bool IsReady => Phase == PlacementPhase.Ready;

    public Vec3 FootMeters(DocumentUnits units) => new(
        units.ToMeters(FootXDocument),
        units.ToMeters(FootYDocument),
        units.ToMeters(FootZDocument));

    public CameraPose ToPose(DocumentUnits units) => PlacementMath.PoseFromDraft(
        FootMeters(units),
        YawRadians,
        LookAt3D ? PitchRadians : 0,
        EyeHeightMeters,
        VerticalFovRadians);

    public PlacementDraft Clone() => new()
    {
        Phase = Phase,
        FootSource = FootSource,
        LookAt3D = LookAt3D,
        HasValidSupport = HasValidSupport,
        StatusMessage = StatusMessage,
        DocumentSerial = DocumentSerial,
        SourceViewId = SourceViewId,
        TargetViewId = TargetViewId,
        CreateNewTargetView = CreateNewTargetView,
        TargetLabel = TargetLabel,
        FootXDocument = FootXDocument,
        FootYDocument = FootYDocument,
        FootZDocument = FootZDocument,
        LevelHintZDocument = LevelHintZDocument,
        YawRadians = YawRadians,
        PitchRadians = PitchRadians,
        EyeHeightMeters = EyeHeightMeters,
        BaseSpeedMetersPerSecond = BaseSpeedMetersPerSecond,
        VerticalFovRadians = VerticalFovRadians,
        AspectWidthOverHeight = AspectWidthOverHeight,
        MovementMode = MovementMode,
        LookProfile = LookProfile
    };
}
