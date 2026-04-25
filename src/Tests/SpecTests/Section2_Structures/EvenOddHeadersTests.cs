/// <summary>
/// Tests for w:settings/w:evenAndOddHeaders parsing and the resulting EvenPageHeader / EvenPageFooter capture.
/// </summary>
public class EvenOddHeadersTests
{
    [Test]
    public async Task ParsedDocument_EvenPageHeader_DefaultsNull()
    {
        // Documents that don't opt in via w:evenAndOddHeaders leave EvenPageHeader / EvenPageFooter null.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.EvenPageHeader).IsNull();
        await Assert.That(doc.EvenPageFooter).IsNull();
    }

    [Test]
    public async Task DocumentParser_PicksUpEvenPageHeader_WhenOptedIn()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "even_odd_headers", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        // Default header carries "ODD HEADER", even header carries "EVEN HEADER".
        await Assert.That(doc.Header).IsNotNull();
        await Assert.That(FlatText(doc.Header!)).Contains("ODD HEADER");

        await Assert.That(doc.EvenPageHeader).IsNotNull();
        await Assert.That(FlatText(doc.EvenPageHeader!)).Contains("EVEN HEADER");
    }

    static string FlatText(HeaderFooterContent content) =>
        string.Concat(content.Elements
            .OfType<ParagraphElement>()
            .SelectMany(_ => _.Runs)
            .Select(_ => _.Text));
}
