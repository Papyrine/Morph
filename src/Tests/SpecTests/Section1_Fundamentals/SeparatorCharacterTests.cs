using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

/// <summary>
/// U+2028 LINE SEPARATOR and U+2029 PARAGRAPH SEPARATOR reaching a backend as literal characters
/// draw a missing-glyph box, because text faces carry no glyph for either — <c>business-plans/01</c>
/// rendered two of them, after "Contoso, Ltd." and "Casey Jensen". The parser now substitutes a
/// space.
/// A space and NOT a line break, which the names and UAX #14 both suggest. A Word probe settled it:
/// given <c>LINESEPAAA</c> U+2028 <c>LINESEPBBB</c>, Word keeps both words on one line separated by
/// a blank gap, while a <c>w:br</c> control in the same document does split. U+2029 behaves
/// identically, and a trailing separator adds no empty line — matching the fixture, where Word's
/// reference shows the same 48px gap to the next paragraph that Morph already produced.
/// </summary>
public class SeparatorCharacterTests
{
    // Escaped, not literal: C# counts U+2028/U+2029 as line terminators in source, so a literal one
    // ends the line mid-token. It also makes them invisible to review.
    const string lineSeparator = "\u2028";
    const string paragraphSeparator = "\u2029";

    [Test]
    [Arguments(lineSeparator)]
    [Arguments(paragraphSeparator)]
    public async Task Separator_BecomesASpace(string separator)
    {
        var run = SingleRun($"AAA{separator}BBB");

        await Assert.That(run.Text).IsEqualTo("AAA BBB");
    }

    [Test]
    [Arguments(lineSeparator)]
    [Arguments(paragraphSeparator)]
    public async Task Separator_DoesNotSplitTheParagraphIntoLines(string separator)
    {
        // The regression guard. Routing these to the "\n" representation a w:br uses would split the
        // run in two and break the line — which is what the character names imply and what Word
        // does not do.
        var paragraph = ParseSingleParagraph($"AAA{separator}BBB");

        await Assert.That(paragraph.Runs.Count).IsEqualTo(1);
        await Assert.That(paragraph.Runs.Any(_ => _.Text.Contains('\n'))).IsFalse();
    }

    [Test]
    public async Task TrailingSeparator_AddsNoLine()
    {
        // business-plans/01's shape exactly: the separator is the last thing in the paragraph.
        var paragraph = ParseSingleParagraph($"TRAILAAA{lineSeparator}");

        await Assert.That(paragraph.Runs.Count).IsEqualTo(1);
        await Assert.That(paragraph.Runs[0].Text).IsEqualTo("TRAILAAA ");
    }

    [Test]
    public async Task ExplicitBreak_StillSplitsTheRun()
    {
        // The contrast that gives the tests above their meaning: a real w:br does become a line
        // break, so the separator handling has not disabled break handling generally.
        var paragraph = ParseParagraph(paragraph =>
        {
            var run = new OoxmlRun();
            run.AppendChild(new Text("AAA"));
            run.AppendChild(new Break());
            run.AppendChild(new Text("BBB"));
            paragraph.AppendChild(run);
        });

        await Assert.That(paragraph.Runs.Select(_ => _.Text)).IsEquivalentTo(["AAA", "\n", "BBB"]);
    }

    [Test]
    public async Task NoSeparator_ReturnsTheSameInstance()
    {
        // Pins the allocation-free fast path: ordinary text must not be copied.
        const string text = "nothing to replace";

        await Assert.That(ReferenceEquals(text.ReplaceSeparatorsWithSpace(), text)).IsTrue();
    }

    [Test]
    public async Task ReplacesEverySeparatorOccurrence()
    {
        var replaced = $"A{lineSeparator}B{paragraphSeparator}C{lineSeparator}".ReplaceSeparatorsWithSpace();

        await Assert.That(replaced).IsEqualTo("A B C ");
    }

    static Run SingleRun(string text) => ParseSingleParagraph(text).Runs.Single();

    static ParagraphElement ParseSingleParagraph(string text) =>
        ParseParagraph(paragraph =>
        {
            var run = new OoxmlRun();
            run.AppendChild(new Text(text)
            {
                Space = SpaceProcessingModeValues.Preserve
            });
            paragraph.AppendChild(run);
        });

    static ParagraphElement ParseParagraph(Action<Paragraph> configure)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = package.AddMainDocumentPart();
            var paragraph = new Paragraph();
            configure(paragraph);
            mainPart.Document = [with(new Body(paragraph))];
        }

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, stream.ToArray());
            return new DocumentParser().Parse(path)
                .Elements
                .OfType<ParagraphElement>()
                .First();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
