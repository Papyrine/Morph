// Runs the catalogue: one Word session, every selected family in order, every case not yet recorded on
// this build of Word, each result written as soon as it is in, the markdown re-rendered after each family.
// A run that stops (a crash, someone at the keyboard, a time budget) is resumed by running it again.
static class ScenarioRunner
{
    const int casesPerSession = 150;
    const int lossesPerCase = 3;
    static readonly TimeSpan idleBeforeResuming = TimeSpan.FromSeconds(10);
    static readonly TimeSpan longestPause = TimeSpan.FromMinutes(30);

    public static bool Run(RunOptions options)
    {
        var families = Catalog.Select(options.Families);
        if (families.Count == 0)
        {
            Log.Warn("No families matched.");
            return false;
        }

        var build = ClickToRunVersion();
        var planned = families.Select(family =>
            {
                var results = ResultStore.Load(family.Id);
                var cases = family.Scenarios().SelectMany(CaseExpander.Expand).ToList();
                if (options.ScenarioId != null)
                {
                    cases = cases.Where(_ => _.Scenario.Id == options.ScenarioId || _.Id == options.ScenarioId || _.Id.StartsWith(options.ScenarioId, StringComparison.Ordinal)).ToList();
                }

                var pending = cases.Where(_ => (options.Force || !IsDone(results, _, build)) && !options.Skipped.Any(skip => _.Id.StartsWith(skip, StringComparison.Ordinal))).ToList();
                return (Family: family, Results: results, Cases: cases, Pending: pending);
            })
            .ToList();
        var total = planned.Sum(_ => _.Pending.Count);
        Log.Info($"{total} cases to run in {planned.Count(_ => _.Pending.Count > 0)} families");

        var userClipboard = ClipboardText.Read();
        var deadline = options.MaxMinutes == null ? DateTime.MaxValue : DateTime.Now.AddMinutes(options.MaxMinutes.Value);
        var calibration = HotspotCalibration.Load();
        var ok = true;
        WordSession? session = null;
        var casesInSession = 0;
        var done = 0;
        var elapsed = Stopwatch.StartNew();
        var paused = new Stopwatch();
        try
        {
            session = WordSession.Start();
            foreach (var (family, results, cases, pending) in planned)
            {
                Log.Info($"Family {family.Id}: {cases.Count} cases, {pending.Count} to run");
                var inFamily = 0;
                foreach (var probeCase in pending)
                {
                    if (DateTime.Now > deadline)
                    {
                        Log.Info("Time budget reached; stopping at a case boundary.");
                        ResultStore.Save(results);
                        return ok;
                    }

                    done++;
                    inFamily++;
                    var eta = Eta(elapsed, done - 1, total);
                    var progress = $"[{done}/{total}]";
                    var caption = $"WordProbe · {family.Id} {inFamily}/{pending.Count} · {done}/{total}{(eta == null ? "" : $" · ETA {eta:HH:mm}")} · {probeCase.Id}";
                    CaseResult result;
                    var losses = 0;
                    var crashes = 0;
                    while (true)
                    {
                        if (casesInSession >= casesPerSession ||
                            session.IsHung())
                        {
                            Log.Info("Restarting Word");
                            session.Dispose();
                            session = WordSession.Start();
                            casesInSession = 0;
                        }

                        casesInSession++;
                        result = RunCase(session, probeCase, calibration, options.Markup, progress, caption);
                        if (session.HasExited())
                        {
                            // Word itself fell over (T-05's tracked cut of a row, 2026-10-27): the case is run once
                            // more in a new Word, and recorded as the crash if it takes that one down too.
                            result = result with
                            {
                                Status = "word-crashed"
                            };
                            crashes++;
                            Log.Warn($"Word crashed on {probeCase.Id}; restarting it.");
                            session.Dispose();
                            session = WordSession.Start();
                            casesInSession = 0;
                            if (crashes < 2)
                            {
                                continue;
                            }

                            break;
                        }

                        if (result.Status != "foreground-lost")
                        {
                            break;
                        }

                        losses++;
                        var reason = result.Remarks.LastOrDefault() ?? "foreground lost";
                        if (losses >= lossesPerCase)
                        {
                            results.Cases[probeCase.Id] = result;
                            ResultStore.Save(results);
                            throw new ForegroundLostException($"{reason} ({losses} times on {probeCase.Id})");
                        }

                        WaitUntilIdle(reason, paused);
                        session.BringToFront();
                    }

                    results.Cases[probeCase.Id] = result;
                    results.Word = WordInformation(session);
                    ResultStore.Save(results);
                    if (result.Status is not ("ok" or "not-applicable"))
                    {
                        ok = false;
                    }

                    if (done % 25 == 0)
                    {
                        Log.Info($"Progress: {done}/{total} in {elapsed.Elapsed:h\\:mm\\:ss}{(eta == null ? "" : $", ETA {Eta(elapsed, done, total):HH:mm}")}");
                    }
                }

                ResultStore.Save(results);
                LogPhases(family.Id, pending.Select(_ => results.Cases[_.Id]).ToList());
                SaveEnvironment(session, calibration);
                MarkdownRenderer.Render();
            }
        }
        finally
        {
            session?.Dispose();
            if (userClipboard != null)
            {
                Com.Try(() =>
                {
                    ClipboardText.Write(userClipboard);
                    return true;
                });
            }
        }

        return ok;
    }

