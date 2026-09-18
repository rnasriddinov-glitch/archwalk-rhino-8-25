using System.Globalization;

namespace ArchWalk.Core.Motion;

/// <summary>Clamp and parse helpers for user-editable height and speed.</summary>
public static class WalkSettings
{
    public static double ClampEyeHeight(double meters)
    {
        if (double.IsNaN(meters) || double.IsInfinity(meters))
            return MotionDefaults.EyeHeightMeters;
        if (meters < MotionDefaults.MinEyeHeightMeters)
            return MotionDefaults.MinEyeHeightMeters;
        if (meters > MotionDefaults.MaxEyeHeightMeters)
            return MotionDefaults.MaxEyeHeightMeters;
        return meters;
    }

    public static double ClampBaseSpeed(double metersPerSecond)
    {
        if (double.IsNaN(metersPerSecond) || double.IsInfinity(metersPerSecond))
            return MotionDefaults.BaseSpeedMetersPerSecond;
        if (metersPerSecond < MotionDefaults.MinBaseSpeedMetersPerSecond)
            return MotionDefaults.MinBaseSpeedMetersPerSecond;
        if (metersPerSecond > MotionDefaults.MaxBaseSpeedMetersPerSecond)
            return MotionDefaults.MaxBaseSpeedMetersPerSecond;
        return metersPerSecond;
    }

    /// <summary>
    /// Parse eye height. Explicit "m"/"м" → metres; "mm"/"мм" → millimetres.
    /// Bare number ≥ 10 → millimetres; bare number &lt; 10 → metres.
    /// </summary>
    public static bool TryParseEyeHeight(string? text, out double meters)
    {
        meters = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim().ToLowerInvariant().Replace(',', '.');
        var unit = UnitKind.Auto;
        // Check millimetres before metres ("мм" / "mm" before "м" / "m").
        if (s.EndsWith("мм", StringComparison.Ordinal) || s.EndsWith("mm", StringComparison.Ordinal))
        {
            unit = UnitKind.Millimetres;
            s = s[..^2].Trim();
        }
        else if (s.EndsWith("м", StringComparison.Ordinal) || s.EndsWith("m", StringComparison.Ordinal))
        {
            unit = UnitKind.Metres;
            s = s[..^1].Trim();
        }

        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return false;

        meters = unit switch
        {
            UnitKind.Millimetres => value / 1000.0,
            UnitKind.Metres => value,
            _ => value >= 10.0 ? value / 1000.0 : value
        };
        meters = ClampEyeHeight(meters);
        return true;
    }

    public static bool TryParseBaseSpeed(string? text, out double metersPerSecond)
    {
        metersPerSecond = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim().ToLowerInvariant().Replace(',', '.');
        foreach (var suffix in new[] { "м/с", "m/s", "mps" })
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                s = s[..^suffix.Length].Trim();
                break;
            }
        }

        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return false;

        metersPerSecond = ClampBaseSpeed(value);
        return true;
    }

    public static string FormatEyeHeightMillimetres(double meters) =>
        (ClampEyeHeight(meters) * 1000.0).ToString("0.###", CultureInfo.InvariantCulture);

    public static string FormatBaseSpeed(double metersPerSecond) =>
        ClampBaseSpeed(metersPerSecond).ToString("0.###", CultureInfo.InvariantCulture);

    enum UnitKind
    {
        Auto = 0,
        Metres = 1,
        Millimetres = 2
    }
}
