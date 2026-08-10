public class ImageSharpExcelScenarioTests
{
    public static IEnumerable<string> GetScenarioDirectories() =>
        ScenarioInputs.Directories(ScenarioFormat.Excel);

    [Test]
    [MethodDataSource(nameof(GetScenarioDirectories))]
    public Task Scenario(string directory) =>
        ScenarioRunner.Run(
            directory,
            ScenarioFormat.Excel,
            "imagesharp_result",
            (input, options) => new ImageSharpExcelConverter().ConvertToImageData(input, options));
}
