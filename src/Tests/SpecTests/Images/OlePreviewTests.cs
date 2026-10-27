using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// A Word document's embedded OLE object is drawn from the preview picture it stores
/// (<see cref="OlePreviewParser"/>). ole_chart_preview is the corpus case: a LibreOffice chart floating
/// as a VML shape over its EMF preview. Each variant here is that file with its object rewritten in one
/// way — in the line, unsized, in an <c>mc:AlternateContent</c>, previewed as a PNG or a WMF.
/// </summary>
public class OlePreviewTests
{
    static readonly string fixture = Path.Combine(ScenarioInputs.Root(ScenarioFormat.Word), "ole_chart_preview", "input.docx");

    const string placement = "position:absolute;left:0;text-align:left;margin-left:92.5pt;margin-top:8.5pt;";

    // A 1x1 PNG.
    static readonly byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // The fixture with its document part edited; edit may add a part and name its relationship.
    static ParsedDocument Parse(Func<MainDocumentPart, string, string>? edit = null)
    {
        using var stream = new MemoryStream();
        stream.Write(File.ReadAllBytes(fixture));
        if (edit != null)
        {
            using var document = WordprocessingDocument.Open(stream, true);
            var main = document.MainDocumentPart!;
            string xml;
            using (var reader = new StreamReader(main.GetStream(FileMode.Open, FileAccess.Read)))
            {
                xml = reader.ReadToEnd();
            }

            var edited = edit(main, xml);
            using var writer = new StreamWriter(main.GetStream(FileMode.Create, FileAccess.Write));
            writer.Write(edited);
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream);
    }

    static List<FloatingImageElement> Floats(ParsedDocument document) =>
        [.. document.Elements.OfType<FloatingImageElement>()];

    static List<Run> InlinePictures(ParsedDocument document) =>
        [.. document.Elements.OfType<ParagraphElement>().SelectMany(_ => _.Runs).Where(_ => _.InlineImageData != null)];

    // The chart's preview picture, pointed at a new part of the given type holding the given bytes.
    static Func<MainDocumentPart, string, string> Preview(PartTypeInfo type, byte[] data) =>
        (main, xml) =>
        {
            var part = main.AddImagePart(type);
            using (var target = part.GetStream(FileMode.Create))
            {
                target.Write(data);
            }

            return xml.Replace("<v:imagedata r:id=\"rId8\"", $"<v:imagedata r:id=\"{main.GetIdOfPart(part)}\"");
        };

    [Test]
    public async Task A_floating_object_is_drawn_from_its_emf_preview_where_its_shape_puts_it()
    {
        var chart = Floats(Parse()).Single();

        await Assert.That(chart.ContentType).IsEqualTo("image/x-emf");
        await Assert.That(chart.Metafile).IsNotNull();
        await Assert.That(chart.WidthPoints).IsEqualTo(322.8).Within(0.001);
        await Assert.That(chart.HeightPoints).IsEqualTo(162.2).Within(0.001);
        await Assert.That(chart.HorizontalPositionPoints).IsEqualTo(92.5).Within(0.001);
        await Assert.That(chart.VerticalPositionPoints).IsEqualTo(8.5).Within(0.001);
        await Assert.That(chart.HorizontalAnchor).IsEqualTo(HorizontalAnchor.Column);
        await Assert.That(chart.VerticalAnchor).IsEqualTo(VerticalAnchor.Paragraph);
        await Assert.That(chart.WrapType).IsEqualTo(WrapType.Square);
        await Assert.That(chart.WrapTextSide).IsEqualTo(WrapTextSide.Largest);
        await Assert.That(chart.BehindText).IsFalse();

        // The chart's labels, standing on the white of the filled shape.
        var texts = chart.Metafile!.Items.OfType<MetafileText>().Select(_ => _.Text).ToList();
        await Assert.That(texts).Contains("Row 1");
        await Assert.That(texts).Contains("Column 3");
        await Assert.That(chart.Metafile.Items[0] is MetafilePath {FillHex: "FFFFFF", LineHex: null}).IsTrue();
    }

    [Test]
    public async Task An_unfilled_shape_puts_no_backdrop_under_the_chart()
    {
        var chart = Floats(Parse((_, xml) => xml.Replace(" filled=\"t\">", " filled=\"f\">"))).Single();

        await Assert.That(chart.Metafile!.Items[0] is MetafilePath {FillHex: "FFFFFF"}).IsFalse();
    }

