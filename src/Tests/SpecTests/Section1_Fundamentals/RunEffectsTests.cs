using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlOutline = DocumentFormat.OpenXml.Wordprocessing.Outline;

/// <summary>
/// Covers the run-formatting bundle: <c>w:vanish</c> / <c>w:specVanish</c>,
/// <c>w:position</c>, <c>w:bdr</c>, <c>w:emboss</c>, <c>w:imprint</c>, <c>w:outline</c>.
/// Each test builds a minimal in-memory DOCX so the parser path runs end-to-end.
/// </summary>
public class RunEffectsTests
{
    [Test]
    public async Task Vanish_DropsRun_FromParsedParagraph()
    {
        var doc = ParseSingleParagraph(rPr =>
        {
            rPr.AppendChild(new Vanish());
        }, "secret");

        var para = doc.Elements.OfType<ParagraphElement>().First();
        // Hidden runs are filtered at parse time, so the paragraph ends up with no runs.
        await Assert.That(para.Runs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SpecVanish_DropsRun_LikeVanish()
    {
        var doc = ParseSingleParagraph(rPr =>
        {
            rPr.AppendChild(new SpecVanish());
        }, "structural");

        var para = doc.Elements.OfType<ParagraphElement>().First();
        await Assert.That(para.Runs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Position_ParsesHalfPoints_AsPoints()
    {
        // w:position w:val="12" → 12 half-points = 6pt baseline shift.
        var doc = ParseSingleParagraph(rPr =>
        {
            rPr.AppendChild(new Position { Val = "12" });
        });

        var run = doc.Elements.OfType<ParagraphElement>().First().Runs[0];
        await Assert.That(run.Properties.BaselineShiftPoints).IsEqualTo(6.0);
    }

    [Test]
    public async Task Position_NegativeValue_LowersBaseline()
    {
        var doc = ParseSingleParagraph(rPr =>
        {
            rPr.AppendChild(new Position { Val = "-8" });
        });

        var run = doc.Elements.OfType<ParagraphElement>().First().Runs[0];
        await Assert.That(run.Properties.BaselineShiftPoints).IsEqualTo(-4.0);
    }

    [Test]
    public async Task RunBorder_ParsesColorAndWidth()
    {
        var doc = ParseSingleParagraph(rPr =>
        {
            rPr.AppendChild(new Border { Val = BorderValues.Single, Size = 8, Color = "FF0000" });
        });

        var run = doc.Elements.OfType<ParagraphElement>().First().Runs[0];
        var border = run.Properties.Border;
        await Assert.That(border).IsNotNull();
        await Assert.That(border!.IsVisible).IsTrue();
        await Assert.That(border.ColorHex).IsEqualTo("FF0000");
    }

    [Test]
    public async Task Emboss_Imprint_OutlineOnly_FlowFromRPr()
    {
        var emboss = ParseSingleParagraph(rPr => rPr.AppendChild(new Emboss()))
            .Elements.OfType<ParagraphElement>().First().Runs[0];
        await Assert.That(emboss.Properties.Emboss).IsTrue();

        var imprint = ParseSingleParagraph(rPr => rPr.AppendChild(new Imprint()))
            .Elements.OfType<ParagraphElement>().First().Runs[0];
        await Assert.That(imprint.Properties.Imprint).IsTrue();

        var outlineOnly = ParseSingleParagraph(rPr => rPr.AppendChild(new OoxmlOutline()))
            .Elements.OfType<ParagraphElement>().First().Runs[0];
        await Assert.That(outlineOnly.Properties.OutlineOnly).IsTrue();
    }

    [Test]
    public async Task Defaults_AllRunEffectFlags_AreOff()
    {
        var props = new RunProperties();

        await Assert.That(props.Hidden).IsFalse();
        await Assert.That(props.BaselineShiftPoints).IsEqualTo(0);
        await Assert.That(props.Border).IsNull();
        await Assert.That(props.Emboss).IsFalse();
        await Assert.That(props.Imprint).IsFalse();
        await Assert.That(props.OutlineOnly).IsFalse();
    }

    static ParsedDocument ParseSingleParagraph(Action<OoxmlRunProperties> configureRPr, string text = "x")
    {
        using var stream = new MemoryStream();
        using (var pkg = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = pkg.AddMainDocumentPart();
            var body = new Body();
            var para = new Paragraph();
            var run = new OoxmlRun();
            var rPr = new OoxmlRunProperties();
            configureRPr(rPr);
            run.AppendChild(rPr);
            run.AppendChild(new Text(text));
            para.AppendChild(run);
            body.AppendChild(para);
            mainPart.Document = new(body);
        }

        stream.Position = 0;

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, stream.ToArray());
            return new DocumentParser().Parse(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
