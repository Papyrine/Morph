using System.Text.Json.Serialization;

public class ScenarioResult
{
    public int ExpectedPageCount { get; set; }
    public int ResultingPageCount { get; set; }
    public List<PageDiff>? PageDiffs { get; set; }
}

/// <summary>
/// Per-page comparison against the Word reference render. <paramref name="ErrorMetric"/> is the
/// absolute error (fraction of pixels that differ at all; 0 = identical) and
/// <paramref name="Ssim"/> is structural similarity (1 = identical; null when the page sizes
/// differ). Both are computed together from one decode by <see cref="PageComparison"/>. They fail
/// differently: AE is blind to structure — a page with headings drawn through body text can score
/// the same as a clean one when most pixels are white — while SSIM is blind to sparse pixel-exact
/// differences. Read them together.
/// </summary>
public record PageDiff(int Page, double ErrorMetric, double? Ssim, string ExpectedFile, string VerifiedFile, string ReceivedFile);

[JsonSerializable(typeof(ScenarioResult))]
public partial class ScenarioResultContext : JsonSerializerContext;
