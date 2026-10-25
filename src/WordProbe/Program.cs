// dotnet run --project src/WordProbe -- smoke | fixtures [--force] | run [family ...] [--scenario ID] [--force]
//                                        [--max-minutes N] [--markup] | render | restore-options | list [family]
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        NativeMethods.SetProcessDpiAwarenessContext(-4);
        var screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.ScreenWidth);
        if (args.Length == 0)
        {
            Console.WriteLine("usage: WordProbe smoke | fixtures [--force] | run [family ...] [--scenario ID] [--skip ID] [--force] [--max-minutes N] [--markup] | render | restore-options | list [family]");
            return 2;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToList();
        try
        {
            switch (command)
            {
                case "smoke":
                    Log.Open("smoke");
                    RequireDpi(screenWidth);
                    return SmokeTest.Run() ? 0 : 1;
                case "fixtures":
                    Log.Open("fixtures");
                    FixtureCatalog.BuildAll(force: rest.Contains("--force"));
                    return 0;
                case "run":
                {
                    Log.Open("run");
                    RequireDpi(screenWidth);
                    var options = RunOptions.Parse(rest);
                    var ok = ScenarioRunner.Run(options);
                    MarkdownRenderer.Render();
                    return ok ? 0 : 1;
                }
                case "render":
                    MarkdownRenderer.Render();
                    return 0;
                case "restore-options":
                    Log.Open("restore");
                    return RestoreOptions() ? 0 : 1;
                case "list":
                    Catalog.List(rest.FirstOrDefault());
                    return 0;
                case "describe":
                    Log.Open("describe");
                    RequireDpi(screenWidth);
                    Describe.Run(rest);
                    return 0;
                default:
                    Console.WriteLine($"Unknown command '{command}'.");
                    return 2;
            }
        }
        catch (ForegroundLostException exception)
        {
            Log.Warn($"ABORTED: {exception.Message}");
            return 3;
        }
        finally
        {
            Log.Close();
        }
    }

    // Asks Windows rather than judging by the width: a 1920px display at 125% is just as aware as the
    // 3440px one the catalogue was first recorded on (2 is DPI_AWARENESS_PER_MONITOR_AWARE).
    static void RequireDpi(int screenWidth)
    {
        if (NativeMethods.GetAwarenessFromDpiAwarenessContext(NativeMethods.GetThreadDpiAwarenessContext()) != 2)
        {
            throw new InvalidOperationException($"The process sees a {screenWidth}px wide screen: the PerMonitorV2 manifest did not apply, and clicks would land off target.");
        }
    }

    static bool RestoreOptions()
    {
        var snapshot = OptionsSnapshot.Load();
        if (snapshot == null)
        {
            Log.Info("No options snapshot to restore.");
            return true;
        }

        Word.Application? application = null;
        try
        {
            application = new()
            {
                Visible = false,
                DisplayAlerts = Word.WdAlertLevel.wdAlertsNone
            };
            var failed = OptionsSnapshot.Apply(application, snapshot, snapshot.Values.Keys.ToList());
            foreach (var member in failed)
            {
                Log.Detail($"not restored: {member}");
            }

            application.NormalTemplate.Saved = true;
            application.Quit(Word.WdSaveOptions.wdDoNotSaveChanges);
            OptionsSnapshot.MarkRestored();
            Log.Info($"Options restored from {snapshot.TakenAt:u} ({failed.Count} members could not be set).");
            return true;
        }
        finally
        {
            Com.Release(ref application);
        }
    }
}

record RunOptions(List<string> Families, string? ScenarioId, bool Force, bool Markup, int? MaxMinutes, List<string> Skipped)
{
    public static RunOptions Parse(List<string> arguments)
    {
        var families = new List<string>();
        string? scenario = null;
        var force = false;
        var markup = false;
        int? maxMinutes = null;
        var skipped = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--scenario":
                    scenario = arguments[++i];
                    break;
                case "--skip":
                    // A case (or every case under a prefix) the run leaves alone: for the one that opens a browser.
                    skipped.Add(arguments[++i]);
                    break;
                case "--force":
                    force = true;
                    break;
                case "--markup":
                    markup = true;
                    break;
                case "--max-minutes":
                    maxMinutes = int.Parse(arguments[++i], CultureInfo.InvariantCulture);
                    break;
                default:
                    families.Add(arguments[i]);
                    break;
            }
        }

        return new(families, scenario, force, markup, maxMinutes, skipped);
    }
}
