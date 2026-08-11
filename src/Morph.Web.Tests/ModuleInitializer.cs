using System.Text.RegularExpressions;

static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyPlaywright.Initialize(installPlaywright: true);
        VerifierSettings.UseSsimForPng(.7);
        VerifierSettings.InitializePlugins();

        // bUnit stamps a fresh element-reference GUID on InputFile each render; pin it so component
        // snapshots stay stable. Only matches the bUnit attribute, so Playwright/text snapshots are untouched.
        VerifierSettings.ScrubLinesWithReplace(_ =>
            Regex.Replace(
                _,
                "blazor:elementreference=\"[^\"]*\"",
                "blazor:elementreference=\"scrubbed\"",
                RegexOptions.IgnoreCase));

        // The footer's version, download total and RAM figure also vary from capture to capture, but they
        // are pinned in the DOM before the page is captured (SnapshotTests.PinFooterAsync) rather than
        // scrubbed here — a scrubber can only fix the HTML, and those figures are painted into the
        // screenshot too.
    }
}
