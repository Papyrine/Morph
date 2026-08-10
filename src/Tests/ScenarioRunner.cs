/// <summary>
/// The body every raster scenario test shares: render the scenario's input, compare each page to
/// the office-application reference render (<c>expected_*.png</c>), snapshot the pages plus the
/// metrics through Verify, and regenerate the scenario's <c>compare.md</c>.
///
/// One test class per (backend × input format) because TUnit cannot filter on parameter values, so
/// running just one format's scenarios needs its own class. The classes carry nothing but their
/// data source and a one-line call to here.
/// </summary>
static class ScenarioRunner
{
    static readonly string fontsDirectory =
        Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    public static async Task Run(
        string directory,
        ScenarioFormat format,
        string fileName,
        Func<string, ImageExportOptions, IReadOnlyList<byte[]>> render)
    {
        ContainerOnly.Require();

        var data = render(
            ScenarioInputs.InputFile(directory),
            new()
            {
                FontDirectory = fontsDirectory,
                Pages = ScenarioInputs.Pages(format),
                Dpi = ScenarioInputs.Dpi(format)
            });

        var expectedFiles = Directory.GetFiles(directory, "expected_*.png")
            .Order()
            .ToArray();
        var diffs = PageDiffs(expectedFiles, data, fileName);

        var targets = new List<Target>(data.Count);
        for (var index = 0; index < data.Count; index++)
        {
            targets.Add(new("png", new MemoryStream(data[index]), $"page_{index + 1:0000}"));
        }

        var result = new ScenarioResult
        {
            ExpectedPageCount = expectedFiles.Length,
            ResultingPageCount = data.Count,
            PageDiffs = diffs
        };

        await Verify(result, targets)
            .UseDirectory(directory)
            .UseFileName(fileName)
            .IgnoreParameters();

        ScenarioMarkdownGenerator.Regenerate(directory);
    }

    // A page-count mismatch suppresses the metric entirely rather than comparing misaligned pages —
    // the resulting null is itself the signal that the scenario paginates differently from the
    // reference application (see BaselineHealthTests, which exists because that blind spot is real).
    static List<PageDiff>? PageDiffs(string[] expectedFiles, IReadOnlyList<byte[]> actualFiles, string fileName)
    {
        var pageCount = actualFiles.Count;
        if (expectedFiles.Length != pageCount)
        {
            return null;
        }

        var diffs = new List<PageDiff>(pageCount);
        for (var i = 0; i < pageCount; i++)
        {
            var expectedFile = expectedFiles[i];
            var (errorMetric, ssim) = PageComparison.Compare(expectedFile, actualFiles[i]);
            diffs.Add(new(
                i + 1,
                errorMetric,
                ssim,
                Path.GetFileName(expectedFile),
                $"{fileName}#page_{i + 1:0000}.verified.png",
                $"{fileName}#page_{i + 1:0000}.received.png"));
        }

        return diffs;
    }
}
