public class SkiaPowerPointScenarioTests
{
    public static IEnumerable<string> GetScenarioDirectories() =>
        ScenarioInputs.Directories(ScenarioFormat.PowerPoint);

    [Test]
    [MethodDataSource(nameof(GetScenarioDirectories))]
    public Task Scenario(string directory) =>
        ScenarioRunner.Run(
            directory,
            ScenarioFormat.PowerPoint,
            "skia_result",
            (input, options) => new SkiaPowerPointConverter().ConvertToImageData(input, options));
}
