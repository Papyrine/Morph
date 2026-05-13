/// <summary>
/// InkML trace-point parser benchmarks. No DOCX input in the corpus contains
/// ink, so we feed synthetic trace strings of representative shapes directly to
/// <see cref="InkParser.ParseTracePoints"/> — the method whose alloc footprint
/// the recent span-split refactor reduced.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class InkBenchmarks
{
    string shortStroke = "";
    string longStroke = "";
    string mixedStrokes = "";

    [GlobalSetup]
    public void Setup()
    {
        shortStroke = BuildTrace(pointCount: 20, includeRelativePrefix: false);
        longStroke = BuildTrace(pointCount: 5_000, includeRelativePrefix: false);
        mixedStrokes = BuildTrace(pointCount: 5_000, includeRelativePrefix: true);
    }

    static string BuildTrace(int pointCount, bool includeRelativePrefix)
    {
        var builder = new StringBuilder(pointCount * 12);
        for (var i = 0; i < pointCount; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            if (includeRelativePrefix && i > 0 && i % 4 == 0)
            {
                builder.Append('\'');
            }

            builder.Append(i * 7).Append(' ').Append(i * 11);
        }
        return builder.ToString();
    }

    [Benchmark]
    [BenchmarkCategory("Ink")]
    public object Parse_ShortStroke() => InkParser.ParseTracePoints(shortStroke);

    [Benchmark]
    [BenchmarkCategory("Ink")]
    public object Parse_LongStroke() => InkParser.ParseTracePoints(longStroke);

    [Benchmark]
    [BenchmarkCategory("Ink")]
    public object Parse_MixedStrokes() => InkParser.ParseTracePoints(mixedStrokes);
}
