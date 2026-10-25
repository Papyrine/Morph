// One visible Word: started by the harness (never the user's instance), placed at a known size, kept in
// the foreground, its options snapshotted before anything is changed and restored before it quits.
record SessionSettings(
    int WindowLeft = 40,
    int WindowTop = 20,
    int WindowWidth = 1800,
    int WindowHeight = 1380,
    int ZoomPercent = 100,
    bool Visible = true);

sealed class WordSession : IDisposable
{
    readonly SessionSettings settings;
    Word.Application? application;
    MessageFilter? filter;
    DialogWatchdog? watchdog;
    bool quit;
    bool windowBaselineApplied;

    WordSession(SessionSettings settings) =>
        this.settings = settings;

    public Word.Application Application => application ?? throw new InvalidOperationException("The Word session has ended.");

    public uint ProcessId { get; private set; }

    public IntPtr Window { get; private set; }

    public WordOptionsSnapshot OriginalOptions { get; private set; } = null!;

    public DialogWatchdog Watchdog => watchdog!;

    public Keyboard Keyboard { get; private set; } = null!;

    public Mouse Mouse { get; private set; } = null!;

    public string Version => Application.Version;

    public string Build => Application.Build;

    public static WordSession Start(SessionSettings? settings = null)
    {
        var session = new WordSession(settings ?? new());
        session.Open();
        return session;
    }

    void Open()
    {
        if (NativeMethods.IsToggled(NativeMethods.KeyCapital))
        {
            throw new InvalidOperationException("Caps Lock is on; typed letters would arrive inverted. Turn it off and rerun.");
        }

        Log.Info("Starting Word");
        application = new()
        {
            Visible = settings.Visible,
            DisplayAlerts = Word.WdAlertLevel.wdAlertsNone
        };
        Log.Info($"Word {application.Version} build {application.Build}");

        // The first window comes with the first document; until then the application-level handle is
        // enough for the process id.
        ProcessId = ProcessOfWord();
        watchdog = new(ProcessId, () => Window);
        filter = MessageFilter.Register(watchdog, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1.5), () => !DialogAllowed);

        OriginalOptions = OptionsSnapshot.Capture(application);
        OptionsSnapshot.Save(OriginalOptions);
        File.WriteAllText(Paths.SessionFile, JsonSerializer.Serialize(new
        {
            pid = ProcessId,
            startedAt = DateTimeOffset.Now
        }));
        ProbeOptions.ApplyBaseline(application);

        while (application.Documents.Count > 0)
        {
            application.Documents[1].Close(Word.WdSaveOptions.wdDoNotSaveChanges);
        }

