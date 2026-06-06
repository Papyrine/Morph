/// <summary>
/// Dev-only utility that renders the committed reference <c>expected.html</c> files to PNG images
/// using the shared Playwright pipeline, producing <c>expected.html.png</c> beside them. Useful for
/// eyeballing how the reference would actually look in a browser without opening every file by hand.
///
/// Skipped during normal runs. Enable with <c>MORPH_RENDER_EXPECTED=1</c>.
/// </summary>
public class ExpectedRenderTool
{
    [Test]
    public async Task RenderExpected()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MORPH_RENDER_EXPECTED")))
        {
            return;
        }

        var inputs = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");

        // Process serially: the shared browser is fine with concurrent pages but rendering several
        // hundred reference documents (some with sizeable base64-inlined images) sequentially keeps
        // peak memory predictable for the seeding pass.
        var failures = new List<(string path, Exception error)>();

        foreach (var htmlPath in Directory.GetFiles(inputs, "expected.html", SearchOption.AllDirectories))
        {
            try
            {
                var png = await BrowserScreenshot.RenderHtmlAsync(File.ReadAllText(htmlPath));
                await File.WriteAllBytesAsync(htmlPath + ".png", png);
            }
            catch (Exception ex)
            {
                failures.Add((htmlPath, ex));
            }
        }

        if (failures.Count > 0)
        {
            // Surface what was skipped so the seeding pass is self-documenting.
            var summary = new StringBuilder();
            summary.Append($"{failures.Count} file(s) failed to render:\n");
            foreach (var (path, error) in failures)
            {
                summary.Append($"  {path}: {error.GetType().Name}: {error.Message}\n");
            }

            Console.Error.WriteLine(summary.ToString());
        }
    }
}
