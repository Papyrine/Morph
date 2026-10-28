// Every Win32 call the harness makes: real input (SendInput), the foreground and DPI checks, cursor
// inspection for hot-spot calibration, and the clipboard. Kept in one place so the P/Invoke surface is
// auditable.
static class NativeMethods
{
    public const uint InputMouse = 0;
    public const uint InputKeyboard = 1;

    public const uint KeyEventExtendedKey = 0x0001;
    public const uint KeyEventKeyUp = 0x0002;
    public const uint KeyEventUnicode = 0x0004;

    public const uint MouseEventMove = 0x0001;
    public const uint MouseEventLeftDown = 0x0002;
    public const uint MouseEventLeftUp = 0x0004;
    public const uint MouseEventRightDown = 0x0008;
    public const uint MouseEventRightUp = 0x0010;
    public const uint MouseEventVirtualDesk = 0x4000;
    public const uint MouseEventAbsolute = 0x8000;

    public const int ScreenWidth = 0;
    public const int ScreenHeight = 1;
    public const int DoubleClickWidth = 36;
    public const int DragWidth = 68;
    public const int VirtualScreenX = 76;
    public const int VirtualScreenY = 77;
    public const int VirtualScreenWidth = 78;
    public const int VirtualScreenHeight = 79;

    public const int ShowRestore = 9;
    public const uint NoZOrder = 0x0004;
    public const uint NoActivate = 0x0010;
    public const uint WindowMessageNull = 0;
    public const uint WindowMessageClose = 0x0010;
    public const uint WindowMessageGetText = 0x000D;
    public const uint SendMessageAbortIfHung = 0x0002;

    public delegate bool EnumWindowsProcedure(IntPtr window, IntPtr parameter);
    public const uint AbortIfHung = 0x0002;

    public const uint ClipboardUnicodeText = 13;
    public const uint GlobalMoveable = 0x0002;

    public const uint ExecutionContinuous = 0x80000000;
    public const uint ExecutionSystemRequired = 0x0001;
    public const uint ExecutionDisplayRequired = 0x0002;

    public const int CursorArrow = 32512;
    public const int CursorIBeam = 32513;
    public const int CursorSizeAll = 32646;
    public const int CursorHand = 32649;

    public const int KeyShift = 0x10;
    public const int KeyControl = 0x11;
    public const int KeyMenu = 0x12;
    public const int KeyCapital = 0x14;
    public const int KeyLeftButton = 0x01;
    public const int KeyRightButton = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public Point Position;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("user32.dll")]
    public static extern short VkKeyScanExW(char character, IntPtr layout);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint attach, uint attachTo, bool flag);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr window, StringBuilder name, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    public static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProcedure callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    public static extern IntPtr SetClipboardData(uint format, IntPtr handle);

    [DllImport("user32.dll")]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    public static extern uint SetThreadExecutionState(uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LastInputInfo info);

    /// <summary>How long since the last input on this desktop, the harness's own SendInput included.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LastInputInfo
        {
            Size = (uint) Marshal.SizeOf<LastInputInfo>()
        };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(unchecked((uint) Environment.TickCount - info.Time));
    }

    public static string ClassName(IntPtr window)
    {
        var builder = new StringBuilder(256);
        GetClassNameW(window, builder, builder.Capacity);
        return builder.ToString();
    }

    public static string WindowText(IntPtr window)
    {
        var builder = new StringBuilder(512);
        GetWindowTextW(window, builder, builder.Capacity);
        return builder.ToString();
    }

    /// <summary>
    /// The text of every child control of another process's window (a dialog's labels, buttons and radio
    /// buttons), read with WM_GETTEXT since GetWindowText only answers for the caller's own process.
    /// </summary>
    public static string ChildTexts(IntPtr window)
    {
        var texts = new List<string>();
        EnumChildWindows(window, (child, _) =>
        {
            var text = RemoteText(child).Replace("\r\n", " ").Replace('\n', ' ').Trim();
            if (text.Length > 0 &&
                !texts.Contains(text))
            {
                texts.Add(text);
            }

            return true;
        }, IntPtr.Zero);
        var joined = string.Join(" | ", texts);
        return joined.Length > 600 ? joined[..600] + "…" : joined;
    }

    static string RemoteText(IntPtr window)
    {
        const int capacity = 2048;
        var buffer = Marshal.AllocHGlobal(capacity * 2);
        try
        {
            SendMessageTimeoutW(window, WindowMessageGetText, capacity, buffer, SendMessageAbortIfHung, 300, out var length);
            var count = (int) length;
            return count <= 0 ? "" : Marshal.PtrToStringUni(buffer, count);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static bool IsKeyDown(int key) =>
        (GetAsyncKeyState(key) & 0x8000) != 0;

    public static bool IsToggled(int key) =>
        (GetKeyState(key) & 1) != 0;

    public static uint ProcessOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    public static uint ThreadOf(IntPtr window) =>
        GetWindowThreadProcessId(window, out _);
}
