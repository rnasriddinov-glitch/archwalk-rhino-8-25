using System.Runtime.InteropServices;
using ArchWalk.Core.Motion;

namespace ArchWalk.WindowsInput;

public sealed class WindowsInputBridge : IDisposable
{
    readonly HashSet<PhysicalKey> _down = [];
    readonly object _gate = new();
    NativeMethods.HookProc? _hookProc;
    IntPtr _hook;
    CaptureLease? _lease;
    int _generation;
    bool _disposed;
    int _centerX;
    int _centerY;
    int _expectedX;
    int _expectedY;
    bool _expectRecenter;
    double _pendingYawPixels;
    double _pendingPitchPixels;
    int _pendingWheelSteps;
    int _eatenKeyMessages;
    int _eatenCharMessages;
    int _eatenMouseMessages;
    bool _looking;
    NativeMethods.POINT _lookCursor;
    bool _lookCursorValid;

    public event Action<PhysicalKey, bool>? KeyChanged;
    public event Action? EscapePressed;
    public event Action? TabPressed;
    public event Action? BackspacePressed;
    public event Action? HomePressed;
    public event Action? FlyTogglePressed;
    public event Action? FocusLost;
    public event Action? CursorLeftViewport;
    public event Action<string>? RhinoChord;
    public event Action<string>? Diagnostic;

    public bool IsCaptured => _lease is not null;
    public bool IsLooking => _looking;
    public MouseLookProfile LookProfile { get; private set; } = MouseLookProfile.FreeLook;
    public int EatenKeyMessages => _eatenKeyMessages;
    public int EatenCharMessages => _eatenCharMessages;
    public int EatenMouseMessages => _eatenMouseMessages;
    public int CaptureGeneration => _lease?.Generation ?? 0;
    public string? LastRhinoChord { get; private set; }

    public bool IsDown(PhysicalKey key)
    {
        lock (_gate)
            return _down.Contains(key);
    }

    public IReadOnlyCollection<PhysicalKey> SnapshotDownKeys()
    {
        lock (_gate)
            return _down.ToArray();
    }

    public bool Capture(IntPtr viewportHwnd, IntPtr mainHwnd) =>
        Capture(viewportHwnd, mainHwnd, MouseLookProfile.FreeLook);