    // Each phase's median and total over the cases a family ran, and the whole family's.
    static void LogPhases(string family, List<CaseResult> ran)
    {
        if (ran.Count == 0)
        {
            return;
        }

        var parts = ran
            .SelectMany(_ => _.Phases ?? [])
            .GroupBy(_ => _.Key, StringComparer.Ordinal)
            .Select(_ =>
            {
                var times = _.Select(phase => phase.Value).Order().ToList();
                return $"{_.Key} {times[times.Count / 2]} ms ({times.Sum() / 1000.0:0.0} s)";
            });
        Log.Info($"Phases of {family}, median (total) over {ran.Count} cases: {string.Join(" · ", parts)}; all {ran.Sum(_ => _.DurationMs) / 1000.0:0.0} s");
    }

    // When the run will end, at the pace of the cases run so far (restarts and pauses included).
    static DateTime? Eta(Stopwatch elapsed, int done, int total)
    {
        if (done == 0)
        {
            return null;
        }

        return DateTime.Now + elapsed.Elapsed / done * (total - done);
    }

    // Someone took the foreground or is at the keyboard: the run waits for them to stop rather than end.
    static void WaitUntilIdle(string reason, Stopwatch paused)
    {
        Log.Warn($"Paused: {reason}. Resuming once the machine has been idle for {idleBeforeResuming.TotalSeconds:0} s.");
        paused.Start();
        while (NativeMethods.IdleTime() < idleBeforeResuming)
        {
            if (paused.Elapsed > longestPause)
            {
                throw new ForegroundLostException($"{reason} (paused {paused.Elapsed:h\\:mm\\:ss} in all)");
            }

            Thread.Sleep(1000);
        }

        paused.Stop();
        // A modifier still down after the idle wait is the harness's own, left held by the case that stopped.
        var held = Modifiers.None;
        if (NativeMethods.IsKeyDown(NativeMethods.KeyShift))
        {
            held |= Modifiers.Shift;
        }

        if (NativeMethods.IsKeyDown(NativeMethods.KeyControl))
        {
            held |= Modifiers.Ctrl;
        }

        if (NativeMethods.IsKeyDown(NativeMethods.KeyMenu))
        {
            held |= Modifiers.Alt;
        }

        if (held != Modifiers.None)
        {
            Log.Warn($"Releasing {held}, still held after the pause.");
            Keyboard.ModifierUp(held);
        }

        Log.Info("Resuming.");
    }

