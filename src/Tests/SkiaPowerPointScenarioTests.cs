public class SkiaPowerPointScenarioTests
{
    public static IEnumerable<string> GetScenarioDirectories() =>
        ScenarioInputs.Directories(ScenarioFormat.PowerPoint);

    [Test]
    [MethodDataSource(nameof(GetScenarioDirectories))]
    public async Task Scenario(string directory) =>
        await ScenarioRunner.Run(
            directory,
            ScenarioFormat.PowerPoint,
            "skia_result",
            (input, options) => new SkiaPowerPointConverter().ConvertToImageData(input, options));
}
