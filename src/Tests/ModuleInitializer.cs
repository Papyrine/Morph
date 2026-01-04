public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.UseStrictJson();
        VerifierSettings.InitializePlugins();

        // Force A4 size for consistent test results across regions
        DefaultPageSize.UseLetterSize = false;

        // Use 1.08 font width scale to better match Microsoft Word's text rendering
        DefaultFontSettings.FontWidthScale = 1.08;
    }
}
