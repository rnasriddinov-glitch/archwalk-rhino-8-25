using ArchWalk.Core.Motion;
using Rhino;

namespace ArchWalk.RhinoPlugin.Settings;

/// <summary>Personal defaults persisted in PlugIn.Settings; used for new observers and command options.</summary>
public static class WalkUserSettings
{
    const string EyeHeightKey = "EyeHeightMeters";
    const string BaseSpeedKey = "BaseSpeedMetersPerSecond";

    static double _eyeHeight = MotionDefaults.EyeHeightMeters;
    static double _baseSpeed = MotionDefaults.BaseSpeedMetersPerSecond;
    static bool _loaded;

    public static event Action? Changed;

    public static double EyeHeightMeters
    {
        get
        {
            EnsureLoaded();
            return _eyeHeight;
        }
    }

    public static double BaseSpeedMetersPerSecond
    {
        get
        {
            EnsureLoaded();
            return _baseSpeed;
        }
    }

    public static void Set(double eyeHeightMeters, double baseSpeedMetersPerSecond)
    {
        EnsureLoaded();
        var h = WalkSettings.ClampEyeHeight(eyeHeightMeters);
        var v = WalkSettings.ClampBaseSpeed(baseSpeedMetersPerSecond);
        if (System.Math.Abs(h - _eyeHeight) < 1e-15 && System.Math.Abs(v - _baseSpeed) < 1e-15)
            return;
        _eyeHeight = h;
        _baseSpeed = v;
        Persist();
        RaiseChanged();
    }

    public static void SetEyeHeight(double meters) =>
        Set(meters, BaseSpeedMetersPerSecond);

    public static void SetBaseSpeed(double metersPerSecond) =>
        Set(EyeHeightMeters, metersPerSecond);

    static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            var settings = ArchWalkPlugIn.Instance?.Settings;
            if (settings is null)
                return;
            _eyeHeight = WalkSettings.ClampEyeHeight(
                settings.GetDouble(EyeHeightKey, MotionDefaults.EyeHeightMeters));
            _baseSpeed = WalkSettings.ClampBaseSpeed(
                settings.GetDouble(BaseSpeedKey, MotionDefaults.BaseSpeedMetersPerSecond));
        }
        catch
        {
            _eyeHeight = MotionDefaults.EyeHeightMeters;
            _baseSpeed = MotionDefaults.BaseSpeedMetersPerSecond;
        }
    }

    static void Persist()
    {
        try
        {
            var settings = ArchWalkPlugIn.Instance?.Settings;
            if (settings is null)
                return;
            settings.SetDouble(EyeHeightKey, _eyeHeight);
            settings.SetDouble(BaseSpeedKey, _baseSpeed);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine("ARCHWALK: не удалось сохранить настройки — " + ex.Message);
        }
    }

    static void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch { /* UI must not break settings */ }
    }
}
