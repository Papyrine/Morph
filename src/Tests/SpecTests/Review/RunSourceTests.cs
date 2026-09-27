using static ReviewDocuments;

// The parser can stamp every run with where its text came from, which is how the viewer ties a place on a
// page back to the markup a comment or a revision is anchored in. The stamps are written in the
// coordinates of SourceRuns, which the review code splits runs by, so the two have to agree exactly.
public class RunSourceTests
{
    [Test]
    public async Task WithoutCapture_RunsCarryNoSource()
    {
        var runs = Runs(Corpus("tracked_changes", "01"), captureSources: false);

        await Assert.That(runs.Count).IsEqualTo(4);
        await Assert.That(runs.All(_ => _.Source == null)).IsTrue();
    }

    [Test]
    public async Task Capture_NumbersRunsInDocumentOrder()
    {
        var runs = Runs(Corpus("tracked_changes", "01"), captureSources: true);

        await Assert.That(Stamps(runs)).IsEqualTo("'Hello '@0+0 'inserted '@1+0 'world '@2+0 'removed.'@3+0");
    }

    // A tab splits one w:r into several model runs; each says where in the w:r it starts.
    [Test]
    public async Task Capture_CountsPositionsWithinARun()
    {
        var docx = Build(P("<w:r><w:t>ab</w:t><w:tab/><w:t xml:space=\"preserve\">cd </w:t><w:noBreakHyphen/><w:t>e</w:t></w:r>", R("next")));

        var runs = Runs(docx, captureSources: true);

        await Assert.That(Stamps(runs)).IsEqualTo("'ab'@0+0 '\t'@0+2 'cd ‑e'@0+3 'next'@1+0");
    }

    // Edge whitespace the parser trims takes no position, so an offset into the run's text is always an
    // offset into what was parsed.
    [Test]
    public async Task Capture_TrimmedWhitespaceTakesNoPosition()
    {
        var docx = Build(P("<w:r><w:t> ab </w:t><w:tab/><w:t>c</w:t></w:r>"));

        var runs = Runs(docx, captureSources: true);

        await Assert.That(Stamps(runs)).IsEqualTo("'ab'@0+0 '\t'@0+2 'c'@0+3");
    }

    // A line break is a run of its own in the model and no position in the source: the text either side
    // of it stays consecutive.
    [Test]
    public async Task Capture_ABreakTakesNoPosition()
    {
        var docx = Build(P("<w:r><w:t>ab</w:t><w:br/><w:t>cd</w:t></w:r>"));

        var runs = Runs(docx, captureSources: true);

        await Assert.That(Stamps(runs)).IsEqualTo("'ab'@0+0 '\n' 'cd'@0+2");
    }

    // Runs inside a revision are numbered with the rest: the ordinal counts w:r elements wherever they sit.
    [Test]
    public async Task Capture_NumbersRunsInsideTablesAndRevisions()
    {
        var docx = Build(
            P(R("a")) +
            $"<w:tbl><w:tr><w:tc>{P(Ins("A", 1, R("b")))}</w:tc><w:tc>{P(R("c"))}</w:tc></w:tr></w:tbl>" +
            P(R("d")));

        var document = Parse(docx, captureSources: true);
        var cells = document.Elements.OfType<TableElement>().Single().Rows.Single().Cells;
        var inCells = cells.SelectMany(_ => _.Content.OfType<ParagraphElement>()).SelectMany(_ => _.Runs).ToList();
        var last = document.Elements.OfType<ParagraphElement>().Last().Runs;

        await Assert.That(Stamps(inCells)).IsEqualTo("'b'@1+0 'c'@2+0");
        await Assert.That(Stamps(last)).IsEqualTo("'d'@3+0");
    }

    // Stamping is bookkeeping: the document parsed is the same document.
    [Test]
    [Arguments("tracked_changes", "01")]
    [Arguments("comments", "01")]
    [Arguments("hyperlinks", "")]
    public async Task Capture_ChangesNothingElse(string category, string scenario)
    {
        var docx = Corpus(scenario.Length == 0 ? [category] : [category, scenario]);

        var plain = Runs(docx, captureSources: false);
        var stamped = Runs(docx, captureSources: true);

        await Assert.That(stamped.Count).IsEqualTo(plain.Count);
        for (var index = 0; index < plain.Count; index++)
        {
            await Assert.That(stamped[index].Text).IsEqualTo(plain[index].Text);
            await Assert.That(stamped[index].Properties).IsEqualTo(plain[index].Properties);
        }
    }

    [Test]
    public async Task SourceRuns_LengthAndText_Agree()
    {
        var docx = Build(P("<w:r><w:rPr><w:b/></w:rPr><w:t> ab </w:t><w:tab/><w:softHyphen/><w:br/><w:t xml:space=\"preserve\"> c</w:t></w:r>"));

        var (length, text) = Read(
            docx,
            package =>
            {
                var run = package.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>().Single();
                return (SourceRuns.Length(run), SourceRuns.Text(run));
            });

        await Assert.That(text).IsEqualTo("ab\t­ c");
        await Assert.That(length).IsEqualTo(text.Length);
    }

    static ParsedDocument Parse(byte[] docx, bool captureSources)
    {
        using var stream = new MemoryStream(docx);
        return DocumentConverter.Parse(stream, null, null, captureSources);
    }

    static List<Run> Runs(byte[] docx, bool captureSources) =>
        Parse(docx, captureSources)
            .Elements
            .OfType<ParagraphElement>()
            .SelectMany(_ => _.Runs)
            .ToList();

    static string Stamps(IEnumerable<Run> runs) =>
        string.Join(' ', runs.Select(Stamp));

    static string Stamp(Run run)
    {
        if (run.Source is not { } source)
        {
            return $"'{run.Text}'";
        }

        return $"'{run.Text}'@{source.Run}+{source.Start}";
    }
}
