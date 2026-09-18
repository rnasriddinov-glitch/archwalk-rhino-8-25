using ArchWalk.Core.Camera;
using ArchWalk.Core.Math;
using ArchWalk.Core.Support;

namespace ArchWalk.Core.Motion;

public enum MotionStepStatus
{
    Advanced = 0,
    PausedDueToHitch = 1
}

public readonly record struct MotionStepResult(MotionStepStatus Status, int TicksApplied, double SimulatedSeconds);

public sealed class MotionCore
{
    double _accumulator;
    Vec3 _velocity;
    CameraPose _pose;
    MovementMode _mode;
    double _baseSpeed;
    CameraPose _sessionStart;
    MovementMode _sessionStartMode;
    double _sessionStartSpeed;
    ISupportField? _support;
    double _supportToleranceMeters;
    bool _eyeSmoothing;
    double _displayEyeZ;
    string? _hudHint;

    public MotionCore(
        CameraPose pose,
        MovementMode mode,
        double baseSpeedMetersPerSecond,
        ISupportField? support = null,
        double supportToleranceMeters = MotionDefaults.SupportToleranceMinMeters,
        bool eyeSmoothing = true)
    {
        if (mode == MovementMode.Surface && support is null)
            throw new ArgumentOutOfRangeException(nameof(support), "Surface walking requires an ISupportField.");
        _pose = Sanitize(pose);
        _mode = mode;
        _baseSpeed = ClampSpeed(baseSpeedMetersPerSecond);
        _support = support;
        _supportToleranceMeters = SurfaceNavigator.ClampSupportTolerance(supportToleranceMeters);
        _eyeSmoothing = eyeSmoothing;
        _sessionStart = _pose;
        _sessionStartMode = _mode;
        _sessionStartSpeed = _baseSpeed;
        _velocity = Vec3.Zero;
        _displayEyeZ = _pose.EyeMeters.Z;
    }

    public static MotionCore CreateDefaultLevel() =>
        new(CameraPose.CreateDefault(), MovementMode.Level, MotionDefaults.BaseSpeedMetersPerSecond);

    public static MotionCore CreateDefaultFly() =>
        new(CameraPose.CreateDefault(), MovementMode.Fly, MotionDefaults.BaseSpeedMetersPerSecond);

    public static MotionCore CreateDefaultSurface(ISupportField support) =>
        new(CameraPose.CreateDefault(), MovementMode.Surface, MotionDefaults.BaseSpeedMetersPerSecond, support);

    public CameraPose Pose => _pose;

    /// <summary>Pose used for the Rhino camera; eye Z may lag physical feet during step smoothing.</summary>
    public CameraPose RenderPose
    {
        get
        {
            if (_mode != MovementMode.Surface || !_eyeSmoothing)
                return _pose;
            var footZ = _displayEyeZ - _pose.EyeHeightMeters;
            return _pose.WithFoot(new Vec3(_pose.FootXMeters, _pose.FootYMeters, footZ));
        }
    }

    public Vec3 Velocity => _velocity;
    public MovementMode Mode => _mode;
    public double BaseSpeedMetersPerSecond => _baseSpeed;
    public CameraPose SessionStart => _sessionStart;
    public MovementMode SessionStartMode => _sessionStartMode;
    public string? HudHint => _hudHint;
    public double DisplayEyeZMeters => _displayEyeZ;
    public bool EyeSmoothingEnabled => _eyeSmoothing;

    public void SetSupportField(ISupportField? support, double supportToleranceMeters = MotionDefaults.SupportToleranceMinMeters)
    {
        _support = support;
        _supportToleranceMeters = SurfaceNavigator.ClampSupportTolerance(supportToleranceMeters);
    }

    public void SetEyeSmoothing(bool enabled)
    {
        _eyeSmoothing = enabled;
        if (!enabled)
            _displayEyeZ = _pose.EyeMeters.Z;
    }

    public void RememberSessionStart()
    {
        _sessionStart = _pose;
        _sessionStartMode = _mode;
        _sessionStartSpeed = _baseSpeed;
    }

    public void ResetToSessionStart()
    {
        _pose = _sessionStart;
        _mode = _sessionStartMode;
        _baseSpeed = _sessionStartSpeed;
        _displayEyeZ = _pose.EyeMeters.Z;
        HardStop();
        _hudHint = null;
    }

    public void SetMode(MovementMode mode)
    {
        if (mode == MovementMode.Surface && _support is null)
            throw new InvalidOperationException("Surface walking requires an ISupportField.");
        if (_mode == mode)
            return;
        _mode = mode;
        HardStop();
        _hudHint = null;
        if (mode == MovementMode.Surface)
            SnapDisplayEye();
    }

