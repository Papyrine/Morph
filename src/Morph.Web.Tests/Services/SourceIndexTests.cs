// The layout engine keeps no link from the text it places back to the runs that text was parsed from; the
// source index reads one back by following each paragraph's lines through its runs. Each rule of that is
// pinned on the smallest synthesized page that shows it, and the whole on real documents.
public class SourceIndexTests
{
    static readonly PageSettings letter = new()
    {
        WidthPoints = 612,
        HeightPoints = 792,
        MarginBottom = 72
    };

    // A paragraph of runs stamped as the parser stamps them: one source run each, from its start.
    static ParagraphElement Paragraph(params string[] texts) =>
        new()
        {
            Runs = texts
                .Select((text, index) => new Run
                {
                    Text = text,
                    Source = new(index, 0)
                })
                .ToList()
        };

    static PlacedLine Line(ParagraphElement paragraph, int lineIndex, params PlacedRun[] runs) =>
        new(72, 100 + 12 * lineIndex, 468, 12, 109 + 12 * lineIndex, paragraph, lineIndex, runs, []);

    static PlacedRun Text(float x, string text) =>
        new(x, text.Length * 5, text, new());

    static PageTextLayer Layer(params PlacedItem[] items)
    {
        var document = new LaidOutDocument([new(1, letter, items)]);
        return TextLayerBuilder.Build(document.Pages[0], SourceIndex.Build(document));
    }

    // Each stretch as 'text'@run+offset, read back out of the layer's own text.
    static string Describe(PageTextLayer layer) =>
        string.Join(' ', layer.Sources.Select(_ => $"'{layer.Text.Substring(_.Start, _.Length)}'@{_.Run}+{_.Offset}"));

