using System.Diagnostics;
using ArchWalk.Core.Camera;
using ArchWalk.Core.Motion;
using ArchWalk.Core.Support;
using ArchWalk.Core.Units;
using ArchWalk.RhinoPlugin.Camera;
using ArchWalk.RhinoPlugin.Ground;
using ArchWalk.RhinoPlugin.Input;
using ArchWalk.WindowsInput;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.UI;

namespace ArchWalk.RhinoPlugin.Session;

public static class SessionController
{
    static readonly HashSet<string> OwnCommands =
    [
        "AWEnter", "AWExit", "AWReturn", "AWResetInput", "AWPlace", "AWPanel", "AWSaveView",
        "AWP0Input", "AWP0Camera", "AWP0Preview", "AWP0Data", "AWP0Ground", "AWP0RunHostTests",
        "AWP1RunHostTests", "AWP2RunHostTests", "AWP3RunHostTests", "AWP4RunHostTests",
        "AWP5RunHostTests"
    ];

    static readonly object Gate = new();
    static readonly Queue<Action> Queued = new();
    static readonly Stopwatch Clock = new();

    static bool _hooks;
    static bool _idleHooked;
    static bool _geometryHooks;
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
    static RhinoDoc? _doc;
    static MotionCore? _core;
    static WindowsInputBridge? _bridge;
    static WalkHud? _hud;
    static WalkMouseSink? _mouse;
    static GroundCache? _ground;
    static ISupportField? _support;
    static string? _statusMessage;

    public static bool SuppressHostScripts { get; set; }
    public static SessionState State { get; private set; } = SessionState.Idle;
    public static MotionCore? Core => _core;
    public static MouseLookProfile LookProfile { get; private set; } = MouseLookProfile.FreeLook;
    public static WindowsInputBridge? Bridge => _bridge;
    public static Guid ViewId => _viewId;
    public static string? LastExitReason { get; private set; }
    public static string? LastPassthrough { get; private set; }
    public static string? LastStatusMessage => _statusMessage;
    public static GroundCache? ActiveGround => _ground;

