#if !DEBUG

public class DocumentConverterConcurrencyTests
{
    // A single DocumentConverter instance must produce byte-identical output for the same
    // input across concurrent calls. Any mutable state hanging off the converter would get
    // clobbered when callers hit it from multiple threads, so this test pins that contract.
    //
    // It interleaves two distinct inputs through the same converter so the parser sees
    // overlapping work on different documents — that's what triggers the cross-contamination
    // (parsing the same docx twice can mask the race because the overwritten field happens
    // to hold the same value either way). complex_tables and bullet_list together exercise
    // the parser fields the original race corrupted (style borders, numbering, theme).
    //
    // Both Skia and ImageSharp derive from WordRender.DocumentConverter, so the parse path
    // covered here is shared across both backends.
    [Test]
    public async Task SharedConverterIsByteIdenticalUnderConcurrentRenders()
    {
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        var inputA = await File.ReadAllBytesAsync(Path.Combine(inputsDir, "complex_tables", "input.docx"));
        var inputB = await File.ReadAllBytesAsync(Path.Combine(inputsDir, "bullet_list", "input.docx"));

        var converter = new WordRender.Skia.DocumentConverter();
        var baselineA = Render(converter, inputA);
        var baselineB = Render(converter, inputB);

        var failures = new ConcurrentBag<string>();

        await Parallel.ForAsync(0, 128, (i, _) =>
        {
            var useA = (i & 1) == 0;
            var input = useA ? inputA : inputB;
            var baseline = useA ? baselineA : baselineB;
            var label = useA ? "complex_tables" : "bullet_list";

            var pages = Render(converter, input);
            if (pages.Count != baseline.Count)
            {
                failures.Add($"{label} iteration {i}: page count {pages.Count} != baseline {baseline.Count}");
                return ValueTask.CompletedTask;
            }

            for (var page = 0; page < baseline.Count; page++)
            {
                if (!pages[page].AsSpan().SequenceEqual(baseline[page]))
                {
                    failures.Add($"{label} iteration {i} page {page + 1}: bytes differ from sequential baseline");
                    break;
                }
            }

            return ValueTask.CompletedTask;
        });

        if (!failures.IsEmpty)
        {
            throw new(
                $"Shared DocumentConverter produced inconsistent output across threads:{Environment.NewLine}" +
                string.Join(Environment.NewLine, failures.Distinct().Take(8)));
        }
    }

    static IReadOnlyList<byte[]> Render(WordRender.DocumentConverter converter, byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return converter.ConvertToImageData(stream);
    }
}

#endif