    // Done when it ran cleanly, to the scenario as it is now, on this build of Word. A case recorded before
    // results carried their build takes its family's.
    static bool IsDone(FamilyResults results, ProbeCase probeCase, string build) =>
        results.Cases.TryGetValue(probeCase.Id, out var existing) &&
        existing.Status is "ok" or "not-applicable" &&
        existing.DefinitionHash == probeCase.DefinitionHash &&
        (existing.Build ?? results.Word?.ClickToRun) == build;

    public static WordInfo WordInformation(WordSession session) =>
        new(session.Version, session.Build, UiLanguage(session), (int) NativeMethods.GetDpiForWindow(session.Window), ClickToRunVersion());

    static string UiLanguage(WordSession session)
    {
        try
        {
            var settings = LateBound.Get(session.Application, "LanguageSettings")!;
            var id = Convert.ToInt32(LateBound.Get(settings, "LanguageID", 2), CultureInfo.InvariantCulture);
            return CultureInfo.GetCultureInfo(id).Name;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    static string ClickToRunVersion()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration");
            return key?.GetValue("VersionToReport")?.ToString() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static void SaveEnvironment(WordSession session, HotspotCalibration calibration)
    {
        var existing = ResultStore.LoadEnvironment();
        var environment = new EnvironmentInfo(
            WordInformation(session),
            DateTimeOffset.Now,
            $"{NativeMethods.GetSystemMetrics(NativeMethods.ScreenWidth)}x{NativeMethods.GetSystemMetrics(NativeMethods.ScreenHeight)} at {NativeMethods.GetDpiForWindow(session.Window) * 100 / 96} %",
            ProbeOptions.ReadRecorded(session.Application),
            calibration.Offsets,
            existing?.UndoSource ?? "control128",
            FixtureOpener.UseCopyFallback,
            existing?.Remarks ?? []);
        ResultStore.SaveEnvironment(environment);
    }

    public static CaseResult RunCase(WordSession session, ProbeCase probeCase, HotspotCalibration calibration, bool markupAlways, string progress = "", string? caption = null)
    {
        var watch = Stopwatch.StartNew();
        // Where the case's time went, phase by phase: what makes a run's time something to cut rather than wait out.
        var phases = new Dictionary<string, int>(StringComparer.Ordinal);
        var lapped = 0L;
        void Lap(string phase)
        {
            phases[phase] = (int) (watch.ElapsedMilliseconds - lapped);
            lapped = watch.ElapsedMilliseconds;
        }

        var remarks = new List<string>();
        var status = "ok";
        Observation? observation = null;
        var lines = new List<string>();
        List<StepTrace>? trace = null;
        Log.Info($"{progress}{(progress.Length > 0 ? " " : "")}Case {probeCase.Id}");
        var changedOptions = new List<string>();
        try
        {
            var fixturePath = Path.Combine(Paths.Fixtures, $"{probeCase.Fixture}.docx");
            if (!File.Exists(fixturePath))
            {
                throw new FileNotFoundException($"Fixture '{probeCase.Fixture}' has not been built; run `fixtures` first.", fixturePath);
            }

            var probe = session.OpenFixture(fixturePath);
            if (caption != null)
            {
                // The run's progress, in the title bar: never in the document, which is what is being measured.
                Com.Try(() =>
                {
                    probe.Document.ActiveWindow.Caption = caption;
                    return true;
                });
            }

            Lap("open");
            foreach (var (part, milliseconds) in session.OpenPhases)
            {
                phases[part] = milliseconds;
            }
            foreach (var (member, value) in probeCase.Options)
            {
                ProbeOptions.Set(session.Application, member, value);
                changedOptions.Add(member);
            }

            if (probeCase.Tracked)
            {
                probe.Document.TrackRevisions = true;
            }

            var observe = probeCase.Scenario.Observe;
            var markupBefore = markupAlways || observe.HasFlag(Observe.Markup) ? Markup.Body(probe.Document) : null;
            Lap("options");
            var executor = new StepExecutor(session, probe, calibration)
            {
                Trace = probeCase.Scenario.Trace
            };
            foreach (var step in probeCase.Steps())
            {
                try
                {
                    executor.Execute(step);
                }
                catch (Exception exception) when (step is SelectStep or SeekStep && exception is not ForegroundLostException)
                {
                    // A set-up step the fixture cannot satisfy (a cell in a document without a table, a text box
                    // that is not there): the axis combination does not apply, which is not a failure of Word.
                    throw new NotApplicableException($"{step.Text}: {exception.Message}");
                }
            }

            Thread.Sleep(60);
            Lap("steps");
            remarks.AddRange(executor.Remarks);
            if (probeCase.Scenario.Trace)
            {
                trace = executor.Traces;
            }
            observation = Observer.Read(probe, observe, executor.HotspotProbes, executor.Dialog, markupBefore, remarks);
            lines = StateNotation.Lines(observation);
            foreach (var line in lines)
            {
                Log.Detail(line);
            }

            if (observation.MarkupDiff != null)
            {
                Directory.CreateDirectory(Paths.Markup);
                File.WriteAllText(Path.Combine(Paths.Markup, $"{Safe(probeCase.Id)}.diff"), observation.MarkupDiff);
                observation = observation with
                {
                    MarkupDiff = $"markup/{Safe(probeCase.Id)}.diff"
                };
            }

            Lap("observe");
            if (executor.Dialog is {ClassName: "(none)"})
            {
                status = "dialog-missing";
            }
        }
        catch (ForegroundLostException exception)
        {
            status = "foreground-lost";
            remarks.Add(exception.Message);
            Log.Warn(exception.Message);
        }
        catch (NotApplicableException exception)
        {
            status = "not-applicable";
            remarks.Add(exception.Message);
            Log.Info($"not applicable: {exception.Message}");
        }
        catch (COMException exception) when (Com.IsBusy(exception))
        {
            status = "word-busy";
            remarks.Add(exception.Message);
            Log.Warn($"Word busy: {exception.Message}");
            Com.Try(() => session.Watchdog.TryDismiss());
        }
        catch (Exception exception)
        {
            status = "error";
            remarks.Add($"{exception.GetType().Name}: {exception.Message}");
            Log.Warn($"{probeCase.Id}: {exception.GetType().Name}: {exception.Message}");
            Log.Detail(exception.ToString());
        }
        finally
        {
            try
            {
                if (changedOptions.Count > 0)
                {
                    OptionsSnapshot.Apply(session.Application, session.OriginalOptions, changedOptions);
                }

                // A key can change an option the case never named: Insert toggles Options.Overtype (E-11), and the
                // first case after it ran in overtype until this reset was added. Every recorded option goes back to
                // the value the snapshot holds, so a key's side effect never reaches the next case.
                OptionsSnapshot.Apply(session.Application, session.OriginalOptions, ProbeOptions.Recorded.Except(changedOptions, StringComparer.Ordinal));
                Lap("reset");

                session.DialogAllowed = false;
                var unexpected = session.Watchdog.Snapshot();
                if (unexpected != null)
                {
                    remarks.Add($"unexpected window: {unexpected.ClassName} \"{unexpected.Title}\"");
                    session.Watchdog.TryDismiss();
                    if (status == "ok")
                    {
                        status = "dialog-unexpected";
                    }
                }

                session.CloseDocuments();
                Lap("close");
            }
            catch (Exception exception)
            {
                remarks.Add($"cleanup: {exception.Message}");
            }
        }

        return new(
            probeCase.DefinitionHash,
            probeCase.Scenario.Title,
            probeCase.Fixture,
            probeCase.StepTexts.ToList(),
            probeCase.Options.ToDictionary(_ => _.Key, _ => _.Value, StringComparer.Ordinal),
            probeCase.Tracked,
            status,
            (int) watch.ElapsedMilliseconds,
            DateTimeOffset.Now,
            lines,
            observation,
            remarks,
            [],
            trace,
            phases,
            ClickToRunVersion());
    }

    static string Safe(string id) =>
        Regex.Replace(id, "[^A-Za-z0-9._-]+", "_");
}
