using static ReviewDocuments;

// Word's change bars (PlacedLine.ChangeBar), probed 2026-11-07 on _probe_changebars: an inserted,
// deleted or reformatted run bars its own line; changed paragraph properties bar the paragraph's first
// line, a revised paragraph mark its last; the first line's bar takes in the space the paragraph keeps
// before it, the last line's its space after. Each paragraph here is three lines of 11pt Arial with
// 12pt before and 18pt after.
public class ChangeBarTests
{
    const string by = "w:id=\"1\" w:author=\"Ann\" w:date=\"2026-11-07T10:30:00Z\"";
    const string spacing = "<w:spacing w:before=\"240\" w:after=\"360\"/>";
    const string font = "<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\"/><w:sz w:val=\"22\"/>";

    static readonly string words = string.Join(' ', Enumerable.Repeat("alpha bravo charlie delta echo foxtrot golf hotel", 4));

    static string Run(string text, string properties = "") =>
        $"<w:r><w:rPr>{font}{properties}</w:rPr><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    // A plain paragraph, then the one under test, then a plain one: the bars of the middle one, as
    // (line, top, bottom) with top and bottom measured from that line's own box.
    static async Task<List<(int Line, float Above, float Below)>> BarsAsync(string paragraphProperties, string content, string ownSpacing = spacing)
    {
        var plain = $"<w:p><w:pPr>{spacing}</w:pPr>{Run(words)}</w:p>";
        var docx = Build(plain + $"<w:p><w:pPr>{ownSpacing}{paragraphProperties}</w:pPr>{content}</w:p>" + plain);
        using var stream = new MemoryStream(docx);
        var document = new DocumentParser().Parse(stream);
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(document.Elements, document.PageSettings, null, null, null, null, null, null, DocumentNotes.From(document));
        var lines = laidOut.Pages[0].Items.OfType<PlacedLine>().ToList();
        var middle = document.Elements.OfType<ParagraphElement>().ElementAt(1);
        var bars = lines
            .Where(_ => ReferenceEquals(_.Paragraph, middle) && _.ChangeBar != null)
            .Select(_ => (_.LineIndex, _.Y - _.ChangeBar!.Value.Top, _.ChangeBar.Value.Bottom - (_.Y + _.Height)))
            .ToList();
        await Assert.That(lines.Count(_ => ReferenceEquals(_.Paragraph, middle))).IsEqualTo(3);
        await Assert.That(lines.Where(_ => !ReferenceEquals(_.Paragraph, middle)).All(_ => _.ChangeBar == null)).IsTrue();
        return bars;
    }

    // What the middle paragraph reads as: its first words, then one more word that a revision may wrap.
    static string Words(string revised) =>
        Run("alpha bravo charlie delta echo foxtrot golf hotel alpha bravo charlie delta echo foxtrot golf hotel ") + revised + Run(" " + words[..90]);

    // Bars a test expects, each (line, above, below) in points.
    static async Task Expect(List<(int Line, float Above, float Below)> bars, params (int Line, float Above, float Below)[] expected)
    {
        await Assert.That(bars.Count).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(bars[index].Line).IsEqualTo(expected[index].Line);
            await Assert.That(bars[index].Above).IsEqualTo(expected[index].Above).Within(0.01f);
            await Assert.That(bars[index].Below).IsEqualTo(expected[index].Below).Within(0.01f);
        }
    }

    [Test]
    public async Task AFormattedRun_BarsItsOwnLine()
    {
        var bars = await BarsAsync("", Words($"<w:r><w:rPr>{font}<w:b/><w:rPrChange {by}><w:rPr>{font}</w:rPr></w:rPrChange></w:rPr><w:t>BOLD</w:t></w:r>"));

        await Expect(bars, (1, 0, 0));
    }

    // On the first line the bar takes in the 12pt the paragraph keeps beyond the 18pt after before it:
    // none here, since ParagraphGap keeps only the excess.
    [Test]
    public async Task ChangedProperties_BarTheFirstLine()
    {
        var bars = await BarsAsync($"<w:keepNext/><w:pPrChange {by}><w:pPr>{spacing}</w:pPr></w:pPrChange>", Run(words));

        await Expect(bars, (0, 0, 0));
    }

    [Test]
    public async Task ARevisedMark_BarsTheLastLine_AndTheSpaceAfter()
    {
        var bars = await BarsAsync($"<w:rPr><w:ins {by}/></w:rPr>", Run(words));

        await Expect(bars, (2, 0, 18));
    }

    [Test]
    public async Task AnInsertionOnTheLastLine_TakesInTheSpaceAfter()
    {
        var bars = await BarsAsync("", Run(words) + $"<w:ins {by}>{Run(" LAST")}</w:ins>");

        await Expect(bars, (2, 0, 18));
    }

    // With more before than the previous paragraph's after, the excess is the paragraph's own and the
    // first line's bar takes it in.
    [Test]
    public async Task AnInsertionOnTheFirstLine_TakesInTheParagraphsOwnSpaceBefore()
    {
        var bars = await BarsAsync(
            "",
            $"<w:ins {by}>{Run("FIRST ")}</w:ins>" + Run(words),
            "<w:spacing w:before=\"720\" w:after=\"360\"/>");

        await Expect(bars, (0, 36 - 18, 0));
    }
}
