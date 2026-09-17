using System.Runtime.InteropServices;
using ArchWalk.Core.Motion;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Session;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ArchWalk.RhinoPlugin.Commands;

[Guid("a41c8e70-2b9f-4d55-9c1a-6e0f3b7d2a11")]
public sealed class AWEnterCommand : Command
{
    public override string EnglishName => "AWEnter";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var view = doc.Views.ActiveView;
        if (view is null)
        {
            RhinoApp.WriteLine("ARCHWALK: нет активного вида.");
            return Result.Failure;
        }

        var movement = MovementMode.Level;
        var look = MouseLookProfile.FreeLook;
        var gp = new GetPoint();
        gp.SetCommandPrompt("ARCHWALK: точка ног (По отметке)");
        var flyOpt = gp.AddOption("Fly");
        var rmbOpt = gp.AddOption("RightButton");
        gp.DynamicDraw += (_, e) =>
        {
            if (!RhinoUnits.TryFromDoc(doc, out var units, out var unusedError))
                return;
            var top = e.CurrentPoint + (Vector3d.ZAxis * units.ToDocument(MotionDefaults.EyeHeightMeters));
            e.Display.DrawLine(e.CurrentPoint, top, System.Drawing.Color.Gold);
            e.Display.DrawPoint(e.CurrentPoint, System.Drawing.Color.Gold);
        };

        while (true)
        {
            var result = gp.Get();
            if (result == GetResult.Option && gp.Option() is not null)
            {
                if (gp.Option()!.Index == flyOpt)
                    movement = movement == MovementMode.Fly ? MovementMode.Level : MovementMode.Fly;
                else if (gp.Option()!.Index == rmbOpt)
                    look = look == MouseLookProfile.RightButton ? MouseLookProfile.FreeLook : MouseLookProfile.RightButton;
                gp.SetCommandPrompt(
                    "ARCHWALK: точка ног  [" +
                    (movement == MovementMode.Fly ? "Полёт" : "По отметке") + ", " +
                    (look == MouseLookProfile.RightButton ? "ПКМ" : "свободный взгляд") + "]");
                continue;
            }

            if (result != GetResult.Point)
                return Result.Cancel;
            break;
        }

        var yaw = RhinoUnits.YawFromCameraDirection(view.MainViewport.CameraDirection);
        return SessionController.EnterAtDocumentPoint(doc, view, gp.Point(), yaw, movement, look, deferCapture: true)
            ? Result.Success
            : Result.Failure;
    }
}
