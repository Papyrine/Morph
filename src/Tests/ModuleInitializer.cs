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

        // FontWidthScale left at its 1.0 default. A full-corpus measurement (ImageSharp, ErrorMetric
        // vs the Word reference PNGs) found 1.08 gave no ErrorMetric gain over 1.0 and slightly worse
        // page-count matching, while inflating each word's advance without widening the glyphs — the
        // visible inter-word gaps vs Word — and tipping header/footer tables into a pagination path
        // that could recurse. So the harness renders at the natural 1.0 width.
        DefaultFontSettings.FontWidthScale = 1.0;

        // Disable font hinting + subpixel positioning in tests: greyscale AA at
        // integer x positions gives pixel-identical output across machines so
        // scenario verified PNGs/JSON don't drift between local and CI.
        DefaultFontSettings.DeterministicRendering = true;

        VerifierSettings.UseSsimForPng();
        VerifyDiffPlex.Initialize(OutputType.Compact);
        // Expands pdf snapshots (ExportScenarioTests.PdfOutput) into info + the pdf +
        // per-page PNGs rendered by PDFium. Render at 150 DPI to match the Skia/ImageSharp
        // scenario renders (ImageExportOptions.Dpi) and the Word reference PNGs — at the
        // library default of 96 DPI the page images were coarser, so thin vector strokes
        // (e.g. custGeom leaf veins) rasterised heavier than the other backends.
        VerifyPDFium.Initialize(150);
        VerifierSettings.InitializePlugins();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // One aggregate set per input-format corpus. Roots that do not exist yet are skipped, so
            // a format can land its parser before its scenarios are committed.
            foreach (var format in Enum.GetValues<ScenarioFormat>())
            {
                var root = ScenarioInputs.Root(format);
                if (!Directory.Exists(root))
                {
                    continue;
                }

                ScenarioMarkdownGenerator.RegenerateAll(root);
                ScenarioMarkdownGenerator.RegenerateAllExport(root);
            }
        };
    }
}
