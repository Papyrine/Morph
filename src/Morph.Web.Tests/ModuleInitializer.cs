using System.Text.RegularExpressions;

static partial class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyPlaywright.Initialize(installPlaywright: true);
        VerifierSettings.UseSsimForPng(.7);
        VerifierSettings.Inline(maxLines: 10, applyMaxLinesToExisting: true);
        VerifierSettings.InitializePlugins();

        // The sample workbook states no paper size, so the paper — and through it a fitted sheet's
        // column widths — otherwise falls to the machine's region: A4 here, Letter on a US runner.
        DefaultPageSize.UseLetterSize = false;

        // bUnit stamps a fresh element-reference GUID on InputFile each render; pin it so component
        // snapshots stay stable. Only matches the bUnit attribute, so Playwright/text snapshots are untouched.
        VerifierSettings.ScrubLinesWithReplace(_ =>
            ElementReference.Replace(_, "blazor:elementreference=\"scrubbed\""));

        // Playwright hides the caret while it takes a screenshot by styling the focused element
        // caret-color: transparent, and takes the style off again afterwards. Verify.Playwright reads
        // the page's HTML beside that screenshot, so whether the style is in it was a race: CI read
        // style="" where the baseline had the style. Neither is the page's own.
        VerifierSettings.ScrubLinesWithReplace(_ =>
            _.Replace(" style=\"caret-color: transparent !important;\"", "").Replace(" style=\"\"", ""));

        // The footer's version, download total and RAM figure also vary from capture to capture, but they
        // are pinned in the DOM before the page is captured (SnapshotTests.PinFooterAsync) rather than
        // scrubbed here — a scrubber can only fix the HTML, and those figures are painted into the
        // screenshot too.
    }

    [GeneratedRegex("blazor:elementreference=\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex ElementReference { get; }
}
