using System.Diagnostics;
using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Input;
using ArchWalk.WindowsInput;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.UI;

namespace ArchWalk.RhinoPlugin.Session;

public static class SessionController
{
    static readonly HashSet<string> OwnCommands =
    [
        "AWEnter", "AWExit", "AWReturn", "AWResetInput", "AWPlace", "AWPanel",
        "AWP0Input", "AWP0Camera", "AWP0Preview", "AWP0Data", "AWP0Ground", "AWP0RunHostTests",
        "AWP1RunHostTests", "AWP2RunHostTests"
    ];

    static readonly object Gate = new();
    static readonly Queue<Action> Queued = new();
    static readonly Stopwatch Clock = new();

    static bool _hooks;
    static bool _idleHooked;
    static bool _snapshotRestored;
    static bool _resumeArmed;
    static bool _pendingCapture;
    static long _lastTimestamp;
    static uint _docSerial;
    static Guid _viewId;
    static Guid _viewportId;
    static MovementMode _groundMode = MovementMode.Level;
    static DocumentUnits _units;
    static ViewportSnapshot? _snapshot;
    static RhinoView? _view;
    static MotionCore? _core;
    static WindowsInputBridge? _bridge;
    static WalkHud? _hud;
    static WalkMouseSink? _mouse;

    public static bool SuppressHostScripts { get; set; }
    public static SessionState State { get; private set; } = SessionState.Idle;
    public static MotionCore? Core => _core;
    public static MouseLookProfile LookProfile { get; private set; } = MouseLookProfile.FreeLook;
    public static WindowsInputBridge? Bridge => _bridge;
    public static Guid ViewId => _viewId;
    public static string? LastExitReason { get; private set; }
    public static string? LastPassthrough { get; private set; }

    public static bool IsActive => State is SessionState.Captured or SessionState.Paused or SessionState.EnterPending;

    public static void InstallHostHooks()
    {
        if (_hooks)
            return;
        _hooks = true;
        Command.BeginCommand += OnBeginCommand;
        RhinoView.Destroy += OnViewDestroy;
        RhinoView.SetActive += OnViewSetActive;
        RhinoDoc.CloseDocument += OnCloseDocument;
        RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
        RhinoDoc.UnitsChangedWithScaling += OnUnitsChanged;
        RhinoDoc.DocumentPropertiesChanged += OnDocumentPropertiesChanged;
        RhinoApp.Closing += (_, _) => Reset("rhino-closing");
    }

    public static bool Enter(
        RhinoDoc doc,
        RhinoView view,
        CameraPose pose,
        MovementMode mode,
        MouseLookProfile look,
        bool deferCapture)
    {
        if (mode == MovementMode.Surface)
            throw new ArgumentOutOfRangeException(nameof(mode), "Surface walking is P4; P1 uses Level or Fly.");
        if (!RhinoUnits.TryFromDoc(doc, out var units, out var error))
        {
            if (error is not null)
                RhinoApp.WriteLine(error);
            return false;
        }

        InputSession.Release("session-enter");
        if (IsActive)
            Exit(WalkExitKind.KeepView, "re-enter");

        State = SessionState.EnterPending;
        _view = view;
        _docSerial = doc.RuntimeSerialNumber;
        _viewId = view.MainViewport.Id;
        _viewportId = view.MainViewport.Id;
        _units = units;
        LookProfile = look;
        _groundMode = mode == MovementMode.Fly ? MovementMode.Level : mode;
        _snapshot = CameraAdapter.Capture(view.MainViewport);
        _snapshotRestored = false;
        _core = new MotionCore(pose, mode, MotionDefaults.BaseSpeedMetersPerSecond);
        LastExitReason = null;
        LastPassthrough = null;

        if (!TryApplyCamera())
        {
            RollbackFailedEnter();
            RhinoApp.WriteLine("ARCHWALK: не удалось применить камеру.");
            return false;
        }

        EnsureHud();
        EnsureIdle();
        _pendingCapture = true;
        if (!deferCapture)
            CompleteCapture();
        else
            RhinoApp.WriteLine("ARCHWALK: вход после завершения команды.");
        return true;
    }

    public static bool EnterAtDocumentPoint(
        RhinoDoc doc,
        RhinoView view,
        Point3d footDocument,
        double yawRadians,
        MovementMode mode,
        MouseLookProfile look,
        bool deferCapture)
    {
        if (!RhinoUnits.TryFromDoc(doc, out var units, out var error))
        {
            if (error is not null)
                RhinoApp.WriteLine(error);
            return false;
        }

        var pose = new CameraPose(
            units.ToMeters(footDocument.X),
            units.ToMeters(footDocument.Y),
            units.ToMeters(footDocument.Z),
            MotionDefaults.EyeHeightMeters,
            yawRadians,
            0,
            MotionDefaults.VerticalFovRadians);
        return Enter(doc, view, pose, mode, look, deferCapture);
    }