    public bool Capture(IntPtr viewportHwnd, IntPtr mainHwnd, MouseLookProfile profile)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsInputBridge));
        if (IsCaptured)
            return true;

        LookProfile = profile;
        LastRhinoChord = null;
        NativeMethods.GetCursorPos(out var saved);
        _generation++;
        _lease = new CaptureLease(_generation, viewportHwnd, mainHwnd, saved, true);

        _hookProc = GetMsgProc;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_GETMESSAGE,
            _hookProc,
            IntPtr.Zero,
            NativeMethods.GetCurrentThreadId());
        if (_hook == IntPtr.Zero)
        {
            Diagnostic?.Invoke("SetWindowsHookEx WH_GETMESSAGE failed: " + Marshal.GetLastWin32Error());
            Release();
            return false;
        }

        if (profile == MouseLookProfile.FreeLook)
            StartLook(saveCursor: false);
        else
            _looking = false;

        Diagnostic?.Invoke($"Captured gen={_generation} hwnd={viewportHwnd} look={profile}");
        return true;
    }

    public void Release()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _hookProc = null;
        }

        if (_looking)
            StopLook(restoreCursor: false);

        lock (_gate)
        {
            _down.Clear();
            _pendingYawPixels = 0;
            _pendingPitchPixels = 0;
            _pendingWheelSteps = 0;
            _expectRecenter = false;
        }

        _lease?.Dispose();
        _lease = null;
        _looking = false;
    }

    public LookSample ConsumeLook()
    {
        lock (_gate)
        {
            var yaw = _looking ? _pendingYawPixels : 0;
            var pitch = _looking ? _pendingPitchPixels : 0;
            var wheel = _pendingWheelSteps;
            var sample = new LookSample(yaw, pitch, wheel, CopyDown(), _looking);
            _pendingYawPixels = 0;
            _pendingPitchPixels = 0;
            _pendingWheelSteps = 0;
            return sample;
        }
    }

    public void NotifyRightButton(bool down)
    {
        if (LookProfile != MouseLookProfile.RightButton || !IsCaptured)
            return;
        if (down && !_looking)
            StartLook(saveCursor: true);
        else if (!down && _looking)
            StopLook(restoreCursor: true);
    }

    public void AddLookPixels(double yawLogicalPixels, double pitchLogicalPixels)
    {
        if (!_looking)
            return;
        lock (_gate)
        {
            _pendingYawPixels += yawLogicalPixels;
            _pendingPitchPixels += pitchLogicalPixels;
        }
    }

    public void InjectScanKey(int scanCode, bool down, bool extended = false)
    {
        var hwnd = _lease?.ViewportHwnd ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
            hwnd = _lease?.MainHwnd ?? IntPtr.Zero;
        var msg = down ? NativeMethods.WM_KEYDOWN : NativeMethods.WM_KEYUP;
        var lParam = NativeMethods.KeyLParam(scanCode, !down, extended);
        NativeMethods.PostMessage(hwnd, msg, IntPtr.Zero, lParam);
    }

    public void InjectChar(char ch)
    {
        var hwnd = _lease?.MainHwnd ?? IntPtr.Zero;
        NativeMethods.PostMessage(hwnd, NativeMethods.WM_CHAR, new IntPtr(ch), IntPtr.Zero);
    }

    public int PumpPostedMessages(int max = 64)
    {
        var n = 0;
        while (n < max && NativeMethods.PeekMessage(out var msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
        {
            n++;
            if (msg.message == NativeMethods.WM_NULL)
                continue;
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
        return n;
    }

    public bool CursorIsInViewport()
    {
        if (_lease is null)
            return false;
        NativeMethods.GetCursorPos(out var pt);
        return _lease.ContainsScreenPoint(pt);
    }

    IntPtr GetMsgProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.PM_REMOVE && IsCaptured && lParam != IntPtr.Zero)
        {
            var msg = Marshal.PtrToStructure<NativeMethods.MSG>(lParam);
            if (TryHandle(ref msg))
                Marshal.StructureToPtr(msg, lParam, false);
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    bool TryHandle(ref NativeMethods.MSG msg)
    {
        switch (msg.message)
        {
            case NativeMethods.WM_ACTIVATEAPP:
                if (msg.wParam == IntPtr.Zero)
                    FocusLost?.Invoke();
                return false;
            case NativeMethods.WM_SYSKEYDOWN:
            {
                var vk = msg.wParam.ToInt32();
                if (vk is NativeMethods.VK_TAB or NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
                {
                    FocusLost?.Invoke();
                    return false;
                }
                if (IsRhinoChord(NativeMethods.ScanCode(msg.lParam), down: true))
                    return PassChord(NativeMethods.ScanCode(msg.lParam));
                EatKey(ref msg, down: true);
                return true;
            }
            case NativeMethods.WM_KEYDOWN:
                if (IsRhinoChord(NativeMethods.ScanCode(msg.lParam), down: true))
                    return PassChord(NativeMethods.ScanCode(msg.lParam));
                EatKey(ref msg, down: true);
                return true;
            case NativeMethods.WM_KEYUP:
            case NativeMethods.WM_SYSKEYUP:
                EatKey(ref msg, down: false);
                return true;
            case NativeMethods.WM_CHAR:
            case NativeMethods.WM_DEADCHAR:
            case NativeMethods.WM_SYSCHAR:
            case NativeMethods.WM_UNICHAR:
                _eatenCharMessages++;
                msg.message = NativeMethods.WM_NULL;
                return true;
            case NativeMethods.WM_MOUSEMOVE:
                return HandleMouseMove(ref msg);
            case NativeMethods.WM_MOUSEWHEEL:
            {
                if (!ShouldOwnMouse(msg))
                    return LeaveIfOutside(msg);
                var delta = (short)((msg.wParam.ToInt64() >> 16) & 0xFFFF);
                var steps = delta / NativeMethods.WHEEL_DELTA;
                lock (_gate)
                    _pendingWheelSteps += steps;
                _eatenMouseMessages++;
                msg.message = NativeMethods.WM_NULL;
                return true;
            }
            case NativeMethods.WM_RBUTTONDOWN:
                if (!ShouldOwnMouse(msg))
                    return LeaveIfOutside(msg);
                NotifyRightButton(true);
                _eatenMouseMessages++;
                msg.message = NativeMethods.WM_NULL;
                return true;
            case NativeMethods.WM_RBUTTONUP:
                if (!ShouldOwnMouse(msg) && !_looking)
                    return LeaveIfOutside(msg);
                NotifyRightButton(false);
                _eatenMouseMessages++;
                msg.message = NativeMethods.WM_NULL;
                return true;
            case NativeMethods.WM_LBUTTONDOWN:
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_MBUTTONDOWN:
            case NativeMethods.WM_MBUTTONUP:
            case NativeMethods.WM_XBUTTONDOWN:
            case NativeMethods.WM_XBUTTONUP:
            case NativeMethods.WM_MOUSEHWHEEL:
                if (!ShouldOwnMouse(msg))
                    return LeaveIfOutside(msg);
                _eatenMouseMessages++;
                msg.message = NativeMethods.WM_NULL;
                return true;
        }

        return false;
    }

    bool PassChord(int scan)
    {
        var name = scan == NativeMethods.SCAN_Z ? "Undo" : "Save";
        LastRhinoChord = name;
        RhinoChord?.Invoke(name);
        return false;
    }

    bool IsRhinoChord(int scan, bool down)
    {
        if (!down)
            return false;
        if (scan is not (NativeMethods.SCAN_S or NativeMethods.SCAN_Z))
            return false;
        lock (_gate)
        {
            if (_down.Contains(PhysicalKey.LeftCtrl) || _down.Contains(PhysicalKey.RightCtrl))
                return true;
        }
        return (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
    }

    void EatKey(ref NativeMethods.MSG msg, bool down)
    {
        _eatenKeyMessages++;
        var scan = NativeMethods.ScanCode(msg.lParam);
        var extended = NativeMethods.IsExtended(msg.lParam);
        if (TryMap(scan, extended, out var key))
        {
            var changed = false;
            lock (_gate)
            {
                if (down)
                    changed = _down.Add(key);
                else
                    changed = _down.Remove(key);
            }

            if (changed)
            {
                KeyChanged?.Invoke(key, down);
                if (down && key == PhysicalKey.Escape)
                    EscapePressed?.Invoke();
                if (down && key == PhysicalKey.Tab)
                    TabPressed?.Invoke();
                if (down && key == PhysicalKey.Backspace)
                    BackspacePressed?.Invoke();
                if (down && key == PhysicalKey.Home)
                    HomePressed?.Invoke();
                if (down && key == PhysicalKey.F)
                    FlyTogglePressed?.Invoke();
            }
        }

        msg.message = NativeMethods.WM_NULL;
    }

    bool HandleMouseMove(ref NativeMethods.MSG msg)
    {
        if (!_looking)
        {
            if (!ShouldOwnMouse(msg))
                return LeaveIfOutside(msg);
            _eatenMouseMessages++;
            msg.message = NativeMethods.WM_NULL;
            return true;
        }

        _eatenMouseMessages++;
        NativeMethods.GetCursorPos(out var pt);
        if (_expectRecenter && pt.X == _expectedX && pt.Y == _expectedY)
        {
            _expectRecenter = false;
            msg.message = NativeMethods.WM_NULL;
            return true;
        }

        var dx = pt.X - _centerX;
        var dy = pt.Y - _centerY;
        if (dx != 0 || dy != 0)
        {
            var dpi = 96u;
            if (_lease is not null && _lease.ViewportHwnd != IntPtr.Zero)
            {
                try { dpi = NativeMethods.GetDpiForWindow(_lease.ViewportHwnd); }
                catch { dpi = 96; }
            }
            if (dpi == 0) dpi = 96;
            var logicalX = dx * 96.0 / dpi;
            var logicalY = dy * 96.0 / dpi;
            lock (_gate)
            {
                _pendingYawPixels += logicalX;
                _pendingPitchPixels += logicalY;
            }
            Recenter();
        }

        msg.message = NativeMethods.WM_NULL;
        return true;
    }

    bool ShouldOwnMouse(NativeMethods.MSG msg)
    {
        if (_looking)
            return true;
        if (LookProfile == MouseLookProfile.FreeLook)
            return true;
        return _lease is not null && _lease.ContainsScreenPoint(msg.pt);
    }

    bool LeaveIfOutside(NativeMethods.MSG msg)
    {
        if (_lease is not null && !_lease.ContainsScreenPoint(msg.pt))
            CursorLeftViewport?.Invoke();
        return false;
    }

    void StartLook(bool saveCursor)
    {
        if (_lease is null || _looking)
            return;
        if (saveCursor)
        {
            _lookCursorValid = NativeMethods.GetCursorPos(out _lookCursor);
        }
        _lease.HideCursor();
        _lease.ClipToViewport();
        Recenter();
        lock (_gate)
        {
            _pendingYawPixels = 0;
            _pendingPitchPixels = 0;
        }
        _looking = true;
    }

    void StopLook(bool restoreCursor)
    {
        if (!_looking)
            return;
        _looking = false;
        _expectRecenter = false;
        lock (_gate)
        {
            _pendingYawPixels = 0;
            _pendingPitchPixels = 0;
        }
        _lease?.Unclip();
        _lease?.ShowCursor();
        if (restoreCursor && _lookCursorValid)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(foreground, out var fgPid);
            if (fgPid == NativeMethods.GetCurrentProcessId())
                NativeMethods.SetCursorPos(_lookCursor.X, _lookCursor.Y);
        }
        _lookCursorValid = false;
    }

    void Recenter()
    {
        if (_lease is null || !_lease.TryGetClientCenterScreen(out var center))
            return;
        _centerX = center.X;
        _centerY = center.Y;
        _expectedX = center.X;
        _expectedY = center.Y;
        _expectRecenter = true;
        NativeMethods.SetCursorPos(center.X, center.Y);
    }

    static bool TryMap(int scan, bool extended, out PhysicalKey key)
    {
        key = default;
        switch (scan)
        {
            case NativeMethods.SCAN_W: key = PhysicalKey.W; return true;
            case NativeMethods.SCAN_A: key = PhysicalKey.A; return true;
            case NativeMethods.SCAN_S: key = PhysicalKey.S; return true;
            case NativeMethods.SCAN_D: key = PhysicalKey.D; return true;
            case NativeMethods.SCAN_Q: key = PhysicalKey.Q; return true;
            case NativeMethods.SCAN_E: key = PhysicalKey.E; return true;
            case NativeMethods.SCAN_F: key = PhysicalKey.F; return true;
            case NativeMethods.SCAN_TAB: key = PhysicalKey.Tab; return true;
            case NativeMethods.SCAN_ESC: key = PhysicalKey.Escape; return true;
            case NativeMethods.SCAN_BACK: key = PhysicalKey.Backspace; return true;
            case NativeMethods.SCAN_LSHIFT: key = PhysicalKey.LeftShift; return true;
            case NativeMethods.SCAN_RSHIFT: key = PhysicalKey.RightShift; return true;
            case NativeMethods.SCAN_CTRL:
                key = extended ? PhysicalKey.RightCtrl : PhysicalKey.LeftCtrl;
                return true;
            case NativeMethods.SCAN_HOME:
                if (extended)
                {
                    key = PhysicalKey.Home;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    PhysicalKey[] CopyDown()
    {
        lock (_gate)
            return _down.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Release();
    }
}

public readonly record struct LookSample(
    double YawLogicalPixels,
    double PitchLogicalPixels,
    int WheelSteps,
    IReadOnlyList<PhysicalKey> DownKeys,
    bool Looking);
