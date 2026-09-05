using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// <c>w:rFonts/@w:hint="eastAsia"</c> switches a run made of script-ambiguous characters (the symbol
/// blocks, ☐ among them) to its East Asian face — the run's own <c>w:eastAsia</c>, else the
/// docDefaults one (<c>DocumentParser.ApplyEastAsiaHint</c>). Word-read on wedding/10 and wedding/04.
/// </summary>
public class EastAsiaFontHintTests
{
    [Test]
    public async Task A_hinted_symbol_run_takes_its_own_East_Asian_face()
    {
        using var stream = BuildDocument(new RunFonts { Hint = FontTypeHintValues.EastAsia, EastAsia = "MS Gothic" }, "☐");
        var run = FirstRun(stream);

        await Assert.That(run.Properties.FontFamily).IsEqualTo("MS Gothic");
    }

    [Test]
    public async Task A_bare_hint_falls_back_to_the_docDefaults_East_Asian_face()
    {
        using var stream = BuildDocument(new RunFonts { Hint = FontTypeHintValues.EastAsia }, "☐ ☐");
        var run = FirstRun(stream);

        await Assert.That(run.Properties.FontFamily).IsEqualTo("MS Mincho");
    }

    [Test]
    public async Task Latin_text_keeps_its_Latin_face_whatever_the_hint_says()
    {
        using var stream = BuildDocument(new RunFonts { Hint = FontTypeHintValues.EastAsia, EastAsia = "MS Gothic" }, "Choose the members");
        var run = FirstRun(stream);

        await Assert.That(run.Properties.FontFamily).IsEqualTo("Calibri");
    }

    [Test]
    public async Task A_symbol_run_without_the_hint_keeps_its_Latin_face()
    {
        using var stream = BuildDocument(new RunFonts { EastAsia = "MS Gothic" }, "☐");
        var run = FirstRun(stream);

        await Assert.That(run.Properties.FontFamily).IsEqualTo("Calibri");
    }

    static Run FirstRun(MemoryStream stream)
    {
        var document = new DocumentParser().Parse(stream);
        return document.Elements.OfType<ParagraphElement>().First().Runs[0];
    }

    static MemoryStream BuildDocument(RunFonts fonts, string text)
    {
        var body = new Body(
            new Paragraph(new W.Run(new W.RunProperties(fonts), new Text(text) { Space = SpaceProcessingModeValues.Preserve })));

        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = [with(body)];

            var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(
                new DocDefaults(
                    new RunPropertiesDefault(
                        new RunPropertiesBaseStyle(
                            new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "MS Mincho" },
                            new FontSize { Val = "22" }))));
        }

        stream.Position = 0;
        return stream;
    }
}