    public static void Pause(string reason)
    {
        if (State != SessionState.Captured)
            return;
        _core?.HardStop();
        DetachBridge();
        State = SessionState.Paused;
        _resumeArmed = true;
        _lastTimestamp = 0;
        UpdateHud();
        _view?.Redraw();
        RhinoApp.WriteLine("ARCHWALK пауза: " + reason);
    }

    public static bool TryResume()
    {
        if (State != SessionState.Paused)
            return false;
        if (_view is null || _view.Handle == IntPtr.Zero)
        {
            Exit(WalkExitKind.KeepView, "resume-no-view");
            return false;
        }

        if (!CompleteCapture())
            return false;
        _resumeArmed = false;
        RhinoApp.WriteLine("ARCHWALK: продолжение. Клавиши нужно нажать заново.");
        return true;
    }

    public static void Exit(WalkExitKind kind, string reason)
    {
        if (State == SessionState.Idle)
            return;

        State = SessionState.Ending;
        LastExitReason = reason;
        _pendingCapture = false;
        _resumeArmed = false;
        _core?.HardStop();
        DetachBridge();
        DisableHud();

        var view = _view;
        var snapshot = _snapshot;
        var restore = kind == WalkExitKind.RestoreSnapshot && !_snapshotRestored;
        ClearSessionFields();

        if (restore && view is not null && snapshot is not null && view.Handle != IntPtr.Zero)
        {
            try
            {
                CameraAdapter.Restore(view.MainViewport, snapshot);
                _snapshotRestored = true;
                view.Redraw();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("ARCHWALK restore: " + ex.Message);
            }
        }
        else
        {
            view?.Redraw();
        }

        State = SessionState.Idle;
        RhinoApp.WriteLine("ARCHWALK выход (" + kind + "): " + reason);
    }

    public static void Reset(string reason)
    {
        if (State == SessionState.Idle && _bridge is null)
            return;
        Exit(WalkExitKind.KeepView, reason);
    }

    public static void Enqueue(Action action)
    {
        lock (Gate)
            Queued.Enqueue(action);
    }

    public static void TickForTest(double elapsedSeconds, bool simulateHitch = false)
    {
        DrainQueue();
        if (State == SessionState.EnterPending)
            CompleteCapture();
        if (State != SessionState.Captured)
            return;
        if (simulateHitch)
        {
            Step(elapsedSeconds);
            return;
        }

        if (elapsedSeconds <= 1e-15)
        {
            Step(0);
            return;
        }

        var remaining = elapsedSeconds;
        while (remaining > 1e-15 && State == SessionState.Captured)
        {
            var slice = remaining > MotionDefaults.HitchCatchUpCapSeconds
                ? MotionDefaults.HitchCatchUpCapSeconds
                : remaining;
            Step(slice);
            remaining -= slice;
        }
    }

    static void EnsureIdle()
    {
        if (_idleHooked)
            return;
        RhinoApp.Idle += OnIdle;
        _idleHooked = true;
    }

    static void OnIdle(object? sender, EventArgs e)
    {
        DrainQueue();
        if (State == SessionState.EnterPending && _pendingCapture)
            CompleteCapture();
        if (State != SessionState.Captured)
            return;

        if (LookProfile == MouseLookProfile.RightButton && _bridge is not null && !_bridge.IsLooking && !_bridge.CursorIsInViewport())
        {
            Pause("cursor-left-view");
            return;
        }

        var now = Clock.ElapsedTicks;
        if (_lastTimestamp == 0)
        {
            _lastTimestamp = now;
            return;
        }

        var elapsed = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
        _lastTimestamp = now;
        Step(elapsed);
    }

    static void DrainQueue()
    {
        while (true)
        {
            Action? action;
            lock (Gate)
            {
                if (Queued.Count == 0)
                    return;
                action = Queued.Dequeue();
            }
            action();
        }
    }

