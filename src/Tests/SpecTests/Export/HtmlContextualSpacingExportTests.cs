/// <summary>
/// Tests for <c>w:contextualSpacing</c> in the HTML export, by the rule the layout engine was
/// probed to (<c>ParagraphGap</c>): a paragraph that opts in drops its space-after against a
/// following paragraph of its own style and its space-before against a preceding one, whether or
/// not the neighbour opts in.
/// </summary>
public class HtmlContextualSpacingExportTests
{
    static ParagraphElement Line(string style, string text, double before, double after, bool contextual) =>
        new()
        {
            Runs = [TextRun(text)],
            Properties = new()
            {
                StyleId = style,
                SpacingBeforePoints = before,
                SpacingAfterPoints = after,
                ContextualSpacing = contextual
            }
        };

    static string Export(params DocumentElement[] elements) =>
        HtmlExporter.Export(Doc(elements), new() {EmitDocument = false});

    /// <summary>
    /// business-plans/10 sets its two cover lines in a style with 342pt before and contextual
    /// spacing. Only the after half was dropped, so the second line kept its 342pt and fell off the
    /// cover art onto the white page beneath, white on white.
    /// </summary>
    [Test]
    public async Task SecondOfTwoContextualParagraphs_DropsItsSpaceBefore()
    {
        var html = Export(
            Line("VersionNumber", "Version 3.0", 342, 8, contextual: true),
            Line("VersionNumber", "April 4, 20XX", 342, 8, contextual: true));

        await Assert.That(html).IsEqualTo(
            "<p style=\"margin-top: 342pt; margin-bottom: 0pt\">Version 3.0</p>\n" +
            "<p>April 4, 20XX</p>\n");
    }

    /// <summary>The flag acts on each paragraph's own half: the second alone opting in drops only its before.</summary>
    [Test]
    public async Task OnlyTheSecondOptingIn_DropsOnlyItsBefore()
    {
        var html = Export(
            Line("Address", "first", 0, 30, contextual: false),
            Line("Address", "second", 20, 8, contextual: true));

        await Assert.That(html).IsEqualTo(
            "<p style=\"margin-bottom: 30pt\">first</p>\n" +
            "<p>second</p>\n");
    }

    /// <summary>
    /// Word gives the second paragraph the excess of its before over the first one's DECLARED
    /// after, dropped or not: a contextual 30pt after followed by a plain 40pt before leaves 10.
    /// A browser's margin collapse would have left the whole 40.
    /// </summary>
    [Test]
    public async Task OnlyTheFirstOptingIn_LeavesTheExcessOfTheSecondsBefore()
    {
        var html = Export(
            Line("Address", "first", 0, 30, contextual: true),
            Line("Address", "second", 40, 8, contextual: false));

        await Assert.That(html).IsEqualTo(
            "<p style=\"margin-bottom: 0pt\">first</p>\n" +
            "<p style=\"margin-top: 10pt\">second</p>\n");
    }

    /// <summary>Two paragraphs of different styles keep their spacing whatever they opt into.</summary>
    [Test]
    public async Task DifferentStyles_KeepTheirSpacing()
    {
        var html = Export(
            Line("Address", "first", 0, 30, contextual: true),
            Line("Closing", "second", 20, 8, contextual: true));

        await Assert.That(html).IsEqualTo(
            "<p style=\"margin-bottom: 30pt\">first</p>\n" +
            "<p style=\"margin-top: 20pt\">second</p>\n");
    }
}
