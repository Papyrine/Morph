// Runs the catalogue: one Word session, every selected family in order, every case not yet recorded,
// each result written as soon as it is in, the markdown re-rendered after each family.
static class ScenarioRunner
{
    const int casesPerSession = 150;

    public static bool Run(RunOptions options)
    {
        var families = Catalog.Select(options.Families);
        if (families.Count == 0)
        {
            Log.Warn("No families matched.");
            return false;
        }

        var userClipboard = ClipboardText.Read();
        var deadline = options.MaxMinutes == null ? DateTime.MaxValue : DateTime.Now.AddMinutes(options.MaxMinutes.Value);
        var calibration = HotspotCalibration.Load();
        var ok = true;
        WordSession? session = null;
        var casesInSession = 0;
        try
        {
            session = WordSession.Start();
            foreach (var family in families)
            {
                var results = ResultStore.Load(family.Id);
                var cases = family.Scenarios().SelectMany(CaseExpander.Expand).ToList();
                if (options.ScenarioId != null)
                {
                    cases = cases.Where(_ => _.Scenario.Id == options.ScenarioId || _.Id == options.ScenarioId || _.Id.StartsWith(options.ScenarioId, StringComparison.Ordinal)).ToList();
                }

                var pending = cases.Where(_ => (options.Force || !IsDone(results, _)) && !options.Skipped.Any(skip => _.Id.StartsWith(skip, StringComparison.Ordinal))).ToList();
                Log.Info($"Family {family.Id}: {cases.Count} cases, {pending.Count} to run");
                foreach (var probeCase in pending)
                {
                    if (DateTime.Now > deadline)
                    {
                        Log.Info("Time budget reached; stopping at a case boundary.");
                        ResultStore.Save(results);
                        return ok;
                    }

                    if (casesInSession >= casesPerSession ||
                        session.IsHung())
                    {
                        Log.Info("Restarting Word");
                        session.Dispose();
                        session = WordSession.Start();
                        casesInSession = 0;
                    }

                    casesInSession++;
                    var result = RunCase(session, probeCase, calibration, options.Markup);
                    results.Cases[probeCase.Id] = result;
                    results.Word = WordInformation(session);
                    ResultStore.Save(results);
                    if (result.Status == "foreground-lost")
                    {
                        throw new ForegroundLostException(result.Remarks.LastOrDefault() ?? "foreground lost");
                    }

                    if (result.Status is not ("ok" or "not-applicable"))
                    {
                        ok = false;
                    }
                }

                ResultStore.Save(results);
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

    static bool IsDone(FamilyResults results, ProbeCase probeCase) =>
        results.Cases.TryGetValue(probeCase.Id, out var existing) &&
        existing.Status is "ok" or "not-applicable" &&
        existing.DefinitionHash == probeCase.DefinitionHash;

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

    public static CaseResult RunCase(WordSession session, ProbeCase probeCase, HotspotCalibration calibration, bool markupAlways)
    {
        var watch = Stopwatch.StartNew();
        var remarks = new List<string>();
        var status = "ok";
        Observation? observation = null;
        var lines = new List<string>();
        List<StepTrace>? trace = null;
        Log.Info($"Case {probeCase.Id}");
        var changedOptions = new List<string>();
        try
        {
            var fixturePath = Path.Combine(Paths.Fixtures, $"{probeCase.Fixture}.docx");
            if (!File.Exists(fixturePath))
            {
                throw new FileNotFoundException($"Fixture '{probeCase.Fixture}' has not been built; run `fixtures` first.", fixturePath);
            }

            var probe = session.OpenFixture(fixturePath);
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
            trace);
    }

    static string Safe(string id) =>
        Regex.Replace(id, "[^A-Za-z0-9._-]+", "_");
}
