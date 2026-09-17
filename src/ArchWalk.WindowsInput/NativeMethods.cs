using System.Runtime.InteropServices;

namespace ArchWalk.WindowsInput;

internal static class NativeMethods
{
    public const int WH_GETMESSAGE = 3;
    public const int PM_REMOVE = 1;
    public const int WM_NULL = 0x0000;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_CHAR = 0x0102;
    public const int WM_DEADCHAR = 0x0103;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_SYSCHAR = 0x0106;
    public const int WM_UNICHAR = 0x0109;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_MOUSEHWHEEL = 0x020E;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;
    public const int WM_ACTIVATE = 0x0006;
    public const int WM_ACTIVATEAPP = 0x001C;
    public const int WM_KILLFOCUS = 0x0008;
    public const int WM_SETFOCUS = 0x0007;

    public const int VK_TAB = 0x09;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_MENU = 0x12;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int WHEEL_DELTA = 120;

    public const int SCAN_ESC = 0x01;
    public const int SCAN_Q = 0x10;
    public const int SCAN_W = 0x11;
    public const int SCAN_E = 0x12;
    public const int SCAN_A = 0x1E;
    public const int SCAN_S = 0x1F;
    public const int SCAN_D = 0x20;
    public const int SCAN_F = 0x21;
    public const int SCAN_TAB = 0x0F;
    public const int SCAN_BACK = 0x0E;
    public const int SCAN_LSHIFT = 0x2A;
    public const int SCAN_RSHIFT = 0x36;
    public const int SCAN_CTRL = 0x1D;
    public const int SCAN_Z = 0x2C;
    public const int SCAN_HOME = 0x47;
    public const int VK_S = 0x53;
    public const int VK_Z = 0x5A;
    public const int VK_RBUTTON = 0x02;

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClipCursor(ref RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentProcessId();

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public static int ScanCode(IntPtr lParam) => (int)(((long)lParam >> 16) & 0xFF);

    public static bool IsExtended(IntPtr lParam) => ((long)lParam & (1 << 24)) != 0;

    public static IntPtr KeyLParam(int scanCode, bool keyUp, bool extended = false)
    {
        var value = 1 | (scanCode << 16);
        if (extended) value |= 1 << 24;
        if (keyUp) value |= unchecked((int)0xC0000000);
        return new IntPtr(value);
    }
}