    public static bool IsActive => State is SessionState.Captured or SessionState.Paused or SessionState.EnterPending;
    public static bool IdleHooked => _idleHooked;
    public static bool HasCaptureBridge => _bridge is not null;

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
        EnsureGeometryHooks();
    }

    static void EnsureGeometryHooks()
    {
        if (_geometryHooks)
            return;
        _geometryHooks = true;
        RhinoDoc.AddRhinoObject += OnGeometryMutated;
        RhinoDoc.DeleteRhinoObject += OnGeometryMutated;
        RhinoDoc.ReplaceRhinoObject += OnGeometryReplace;
        RhinoDoc.UndeleteRhinoObject += OnGeometryMutated;
        RhinoDoc.ModifyObjectAttributes += OnAttributesChanged;
        RhinoDoc.LayerTableEvent += OnLayerTableEvent;
    }

    public static bool Enter(
        RhinoDoc doc,
        RhinoView view,
        CameraPose pose,
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

        InputSession.Release("session-enter");
        if (IsActive)
            Exit(WalkExitKind.KeepView, "re-enter");

        ISupportField? support = null;
        GroundCache? ground = null;
        if (mode == MovementMode.Surface)
        {
            ground = GroundCacheBuilder.GetOrBuild(doc, units);
            support = ground.AsSupportField();
            if (!SurfaceNavigator.CanAttach(support, pose.FootMeters, SupportTolerance(units)))
            {
                RhinoApp.WriteLine("ARCHWALK: рядом нет пола — поставьте наблюдателя на поверхность или выберите «По отметке».");
                return false;
            }
            if (!SurfaceNavigator.TryResolveFoot(
                    support,
                    pose.FootMeters,
                    pose.FootXMeters,
                    pose.FootYMeters,
                    SupportTolerance(units),
                    out var snapped,
                    out _))
            {
                RhinoApp.WriteLine("ARCHWALK: опора не подтверждена.");
                return false;
            }
            pose = pose.WithFoot(snapped);
        }

        State = SessionState.EnterPending;
        _view = view;
        _doc = doc;
        _docSerial = doc.RuntimeSerialNumber;
        _viewId = view.MainViewport.Id;
        _viewportId = view.MainViewport.Id;
        _units = units;
        LookProfile = look;
        _groundMode = mode == MovementMode.Fly ? MovementMode.Level : mode;
        _ground = ground;
        _support = support;
        _snapshot = CameraAdapter.Capture(view.MainViewport);
        _snapshotRestored = false;
        _core = new MotionCore(
            pose,
            mode,
            MotionDefaults.BaseSpeedMetersPerSecond,
            support,
            SupportTolerance(units),
            eyeSmoothing: mode == MovementMode.Surface);
        LastExitReason = null;
        LastPassthrough = null;
        _statusMessage = null;

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
        DrainQueue();
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
        ReleaseIdle();
        ArchWalk.RhinoPlugin.Observers.ObserverWorkflow.ClearSessionRecord();
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

    static void ReleaseIdle()
    {
        if (!_idleHooked)
            return;
        RhinoApp.Idle -= OnIdle;
        _idleHooked = false;
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
        {
            if (_groundMode == MovementMode.Surface)
            {
                EnsureSupportReady();
                if (_support is null || !_core.TryAttachSurface())
                {
                    _statusMessage = "Рядом нет пола — поставьте наблюдателя на поверхность";
                    RhinoApp.WriteLine("ARCHWALK: " + _statusMessage);
                    UpdateHud();
                    _view?.Redraw();
                    return;
                }
            }
            else
            {
                _core.SetMode(_groundMode);
            }
        }
        else
        {
            _groundMode = _core.Mode;
            _core.SetMode(MovementMode.Fly);
            _statusMessage = null;
        }
        TryApplyCamera();
        UpdateHud();
        _view?.Redraw();
    }

    static void EnsureSupportReady()
    {
        if (_doc is null)
            return;
        _ground = GroundCacheBuilder.GetOrBuild(_doc, _units);
        _support = _ground.AsSupportField();
        _core?.SetSupportField(_support, SupportTolerance(_units));
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

            if (_core.HudHint is not null)
                _statusMessage = _core.HudHint;

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
        return CameraAdapter.ApplyPose(_view.MainViewport, _core.RenderPose, _units);
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
        _hud?.Update(State, _core, _units, LookProfile, _statusMessage);
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
        ReleaseIdle();
        if (view is not null && snapshot is not null && view.Handle != IntPtr.Zero)
            CameraAdapter.Restore(view.MainViewport, snapshot);
    }

    static void ClearSessionFields()
    {
        _pendingCapture = false;
        _resumeArmed = false;
        _lastTimestamp = 0;
        _view = null;
        _doc = null;
        _core = null;
        _snapshot = null;
        _ground = null;
        _support = null;
        _statusMessage = null;
        _docSerial = 0;
        _viewId = Guid.Empty;
        _viewportId = Guid.Empty;
    }

    static double SupportTolerance(DocumentUnits units)
    {
        var absolute = units.MetersPerDocumentUnit * 0.001;
        return SurfaceNavigator.ClampSupportTolerance(absolute);
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
        GroundCacheBuilder.Clear(e.Document);
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
        if (e.Document is not null)
            GroundCacheBuilder.Invalidate(e.Document);
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

    static void OnGeometryMutated(object? sender, RhinoObjectEventArgs e)
    {
        if (e.TheObject?.Document is { } doc)
            InvalidateSupport(doc, "geometry");
    }

    static void OnGeometryReplace(object? sender, RhinoReplaceObjectEventArgs e)
    {
        if (e.Document is { } doc)
            InvalidateSupport(doc, "geometry-replace");
    }

    static void OnAttributesChanged(object? sender, RhinoModifyObjectAttributesEventArgs e)
    {
        if (e.Document is { } doc)
            InvalidateSupport(doc, "attributes");
    }

    static void OnLayerTableEvent(object? sender, Rhino.DocObjects.Tables.LayerTableEventArgs e)
    {
        if (e.Document is { } doc)
            InvalidateSupport(doc, "layer");
    }

    static void InvalidateSupport(RhinoDoc doc, string reason)
    {
        GroundCacheBuilder.Invalidate(doc);
        if (!IsActive || doc.RuntimeSerialNumber != _docSerial)
            return;
        Exit(WalkExitKind.KeepView, "support-invalidated:" + reason);
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