    static bool CompleteCapture()
    {
        if (_view is null || _core is null)
        {
            RollbackFailedEnter();
            return false;
        }

        DetachBridge();
        _bridge = new WindowsInputBridge();
        _bridge.Diagnostic += line => RhinoApp.WriteLine("ARCHWALK input: " + line);
        _bridge.EscapePressed += () => Enqueue(() => Exit(WalkExitKind.KeepView, "escape"));
        _bridge.TabPressed += () => Enqueue(() => Pause("tab"));
        _bridge.BackspacePressed += () => Enqueue(() => Exit(WalkExitKind.RestoreSnapshot, "backspace"));
        _bridge.HomePressed += () => Enqueue(OnHome);
        _bridge.FlyTogglePressed += () => Enqueue(OnFlyToggle);
        _bridge.FocusLost += () => Enqueue(() => Pause("focus-lost"));
        _bridge.CursorLeftViewport += () => Enqueue(() =>
        {
            if (State == SessionState.Captured && LookProfile == MouseLookProfile.RightButton)
                Pause("cursor-left-view");
        });
        _bridge.RhinoChord += name => Enqueue(() => OnRhinoChord(name));

        var hwnd = _view.Handle;
        var main = InputSession.ResolveMainWindowHandle(hwnd);
        if (!_bridge.Capture(hwnd, main, LookProfile))
        {
            if (State == SessionState.EnterPending)
                RollbackFailedEnter();
            else
                Exit(WalkExitKind.KeepView, "capture-failed");
            return false;
        }

        _mouse ??= new WalkMouseSink();
        _mouse.Enabled = true;
        EnsureHud();
        _hud!.Enabled = true;
        Clock.Restart();
        _lastTimestamp = 0;
        _pendingCapture = false;
        State = SessionState.Captured;
        UpdateHud();
        _view.Redraw();
        return true;
    }

    static void OnHome()
    {
        if (_core is null || State != SessionState.Captured)
            return;
        _core.ResetToSessionStart();
        TryApplyCamera();
        UpdateHud();
        _view?.Redraw();
    }

    static void OnFlyToggle()
    {
        if (_core is null || State != SessionState.Captured)
            return;
        if (_core.Mode == MovementMode.Fly)
            _core.SetMode(_groundMode);
        else
        {
            _groundMode = _core.Mode == MovementMode.Surface ? MovementMode.Level : _core.Mode;
            _core.SetMode(MovementMode.Fly);
        }
        TryApplyCamera();
        UpdateHud();
        _view?.Redraw();
    }

    static void OnRhinoChord(string name)
    {
        LastPassthrough = name;
        Exit(WalkExitKind.KeepView, "chord-" + name);
        if (SuppressHostScripts)
            return;
        try
        {
            var script = name == "Undo" ? "_Undo" : "_Save";
            RhinoApp.RunScript(script, false);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine("ARCHWALK chord: " + ex.Message);
        }
    }

    static void Step(double elapsedSeconds)
    {
        if (_core is null || _view is null || _bridge is null)
            return;
        try
        {
            if (elapsedSeconds > MotionDefaults.HitchPauseSeconds)
            {
                Pause("hitch");
                return;
            }

            var sample = _bridge.ConsumeLook();
            var intent = ToIntent(sample);
            var result = _core.Advance(elapsedSeconds, intent);
            if (result.Status == MotionStepStatus.PausedDueToHitch)
            {
                Pause("hitch");
                return;
            }

            TryApplyCamera();
            UpdateHud();
            _view.Redraw();
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine("ARCHWALK tick: " + ex.Message);
            Exit(WalkExitKind.KeepView, "tick-exception");
        }
    }

    static InputIntent ToIntent(LookSample sample)
    {
        var keys = sample.DownKeys;
        bool Has(PhysicalKey key) => keys.Contains(key);
        var looking = sample.Looking;
        var yaw = looking ? LookSettings.YawRadiansFromPixels(sample.YawLogicalPixels) : 0;
        var pitch = looking ? LookSettings.PitchRadiansFromPixels(sample.PitchLogicalPixels, invertVertical: false) : 0;
        return new InputIntent(
            Forward: Has(PhysicalKey.W),
            Back: Has(PhysicalKey.S),
            Left: Has(PhysicalKey.A),
            Right: Has(PhysicalKey.D),
            Up: Has(PhysicalKey.E),
            Down: Has(PhysicalKey.Q),
            Sprint: Has(PhysicalKey.LeftShift) || Has(PhysicalKey.RightShift),
            Precise: Has(PhysicalKey.LeftCtrl) || Has(PhysicalKey.RightCtrl),
            YawDeltaRadians: yaw,
            PitchDeltaRadians: pitch,
            SpeedWheelSteps: sample.WheelSteps);
    }

    static bool TryApplyCamera()
    {
        if (_view is null || _core is null)
            return false;
        return CameraAdapter.ApplyPose(_view.MainViewport, _core.Pose, _units);
    }

    static void EnsureHud()
    {
        _hud ??= new WalkHud();
        _hud.Bind(_viewportId);
        _hud.Enabled = true;
        UpdateHud();
    }

