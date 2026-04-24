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

        VerifyImageMagick.RegisterComparers(threshold: 0.5);
        VerifyDiffPlex.Initialize(OutputType.Compact);
        VerifierSettings.InitializePlugins();
    }
}
