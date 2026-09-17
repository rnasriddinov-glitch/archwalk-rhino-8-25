namespace ArchWalk.Core.Camera;

public static class CameraOptics
{
    public static double HorizontalFovRadians(double verticalFovRadians, double aspectWidthOverHeight)
    {
        if (aspectWidthOverHeight <= 0 || double.IsNaN(aspectWidthOverHeight))
            throw new ArgumentOutOfRangeException(nameof(aspectWidthOverHeight));
        return 2.0 * System.Math.Atan(aspectWidthOverHeight * System.Math.Tan(verticalFovRadians / 2.0));
    }

    /// <summary>
    /// ViewportInfo.CameraAngle is half of the smaller frustum angle, not full vertical FOV.
    /// </summary>
    public static double RhinoHalfSmallerAngleRadians(double verticalFovRadians, double aspectWidthOverHeight)
    {
        var horizontal = HorizontalFovRadians(verticalFovRadians, aspectWidthOverHeight);
        return System.Math.Min(verticalFovRadians, horizontal) / 2.0;
    }

    public static void PerspectiveFrustum(
        double verticalFovRadians,
        double aspectWidthOverHeight,
        double nearDistance,
        out double left,
        out double right,
        out double bottom,
        out double top)
    {
        top = nearDistance * System.Math.Tan(verticalFovRadians / 2.0);
        bottom = -top;
        right = top * aspectWidthOverHeight;
        left = -right;
    }
}
