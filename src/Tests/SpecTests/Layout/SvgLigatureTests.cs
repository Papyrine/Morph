using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// The SVG painter writes an x for every character, and turns ligatures off so the browser draws each
/// character apart. SixLabors.Fonts makes bold Aptos's "fi" one glyph, so the "i" has no glyph of its own
/// to take an x from: it must still stand after the "f", or it is drawn over it and leaves a gap
/// ("f rst").
/// </summary>
public class SvgLigatureTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_ligatures_tail_stands_after_its_head(bool bold)
    {
        using var stream = Build(bold);
        var parsed = DocumentConverter.Parse(stream, null);
        var options = new ImageExportOptions
        {
            Dpi = 96,
            FontDirectory = fontsDirectory
        };
        var laidOut = ImageSharpDocumentConverter.Layout(parsed, options);
        using var context = ImageSharpDocumentConverter.CreateContext(parsed, options);
        var markup = SvgPainter.PaintPage(laidOut.Pages[0], context, "Morph Text").Markup;

        var text = Regex.Match(markup, "<text x=\"([^\"]*)\"[^>]*>first</text>");
        await Assert.That(text.Success).IsTrue();
        var xs = text.Groups[1].Value.Split(' ').Select(_ => double.Parse(_, CultureInfo.InvariantCulture)).ToList();
        await Assert.That(xs.Count).IsEqualTo(5);
        for (var i = 1; i < xs.Count; i++)
        {
            await Assert.That(xs[i]).IsGreaterThan(xs[i - 1]);
        }
    }

    static MemoryStream Build(bool bold)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var properties = new W.RunProperties(new W.RunFonts {Ascii = "Aptos", HighAnsi = "Aptos"});
            if (bold)
            {
                properties.Append(new W.Bold());
            }

            properties.Append(new W.FontSize {Val = "24"});
            var main = document.AddMainDocumentPart();
            main.Document = new(new W.Body(new W.Paragraph(new W.Run(properties, new W.Text("first")))));
        }

        stream.Position = 0;
        return stream;
    }
}
