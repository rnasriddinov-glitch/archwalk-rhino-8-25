using ArchWalk.Core.Units;
using Rhino;

namespace ArchWalk.RhinoPlugin.Camera;

public static class RhinoUnits
{
    public static bool TryFromDoc(RhinoDoc doc, out DocumentUnits units, out string? error)
    {
        units = default;
        error = null;
        var meters = RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters);
        if (doc.ModelUnitSystem is UnitSystem.None or UnitSystem.Unset || meters <= 0 || double.IsNaN(meters) || double.IsInfinity(meters))
        {
            error = "ARCHWALK: у документа нет физической единицы. Задайте единицы модели.";
            return false;
        }

        units = DocumentUnits.FromMetersPerUnit(meters);
        return true;
    }

    public static double YawFromCameraDirection(Rhino.Geometry.Vector3d direction)
    {
        var x = direction.X;
        var y = direction.Y;
        if ((x * x) + (y * y) < 1e-18)
            return 0;
        return System.Math.Atan2(x, y);
    }
}