    public bool TryAttachSurface()
    {
        if (_support is null)
            return false;
        if (!SurfaceNavigator.CanAttach(_support, _pose.FootMeters, _supportToleranceMeters))
            return false;
        if (!SurfaceNavigator.TryResolveFoot(
                _support,
                _pose.FootMeters,
                _pose.FootXMeters,
                _pose.FootYMeters,
                _supportToleranceMeters,
                out var foot,
                out _))
            return false;
        _pose = _pose.WithFoot(foot);
        _mode = MovementMode.Surface;
        HardStop();
        SnapDisplayEye();
        _hudHint = null;
        return true;
    }

    public void HardStop()
    {
        _velocity = Vec3.Zero;
        _accumulator = 0;
    }

    public void SetFootMeters(Vec3 foot)
    {
        _pose = _pose.WithFoot(foot);
        if (_mode != MovementMode.Surface || !_eyeSmoothing)
            _displayEyeZ = _pose.EyeMeters.Z;
    }

    public void SetBaseSpeed(double metersPerSecond) => _baseSpeed = ClampSpeed(metersPerSecond);

    /// <summary>Change eye height; foot position stays fixed. Display eye Z snaps to the new height.</summary>
    public void SetEyeHeight(double meters)
    {
        var h = WalkSettings.ClampEyeHeight(meters);
        _pose = _pose with { EyeHeightMeters = h };
        _displayEyeZ = _pose.EyeMeters.Z;
    }

    public MotionStepResult Advance(double elapsedSeconds, in InputIntent intent)
    {
        ApplyLook(intent);
        ApplyWheel(intent);

        if (elapsedSeconds < 0)
            elapsedSeconds = 0;

        if (elapsedSeconds > MotionDefaults.HitchPauseSeconds)
        {
            HardStop();
            return new MotionStepResult(MotionStepStatus.PausedDueToHitch, 0, 0);
        }

        var add = elapsedSeconds > MotionDefaults.HitchCatchUpCapSeconds
            ? MotionDefaults.HitchCatchUpCapSeconds
            : elapsedSeconds;
        _accumulator += add;

        var ticks = 0;
        while (_accumulator >= MotionDefaults.TickSeconds - 1e-15 && ticks < MotionDefaults.MaxTicksPerUpdate)
        {
            Tick(intent);
            _accumulator -= MotionDefaults.TickSeconds;
            ticks++;
        }

        return new MotionStepResult(MotionStepStatus.Advanced, ticks, ticks * MotionDefaults.TickSeconds);
    }

    void ApplyLook(in InputIntent intent)
    {
        var scale = intent.LookMultiplier;
        var yaw = _pose.YawRadians + (intent.YawDeltaRadians * scale);
        var pitch = _pose.PitchRadians + (intent.PitchDeltaRadians * scale);
        _pose = _pose.WithLook(yaw, pitch);
    }

    void ApplyWheel(in InputIntent intent)
    {
        if (intent.SpeedWheelSteps == 0)
            return;
        var speed = _baseSpeed;
        var steps = intent.SpeedWheelSteps;
        if (steps > 0)
        {
            for (var i = 0; i < steps; i++)
                speed *= MotionDefaults.WheelSpeedStep;
        }
        else
        {
            for (var i = 0; i < -steps; i++)
                speed /= MotionDefaults.WheelSpeedStep;
        }

        _baseSpeed = ClampSpeed(speed);
    }

    void Tick(in InputIntent intent)
    {
        if (_mode == MovementMode.Fly)
            TickFly(intent);
        else if (_mode == MovementMode.Surface)
            TickSurface(intent);
        else
            TickLevel(intent);
    }

    void TickLevel(in InputIntent intent)
    {
        var dt = MotionDefaults.TickSeconds;
        var basis = _pose.Basis();
        var wish = Vec3.Zero;
        if (intent.Forward) wish += basis.Horizontal;
        if (intent.Back) wish -= basis.Horizontal;
        if (intent.Right) wish += basis.Right;
        if (intent.Left) wish -= basis.Right;
        if (wish.LengthSquared > 1e-18)
            wish = wish.WithZ(0).Normalized();
        else
            wish = Vec3.Zero;

        var target = wish * (_baseSpeed * intent.HorizontalSpeedMultiplier);
        _velocity = SmoothVelocity(_velocity, target, dt);

        var foot = _pose.FootMeters + (_velocity * dt);
        var z = foot.Z;
        if (intent.Up)
            z += MotionDefaults.VerticalSpeedMetersPerSecond * dt;
        if (intent.Down)
            z -= MotionDefaults.VerticalSpeedMetersPerSecond * dt;
        foot = new Vec3(foot.X, foot.Y, z);
        _pose = _pose.WithFoot(foot);
        _displayEyeZ = _pose.EyeMeters.Z;
        _hudHint = null;
    }

