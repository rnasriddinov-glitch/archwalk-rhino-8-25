using ArchWalk.WindowsInput;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.UI;

namespace ArchWalk.RhinoPlugin.Input;

public static class InputSession
{
    static WindowsInputBridge? _bridge;
    static HudConduit? _hud;
    static WalkMouseCallback? _mouse;
    static bool _idleHooked;
    static string? _queuedRelease;

    public static WindowsInputBridge? Bridge => _bridge;
    public static bool IsCaptured => _bridge?.IsCaptured == true;

    public static bool Capture(RhinoView view, string reason)
    {
        Session.SessionController.Reset("p0-capture");
        Release("recapture");
        _bridge = new WindowsInputBridge();
        _bridge.Diagnostic += line => RhinoApp.WriteLine("ARCHWALK input: " + line);
        _bridge.EscapePressed += () => _queuedRelease = "escape";
        _bridge.FocusLost += () => _queuedRelease = "focus-lost";
        var hwnd = view.Handle;
        var main = ResolveMainWindowHandle(hwnd);
        if (!_bridge.Capture(hwnd, main))
            return false;

        _mouse = new WalkMouseCallback { Enabled = true };
        _hud = new HudConduit { Enabled = true };
        if (!_idleHooked)
        {
            RhinoApp.Idle += OnIdle;
            _idleHooked = true;
        }
        view.Redraw();
        RhinoApp.WriteLine($"ARCHWALK P0A capture ON ({reason}). Esc releases. WASD uses physical keys.");
        return true;
    }

    public static void Release(string reason)
    {
        _queuedRelease = null;
        if (_mouse is not null)
        {
            _mouse.Enabled = false;
            _mouse = null;
        }
        if (_hud is not null)
        {
            _hud.Enabled = false;
            _hud = null;
        }
        if (_bridge is not null)
        {
            _bridge.Release();
            _bridge.Dispose();
            _bridge = null;
            RhinoApp.WriteLine("ARCHWALK input released: " + reason);
        }
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    public static IntPtr ResolveMainWindowHandle(IntPtr fallback)
    {
        var prop = typeof(RhinoApp).GetProperty("MainWindowHandle");
        var value = prop?.GetValue(null);
        return value switch
        {
            IntPtr pointer => pointer,
            Func<IntPtr> getter => getter(),
            _ => fallback
        };
    }

    static void OnIdle(object? sender, EventArgs e)
    {
        if (_queuedRelease is not null)
        {
            var reason = _queuedRelease;
            Release(reason);
            return;
        }
        if (_bridge is null || !_bridge.IsCaptured)
            return;
        var sample = _bridge.ConsumeLook();
        _hud?.Update(sample);
    }
}

sealed class WalkMouseCallback : MouseCallback
{
    protected override void OnMouseDown(MouseCallbackEventArgs e)
    {
        if (!InputSession.IsCaptured)
            return;
        e.Cancel = true;
    }

    protected override void OnMouseUp(MouseCallbackEventArgs e)
    {
        if (!InputSession.IsCaptured)
            return;
        e.Cancel = true;
    }

    protected override void OnMouseDoubleClick(MouseCallbackEventArgs e)
    {
        if (!InputSession.IsCaptured)
            return;
        e.Cancel = true;
    }
}

sealed class HudConduit : DisplayConduit
{
    string _text = "ARCHWALK P0A";

    public void Update(LookSample sample)
    {
        var keys = sample.DownKeys.Count == 0 ? "—" : string.Join(" ", sample.DownKeys);
        _text = $"ARCHWALK P0A  keys {keys}  look {sample.YawLogicalPixels:0.0},{sample.PitchLogicalPixels:0.0}";
    }

    protected override void DrawForeground(DrawEventArgs e)
    {
        if (!InputSession.IsCaptured)
            return;
        e.Display.Draw2dText(_text, System.Drawing.Color.Gold, new Point2d(16, 28), false, 14);
    }
}
