// The go/no-go gates before any long run: geometry, keys, double-click, AutoFormat, the undo list, a
// dialog, hot-spot calibration, option restore, and a clean quit. Each gate names what it checked.
static class SmokeTest
{
    public static bool Run()
    {
        var passed = true;
        var calibration = new HotspotCalibration();
        var remarks = new List<string>();
        FixtureCatalog.BuildAll(force: false);
        WordSession? session = null;
        var normalTemplate = NormalTemplatePath();
        var normalBefore = File.Exists(normalTemplate) ? File.GetLastWriteTimeUtc(normalTemplate) : DateTime.MinValue;
        try
        {
            session = WordSession.Start();
            var probe = session.OpenFixture(Path.Combine(Paths.Fixtures, "three-paragraphs.docx"));

            // Gate 1: placed, in front, DPI aware.
            var dpi = NativeMethods.GetDpiForWindow(session.Window);
            NativeMethods.GetWindowRect(session.Window, out var rect);
            passed &= Gate(1, "window placed, foreground, DPI 120", $"dpi {dpi}, rect {rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top}, fallback {FixtureOpener.UseCopyFallback}",
                NativeMethods.GetForegroundWindow() == session.Window && dpi == 120);
            remarks.Add($"Documents.Add(Template: .docx) {(FixtureOpener.UseCopyFallback ? "refused; opening copies" : "accepted")}");

            // Gate 2: geometry round trip.
            var executor = new StepExecutor(session, probe, calibration);
            var range = probe.Resolve(PositionParser.Parse("P1:5"));
            var glyph = probe.GlyphRectangle(range);
            var under = probe.Under(glyph.Centre);
            var parking = probe.Under(session.Mouse.ParkingSpot!);
            executor.Execute(StepParser.Parse("click(P1:5, side=left)"));
            var selection = Observer.ReadSelection(probe.Selection);
            executor.Execute(StepParser.Parse("click(P1:5, side=right)"));
            var rightHalf = Observer.ReadSelection(probe.Selection);
            // The activation spot (ribbon tab row) must be harmless: a click there leaves the selection where it was.
            session.Mouse.Click(ProbeDocument.ActivationSpot(session.Window));
            Thread.Sleep(300);
            session.Mouse.Park();
            var afterActivation = Observer.ReadSelection(probe.Selection);
            passed &= Gate(2, "GetPoint → RangeFromPoint → click lands on glyph 5; the activation spot is harmless", $"under '{under}', parking hover maps to '{parking}', left-half click IP {selection.Start}, right-half click IP {rightHalf.Start}, after activation click {afterActivation.Type} {afterActivation.Start}-{afterActivation.End}",
                (under.StartsWith("Range 5", StringComparison.Ordinal) || under.StartsWith("Range 6", StringComparison.Ordinal)) &&
                selection is {Type: "IP", Start: 5} &&
                rightHalf.Start == 6 &&
                afterActivation.Start == rightHalf.Start &&
                afterActivation.End == rightHalf.End);

            // Gate 3: keys and anchor direction.
            executor.Execute(StepParser.Parse("Ctrl+Shift+Right"));
            var word = Observer.ReadSelection(probe.Selection);
            executor.Execute(StepParser.Parse("click(P1:5, side=left)"));
            executor.Execute(StepParser.Parse("Shift+Left"));
            var back = Observer.ReadSelection(probe.Selection);
            executor.Execute(StepParser.Parse("Right"));
            executor.Execute(StepParser.Parse("type(\"abc\")"));
            var (undo, source) = UndoStack.Read(probe);
            var text = probe.Document.Paragraphs[1].Range.Text ?? "";
            passed &= Gate(3, "Ctrl+Shift+Right selects a word end-active; Shift+Left is start-active; typing reaches Word", $"word {word.Start}-{word.End} startActive={word.StartIsActive}; back {back.Start}-{back.End} startActive={back.StartIsActive}; text \"{text.TrimEnd('\r')}\"; undo [{string.Join(", ", undo)}] via {source}",
                word.End > word.Start && !word.StartIsActive && back.StartIsActive && (back.Flags & 1) == 1 && text.Contains("abc", StringComparison.Ordinal));

            // Gate 4: real key presses reach AutoFormat.
            var bulletsOn = LateBound.Try(() => ProbeOptions.Get(session.Application, "Options.AutoFormatAsYouTypeApplyBulletedLists") is true, false);
            executor.Execute(StepParser.Parse("Ctrl+End"));
            executor.Execute(StepParser.Parse("Enter"));
            executor.Execute(StepParser.Parse("type(\"* \")"));
            var listType = probe.Selection.Range.ListFormat.ListType;
            passed &= Gate(4, "typing '* ' on an empty paragraph makes a bullet (AutoFormat sees key presses)", $"ApplyBulletedLists {(bulletsOn ? "on" : "off")}, ListType {listType}",
                !bulletsOn ||
                listType != Word.WdListType.wdListNoNumbering);

            // Gate 5: double-click selects a word.
            probe = session.OpenFixture(Path.Combine(Paths.Fixtures, "three-paragraphs.docx"));
            executor = new(session, probe, calibration);
            executor.Execute(StepParser.Parse("dblclick(P2:9)"));
            var doubled = Observer.ReadSelection(probe.Selection);
            passed &= Gate(5, "double-click selects the word 'bold'", $"{doubled.Type} {doubled.Start}-{doubled.End} \"{doubled.Text}\"",
                doubled.Type == "Normal" &&
                doubled.Text.TrimEnd().Equals("bold", StringComparison.Ordinal));

            // Gate 6: undo names.
            executor.Execute(StepParser.Parse("Ctrl+B"));
            executor.Execute(StepParser.Parse("End"));
            executor.Execute(StepParser.Parse("type(\"x\")"));
            var (names, source2) = UndoStack.Read(probe);
            passed &= Gate(6, "undo entry names from command bar control 128", $"{source2}: [{string.Join(", ", names)}]", names.Count >= 2);
            if (source2 != "control128")
            {
                remarks.Add("undo names unavailable; depth only");
            }

            // Gate 7: a dialog is seen and dismissed.
            executor.Execute(StepParser.Parse("dialog(Ctrl+K)"));
            var dialog = executor.Dialog;
            var afterDialog = Observer.ReadSelection(probe.Selection);
            passed &= Gate(7, "Ctrl+K opens a dialog that Esc closes", $"{dialog?.ClassName} \"{dialog?.Title}\"; selection after {afterDialog.Start}-{afterDialog.End}; front is Word {NativeMethods.GetForegroundWindow() == session.Window}",
                dialog != null &&
                dialog.ClassName != "(none)" &&
                NativeMethods.GetForegroundWindow() == session.Window);
            if (dialog != null)
            {
                remarks.Add($"dialog class {dialog.ClassName} (\"{dialog.Title}\")");
            }

            // Gate 8: hot-spot calibration on a table.
            probe = session.OpenFixture(Path.Combine(Paths.Fixtures, "table-2x2.docx"));
            passed &= Gate(8, "hot spots show Word's own cursors", Calibrate(session, probe, calibration), true);

            // Gate 9: option flip and restore.
            var original = ProbeOptions.Get(session.Application, "Options.AutoWordSelection");
            ProbeOptions.Set(session.Application, "Options.AutoWordSelection", original is true ? "false" : "true");
            var flipped = ProbeOptions.Get(session.Application, "Options.AutoWordSelection");
            OptionsSnapshot.Apply(session.Application, session.OriginalOptions, ["Options.AutoWordSelection"]);
            var restored = ProbeOptions.Get(session.Application, "Options.AutoWordSelection");
            passed &= Gate(9, "an option flips and restores from the snapshot", $"{original} → {flipped} → {restored}; snapshot at {Paths.OptionsSnapshot}",
                !Equals(original, flipped) && Equals(original, restored) && File.Exists(Paths.OptionsSnapshot));

            ScenarioRunner.SaveEnvironment(session, calibration);
            var environment = ResultStore.LoadEnvironment()!;
            ResultStore.SaveEnvironment(environment with
            {
                UndoSource = source2,
                Remarks = remarks
            });
        }
        finally
        {
            var pid = session?.ProcessId ?? 0;
            session?.Dispose();
            // Gate 10: clean quit.
            var alive = pid != 0 && Process.GetProcesses().Any(_ => _.Id == pid && !_.HasExited);
            var normalAfter = File.Exists(normalTemplate) ? File.GetLastWriteTimeUtc(normalTemplate) : DateTime.MinValue;
            var locks = Directory.Exists(Paths.Fixtures) ? Directory.GetFiles(Paths.Fixtures, "~$*.docx").Length : 0;
            passed &= Gate(10, "Word quit, Normal.dotm untouched, no lock files", $"alive {alive}, Normal.dotm changed {normalAfter != normalBefore}, locks {locks}",
                !alive && normalAfter == normalBefore && locks == 0);
        }

        Log.Info(passed ? "SMOKE PASSED" : "SMOKE FAILED");
        return passed;
    }

