using static ReviewDocuments;

// A revised run is drawn as Word prints it — an insertion underlined, a deletion struck through, both in
// the revision colour — wherever in a paragraph the revision sits. The parser reaches runs through
// content controls and hyperlinks as well as straight off the paragraph, so the mark is read from the
// markup around the run (RevisionOf) rather than from the path that led to it.
public class RevisionMarkTests
{
    [Test]
    public async Task InAParagraph()
    {
        var runs = Runs(P(R("plain "), Ins("Ann", 1, R("added ")), Del("Ann", 2, "dropped")));

        await Assert.That(Marks(runs)).IsEqualTo("plain |+added |-dropped");
    }

    // The bundled sample keeps its body text in content controls; a change made there is a change.
    [Test]
    public async Task InAContentControl()
    {
        var runs = Runs(
            P(
                "<w:sdt><w:sdtPr><w:id w:val=\"1\"/></w:sdtPr><w:sdtContent>" +
                R("plain ") + Ins("Ann", 1, R("added ")) + Del("Ann", 2, "dropped") +
                "</w:sdtContent></w:sdt>"));

        await Assert.That(Marks(runs)).IsEqualTo("plain |+added |-dropped");
    }

    // Revised text inside a link used to be left out of the paragraph altogether.
    [Test]
    public async Task InAHyperlink()
    {
        var runs = Runs(
            P(
                "<w:hyperlink w:anchor=\"target\">" +
                R("link ") + Ins("Ann", 1, R("added ")) + Del("Ann", 2, "dropped") +
                "</w:hyperlink>"));

        await Assert.That(Marks(runs)).IsEqualTo("link |+added |-dropped");
        await Assert.That(runs.All(_ => _.HyperlinkUrl == "#target")).IsTrue();
    }

    // Text one reviewer inserted and another then deleted is, as it stands, deleted.
    [Test]
    public async Task TheNearestRevision_IsTheOneDrawn()
    {
        var runs = Runs(P(Ins("Ann", 1, R("kept "), Del("Bob", 2, "dropped"))));

        await Assert.That(Marks(runs)).IsEqualTo("+kept |-dropped");
    }

    // A move is drawn as a deletion where the text was and an insertion where it went. Before, moved
    // text was not drawn at either end.
    [Test]
    public async Task AMove_IsDrawnAtBothEnds()
    {
        var runs = Runs(
            P($"<w:moveFrom {By("Ann", 1)}>{R("moved")}</w:moveFrom>", R(" rest")) +
            P(R("before "), $"<w:moveTo {By("Ann", 2)}>{R("moved")}</w:moveTo>"));

        await Assert.That(Marks(runs)).IsEqualTo("-moved| rest|before |+moved");
    }

    [Test]
    public async Task ARevisedRun_IsDrawnInTheRevisionColour_WithItsOwnFontKept()
    {
        var runs = Runs(P(Ins("Ann", 1, "<w:r><w:rPr><w:b/><w:sz w:val=\"40\"/></w:rPr><w:t>added</w:t></w:r>")));

        var properties = runs.Single().Properties;
        await Assert.That(properties.ColorHex).IsEqualTo("D13438");
        await Assert.That(properties.Underline).IsTrue();
        await Assert.That(properties.Bold).IsTrue();
        await Assert.That(properties.FontSizePoints).IsEqualTo(20);
    }

    static List<Run> Runs(string body)
    {
        using var stream = new MemoryStream(Build(body));
        return DocumentConverter.Parse(stream, null)
            .Elements
            .OfType<ParagraphElement>()
            .SelectMany(_ => _.Runs)
            .ToList();
    }

    // Each run's text, led by + for one drawn inserted and - for one drawn deleted.
    static string Marks(IEnumerable<Run> runs) =>
        string.Join('|', runs.Select(Mark));

    static string Mark(Run run)
    {
        var properties = run.Properties;
        if (!properties.IsRevisionMark)
        {
            return run.Text;
        }

        if (properties.Strikethrough)
        {
            return $"-{run.Text}";
        }

        return $"+{run.Text}";
    }
}
