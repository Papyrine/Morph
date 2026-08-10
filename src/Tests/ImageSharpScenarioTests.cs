public class ImageSharpScenarioTests
{
    public static IEnumerable<string> GetScenarioDirectories() =>
        ScenarioInputs.Directories(ScenarioFormat.Word);

    [Test]
    [MethodDataSource(nameof(GetScenarioDirectories))]
    public Task Scenario(string directory) =>
        ScenarioRunner.Run(
            directory,
            ScenarioFormat.Word,
            "imagesharp_result",
            (input, options) => new ImageSharpDocumentConverter().ConvertToImageData(input, options));
}
