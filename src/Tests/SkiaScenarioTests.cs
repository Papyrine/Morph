public class SkiaScenarioTests
{
    public static IEnumerable<string> GetScenarioDirectories() =>
        ScenarioInputs.Directories(ScenarioFormat.Word);

    [Test]
    [MethodDataSource(nameof(GetScenarioDirectories))]
    public async Task Scenario(string directory) =>
        await ScenarioRunner.Run(
            directory,
            ScenarioFormat.Word,
            "skia_result",
            (input, options) => new SkiaDocumentConverter().ConvertToImageData(input, options));
}