    [Test]
    public async Task UnstampedPages_HaveNoIndex()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [new() { Text = "plain" }]
        };
        var document = new LaidOutDocument([new(1, letter, [Line(paragraph, 0, Text(72, "plain"))])]);

        await Assert.That(SourceIndex.Build(document)).IsNull();
        await Assert.That(TextLayerBuilder.Build(document.Pages[0]).Sources).IsEmpty();
    }

    [Test]
    public async Task ARun_MapsToTheRunItCameFrom()
    {
        var paragraph = Paragraph("Hello ", "world");

        var layer = Layer(Line(paragraph, 0, Text(72, "Hello "), Text(102, "world")));

        await Assert.That(layer.Text).IsEqualTo("Hello world\n");
        await Assert.That(Describe(layer)).IsEqualTo("'Hello '@0+0 'world'@1+0");
    }

    // The engine draws neighbouring runs of one formatting as one: the placed run is two pieces.
    [Test]
    public async Task APlacedRunAcrossTwoSourceRuns_IsTwoPieces()
    {
        var paragraph = Paragraph("Hel", "lo");

        var layer = Layer(Line(paragraph, 0, Text(72, "Hello")));

        await Assert.That(Describe(layer)).IsEqualTo("'Hel'@0+0 'lo'@1+0");
    }

    // A wrap swallows the space it breaks at; the next line picks up after it.
    [Test]
    public async Task AWrap_PassesOverTheSpaceItSwallowed()
    {
        var paragraph = Paragraph("one two three");

        var layer = Layer(
            Line(paragraph, 0, Text(72, "one two")),
            Line(paragraph, 1, Text(72, "three")));

        await Assert.That(layer.Text).IsEqualTo("one two three\n");
        await Assert.That(Describe(layer)).IsEqualTo("'one two'@0+0 'three'@0+8");
    }

    // A justified line is a run per word, the spaces between them gone from the layout altogether. The
    // layer puts a space back for copying; it comes from nowhere in the document.
    [Test]
    public async Task AJustifiedLine_MapsEachWord()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [new() { Text = "The quick fox", Source = new(4, 0) }],
            Properties = new()
            {
                Alignment = TextAlignment.Justify
            }
        };

        var layer = Layer(Line(paragraph, 0, Text(72, "The"), Text(110, "quick"), Text(160, "fox")));

        await Assert.That(layer.Text).IsEqualTo("The quick fox\n");
        await Assert.That(Describe(layer)).IsEqualTo("'The'@4+0 'quick'@4+4 'fox'@4+10");
    }

    // A tab leaves no run on the line, only a gap: the run after it starts past the tab's position.
    [Test]
    public async Task ATab_IsPassedOver()
    {
        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new() { Text = "ab", Source = new(0, 0) },
                new() { Text = "\t", IsTab = true, Source = new(0, 2) },
                new() { Text = "cd", Source = new(0, 3) }
            ]
        };

        var layer = Layer(Line(paragraph, 0, Text(72, "ab"), Text(144, "cd")));

        await Assert.That(Describe(layer)).IsEqualTo("'ab'@0+0 'cd'@0+3");
    }

    // w:caps is applied when the text is laid out, not when it is parsed.
    [Test]
    public async Task CapitalisedText_IsFollowedThroughItsOriginal()
    {
        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new()
                {
                    Text = "shout it",
                    Source = new(0, 0),
                    Properties = new()
                    {
                        AllCaps = true
                    }
                }
            ]
        };

        var layer = Layer(Line(paragraph, 0, Text(72, "SHOUT IT")));

        await Assert.That(Describe(layer)).IsEqualTo("'SHOUT IT'@0+0");
    }

    // A list's marker and a margin line number are set out to the left of the line, and are no part of
    // the paragraph's text.
    [Test]
    public async Task RunsOutdentedFromTheLine_AreNotThePararaphs()
    {
        var paragraph = Paragraph("First item");

        var layer = Layer(Line(paragraph, 0, Text(20, "12"), Text(54, "1."), Text(72, "First item")));

        await Assert.That(layer.Text).StartsWith("12");
        await Assert.That(Describe(layer)).IsEqualTo("'First item'@0+0");
    }

    // A note's reference is drawn as its number — two characters here — and is one position in the source.
    [Test]
    public async Task ANoteReference_IsOnePosition()
    {
        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new() { Text = "See", Source = new(0, 0) },
                new() { Text = "12", Source = new(0, 3, Atomic: true), FootnoteReferenceId = "5" },
                new() { Text = " there", Source = new(0, 4) }
            ]
        };

        var layer = Layer(Line(paragraph, 0, Text(72, "See"), Text(90, "12"), Text(100, " there")));

        await Assert.That(Describe(layer)).IsEqualTo("'See'@0+0 '12'@0+3 ' there'@0+4");
        await Assert.That(layer.Sources[1].Atomic).IsTrue();
    }

    // A run the parser made up — a field's result, a checkbox — has no source; what is around it does.
    [Test]
    public async Task AnUnstampedRun_LeavesAGap()
    {
        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new() { Text = "Page ", Source = new(0, 0) },
                new() { Text = "7" },
                new() { Text = " of many", Source = new(3, 0) }
            ]
        };

        var layer = Layer(Line(paragraph, 0, Text(72, "Page 7 of many")));

        await Assert.That(Describe(layer)).IsEqualTo("'Page '@0+0 ' of many'@3+0");
    }

    // A header row is laid out again on every page it heads: each time from its first line.
    [Test]
    public async Task ARepeatedParagraph_IsFollowedEachTime()
    {
        var paragraph = Paragraph("Heading");

        var layer = Layer(
            Line(paragraph, 0, Text(72, "Heading")),
            Line(paragraph, 0, Text(72, "Heading")));

        await Assert.That(Describe(layer)).IsEqualTo("'Heading'@0+0 'Heading'@0+0");
    }

    // Text that is not the paragraph's is left unmapped rather than guessed at — and so is the rest of
    // the paragraph, since there is no telling where in it the two would meet again.
    [Test]
    public async Task TextThatIsNotTheParagraphs_IsLeftUnmapped()
    {
        var paragraph = Paragraph("one two three");
        var document = new LaidOutDocument(
        [
            new(
                1,
                letter,
                [
                    Line(paragraph, 0, Text(72, "one")),
                    Line(paragraph, 1, Text(72, "zwei")),
                    Line(paragraph, 2, Text(72, "three"))
                ])
        ]);

        var index = SourceIndex.Build(document)!;
        var layer = TextLayerBuilder.Build(document.Pages[0], index);

        await Assert.That(Describe(layer)).IsEqualTo("'one'@0+0");
        await Assert.That(index.Followed).IsEqualTo(1);
        await Assert.That(index.Lost).IsEqualTo(2);
    }

    // A paragraph that runs over a page break is followed from the page it starts on.
    [Test]
    public async Task AParagraphAcrossPages_CarriesOn()
    {
        var paragraph = Paragraph("before the break and after it");
        var document = new LaidOutDocument(
        [
            new(1, letter, [Line(paragraph, 0, Text(72, "before the break"))]),
            new(2, letter, [Line(paragraph, 1, Text(72, "and after it"))])
        ]);

        var index = SourceIndex.Build(document);
        var second = TextLayerBuilder.Build(document.Pages[1], index);

        await Assert.That(Describe(second)).IsEqualTo("'and after it'@0+17");
    }

    // Real documents, through the real parser and layout: every line of every stamped paragraph is
    // followed, and what is mapped reads the same in the layer as in the file.
    [Test]
    [Arguments("tracked_changes.docx")]
    [Arguments("comments.docx")]
    [Arguments("align_justified.docx")]
    [Arguments("table_of_contents.docx")]
    [Arguments("table_text_direction.docx")]
    [Arguments("header_footer.docx")]
    public async Task Corpus_IsFollowedThroughout(string file)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(ProjectFiles.corpus, file));
        using var document = PagedDocument.Open(bytes, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var runs = ReadRuns(bytes);

        var mapped = 0;
        for (var page = 0; page < document.PageCount; page++)
        {
            var layer = document.TextLayer(page);
            foreach (var source in layer.Sources)
            {
                if (source.Atomic)
                {
                    continue;
                }

                var placed = layer.Text.Substring(source.Start, source.Length);
                var original = runs[source.Run].Substring(source.Offset, source.Length);
                await Assert.That(placed.ToUpperInvariant()).IsEqualTo(original.Replace(' ', ' ').ToUpperInvariant());
                mapped += source.Length;
            }
        }

        await Assert.That(document.Sources).IsNotNull();
        await Assert.That(document.Sources!.Lost).IsEqualTo(0);
        await Assert.That(document.Sources.Followed).IsGreaterThan(0);
        await Assert.That(mapped).IsGreaterThan(0);
    }

    [Test]
    public async Task Sample_IsFollowedThroughout()
    {
        using var document = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory, traceSources: true);

        for (var page = 0; page < document.PageCount; page++)
        {
            document.TextLayer(page);
        }

        await Assert.That(document.Sources!.Lost).IsEqualTo(0);
        await Assert.That(document.Sources.Followed).IsGreaterThan(20);
    }

    // Tracing is the viewer's: a preview's pages are not asked where their text came from.
    [Test]
    public async Task UnlessAskedFor_NothingIsTraced()
    {
        using var document = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);

        await Assert.That(document.Sources).IsNull();
        await Assert.That(document.TextLayer(0).Sources).IsEmpty();
    }

    [Test]
    [Arguments(InputFormat.Xlsx)]
    [Arguments(InputFormat.Pptx)]
    public async Task WorkbooksAndDecks_HaveNoSources(InputFormat format)
    {
        using var document = PagedDocument.Open(Sample.BytesFor(format), format, Sample.FontDirectory, traceSources: true);

        await Assert.That(document.Sources).IsNull();
        await Assert.That(document.TextLayer(0).Sources).IsEmpty();
    }

    // The text of each w:r of the main part, in the coordinates the sources are given in.
    static List<string> ReadRuns(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        return package.MainDocumentPart!.Document!
            .Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>()
            .Select(SourceRuns.Text)
            .ToList();
    }
}
