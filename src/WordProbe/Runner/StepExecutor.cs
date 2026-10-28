// Runs one case's steps against the open fixture: real keys and mouse through the session, COM for the
// set-up steps, and a record of what lay under every mouse target.
sealed class StepExecutor
{
    readonly WordSession session;
    readonly ProbeDocument probe;
    readonly Hotspots hotspots;
    bool lastWasClick;

    public StepExecutor(WordSession session, ProbeDocument probe, HotspotCalibration calibration)
    {
        this.session = session;
        this.probe = probe;
        hotspots = new(probe, calibration);
        session.Watchdog.Opened += OnDialogOpened;
    }

    /// <summary>A dialog that came up during a step that did not expect one (the message filter dismisses it).</summary>
    void OnDialogOpened(DialogInfo dialog)
    {
        if (!session.DialogAllowed)
        {
            var note = $"dialog during step: {dialog.ClassName} \"{dialog.Title}\" (dismissed)";
            if (!Remarks.Contains(note))
            {
                Remarks.Add(note);
            }
        }
    }

    public List<HotspotProbe> HotspotProbes { get; } = [];

    public List<string> Remarks { get; } = [];

    public DialogInfo? Dialog { get; private set; }

    /// <summary>When set, the selection is read after every step.</summary>
    public bool Trace { get; set; }

    public List<StepTrace> Traces { get; } = [];

    public void Execute(Step step)
    {
        Perform(step);
        if (Trace &&
            step is not SelectStep &&
            step is not OptionStep &&
            step is not WaitStep)
        {
            var selection = Observer.ReadSelection(probe.Selection);
            Traces.Add(new(step.Text, selection, StateNotation.CaretLine(probe, selection)));
        }
    }

    void Perform(Step step)
    {
        Log.Step(step.Text);
        switch (step)
        {
            case KeyStep key:
                GuardClick(false);
                session.Keyboard.Press(key.Chord, key.Repeat);
                SettleAfter(key.Chord);
                break;
            case TypeStep type:
                GuardClick(false);
                session.Keyboard.Type(type.Characters);
                Wait(SettleProfile.Type);
                break;
            case MouseStep mouse:
                ExecuteMouse(mouse);
                break;
            case SelectStep select:
                probe.Select(select.Range);
                Thread.Sleep(60);
                break;
            case OptionStep option:
                ProbeOptions.Set(session.Application, option.Member, option.Value);
                Thread.Sleep(60);
                break;
            case TrackStep track:
                probe.Document.TrackRevisions = track.On;
                break;
            case SeekStep seek:
                probe.Window.View.SeekView = seek.View;
                Thread.Sleep(150);
                break;
            case ViewStep view:
                probe.Window.View.RevisionsFilter.Markup = view.Markup;
                Thread.Sleep(150);
                break;
            case WaitStep wait:
                Thread.Sleep(wait.Milliseconds);
                break;
            case ClipboardStep clipboard:
                ClipboardText.Write(clipboard.Content);
                Thread.Sleep(60);
                break;
            case ExpectDialogStep expect:
                ExecuteDialog(expect);
                break;
            case RibbonStep ribbon:
                GuardClick(false);
                // Late bound, as RibbonReader reads the ribbon: the office interop assembly is not referenced.
                ((dynamic) session.Application).CommandBars.ExecuteMso(ribbon.IdMso);
                Wait(SettleProfile.Key);
                break;
            default:
                throw new ArgumentException($"Unknown step {step}.");
        }
    }

    void SettleAfter(KeyChord chord)
    {
        var profile = chord.VirtualKey is 0x0D or 0x09 or 0x08 or 0x2E or 0x20 ? SettleProfile.Type : SettleProfile.Key;
        Wait(profile);
    }

    void Wait(SettleProfile profile)
    {
        if (!Settle.WaitFor(probe, profile))
        {
            Remarks.Add($"settle timeout ({profile})");
        }
    }

    void GuardClick(bool thisIsClick)
    {
        if (lastWasClick && thisIsClick)
        {
            Thread.Sleep(Mouse.SeparateClickGuard);
        }

        lastWasClick = thisIsClick;
    }

