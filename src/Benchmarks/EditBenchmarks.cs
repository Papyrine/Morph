/// <summary>
/// What one edit in <c>MorphEditor</c> costs, stage by stage. Every edit that changes the pages goes
/// file → parse → layout → reload (<c>MorphViewer.ShowAsync</c>), so typing a single character into a
/// paragraph pays for all of:
/// <list type="number">
/// <item><c>Rewrite</c> — open the package, rewrite the paragraph, save the package back to bytes</item>
/// <item><c>Open</c> — parse with sources traced, and lay out the whole document</item>
/// <item><c>TextLayers</c> — the source index and every page's text layer, built eagerly on open</item>
/// <item><c>Outline</c> / <c>Review</c> — the file read again, twice, for editing and for the review pane</item>
/// <item><c>EditMap</c> — every paragraph's box read off the laid-out tree</item>
/// </list>
/// <c>RoundTrip</c> is the sum as <c>MorphViewer.Open</c> runs it, after the edit. The stages are
/// separate so a change to one is visible rather than lost in the others.
///
/// The document parameter spans what the editor sees: a one-page letter, the 12-page synthetic
/// document, and a 5.5MB newsletter whose pictures every save has to re-zip.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class EditBenchmarks
{
    static readonly string inputsDir = Path.GetFullPath(Path.Combine(ProjectFiles.SolutionDirectory, "Tests", "Inputs", "word"));
    static readonly string fontDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.SolutionDirectory, "Fonts"));
    static readonly EditOptions plain = new("Ann", new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), Track: false);
    static readonly EditOptions tracked = plain with
    {
        Track = true
    };

    [Params("letter", "twelve-pages", "newsletter")]
    public string Document { get; set; } = "";

    byte[] bytes = [];
    int paragraph;
    NewParagraph[] typed = [];
    PagedDocument? opened;
    DocumentOutline outline = DocumentOutline.Empty;
    string before = "";
    string after = "";
    SourcePosition boldStart;
    SourcePosition boldEnd;

    [GlobalSetup]
    public void Setup()
    {
        bytes = Document switch
        {
            "letter" => File.ReadAllBytes(Path.Combine(inputsDir, "letters", "01", "input.docx")),
            "twelve-pages" => BenchmarkDocs.HeaderLogoRaster,
            "newsletter" => File.ReadAllBytes(Path.Combine(inputsDir, "newsletters", "03", "input.docx")),
            _ => throw new ArgumentOutOfRangeException(nameof(Document))
        };

        outline = DocumentOutline.Read(bytes);

        // The longest editable paragraph of the body, with a character typed at its end: what a
        // keystroke's commit hands DocumentEditor.Rewrite.
        var target = outline.Paragraphs
            .Where(_ => _.Part == ReviewPart.Body && _.Units.Any(_ => _.Editable))
            .MaxBy(_ => _.Length)!;
        paragraph = target.Ordinal;
        var items = target.Units
            .Select((unit, index) => new NewItem(index, unit.Editable ? unit.Text : null))
            .ToList();
        var last = items.FindLastIndex(_ => _.Text != null);
        items[last] = items[last] with
        {
            Text = items[last].Text + "x"
        };
        typed = [new(items)];

        // The paragraph's first text unit, made bold: a ribbon command on a selection.
        var first = target.Units.First(_ => _.Editable);
        boldStart = new(first.Run, first.Start);
        boldEnd = new(first.Run, first.Start + first.Text.Length);

        before = string.Concat(target.Units.Select(_ => _.Text));
        after = before.Insert(before.Length / 2, "x");

        opened = PagedDocument.Open(bytes, InputFormat.Docx, fontDirectory, traceSources: true);
    }

    [GlobalCleanup]
    public void Cleanup() =>
        opened?.Dispose();

    [Benchmark]
    [BenchmarkCategory("Edit")]
    public byte[] Rewrite() =>
        DocumentEditor.Rewrite(bytes, paragraph, typed, plain).Document;

    /// <summary>The edit as the viewer makes it: reading the review and the outline off its package.</summary>
    [Benchmark]
    [BenchmarkCategory("Edit")]
    public byte[] RewriteCaptured()
    {
        using var capture = DocumentReading.Capture();
        return DocumentEditor.Rewrite(bytes, paragraph, typed, plain).Document;
    }

    [Benchmark]
    [BenchmarkCategory("Edit")]
    public byte[] RewriteTracked() =>
        DocumentEditor.Rewrite(bytes, paragraph, typed, tracked).Document;

    [Benchmark]
    [BenchmarkCategory("Edit")]
    public byte[] Format() =>
        DocumentEditor.Format(bytes, boldStart, boldEnd, new(Bold: true), plain);

    [Benchmark]
    [BenchmarkCategory("Edit")]
    public byte[] SetTracking() =>
        DocumentEditor.SetTracking(bytes, true);

    [Benchmark]
    [BenchmarkCategory("Edit")]
    public int Diff() =>
        TextDiff.Compare(before, after).Count;

    [Benchmark]
    [BenchmarkCategory("Reload")]
    public int Open()
    {
        using var document = PagedDocument.Open(bytes, InputFormat.Docx, fontDirectory, traceSources: true);
        return document.PageCount;
    }

    [Benchmark]
    [BenchmarkCategory("Reload")]
    public int TextLayers()
    {
        // A layer is cached on its document, so each iteration needs one that has built none.
        using var document = PagedDocument.Open(bytes, InputFormat.Docx, fontDirectory, traceSources: true);
        var length = 0;
        for (var index = 0; index < document.PageCount; index++)
        {
            length += document.TextLayer(index).Json.Length;
        }

        return length;
    }

    [Benchmark]
    [BenchmarkCategory("Reload")]
    public int Outline() =>
        DocumentOutline.Read(bytes).Paragraphs.Count;

    [Benchmark]
    [BenchmarkCategory("Reload")]
    public int Review() =>
        DocumentReview.Read(bytes).Changes.Count;

    /// <summary>Both of the above from one opening of the package, as the viewer reads them.</summary>
    [Benchmark]
    [BenchmarkCategory("Reload")]
    public int ReviewAndOutline()
    {
        var (review, read) = DocumentReading.Read(bytes);
        return review.Changes.Count + read.Paragraphs.Count;
    }

    [Benchmark]
    [BenchmarkCategory("Reload")]
    public object EditMap() =>
        opened!.Edits(outline);

    /// <summary>Everything after the edit, as <c>MorphViewer.Open</c> runs it.</summary>
    [Benchmark]
    [BenchmarkCategory("RoundTrip")]
    public int RoundTrip()
    {
        byte[] edited;
        using (DocumentReading.Capture())
        {
            edited = DocumentEditor.Rewrite(bytes, paragraph, typed, plain).Document;
        }

        using var document = PagedDocument.Open(edited, InputFormat.Docx, fontDirectory, traceSources: true);
        var length = 0;
        for (var index = 0; index < document.PageCount; index++)
        {
            length += document.TextLayer(index).Json.Length;
        }

        var (review, read) = DocumentReading.Read(edited);
        var map = document.Edits(read);
        return length + review.Comments.Count + map.GetHashCode();
    }
}
