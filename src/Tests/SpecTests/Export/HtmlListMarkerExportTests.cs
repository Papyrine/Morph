/// <summary>
/// Tests for the formatting of a list item's marker in the HTML export. A browser draws the
/// marker in the item's own font, size and colour, so the marker's formatting goes on the
/// <c>&lt;li&gt;</c> and the runs inside carry only what differs from it.
/// </summary>
public class HtmlListMarkerExportTests
{
    static string Export(params DocumentElement[] elements) =>
        HtmlExporter.Export(Doc(elements), new() {EmitDocument = false});

    static ParagraphElement Item(string marker, NumberingInfo? numbering = null, params Run[] runs) =>
        new()
        {
            Runs = runs,
            Properties = new()
            {
                LeftIndentPoints = 36,
                Numbering = numbering ?? new() {Text = marker}
            }
        };

    /// <summary>
    /// business-plans/08 numbers its 20pt bold green section headings. The number took the body's
    /// 11pt black beside them; it is set as the paragraph's first run is, and the run then repeats
    /// none of it.
    /// </summary>
    [Test]
    public async Task NumberedItem_TakesItsFirstRunsWeightColourAndSize()
    {
        var html = Export(Item("1.", null, TextRun("Summary", bold: true, color: "107D5F", fontSize: 20)));

        await Assert.That(html).Contains("<li style=\"font-weight: bold; color: #107d5f; font-size: 20pt\">Summary</li>");
    }

    /// <summary>A run that differs from its item's marker says so, black included.</summary>
    [Test]
    public async Task RunsThatDifferFromTheMarker_CarryTheirOwnFormatting()
    {
        var html = Export(Item(
            "1.",
            null,
            TextRun("Label: ", bold: true, color: "107D5F"),
            TextRun("plain black text")));

        await Assert.That(html).Contains("<li style=\"font-weight: bold; color: #107d5f\">Label: <span style=\"font-weight: normal; color: #000000;\">plain black text</span></li>");
    }

    /// <summary>
    /// The numbering level's own colour and size win over the first run's (resumes/10's accent
    /// bullets, business-plans/12's SWOT bullets), and the text keeps its own.
    /// </summary>
    [Test]
    public async Task LevelColourAndSize_WinOverTheFirstRuns()
    {
        var numbering = new NumberingInfo
        {
            Text = "•",
            FontFamily = "Symbol",
            ColorHex = "AB0F1B",
            FontSizePoints = 14
        };
        var html = Export(Item("•", numbering, TextRun("Project management", fontSize: 10)));

        await Assert.That(html).Contains("<li style=\"color: #ab0f1b; font-size: 14pt\"><span style=\"color: #000000; font-size: 10pt;\">Project management</span></li>");
    }

    /// <summary>
    /// A glyph bullet is drawn from the bullet face whatever the text's, so it takes neither the
    /// first run's weight nor its family; the run keeps its <c>&lt;strong&gt;</c>.
    /// </summary>
    [Test]
    public async Task GlyphBullet_TakesNeitherWeightNorFamily()
    {
        var numbering = new NumberingInfo {Text = "•", FontFamily = "Symbol"};
        var html = Export(Item("•", numbering, TextRun("Opportunity:", bold: true)));

        await Assert.That(html).Contains("<li><strong>Opportunity:</strong></li>");
    }

    /// <summary>An item in the body's own formatting stays a bare <c>&lt;li&gt;</c>.</summary>
    [Test]
    public async Task ItemInTheBodysFormatting_IsBare()
    {
        var html = Export(Item("1.", null, TextRun("plain")));

        await Assert.That(html).Contains("<li>plain</li>");
    }

    /// <summary>
    /// A nested list sits inside its parent's <c>&lt;li&gt;</c> and inherits what that set, so a
    /// child states its own formatting against the parent's, not the body's.
    /// </summary>
    [Test]
    public async Task NestedItem_StatesItsFormattingAgainstItsParents()
    {
        var html = Export(
            LevelItem(0, TextRun("Parent", color: "107D5F", fontSize: 20)),
            LevelItem(1, TextRun("Same as parent", color: "107D5F", fontSize: 20)),
            LevelItem(1, TextRun("Body text")));

        await Assert.That(html).Contains("<li style=\"color: #107d5f; font-size: 20pt\">Parent");
        await Assert.That(html).Contains("<li>Same as parent</li>");
        await Assert.That(html).Contains("<li style=\"color: #000000; font-size: 11pt\">Body text</li>");
    }

    static ParagraphElement LevelItem(int level, params Run[] runs) =>
        Item("1.", new() {Text = "1.", Level = level}, runs);
}
