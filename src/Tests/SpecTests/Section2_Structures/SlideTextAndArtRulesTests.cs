using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

/// <summary>
/// The slide rules a review of Basic.pptx (a stock PowerPoint template) turned up against
/// PowerPoint's own export of it: group flips, autonumbered bullets, a text box's first paragraph,
/// a table style's edges and translucent bands, and SVG pictures stretched to their frames.
/// </summary>
public class SlideTextAndArtRulesTests
{
    const string drawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    const string presentationNamespace = "http://schemas.openxmlformats.org/presentationml/2006/main";

    // A group's flipH mirrors its children within the group's own box: a child at the group's left
    // edge lands at its right.
    [Test]
    public async Task FlippedGroup_MirrorsChildWithinItsBox()
    {
        var group = new A.TransformGroup(
            new A.Offset { X = 1000, Y = 0 },
            new A.Extents { Cx = 400, Cy = 200 },
            new A.ChildOffset { X = 0, Y = 0 },
            new A.ChildExtents { Cx = 400, Cy = 200 })
        {
            HorizontalFlip = true
        };

        var transform = SlideTransform.Identity.Compose(group);
        var (x, y, width, height) = transform.Apply(0, 0, 100, 50);

        await Assert.That(transform.MirrorsX).IsTrue();
        await Assert.That(transform.MirrorsY).IsFalse();
        await Assert.That((x, y, width, height)).IsEqualTo((1300d, 0d, 100d, 50d));
    }

    // Two flips nested cancel: a child of a mirrored group inside a mirrored group stands unmirrored.
    [Test]
    public async Task NestedFlips_Cancel()
    {
        static A.TransformGroup Flipped() =>
            new(
                new A.Offset { X = 0, Y = 0 },
                new A.Extents { Cx = 400, Cy = 400 },
                new A.ChildOffset { X = 0, Y = 0 },
                new A.ChildExtents { Cx = 400, Cy = 400 })
            {
                VerticalFlip = true
            };

        var transform = SlideTransform.Identity.Compose(Flipped()).Compose(Flipped());
        var (_, y, _, height) = transform.Apply(0, 0, 100, 100);

        await Assert.That(transform.MirrorsY).IsFalse();
        await Assert.That((y, height)).IsEqualTo((0d, 100d));
    }

    // PowerPoint numbers an autonumbered run on, ends it at a plain paragraph of the same level, and
    // passes over a deeper paragraph; every marker was "1." before the counter.
    [Test]
    public async Task AutoNumber_CountsAndRestarts()
    {
        var markers = Markers(
            Numbered("arabicPeriod", "one"),
            Numbered("arabicPeriod", "two"),
            Bulleted("deeper", level: 1),
            Numbered("arabicPeriod", "three"),
            Plain("break"),
            Numbered("arabicPeriod", "again"));

        await Assert.That(markers).IsEqualTo("1.|2.|•|3.||1.");
    }

    [Test]
    public async Task AutoNumber_FollowsSchemeAndStart()
    {
        var markers = Markers(
            Numbered("alphaUcParenR", "a", startAt: 3),
            Numbered("alphaUcParenR", "b"),
            Numbered("romanLcParenBoth", "c"));

        await Assert.That(markers).IsEqualTo("C)|D)|(i)");
    }

    // An empty paragraph shows no marker and takes no number.
    [Test]
    public async Task AutoNumber_SkipsEmptyParagraph()
    {
        var markers = Markers(
            Numbered("arabicPeriod", "one"),
            Numbered("arabicPeriod", ""),
            Numbered("arabicPeriod", "two"));

        await Assert.That(markers).IsEqualTo("1.||2.");
    }

    // PowerPoint sets a text box's first line at the top of the box: only later paragraphs keep
    // their space before.
    [Test]
    public async Task FirstParagraph_DropsSpaceBefore()
    {
        var paragraphs = Parse(
            """<a:p><a:pPr><a:spcBef><a:spcPts val="1000"/></a:spcBef></a:pPr><a:r><a:t>first</a:t></a:r></a:p>""",
            """<a:p><a:pPr><a:spcBef><a:spcPts val="1000"/></a:spcBef></a:pPr><a:r><a:t>second</a:t></a:r></a:p>""");

        await Assert.That(paragraphs[0].Properties.SpacingBeforePoints).IsEqualTo(0);
        await Assert.That(paragraphs[1].Properties.SpacingBeforePoints).IsEqualTo(10);
    }

