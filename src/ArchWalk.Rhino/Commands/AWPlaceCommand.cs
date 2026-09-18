using System.Runtime.InteropServices;
using ArchWalk.Core.Motion;
using ArchWalk.RhinoPlugin.Placement;
using ArchWalk.RhinoPlugin.Session;
using ArchWalk.RhinoPlugin.Settings;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("b82e4c11-7a55-4f10-9d2e-1c8f0a6b3d44")]
public sealed class AWPlaceCommand : Command
{
    public override string EnglishName => "AWPlace";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        if (SessionController.IsActive)
        {
            RhinoApp.WriteLine("ARCHWALK: сначала завершите прогулку.");
            return Result.Cancel;
        }

        var view = doc.Views.ActiveView;
        if (view is null)
        {
            RhinoApp.WriteLine("ARCHWALK: нет активного вида.");
            return Result.Failure;
        }

        var draft = PlacementController.Draft;
        var footSource = draft.Phase == PlacementPhase.Idle ? FootSourceKind.Level : draft.FootSource;

        if (draft.Phase == PlacementPhase.Idle)
        {
            if (!PlacementController.BeginPlace(doc, view, footSource))
                return Result.Failure;
        }

        draft = PlacementController.Draft;
        if (draft.Phase is PlacementPhase.PlacingFoot or PlacementPhase.Idle)
        {
            if (!PickFoot(doc, ref footSource))
            {
                PlacementController.Cancel("esc-foot");
                return Result.Cancel;
            }
        }

        draft = PlacementController.Draft;
        if (draft.Phase is PlacementPhase.Aiming or PlacementPhase.Ready)
        {
            if (draft.Phase == PlacementPhase.Ready)
                PlacementController.EditAim();

            if (!PickAim(doc))
            {
                PlacementController.Cancel("esc-aim");
                return Result.Cancel;
            }
        }

        if (!PlacementController.ConfirmAim(doc))
        {
            PlacementController.Cancel("aim-invalid");
            return Result.Failure;
        }

        RhinoApp.WriteLine("ARCHWALK: черновик готов. Войти — _AWEnter, панель — _AWPanel.");
        return Result.Success;
    }

    static bool PickFoot(RhinoDoc doc, ref FootSourceKind footSource)
    {
        var gp = new GetPoint();
        var levelOpt = gp.AddOption("Level");
        var surfaceOpt = gp.AddOption("Surface");
        var heightMm = new OptionDouble(WalkUserSettings.EyeHeightMeters * 1000.0, 300, 2500);
        var speed = new OptionDouble(WalkUserSettings.BaseSpeedMetersPerSecond, 0.1, 6.0);
        gp.AddOptionDouble("EyeHeight", ref heightMm);
        gp.AddOptionDouble("Speed", ref speed);
        gp.DynamicDraw += (_, e) => PlacementController.DrawDynamic(doc, e.Display, e.CurrentPoint);

        while (true)
        {
            gp.SetCommandPrompt(
                footSource == FootSourceKind.Level
                    ? "ARCHWALK: место у ног (По отметке)"
                    : "ARCHWALK: место у ног (На поверхности)");
            var result = gp.Get();
            if (result == GetResult.Cancel)
                return false;
            if (result == GetResult.Option && gp.Option() is not null)
            {
                var index = gp.Option()!.Index;
                if (index == levelOpt)
                {
                    footSource = FootSourceKind.Level;
                    PlacementController.SetFootSource(FootSourceKind.Level);
                }
                else if (index == surfaceOpt)
                {
                    footSource = FootSourceKind.Surface;
                    PlacementController.SetFootSource(FootSourceKind.Surface);
                }
                else
                    ApplyCommandOptions(heightMm, speed);
                continue;
            }

            if (result != GetResult.Point)
                return false;

            if (!PlacementController.TrySetFootFromCursor(doc, gp.Point(), out var message))
            {
                RhinoApp.WriteLine(message);
                continue;
            }

            return true;
        }
    }

    static bool PickAim(RhinoDoc doc)
    {
        var lookAt3D = PlacementController.Draft.LookAt3D;
        var draft = PlacementController.Draft;
        var foot = new Rhino.Geometry.Point3d(draft.FootXDocument, draft.FootYDocument, draft.FootZDocument);
        var gp = new GetPoint();
        gp.SetCommandPrompt(lookAt3D
            ? "ARCHWALK: точка взгляда 3D"
            : "ARCHWALK: направление взгляда (горизонтально)");
        var lookOpt = gp.AddOption("LookAt3D");
        var heightMm = new OptionDouble(WalkUserSettings.EyeHeightMeters * 1000.0, 300, 2500);
        var speed = new OptionDouble(WalkUserSettings.BaseSpeedMetersPerSecond, 0.1, 6.0);
        gp.AddOptionDouble("EyeHeight", ref heightMm);
        gp.AddOptionDouble("Speed", ref speed);
        if (!lookAt3D)
            gp.Constrain(new Rhino.Geometry.Plane(foot, Rhino.Geometry.Vector3d.ZAxis), false);
        gp.DynamicDraw += (_, e) =>
        {
            PlacementController.TryUpdateAimFromWorldPoint(doc, e.CurrentPoint, out string _);
            PlacementController.DrawDynamic(doc, e.Display, e.CurrentPoint);
        };

        while (true)
        {
            var result = gp.Get();
            if (result == GetResult.Cancel)
                return false;
            if (result == GetResult.Option && gp.Option() is not null)
            {
                var index = gp.Option()!.Index;
                if (index == lookOpt)
                {
                    lookAt3D = !lookAt3D;
                    PlacementController.SetLookAt3D(lookAt3D);
                    gp.ClearConstraints();
                    if (!lookAt3D)
                        gp.Constrain(new Rhino.Geometry.Plane(foot, Rhino.Geometry.Vector3d.ZAxis), false);
                    gp.SetCommandPrompt(lookAt3D
                        ? "ARCHWALK: точка взгляда 3D"
                        : "ARCHWALK: направление взгляда (горизонтально)");
                }
                else
                    ApplyCommandOptions(heightMm, speed);
                continue;
            }

            if (result != GetResult.Point)
                return false;

            if (!PlacementController.TryUpdateAimFromWorldPoint(doc, gp.Point(), out _))
            {
                RhinoApp.WriteLine("ARCHWALK: недопустимая цель направления.");
                continue;
            }

            return true;
        }
    }

    internal static void ApplyCommandOptions(OptionDouble heightMm, OptionDouble speed)
    {
        var h = WalkSettings.ClampEyeHeight(heightMm.CurrentValue / 1000.0);
        var v = WalkSettings.ClampBaseSpeed(speed.CurrentValue);
        WalkUserSettings.Set(h, v);
        PlacementController.SetEyeHeight(h);
        PlacementController.SetBaseSpeed(v);
        RhinoApp.WriteLine(
            "ARCHWALK: H=" + WalkSettings.FormatEyeHeightMillimetres(h) + " мм, v=" +
            WalkSettings.FormatBaseSpeed(v) + " м/с");
    }
}
