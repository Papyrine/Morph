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

        // The footer carries three figures that vary from one capture to the next; pin each before the
        // page-HTML snapshot compares. The version comes from AssemblyInformationalVersion (SDK-suffixed
        // with the commit SHA); the download total is measured from Resource Timing; the RAM figure is the
        // live WebAssembly heap size. The component (bUnit) snapshots have no footer, so these simply don't match.
        VerifierSettings.AddScrubber(
            "html",
            builder =>
            {
                var html = Regex.Replace(
                    builder.ToString(),
                    "(<span class=\"footer-version\">).*?(</span>)",
                    "$1scrubbed$2");
                html = Regex.Replace(
                    html,
                    "(<span class=\"footer-size\"[^>]*>).*?(</span>)",
                    "$1scrubbed$2");
                html = Regex.Replace(
                    html,
                    "(<span class=\"footer-ram\"[^>]*>).*?(</span>)",
                    "$1scrubbed$2");
                builder.Clear();
                builder.Append(html);
            });
    }
}