    void TickSurface(in InputIntent intent)
    {
        var dt = MotionDefaults.TickSeconds;
        if (_support is null)
            throw new InvalidOperationException("Surface walking requires an ISupportField.");

        if (intent.Up || intent.Down)
            _hudHint = "F — полёт для смены уровня";
        else
            _hudHint = null;

        var basis = _pose.Basis();
        var wish = Vec3.Zero;
        if (intent.Forward) wish += basis.Horizontal;
        if (intent.Back) wish -= basis.Horizontal;
        if (intent.Right) wish += basis.Right;
        if (intent.Left) wish -= basis.Right;
        if (wish.LengthSquared > 1e-18)
            wish = wish.WithZ(0).Normalized();
        else
            wish = Vec3.Zero;

        var target = wish * (_baseSpeed * intent.HorizontalSpeedMultiplier);
        _velocity = SmoothVelocity(_velocity, target, dt);

        var from = _pose.FootMeters;
        var proposed = from + (_velocity * dt);
        var step = SurfaceNavigator.TryMove(
            _support,
            from,
            proposed.X,
            proposed.Y,
            _supportToleranceMeters);

        if (step.Status == SurfaceStepStatus.Blocked)
        {
            _velocity = Vec3.Zero;
            if (_hudHint is null)
                _hudHint = step.Hint;
        }
        else
        {
            _pose = _pose.WithFoot(step.FootMeters);
            if (step.Status == SurfaceStepStatus.Slid)
            {
                var delta = new Vec3(step.FootMeters.X - from.X, step.FootMeters.Y - from.Y, 0);
                if (delta.LengthSquared > 1e-18)
                {
                    var dir = delta.Normalized();
                    var keep = Vec3.Dot(_velocity, dir);
                    _velocity = keep > 0 ? dir * keep : Vec3.Zero;
                }
                else
                {
                    _velocity = Vec3.Zero;
                }
            }
        }

        _displayEyeZ = SurfaceNavigator.SmoothEyeZ(_displayEyeZ, _pose.EyeMeters.Z, dt, _eyeSmoothing);
    }

    void TickFly(in InputIntent intent)
    {
        var dt = MotionDefaults.TickSeconds;
        var basis = _pose.Basis();
        var wish = Vec3.Zero;
        if (intent.Forward) wish += basis.Forward;
        if (intent.Back) wish -= basis.Forward;
        if (intent.Right) wish += basis.Right;
        if (intent.Left) wish -= basis.Right;
        if (intent.Up) wish += Vec3.UnitZ;
        if (intent.Down) wish -= Vec3.UnitZ;
        if (wish.LengthSquared > 1e-18)
            wish = wish.Normalized();
        else
            wish = Vec3.Zero;

        var target = wish * (_baseSpeed * intent.HorizontalSpeedMultiplier);
        _velocity = SmoothVelocity(_velocity, target, dt);
        var eye = _pose.EyeMeters + (_velocity * dt);
        _pose = _pose.WithFoot(new Vec3(eye.X, eye.Y, eye.Z - _pose.EyeHeightMeters));
        _displayEyeZ = _pose.EyeMeters.Z;
        _hudHint = null;
    }

    void SnapDisplayEye() => _displayEyeZ = _pose.EyeMeters.Z;

    static Vec3 SmoothVelocity(Vec3 current, Vec3 target, double dt)
    {
        var currentSpeed = current.Length;
        var targetSpeed = target.Length;
        var tau = targetSpeed < currentSpeed - 1e-12
            ? MotionDefaults.BrakeTauSeconds
            : MotionDefaults.AccelTauSeconds;
        if (tau <= 0)
            return target.Length < MotionDefaults.StopSpeedMetersPerSecond ? Vec3.Zero : target;
        var alpha = 1.0 - System.Math.Exp(-dt / tau);
        var next = current + ((target - current) * alpha);
        return next.Length < MotionDefaults.StopSpeedMetersPerSecond ? Vec3.Zero : next;
    }

    static CameraPose Sanitize(CameraPose pose)
    {
        var h = pose.EyeHeightMeters;
        if (h < MotionDefaults.MinEyeHeightMeters) h = MotionDefaults.MinEyeHeightMeters;
        if (h > MotionDefaults.MaxEyeHeightMeters) h = MotionDefaults.MaxEyeHeightMeters;
        var fov = pose.VerticalFovRadians;
        var minFov = MotionDefaults.MinVerticalFovDegrees * System.Math.PI / 180.0;
        var maxFov = MotionDefaults.MaxVerticalFovDegrees * System.Math.PI / 180.0;
        if (fov < minFov) fov = minFov;
        if (fov > maxFov) fov = maxFov;
        return pose with
        {
            EyeHeightMeters = h,
            VerticalFovRadians = fov,
            YawRadians = CameraPose.NormalizeYaw(pose.YawRadians),
            PitchRadians = CameraPose.ClampPitch(pose.PitchRadians)
        };
    }

    static double ClampSpeed(double speed)
    {
        if (speed < MotionDefaults.MinBaseSpeedMetersPerSecond)
            return MotionDefaults.MinBaseSpeedMetersPerSecond;
        if (speed > MotionDefaults.MaxBaseSpeedMetersPerSecond)
            return MotionDefaults.MaxBaseSpeedMetersPerSecond;
        return speed;
    }
}