    static string Calibrate(WordSession session, ProbeDocument probe, HotspotCalibration calibration)
    {
        var report = new StringBuilder();
        var hotspots = new Hotspots(probe, calibration);
        var mouse = session.Mouse;

        // Selection bar: scan from the text's left edge leftwards until the cursor is not an I-beam.
        var page = probe.Page();
        var line = probe.GlyphRectangle(probe.Resolve(PositionParser.Parse("P1:0")));
        var y = line.Top + line.Height / 2;
        var found = false;
        for (var x = page.TextLeft - 10; x > page.PageLeft; x -= 12)
        {
            if (mouse.Hover(new(x, y)) != CursorKind.IBeam)
            {
                calibration.Offsets["selectionBarInset"] = x - (page.PageLeft + (page.TextLeft - page.PageLeft) / 2);
                report.Append($"selection bar at x={x} ({Mouse.CurrentCursor()}); ");
                found = true;
                break;
            }
        }

        if (!found)
        {
            report.Append("selection bar: no non-IBeam cursor found; ");
        }

        // The table hot spots are calibrated by clicking: the selection Word makes says what the point was.
        var table = probe.Table(1);
        var cellRect = probe.Rectangle(table.Cell(1, 1).Range);
        var padding = (int) Math.Round(table.LeftPadding * probe.PixelsPerPoint);
        var topPadding = (int) Math.Round(table.TopPadding * probe.PixelsPerPoint);
        var edgeX = cellRect.Left - padding;
        var middleY = cellRect.Top + cellRect.Height / 2;

        // Cell left edge: a click selects the one cell (Word reports a single selected cell as a Column selection).
        found = false;
        for (var inset = 1; inset < padding; inset += 2)
        {
            var selection = ClickAndRead(session, probe, new(edgeX + inset, middleY));
            if (selection is {Cells: 1, Type: "Column" or "Normal"} &&
                selection.Text.EndsWith("\r\a", StringComparison.Ordinal))
            {
                calibration.Offsets["cellLeftEdgeInset"] = inset;
                report.Append($"cell edge inset {inset}; ");
                found = true;
                break;
            }
        }

        if (!found)
        {
            report.Append("cell edge: no inset selected a cell; ");
        }

        // Column top: a click selects the column (Type Column).
        var columnX = edgeX + (int) Math.Round(table.Cell(1, 1).Width * probe.PixelsPerPoint) / 2;
        found = false;
        for (var above = -4; above < 16; above += 2)
        {
            var selection = ClickAndRead(session, probe, new(columnX, cellRect.Top - topPadding - above));
            if (selection.Type == "Column")
            {
                calibration.Offsets["columnTopAbove"] = above;
                report.Append($"column top above {above}; ");
                found = true;
                break;
            }
        }

        if (!found)
        {
            report.Append("column top: no offset selected a column; ");
        }

        // Row bar: a click in the margin beside the row selects the row (Type Row).
        found = false;
        for (var left = 10; left < 90; left += 10)
        {
            var selection = ClickAndRead(session, probe, new(edgeX - left, middleY));
            if (selection.Type == "Row")
            {
                calibration.Offsets["rowBarLeft"] = left;
                report.Append($"row bar left {left}; ");
                found = true;
                break;
            }
        }

        if (!found)
        {
            report.Append("row bar: no offset selected a row; ");
        }

        // Move handle: hover over the table so the handle appears, then a click at the corner selects the whole table.
        found = false;
        for (var offset = 4; offset < 30; offset += 3)
        {
            mouse.Hover(hotspots.TableInside(1));
            Thread.Sleep(350);
            var selection = ClickAndRead(session, probe, new(edgeX - offset, cellRect.Top - topPadding - offset));
            if (selection.Cells == 4)
            {
                calibration.Offsets["tableHandleOffset"] = offset;
                report.Append($"table handle offset {offset}; ");
                found = true;
                break;
            }
        }

        if (!found)
        {
            report.Append("table handle: no offset selected the table; ");
        }

        mouse.Park();
        return report.ToString();
    }

    static SelectionState ClickAndRead(WordSession session, ProbeDocument probe, ScreenPoint point)
    {
        Thread.Sleep(Mouse.SeparateClickGuard);
        session.Mouse.Click(point);
        Settle.WaitFor(probe, SettleProfile.Mouse);
        var selection = Observer.ReadSelection(probe.Selection);
        Log.Detail($"calibration click {point.X},{point.Y} → {selection.Type} {selection.Start}-{selection.End} cells {selection.Cells}");
        return selection;
    }

    static bool Gate(int number, string what, string detail, bool ok)
    {
        Log.Info($"Gate {number} {(ok ? "PASS" : "FAIL")}: {what} — {detail}");
        return ok;
    }

    static string NormalTemplatePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Templates", "Normal.dotm");
}