        NativeMethods.SetThreadExecutionState(NativeMethods.ExecutionContinuous | NativeMethods.ExecutionSystemRequired | NativeMethods.ExecutionDisplayRequired);
    }

    static uint ProcessOfWord()
    {
        // Word's hidden frame exists as soon as the application does; find the WINWORD started after this
        // process began by the newest WINWORD whose start time is later than ours.
        var ours = Process.GetCurrentProcess().StartTime;
        var candidates = Process.GetProcessesByName("WINWORD")
            .Where(_ => _.StartTime >= ours.AddSeconds(-2))
            .OrderByDescending(_ => _.StartTime)
            .ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No WINWORD process was started by this run.");
        }

        return (uint) candidates[0].Id;
    }

    /// <summary>Opens a fixture as an unsaved new document, places and configures its window.</summary>
    public ProbeDocument OpenFixture(string fixturePath)
    {
        CloseDocuments();
        var document = FixtureOpener.Open(Application, fixturePath);
        var window = document.ActiveWindow;
        Window = window.Hwnd;
        var windowProcess = NativeMethods.ProcessOf(Window);
        if (windowProcess != ProcessId)
        {
            Log.Warn($"Document window belongs to process {windowProcess}, not {ProcessId}; following the window.");
            ProcessId = windowProcess;
        }

        window.WindowState = Word.WdWindowState.wdWindowStateNormal;
        NativeMethods.SetWindowPos(Window, IntPtr.Zero, settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight, NativeMethods.NoZOrder | NativeMethods.NoActivate);
        var view = window.View;
        view.Type = Word.WdViewType.wdPrintView;
        view.Zoom.Percentage = settings.ZoomPercent;
        view.ShowAll = false;
        view.ShowFieldCodes = false;
        view.ShowBookmarks = false;
        view.ShowRevisionsAndComments = true;
        view.RevisionsFilter.Markup = Word.WdRevisionsMarkup.wdRevisionsMarkupAll;
        view.RevisionsFilter.View = Word.WdRevisionsView.wdRevisionsViewFinal;
        view.MarkupMode = Word.WdRevisionsMode.wdInLineRevisions;
        window.DisplayRulers = false;
        Com.Try(() => window.DocumentMap = false);
        window.VerticalPercentScrolled = 0;

        Keyboard = new(Window, AssertForeground);
        Mouse = new(AssertForeground);
        if (!windowBaselineApplied)
        {
            ProbeOptions.ApplyWindowBaseline(Application);
            windowBaselineApplied = true;
        }

        BringToFront();

        var probe = new ProbeDocument(this, document, window);
        Mouse.ParkingSpot = probe.ParkingSpot();
        Mouse.Park();
        return probe;
    }

    public void CloseDocuments()
    {
        while (application != null &&
               application.Documents.Count > 0)
        {
            application.Documents[1].Close(Word.WdSaveOptions.wdDoNotSaveChanges);
        }
    }

    public void BringToFront()
    {
        for (var round = 0; round < 3; round++)
        {
            if (NativeMethods.IsIconic(Window))
            {
                NativeMethods.ShowWindow(Window, NativeMethods.ShowRestore);
            }

            Com.Try(() =>
            {
                Application.Activate();
                return true;
            });
            NativeMethods.SetForegroundWindow(Window);
            Thread.Sleep(80);
            if (NativeMethods.GetForegroundWindow() == Window)
            {
                return;
            }

            var front = NativeMethods.GetForegroundWindow();
            var frontThread = NativeMethods.ThreadOf(front);
            var ours = NativeMethods.GetCurrentThreadId();
            if (frontThread != 0 &&
                NativeMethods.AttachThreadInput(ours, frontThread, true))
            {
                NativeMethods.SetForegroundWindow(Window);
                NativeMethods.AttachThreadInput(ours, frontThread, false);
                Thread.Sleep(80);
                if (NativeMethods.GetForegroundWindow() == Window)
                {
                    return;
                }
            }

            if (Mouse != null)
            {
                Mouse.Click(ProbeDocument.ActivationSpot(Window));
                Thread.Sleep(150);
                Mouse.Park();
                if (NativeMethods.GetForegroundWindow() == Window)
                {
                    return;
                }
            }
        }

        throw new ForegroundLostException($"Word's window could not be brought to the foreground; '{NativeMethods.WindowText(NativeMethods.GetForegroundWindow())}' holds it.");
    }

    public bool DialogAllowed { get; set; }

    /// <summary>Called before every input batch: the user must not be at the keyboard, and Word must be in front.</summary>
    public void AssertForeground()
    {
        if (NativeMethods.IsKeyDown(NativeMethods.KeyShift) ||
            NativeMethods.IsKeyDown(NativeMethods.KeyControl) ||
            NativeMethods.IsKeyDown(NativeMethods.KeyMenu) ||
            NativeMethods.IsKeyDown(NativeMethods.KeyLeftButton) ||
            NativeMethods.IsKeyDown(NativeMethods.KeyRightButton))
        {
            // The harness's own synthetic modifier presses also show here; tolerate the ones it holds.
            if (!HoldingInput)
            {
                throw new ForegroundLostException("A modifier or mouse button is held down: someone is at the keyboard.");
            }
        }

        var front = NativeMethods.GetForegroundWindow();
        if (front == Window)
        {
            return;
        }

        if (DialogAllowed &&
            NativeMethods.ProcessOf(front) == ProcessId)
        {
            return;
        }

        throw new ForegroundLostException($"Word lost the foreground to '{NativeMethods.WindowText(front)}' ({NativeMethods.ClassName(front)}).");
    }

    /// <summary>Set while the harness itself holds a modifier or a mouse button (drags, modifier clicks).</summary>
    public bool HoldingInput { get; set; }

    public bool IsHung()
    {
        if (Window == IntPtr.Zero)
        {
            return false;
        }

        var result = NativeMethods.SendMessageTimeoutW(Window, NativeMethods.WindowMessageNull, IntPtr.Zero, IntPtr.Zero, NativeMethods.AbortIfHung, 5000, out _);
        return result == IntPtr.Zero;
    }

    public void RestoreOptionsAndQuit()
    {
        if (quit)
        {
            return;
        }

        quit = true;
        try
        {
            if (application != null)
            {
                var failed = OptionsSnapshot.Apply(application, OriginalOptions, ProbeOptions.Touched);
                if (failed.Count > 0)
                {
                    Log.Warn($"Options not restored: {string.Join(", ", failed)}");
                }
                else
                {
                    OptionsSnapshot.MarkRestored();
                }

                CloseDocuments();
                application.NormalTemplate.Saved = true;
                application.Quit(Word.WdSaveOptions.wdDoNotSaveChanges);
            }
        }
        catch (COMException exception)
        {
            Log.Warn($"Quit failed: {exception.Message}");
        }
        finally
        {
            Com.Release(ref application);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var deadline = DateTime.Now.AddSeconds(10);
            while (DateTime.Now < deadline &&
                   IsAlive())
            {
                Thread.Sleep(200);
            }

            if (IsAlive())
            {
                Log.Warn($"WINWORD {ProcessId} did not exit; killing it.");
                Com.Try(() =>
                {
                    Process.GetProcessById((int) ProcessId).Kill();
                    return true;
                });
            }

            if (File.Exists(Paths.SessionFile))
            {
                File.Delete(Paths.SessionFile);
            }
        }
    }

    bool IsAlive()
    {
        try
        {
            return !Process.GetProcessById((int) ProcessId).HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        RestoreOptionsAndQuit();
        filter?.Dispose();
        watchdog?.Dispose();
        NativeMethods.SetThreadExecutionState(NativeMethods.ExecutionContinuous);
    }
}
