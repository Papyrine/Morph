using VerifyTests.DiffPlex;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.UseStrictJson();

        // Pin culture/region so locale-dependent behavior (paper size defaults,
        // number/date formatting, RegionInfo.CurrentRegion) is deterministic
        // regardless of the host machine.
        var auCulture = new CultureInfo("en-AU");
        CultureInfo.DefaultThreadCurrentCulture = auCulture;
        CultureInfo.DefaultThreadCurrentUICulture = auCulture;
        Thread.CurrentThread.CurrentCulture = auCulture;
        Thread.CurrentThread.CurrentUICulture = auCulture;

        // Force A4 size for consistent test results across regions
        DefaultPageSize.UseLetterSize = false;

        // Use 1.08 font width scale to better match Microsoft Word's text rendering
        DefaultFontSettings.FontWidthScale = 1.08;

        // Disable font hinting + subpixel positioning in tests: greyscale AA at
        // integer x positions gives pixel-identical output across machines so
        // scenario verified PNGs/JSON don't drift between local and CI.
        DefaultFontSettings.DeterministicRendering = true;

        VerifierSettings.UseSsimForPng();
        VerifyDiffPlex.Initialize(OutputType.Compact);
        // Expands pdf snapshots (ExportScenarioTests.PdfOutput) into info + the pdf +
        // per-page PNGs rendered by PDFium
        VerifyPDFium.Initialize();
        VerifierSettings.InitializePlugins();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            var inputs = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
            ScenarioMarkdownGenerator.RegenerateAll(inputs);
            ScenarioMarkdownGenerator.RegenerateAllExport(inputs);
        };
    }
}
