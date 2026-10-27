// Real mouse input through SendInput, in physical screen pixels. Absolute moves are normalised over the
// virtual screen and verified with GetCursorPos, so a click lands on the pixel Word reported for the glyph.
record ScreenPoint(int X, int Y)
{
    public ScreenPoint Offset(int dx, int dy) =>
        new(X + dx, Y + dy);
}

record ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public ScreenPoint Centre => new(Left + Width / 2, Top + Height / 2);

    public ScreenPoint At(double acrossFraction, double downFraction) =>
        new(Left + (int) Math.Round(Width * acrossFraction), Top + (int) Math.Round(Height * downFraction));
}

enum MouseButton
{
    Left,
    Right
}

sealed class Mouse
{
    public const int MultiClickInterval = 110;
    public const int DragStepDelay = 15;
    public const int PressHoldBeforeDrag = 150;
    public const int ReleaseHold = 60;

    readonly ScreenRect virtualScreen;
    readonly Action? beforeInput;

    public Mouse(Action? beforeInput)
    {
        this.beforeInput = beforeInput;
        virtualScreen = new(
            NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenX),
            NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenY),
            NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenWidth),
            NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenHeight));
    }

    public ScreenRect VirtualScreen => virtualScreen;

    /// <summary>The time two separate clicks must be apart so Word never reads them as a double-click.</summary>
    public static int SeparateClickGuard => (int) NativeMethods.GetDoubleClickTime() + 150;

    public ScreenPoint? ParkingSpot { get; set; }

    public void MoveTo(ScreenPoint point)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Send([Move(point)]);
            Thread.Sleep(10);
            NativeMethods.GetCursorPos(out var actual);
            if (actual.X == point.X &&
                actual.Y == point.Y)
            {
                return;
            }

            if (attempt == 2)
            {
                NativeMethods.SetCursorPos(point.X, point.Y);
            }
        }
    }

    public void Click(ScreenPoint point, MouseButton button = MouseButton.Left, Modifiers modifiers = Modifiers.None, int count = 1)
    {
        beforeInput?.Invoke();
        MoveTo(point);
        Thread.Sleep(40);
        if (modifiers != Modifiers.None)
        {
            Keyboard.ModifierDown(modifiers);
            Thread.Sleep(30);
        }

        for (var i = 0; i < count; i++)
        {
            Send([Button(button, down: true)]);
            Thread.Sleep(30);
            Send([Button(button, down: false)]);
            if (i < count - 1)
            {
                Thread.Sleep(MultiClickInterval);
            }
        }

        if (modifiers != Modifiers.None)
        {
            Thread.Sleep(30);
            Keyboard.ModifierUp(modifiers);
        }
    }

    // clicks above 1 makes the press the last of a double- or triple-click, so the drag extends by words or
    // by paragraphs.
    public void Drag(ScreenPoint from, ScreenPoint to, Modifiers modifiers = Modifiers.None, MouseButton button = MouseButton.Left, int steps = 10, int clicks = 1)
    {
        beforeInput?.Invoke();
        MoveTo(from);
        Thread.Sleep(40);
        if (modifiers != Modifiers.None)
        {
            Keyboard.ModifierDown(modifiers);
            Thread.Sleep(30);
        }

        for (var i = 1; i < clicks; i++)
        {
            Send([Button(button, down: true)]);
            Thread.Sleep(30);
            Send([Button(button, down: false)]);
            Thread.Sleep(MultiClickInterval);
        }

        Send([Button(button, down: true)]);
        Thread.Sleep(PressHoldBeforeDrag);
        for (var i = 1; i <= steps; i++)
        {
            var x = from.X + (to.X - from.X) * i / steps;
            var y = from.Y + (to.Y - from.Y) * i / steps;
            Send([Move(new(x, y))]);
            Thread.Sleep(DragStepDelay);
        }

        MoveTo(to);
        Thread.Sleep(ReleaseHold);
        Send([Button(button, down: false)]);
        if (modifiers != Modifiers.None)
        {
            Thread.Sleep(30);
            Keyboard.ModifierUp(modifiers);
        }
    }

    /// <summary>Moves to the point and reports the cursor Word shows there.</summary>
    public CursorKind Hover(ScreenPoint point)
    {
        MoveTo(point);
        Thread.Sleep(90);
        return CurrentCursor();
    }

    public static CursorKind CurrentCursor()
    {
        var info = new NativeMethods.CursorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.CursorInfo>()
        };
        if (!NativeMethods.GetCursorInfo(ref info))
        {
            return CursorKind.Unknown;
        }

        if (info.Cursor == NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.CursorIBeam))
        {
            return CursorKind.IBeam;
        }

        if (info.Cursor == NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.CursorArrow))
        {
            return CursorKind.Arrow;
        }

        if (info.Cursor == NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.CursorSizeAll))
        {
            return CursorKind.SizeAll;
        }

        if (info.Cursor == NativeMethods.LoadCursorW(IntPtr.Zero, NativeMethods.CursorHand))
        {
            return CursorKind.Hand;
        }

        return info.Cursor == IntPtr.Zero ? CursorKind.Unknown : CursorKind.WordOwn;
    }

    public void Park()
    {
        if (ParkingSpot != null)
        {
            MoveTo(ParkingSpot);
        }
    }

    NativeMethods.Input Move(ScreenPoint point) =>
        new()
        {
            Type = NativeMethods.InputMouse,
            Union = new()
            {
                Mouse = new()
                {
                    X = ((point.X - virtualScreen.Left) * 65536 + virtualScreen.Width / 2) / virtualScreen.Width,
                    Y = ((point.Y - virtualScreen.Top) * 65536 + virtualScreen.Height / 2) / virtualScreen.Height,
                    Flags = NativeMethods.MouseEventMove | NativeMethods.MouseEventAbsolute | NativeMethods.MouseEventVirtualDesk
                }
            }
        };

    static NativeMethods.Input Button(MouseButton button, bool down) =>
        new()
        {
            Type = NativeMethods.InputMouse,
            Union = new()
            {
                Mouse = new()
                {
                    Flags = (button, down) switch
                    {
                        (MouseButton.Left, true) => NativeMethods.MouseEventLeftDown,
                        (MouseButton.Left, false) => NativeMethods.MouseEventLeftUp,
                        (MouseButton.Right, true) => NativeMethods.MouseEventRightDown,
                        _ => NativeMethods.MouseEventRightUp
                    }
                }
            }
        };

    static void Send(NativeMethods.Input[] inputs)
    {
        var sent = NativeMethods.SendInput((uint) inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Length} mouse inputs (error {Marshal.GetLastWin32Error()}).");
        }
    }
}

enum CursorKind
{
    Unknown,
    IBeam,
    Arrow,
    SizeAll,
    Hand,
    WordOwn
}