    [Test]
    public async Task An_object_not_absolutely_positioned_stands_in_the_line()
    {
        var document = Parse((_, xml) => xml.Replace(placement, ""));

        await Assert.That(Floats(document)).IsEmpty();
        var run = InlinePictures(document).Single();
        await Assert.That(run.InlineMetafile).IsNotNull();
        await Assert.That(run.InlineImageWidthPoints).IsEqualTo(322.8).Within(0.001);
        await Assert.That(run.InlineImageHeightPoints).IsEqualTo(162.2).Within(0.001);
    }

    [Test]
    public async Task A_shape_with_no_size_takes_the_objects_original_one()
    {
        // w:dxaOrig and w:dyaOrig are both 1440 twips.
        var chart = Floats(Parse((_, xml) => xml.Replace("width:322.8pt;height:162.2pt;", ""))).Single();

        await Assert.That(chart.WidthPoints).IsEqualTo(72);
        await Assert.That(chart.HeightPoints).IsEqualTo(72);
    }

    [Test]
    public async Task A_negative_z_index_puts_it_behind_the_text()
    {
        var chart = Floats(Parse((_, xml) => xml.Replace("z-index:1;", "z-index:-251658240;"))).Single();

        await Assert.That(chart.BehindText).IsTrue();
        await Assert.That(chart.RelativeHeight).IsEqualTo(251658240u);
    }

    [Test]
    public async Task A_centred_shape_is_centred_between_the_margins()
    {
        var chart = Floats(Parse((_, xml) => xml.Replace(placement, placement + "mso-position-horizontal:center;mso-position-horizontal-relative:margin;"))).Single();

        // A letter page's 612pt less two 1134-twip margins.
        await Assert.That(chart.HorizontalAnchor).IsEqualTo(HorizontalAnchor.Margin);
        await Assert.That(chart.HorizontalPositionPoints).IsEqualTo((612 - 2 * 56.7 - 322.8) / 2).Within(0.001);
    }

    [Test]
    public async Task An_object_in_a_vml_picture_is_drawn_the_same()
    {
        var chart = Floats(Parse((_, xml) => Regex.Replace(xml, "<w:object [^>]*>(.*?)</w:object>", "<w:pict>$1</w:pict>", RegexOptions.Singleline))).Single();

        await Assert.That(chart.Metafile).IsNotNull();
    }

    [Test]
    public async Task An_object_in_the_fallback_of_an_alternate_content_is_drawn_once()
    {
        // A choice the parser draws nothing from leaves the fallback's object; one in both is drawn once.
        var fallback = Parse((_, xml) => Regex.Replace(xml, "(<w:object .*?</w:object>)", "<mc:AlternateContent><mc:Choice Requires=\"w99\"></mc:Choice><mc:Fallback>$1</mc:Fallback></mc:AlternateContent>", RegexOptions.Singleline));
        var both = Parse((_, xml) => Regex.Replace(xml, "(<w:object .*?</w:object>)", "<mc:AlternateContent><mc:Choice Requires=\"w14\">$1</mc:Choice><mc:Fallback>$1</mc:Fallback></mc:AlternateContent>", RegexOptions.Singleline));

        await Assert.That(Floats(fallback).Single().Metafile).IsNotNull();
        await Assert.That(Floats(both)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task A_raster_preview_is_placed_as_a_picture()
    {
        var chart = Floats(Parse(Preview(ImagePartType.Png, png))).Single();

        await Assert.That(chart.ContentType).IsEqualTo("image/png");
        await Assert.That(chart.ImageData).IsEquivalentTo(png);
        await Assert.That(chart.Metafile).IsNull();
    }

    [Test]
    public async Task A_wmf_preview_is_played_like_an_emf()
    {
        var chart = Floats(Parse(Preview(ImagePartType.Wmf, MetafileReaderTests.Wmf()))).Single();

        await Assert.That(chart.Metafile!.Items.OfType<MetafileText>().Single().Text).IsEqualTo("Hi");
    }

    [Test]
    public async Task An_object_with_no_readable_preview_leaves_its_place_blank()
    {
        var missing = Parse((_, xml) => xml.Replace("<v:imagedata r:id=\"rId8\"", "<v:imagedata r:id=\"rId999\""));
        var unreadable = Parse(Preview(ImagePartType.Emf, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]));

        await Assert.That(Floats(missing)).IsEmpty();
        await Assert.That(InlinePictures(missing)).IsEmpty();
        await Assert.That(Floats(unreadable)).IsEmpty();
    }
}
