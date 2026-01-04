using System.Text.Json;
using System.Text.Json.Serialization;

public class ScenarioResult
{
    public int ExpectedPageCount { get; set; }
    public int ResultingPageCount { get; set; }
    public List<PageDiff>? PageDiffs { get; set; }

    public static async Task<ScenarioResult> LoadFromFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            return new ScenarioResult();
        }
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize(json, ScenarioResultContext.Default.ScenarioResult)!;
    }
}

public record PageDiff(int Page, double ErrorMetric, string ExpectedFile, string VerifiedFile, string ReceivedFile);

[JsonSerializable(typeof(ScenarioResult))]
public partial class ScenarioResultContext : JsonSerializerContext;