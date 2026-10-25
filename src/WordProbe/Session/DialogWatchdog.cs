// Watches for a Word-owned window other than the document window taking the foreground: a dialog, or
// a pane that took focus. Win32 only, no COM, so it can run on its own thread while the STA thread is
// blocked inside a rejected call.
record DialogInfo(string ClassName, string Title, DateTime SeenAt)
{
    /// <summary>The text of the dialog's controls (its message, its radio buttons, its buttons), where Win32 can read them.</summary>
    public string? Text { get; init; }

    /// <summary>The window handle, for dismissing; not part of the record that is saved.</summary>
    [JsonIgnore]
    public IntPtr Window { get; init; }
}

sealed class DialogWatchdog : IDisposable
{
    readonly uint wordProcessId;
    readonly Func<IntPtr> mainWindow;
    readonly Thread thread;
    volatile bool stopping;
    DialogInfo? current;

    public DialogWatchdog(uint wordProcessId, Func<IntPtr> mainWindow)
    {
        this.wordProcessId = wordProcessId;
        this.mainWindow = mainWindow;
        thread = new(Poll)
        {
            IsBackground = true,
            Name = "WordProbe dialog watchdog"
        };
        thread.Start();
    }

    public DialogInfo? Current => current;

    public event Action<DialogInfo>? Opened;

    public DialogInfo? Snapshot()
    {
        var front = NativeMethods.GetForegroundWindow();
        if (front == IntPtr.Zero ||
            front == mainWindow() ||
            NativeMethods.ProcessOf(front) != wordProcessId)
        {
            return null;
        }

        return new(NativeMethods.ClassName(front), NativeMethods.WindowText(front), DateTime.Now)
        {
            Window = front,
            Text = NativeMethods.ChildTexts(front)
        };
    }

    /// <summary>Esc to the dialog, then WM_CLOSE if it is still there. Returns true when it went away.</summary>
    public bool TryDismiss()
    {
        var dialog = Snapshot();
        if (dialog == null)
        {
            return true;
        }

        Keyboard.PressEscape();
        Thread.Sleep(300);
        if (Snapshot() == null)
        {
            return true;
        }

        NativeMethods.PostMessageW(dialog.Window, NativeMethods.WindowMessageClose, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(300);
        return Snapshot() == null;
    }

    void Poll()
    {
        while (!stopping)
        {
            try
            {
                var dialog = Snapshot();
                if (dialog != null &&
                    current?.Window != dialog.Window)
                {
                    current = dialog;
                    Opened?.Invoke(dialog);
                }
                else if (dialog == null)
                {
                    current = null;
                }
            }
            catch (Exception)
            {
                // The poll never takes the run down.
            }

            Thread.Sleep(200);
        }
    }

    public void Dispose()
    {
        stopping = true;
        thread.Join(1000);
    }
}
