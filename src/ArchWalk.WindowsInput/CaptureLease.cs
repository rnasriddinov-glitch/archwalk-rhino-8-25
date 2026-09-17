namespace ArchWalk.WindowsInput;

public sealed class CaptureLease : IDisposable
{
    int _cursorHideSteps;
    bool _clipped;
    bool _cursorHidden;
    bool _disposed;

    public int Generation { get; }
    public IntPtr ViewportHwnd { get; }
    public IntPtr MainHwnd { get; }
    internal NativeMethods.POINT SavedCursor { get; }
    public bool SavedCursorValid { get; }

    internal CaptureLease(int generation, IntPtr viewportHwnd, IntPtr mainHwnd, NativeMethods.POINT savedCursor, bool savedCursorValid)
    {
        Generation = generation;
        ViewportHwnd = viewportHwnd;
        MainHwnd = mainHwnd;
        SavedCursor = savedCursor;
        SavedCursorValid = savedCursorValid;
    }

    internal void HideCursor()
    {
        if (_cursorHidden)
            return;
        var display = NativeMethods.ShowCursor(false);
        _cursorHideSteps = 1;
        while (display >= 0)
        {
            display = NativeMethods.ShowCursor(false);
            _cursorHideSteps++;
        }
        _cursorHidden = true;
    }

    internal void ClipToViewport()
    {
        if (ViewportHwnd == IntPtr.Zero)
            return;
        if (!NativeMethods.GetClientRect(ViewportHwnd, out var client))
            return;
        var topLeft = new NativeMethods.POINT { X = client.Left, Y = client.Top };
        var bottomRight = new NativeMethods.POINT { X = client.Right, Y = client.Bottom };
        if (!NativeMethods.ClientToScreen(ViewportHwnd, ref topLeft))
            return;
        if (!NativeMethods.ClientToScreen(ViewportHwnd, ref bottomRight))
            return;
        var screen = new NativeMethods.RECT
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = bottomRight.X,
            Bottom = bottomRight.Y
        };
        if (NativeMethods.ClipCursor(ref screen))
            _clipped = true;
    }

    internal void ShowCursor()
    {
        if (!_cursorHidden)
            return;
        for (var i = 0; i < _cursorHideSteps; i++)
            NativeMethods.ShowCursor(true);
        _cursorHidden = false;
        _cursorHideSteps = 0;
    }

    internal void Unclip()
    {
        if (!_clipped)
            return;
        NativeMethods.ClipCursor(IntPtr.Zero);
        _clipped = false;
    }

    internal bool ContainsScreenPoint(NativeMethods.POINT screen)
    {
        if (ViewportHwnd == IntPtr.Zero)
            return false;
        if (!NativeMethods.GetClientRect(ViewportHwnd, out var client))
            return false;
        var pt = screen;
        if (!NativeMethods.ScreenToClient(ViewportHwnd, ref pt))
            return false;
        return pt.X >= client.Left && pt.X < client.Right && pt.Y >= client.Top && pt.Y < client.Bottom;
    }

    internal bool TryGetClientCenterScreen(out NativeMethods.POINT center)
    {
        center = default;
        if (ViewportHwnd == IntPtr.Zero)
            return false;
        if (!NativeMethods.GetClientRect(ViewportHwnd, out var client))
            return false;
        center = new NativeMethods.POINT
        {
            X = (client.Left + client.Right) / 2,
            Y = (client.Top + client.Bottom) / 2
        };
        return NativeMethods.ClientToScreen(ViewportHwnd, ref center);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_clipped)
        {
            NativeMethods.ClipCursor(IntPtr.Zero);
            _clipped = false;
        }
        if (_cursorHidden)
        {
            for (var i = 0; i < _cursorHideSteps; i++)
                NativeMethods.ShowCursor(true);
            _cursorHidden = false;
            _cursorHideSteps = 0;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var fgPid);
        var sameProcess = fgPid == NativeMethods.GetCurrentProcessId();
        if (sameProcess && SavedCursorValid)
            NativeMethods.SetCursorPos(SavedCursor.X, SavedCursor.Y);
    }
}
