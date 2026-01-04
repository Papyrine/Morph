#if DEBUG

public class ScenarioTests
{
    static ConcurrentBag<string> pageCountFailures = new ();
    static ConcurrentBag<string> metricFailures = new ();
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
        var converter = new DocumentConverter();
        var inputFile = Path.Combine(directory, "input.docx");
        var expectedFiles = Directory.GetFiles(directory, "expected_*.png")
            .Order()
            .ToArray();
        var data = converter.ConvertToImageData(inputFile);

        var diffs = PageDiffs(expectedFiles, data);

        if (expectedFiles.Length != data.Count)
        {
            pageCountFailures.Add(directory);
        }
        else
        {
            var verifiedPath = Path.Combine(directory, "results.verified.json");
            var verified = await ScenarioResult.LoadFromFileAsync(verifiedPath);
            if (verified.PageDiffs != null && diffs != null)
            {
                foreach (var diff in diffs)
                {
                    var verifiedDiff = verified.PageDiffs.FirstOrDefault(v => v.Page == diff.Page);
                    if (verifiedDiff != null && diff.ErrorMetric > verifiedDiff.ErrorMetric)
                    {
                        metricFailures.Add(directory);
                    }
                }
            }
        }

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
            .UseFileName("results")
            .IgnoreParameters();
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
            diffs.Add(new(i + 1, errorMetric, Path.GetFileName(expectedFile), $"results#page_{i+1:0000}.verified.png", $"results#page_{i+1:0000}.received.png"));
        }

        return diffs;
    }


    [After(Class)]
    public static async Task AfterAllTests()
    {
        var combine = Path.Combine(ProjectFiles.ProjectDirectory, "outcome.txt");
        File.Delete(combine);
        await using var writer = File.CreateText(combine);
        await writer.WriteLineAsync("# pageCountFailures " + pageCountFailures.Count);
        foreach (var failure in pageCountFailures.Order())
        {
            await writer.WriteLineAsync(failure);
        }

        await writer.WriteLineAsync("");
        await writer.WriteLineAsync("# metricFailures "+ metricFailures.Count);
        foreach (var failure in metricFailures.Order())
        {
            await writer.WriteLineAsync(failure);
        }
    }
}
#endif