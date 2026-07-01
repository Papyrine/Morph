public class TextExtractionTests
{
    [Test]
    public async Task Paragraphs_SeparatedByBlankLine()
    {
        var text = TextExtraction.FromHtml("<p>First</p><p>Second</p>");

        await Assert.That(text).IsEqualTo("First\n\nSecond\n");
    }

    [Test]
    public async Task Heading_ThenParagraph()
    {
        var text = TextExtraction.FromHtml("<h1>Title</h1><p>Body</p>");

        await Assert.That(text).IsEqualTo("Title\n\nBody\n");
    }

    [Test]
    public async Task ListItems_GetBullets()
    {
        var text = TextExtraction.FromHtml("<ul><li>One</li><li>Two</li></ul>");

        await Assert.That(text).IsEqualTo("- One\n- Two\n");
    }

    [Test]
    public async Task InlineFormatting_IsFlattened()
    {
        var text = TextExtraction.FromHtml("<p>A <strong>bold</strong> and <em>italic</em> word</p>");

        await Assert.That(text).IsEqualTo("A bold and italic word\n");
    }

    [Test]
    public async Task LineBreak_BecomesNewline()
    {
        var text = TextExtraction.FromHtml("<p>Line1<br>Line2</p>");

        await Assert.That(text).IsEqualTo("Line1\nLine2\n");
    }

    [Test]
    public async Task TableCells_AreTabSeparatedByRow()
    {
        var text = TextExtraction.FromHtml("<table><tr><td>A</td><td>B</td></tr><tr><td>C</td><td>D</td></tr></table>");

        await Assert.That(text).IsEqualTo("A\tB\nC\tD\n");
    }

    [Test]
    public async Task CollapsesSourceWhitespace()
    {
        var text = TextExtraction.FromHtml("<p>  lots   of\n   space  </p>");

        await Assert.That(text).IsEqualTo("lots of space\n");
    }

    [Test]
    public async Task Empty_ProducesEmptyString()
    {
        var text = TextExtraction.FromHtml("");

        await Assert.That(text).IsEqualTo(string.Empty);
    }
}
