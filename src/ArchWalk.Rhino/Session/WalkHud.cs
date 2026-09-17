using ArchWalk.Core.Motion;
using ArchWalk.Core.Units;
using Rhino.Display;
using Rhino.Geometry;

namespace ArchWalk.RhinoPlugin.Session;

sealed class WalkHud : DisplayConduit
{
    Guid _viewportId;
    string _text = "ARCHWALK";

    public void Bind(Guid viewportId) => _viewportId = viewportId;

    public void Update(SessionState state, MotionCore? core, DocumentUnits units, MouseLookProfile look)
    {
        if (core is null)
        {
            _text = state == SessionState.Paused ? "ARCHWALK  Пауза" : "ARCHWALK";
            return;
        }

        var mode = core.Mode == MovementMode.Fly
            ? "Полёт"
            : "По отметке Z = " + units.ToDocument(core.Pose.FootZMeters).ToString("0.###");
        var lookLabel = look == MouseLookProfile.RightButton ? "ПКМ — смотреть" : "мышь — смотреть";
        var pause = state == SessionState.Paused ? "  ПАУЗА — клик в виде чтобы продолжить" : "";
        var fov = core.Pose.VerticalFovRadians * 180.0 / System.Math.PI;
        _text = "ARCHWALK  " + mode
            + "  v=" + core.BaseSpeedMetersPerSecond.ToString("0.00") + " м/с"
            + "  H=" + core.Pose.EyeHeightMeters.ToString("0.000") + " м"
            + "  φv=" + fov.ToString("0.0") + "°"
            + "  WASD — идти · " + lookLabel + " · Tab — курсор · Esc — выйти · Backspace — исходный вид"
            + pause;
    }

    protected override void DrawForeground(DrawEventArgs e)
    {
        if (_viewportId != Guid.Empty && e.Viewport.Id != _viewportId)
            return;
        e.Display.Draw2dText(_text, System.Drawing.Color.Gold, new Point2d(16, 28), false, 13);
    }
}
