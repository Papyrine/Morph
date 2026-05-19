using System.Text.Json.Serialization;

public class ScenarioResult
{
    public int ExpectedPageCount { get; set; }
    public int ResultingPageCount { get; set; }
    public List<PageDiff>? PageDiffs { get; set; }
}

public record PageDiff(int Page, double ErrorMetric, string ExpectedFile, string VerifiedFile, string ReceivedFile);

[JsonSerializable(typeof(ScenarioResult))]
public partial class ScenarioResultContext : JsonSerializerContext;