    void ExecuteMouse(MouseStep step)
    {
        var mouse = session.Mouse;
        if (step.Action is MouseAction.Drag or MouseAction.DoubleDrag or MouseAction.TripleDrag)
        {
            GuardClick(true);
            var fromRange = step.From!;
            ScreenPoint from;
            if (!fromRange.IsCollapsed)
            {
                probe.Select(fromRange);
                Thread.Sleep(80);
                var selected = probe.Resolve(fromRange);
                var middle = selected.Start + (selected.End - selected.Start) / 2;
                var middleRange = probe.Document.Range(middle, middle);
                from = probe.GlyphRectangle(middleRange).Centre;
            }
            else
            {
                from = hotspots.Point(fromRange.From, Side.Left, Hotspot.None);
            }

            var to = hotspots.Point(step.To!, Side.Left, Hotspot.None);
            Probe(step.Text + " from", from);
            Probe(step.Text + " to", to);
            session.HoldingInput = true;
            try
            {
                var clicks = step.Action switch
                {
                    MouseAction.DoubleDrag => 2,
                    MouseAction.TripleDrag => 3,
                    _ => 1
                };
                mouse.Drag(from, to, step.Modifiers, clicks: clicks);
            }
            finally
            {
                session.HoldingInput = false;
            }

            Wait(SettleProfile.Mouse);
            mouse.Park();
            return;
        }

        if (step.Hotspot == Hotspot.TableHandle)
        {
            // The move handle only appears while the pointer is over the table.
            var tableIndex = step.At is TablePosition table ? table.Table : 1;
            mouse.Hover(hotspots.TableInside(tableIndex));
            Thread.Sleep(250);
        }

        var point = hotspots.Point(step.At!, step.Side, step.Hotspot);
        if (step.Action == MouseAction.Hover)
        {
            var cursor = mouse.Hover(point);
            HotspotProbes.Add(new(step.Text, point.X, point.Y, probe.Under(point), cursor.ToString()));
            return;
        }

        GuardClick(true);
        Probe(step.Text, point);
        var count = step.Action switch
        {
            MouseAction.DoubleClick => 2,
            MouseAction.TripleClick => 3,
            _ => 1
        };
        var button = step.Action == MouseAction.RightClick ? MouseButton.Right : MouseButton.Left;
        session.HoldingInput = step.Modifiers != Modifiers.None;
        try
        {
            mouse.Click(point, button, step.Modifiers, count);
        }
        finally
        {
            session.HoldingInput = false;
        }

        Wait(SettleProfile.Mouse);
        if (step.Action == MouseAction.RightClick)
        {
            // The context menu is up; it is recorded by the dialog watchdog when a step expects it, otherwise dismissed.
            Thread.Sleep(300);
            Keyboard.PressEscape();
            Thread.Sleep(150);
        }

        if (step.Hotspot != Hotspot.TableHandle &&
            step.Action != MouseAction.RightClick)
        {
            mouse.Park();
        }
    }

    void Probe(string stepText, ScreenPoint point)
    {
        var cursor = session.Mouse.Hover(point);
        HotspotProbes.Add(new(stepText, point.X, point.Y, probe.Under(point), cursor.ToString()));
    }

    void ExecuteDialog(ExpectDialogStep step)
    {
        session.DialogAllowed = true;
        try
        {
            // No COM settle here: a modal dialog rejects COM calls, and the message filter would dismiss it.
            switch (step.Inner)
            {
                case KeyStep key:
                    Log.Step(key.Text);
                    session.Keyboard.Press(key.Chord, key.Repeat);
                    break;
                case MouseStep mouse:
                    Log.Step(mouse.Text);
                    var point = hotspots.Point(mouse.At!, mouse.Side, mouse.Hotspot);
                    session.Mouse.Click(point, mouse.Action == MouseAction.RightClick ? MouseButton.Right : MouseButton.Left, mouse.Modifiers, mouse.Action == MouseAction.DoubleClick ? 2 : 1);
                    break;
                default:
                    Perform(step.Inner);
                    break;
            }

            var deadline = DateTime.Now.AddSeconds(2.5);
            DialogInfo? dialog = null;
            while (DateTime.Now < deadline)
            {
                dialog = session.Watchdog.Snapshot();
                if (dialog != null)
                {
                    break;
                }

                Thread.Sleep(100);
            }

            Dialog = dialog ?? new("(none)", "(no dialog appeared)", DateTime.Now);
            if (dialog != null &&
                step.Accept)
            {
                Log.Detail($"dialog: {dialog.ClassName} \"{dialog.Title}\" (accepted)");
                session.Keyboard.Press(KeyChord.Parse("Enter"));
                Thread.Sleep(500);
                if (session.Watchdog.Snapshot() != null)
                {
                    Remarks.Add("dialog did not close on Enter");
                    session.Watchdog.TryDismiss();
                }
            }
            else if (dialog != null)
            {
                Log.Detail($"dialog: {dialog.ClassName} \"{dialog.Title}\"");
                if (!session.Watchdog.TryDismiss())
                {
                    Remarks.Add("dialog did not close on Esc");
                }
            }
            else
            {
                Remarks.Add("no dialog appeared");
            }
        }
        finally
        {
            session.DialogAllowed = false;
        }

        Thread.Sleep(200);
        if (NativeMethods.GetForegroundWindow() != session.Window)
        {
            session.BringToFront();
        }
    }
}

record StepTrace(string Step, SelectionState Selection, string Line);