    // wholeTbl's top and bottom are the table's edges and insideH the rules between rows (here
    // none); the 20% band blends over the slide background.
    [Test]
    public async Task TableStyle_EdgesAndTranslucentBand()
    {
        using var stream = new MemoryStream();
        using var document = PresentationDocument.Create(stream, DocumentFormat.OpenXml.PresentationDocumentType.Presentation);
        var stylesPart = document.AddPresentationPart().AddNewPart<TableStylesPart>();
        stylesPart.TableStyleList = new(
            $$"""
              <a:tblStyleLst xmlns:a="{{drawingNamespace}}" def="{S}">
                <a:tblStyle styleId="{S}" styleName="Light">
                  <a:wholeTbl><a:tcStyle><a:tcBdr>
                    <a:top><a:ln w="12700"><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></a:ln></a:top>
                    <a:bottom><a:ln w="12700"><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></a:ln></a:bottom>
                    <a:insideH><a:ln><a:noFill/></a:ln></a:insideH>
                  </a:tcBdr></a:tcStyle></a:wholeTbl>
                  <a:band1H><a:tcStyle><a:fill><a:solidFill><a:srgbClr val="0000FF"><a:alpha val="20000"/></a:srgbClr></a:solidFill></a:fill></a:tcStyle></a:band1H>
                </a:tblStyle>
              </a:tblStyleLst>
              """);
        var table = new A.Table(
            $"""<a:tbl xmlns:a="{drawingNamespace}"><a:tblPr bandRow="1"><a:tableStyleId>{"{S}"}</a:tableStyleId></a:tblPr></a:tbl>""");

        var resolver = TableStyleResolver.For(table, stylesPart, null, "FFFFFF");
        var top = resolver.Resolve(table.TableProperties, 0, 3, 0, 1);
        var middle = resolver.Resolve(table.TableProperties, 1, 3, 0, 1);

        await Assert.That(top.Top?.IsVisible).IsTrue();
        await Assert.That(top.Bottom?.IsVisible).IsFalse();
        await Assert.That(middle.Top?.IsVisible).IsFalse();
        await Assert.That(top.BackgroundColorHex).IsEqualTo("CCCCFF");
        await Assert.That(middle.BackgroundColorHex).IsNull();
    }

    // An <img> keeps an SVG's own aspect; the export makes it fill the frame as Office stretches it.
    [Test]
    public async Task Svg_IsStretchedToItsFrame()
    {
        var stretched = Encoding.UTF8.GetString(
            SvgStretch.Apply("""<?xml version="1.0"?><svg width="361" height="1085" viewBox="0 0 361 1085" preserveAspectRatio="xMidYMid"><rect/></svg>"""u8.ToArray())!);

        await Assert.That(stretched).IsEqualTo("""<?xml version="1.0"?><svg preserveAspectRatio="none" width="361" height="1085" viewBox="0 0 361 1085"><rect/></svg>""");
    }

    // With no viewBox there is nothing to stretch, so the SVG's own size becomes one.
    [Test]
    public async Task Svg_WithoutViewBox_GainsOne()
    {
        var stretched = Encoding.UTF8.GetString(
            SvgStretch.Apply("""<svg width="20px" height="10"><rect/></svg>"""u8.ToArray())!);

        await Assert.That(stretched).IsEqualTo("""<svg preserveAspectRatio="none" viewBox="0 0 20 10" width="20px" height="10"><rect/></svg>""");
    }

    static string Numbered(string scheme, string text, int? startAt = null)
    {
        var start = startAt is { } value ? $" startAt=\"{value}\"" : "";
        return $"""<a:p><a:pPr><a:buAutoNum type="{scheme}"{start}/></a:pPr><a:r><a:t>{text}</a:t></a:r></a:p>""";
    }

    static string Bulleted(string text, int level) =>
        $"""<a:p><a:pPr lvl="{level}"><a:buChar char="•"/></a:pPr><a:r><a:t>{text}</a:t></a:r></a:p>""";

    static string Plain(string text) =>
        $"<a:p><a:pPr><a:buNone/></a:pPr><a:r><a:t>{text}</a:t></a:r></a:p>";

    // Each paragraph's marker in order, "|"-separated, empty where it has none.
    static string Markers(params string[] paragraphs) =>
        string.Join("|", Parse(paragraphs).Select(_ => _.Properties.Numbering?.Text));

    static List<ParagraphElement> Parse(params string[] paragraphs)
    {
        var body = new P.TextBody(
            $"""<p:txBody xmlns:p="{presentationNamespace}" xmlns:a="{drawingNamespace}"><a:bodyPr/>{string.Concat(paragraphs)}</p:txBody>""");
        return new DrawingTextParser(null, null, "Arial")
            .Parse(body, new([]))
            .Cast<ParagraphElement>()
            .ToList();
    }
}