    static void UpdateHud()
    {
        _hud?.Update(State, _core, _units, LookProfile);
    }

    static void DisableHud()
    {
        if (_hud is null)
            return;
        _hud.Enabled = false;
        _hud = null;
    }

    static void DetachBridge()
    {
        if (_mouse is not null)
        {
            _mouse.Enabled = false;
            _mouse = null;
        }
        if (_bridge is not null)
        {
            _bridge.Release();
            _bridge.Dispose();
            _bridge = null;
        }
    }

    static void RollbackFailedEnter()
    {
        var view = _view;
        var snapshot = _snapshot;
        DetachBridge();
        DisableHud();
        ClearSessionFields();
        State = SessionState.Idle;
        if (view is not null && snapshot is not null && view.Handle != IntPtr.Zero)
            CameraAdapter.Restore(view.MainViewport, snapshot);
    }

    static void ClearSessionFields()
    {
        _pendingCapture = false;
        _resumeArmed = false;
        _lastTimestamp = 0;
        _view = null;
        _core = null;
        _snapshot = null;
        _docSerial = 0;
        _viewId = Guid.Empty;
        _viewportId = Guid.Empty;
    }

    static void OnBeginCommand(object? sender, CommandEventArgs e)
    {
        NotifyForeignCommand(e.CommandEnglishName ?? "");
    }

    public static void NotifyForeignCommand(string englishName)
    {
        if (!IsActive)
            return;
        if (OwnCommands.Contains(englishName))
            return;
        Exit(WalkExitKind.KeepView, "begin-command " + englishName);
    }

    static void OnViewDestroy(object? sender, ViewEventArgs e)
    {
        if (!IsActive)
            return;
        if (e.View is null)
            return;
        if (e.View.MainViewport.Id != _viewId && e.View.MainViewport.Id != _viewportId)
            return;
        _view = null;
        Exit(WalkExitKind.KeepView, "view-destroy");
    }

    static void OnViewSetActive(object? sender, ViewEventArgs e)
    {
        if (State != SessionState.Captured || e.View is null)
            return;
        if (e.View.MainViewport.Id == _viewId)
            return;
        if (LookProfile == MouseLookProfile.RightButton)
            Pause("other-view");
    }

    static void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        if (!IsActive)
            return;
        if (e.Document.RuntimeSerialNumber != _docSerial)
            return;
        _view = null;
        Exit(WalkExitKind.KeepView, "document-close");
    }

    static void OnActiveDocumentChanged(object? sender, DocumentEventArgs e)
    {
        if (!IsActive)
            return;
        if (e.Document is null || e.Document.RuntimeSerialNumber != _docSerial)
            Exit(WalkExitKind.KeepView, "active-document");
    }

    static void OnUnitsChanged(object? sender, UnitsChangedWithScalingEventArgs e)
    {
        if (!IsActive)
            return;
        if (e.Document is not null && e.Document.RuntimeSerialNumber != _docSerial)
            return;
        Exit(WalkExitKind.KeepView, "units-scaled");
    }

    static void OnDocumentPropertiesChanged(object? sender, DocumentEventArgs e)
    {
        if (!IsActive || e.Document.RuntimeSerialNumber != _docSerial)
            return;
        if (!RhinoUnits.TryFromDoc(e.Document, out var units, out _))
        {
            Exit(WalkExitKind.KeepView, "units-invalid");
            return;
        }
        if (System.Math.Abs(units.MetersPerDocumentUnit - _units.MetersPerDocumentUnit) > 1e-15)
            Exit(WalkExitKind.KeepView, "units-changed");
    }

    public static bool HandlePausedClick(RhinoView? view)
    {
        if (State != SessionState.Paused || !_resumeArmed || view is null)
            return false;
        if (view.MainViewport.Id != _viewId)
            return false;
        return TryResume();
    }
}

sealed class WalkMouseSink : MouseCallback
{
    protected override void OnMouseDown(MouseCallbackEventArgs e)
    {
        if (SessionController.State == SessionState.Paused)
        {
            if (SessionController.HandlePausedClick(e.View))
                e.Cancel = true;
            return;
        }

        if (SessionController.State != SessionState.Captured)
            return;
        e.Cancel = true;
    }

    protected override void OnMouseUp(MouseCallbackEventArgs e)
    {
        if (SessionController.State != SessionState.Captured)
            return;
        e.Cancel = true;
    }

    protected override void OnMouseDoubleClick(MouseCallbackEventArgs e)
    {
        if (SessionController.State != SessionState.Captured)
            return;
        e.Cancel = true;
    }
}
