public class ImageSharpScenarioTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    public static IEnumerable<string> GetScenarioDirectories()
    {
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        return Directory.GetFiles(inputsDir, "input.docx", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)!;
    }

    [Test]
    [MethodDataSource(nameof(GetScenarioDirectories))]
    public async Task Scenario(string directory)
    {
        var converter = new ImageSharpDocumentConverter();
        var inputFile = Path.Combine(directory, "input.docx");
        var expectedFiles = Directory.GetFiles(directory, "expected_*.png")
            .Order()
            .ToArray();
        var data = converter.ConvertToImageData(
            inputFile,
            new()
            {
                FontDirectory = fontsDirectory
            });

        var diffs = PageDiffs(expectedFiles, data);

        var targets = new List<Target>(data.Count);
        for (var index = 0; index < data.Count; index++)
        {
            var item = data[index];
            targets.Add(new("png", new MemoryStream(item), $"page_{index + 1:0000}"));
        }

        var result = new ScenarioResult
        {
            ExpectedPageCount = expectedFiles.Length,
            ResultingPageCount = data.Count,
            PageDiffs = diffs
        };
        await Verify(result, targets)
            .UseDirectory(directory)
            .UseFileName("imagesharp_result")
            .IgnoreParameters();

        ScenarioMarkdownGenerator.Regenerate(directory);
    }

    static List<PageDiff>? PageDiffs(string[] expectedFiles, IReadOnlyList<byte[]> actualFiles)
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
            var actualFile = actualFiles[i];

            using var expected = new MagickImage(expectedFile);
            using var actual = new MagickImage(actualFile);

            expected.Compare(actual, ErrorMetric.Absolute, out var errorMetric);

            errorMetric = Math.Round(errorMetric, 4);
            diffs.Add(new(i + 1, errorMetric, Path.GetFileName(expectedFile), $"imagesharp_result#page_{i + 1:0000}.verified.png", $"imagesharp_result#page_{i + 1:0000}.received.png"));
        }

        return diffs;
    }
}
