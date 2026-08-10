public class ImageSharpPowerPointScenarioTests
{
    public static IEnumerable<string> GetScenarioDirectories() =>
        ScenarioInputs.Directories(ScenarioFormat.PowerPoint);

    [Test]
    [MethodDataSource(nameof(GetScenarioDirectories))]
    public Task Scenario(string directory) =>
        ScenarioRunner.Run(
            directory,
            ScenarioFormat.PowerPoint,
            "imagesharp_result",
            (input, options) => new ImageSharpPowerPointConverter().ConvertToImageData(input, options));
}
