using System.Text.Json;

// The text layer is read straight off the laid-out tree, so each rule is pinned with the smallest
// synthesized page that exhibits it: the builder sees nothing but placed items. Plain text asserts what a
// copy yields (and what find searches); the JSON asserts where morph-text.js will put the text.
public class TextLayerBuilderTests
{
    static readonly PageSettings letter = new()
    {
        WidthPoints = 612,
        HeightPoints = 792,
        MarginBottom = 72
    };

    static RunProperties Font(double size = 10, bool bold = false, bool italic = false) =>
        new()
        {
            FontSizePoints = size,
            Bold = bold,
            Italic = italic
        };

    static ParagraphElement Paragraph(TextAlignment alignment = TextAlignment.Left, bool withTab = false) =>
        new()
        {
            Runs = withTab
                ? [new() { Text = "a" }, new() { Text = "\t", IsTab = true }]
                : [new() { Text = "a" }],
            Properties = new()
            {
                Alignment = alignment
            }
        };

    static PlacedLine Line(ParagraphElement paragraph, int lineIndex, float y, params PlacedRun[] runs) =>
        new(72, y, 468, 12, y + 9, paragraph, lineIndex, runs, []);

    static PlacedRun Text(float x, float width, string text, RunProperties? properties = null) =>
        new(x, width, text, properties ?? Font());

    static PageTextLayer Build(params PlacedItem[] items) =>
        TextLayerBuilder.Build(new(1, letter, items));

    static JsonElement Items(PageTextLayer layer) =>
        JsonDocument.Parse(layer.Json).RootElement.GetProperty("i");

    [Test]
    public async Task Line_CarriesItsBoxBaselineAndSpans()
    {
        var layer = Build(Line(Paragraph(), 0, 100, Text(72, 30, "Hello", Font(10, bold: true))));

        await Assert.That(layer.Text).IsEqualTo("Hello\n");
        var line = Items(layer)[0];
        await Assert.That(line.GetProperty("x").GetDouble()).IsEqualTo(72);
        await Assert.That(line.GetProperty("y").GetDouble()).IsEqualTo(100);
        await Assert.That(line.GetProperty("h").GetDouble()).IsEqualTo(12);
        await Assert.That(line.GetProperty("b").GetDouble()).IsEqualTo(109);
        await Assert.That(line.GetProperty("e").GetInt32()).IsEqualTo(2);
        var span = line.GetProperty("s")[0];
        await Assert.That(span.GetProperty("x").GetDouble()).IsEqualTo(72);
        await Assert.That(span.GetProperty("w").GetDouble()).IsEqualTo(30);
        await Assert.That(span.GetProperty("t").GetString()).IsEqualTo("Hello");
        await Assert.That(span.GetProperty("z").GetDouble()).IsEqualTo(10);
        await Assert.That(span.GetProperty("f").GetInt32()).IsEqualTo(1);
    }

    // A justified line is one run per word with the spaces removed; the gaps come back as positioned
    // separators so a copy doesn't run the words together.
    [Test]
    public async Task JustifiedGaps_BecomeSpaceSeparators()
    {
        var layer = Build(Line(Paragraph(TextAlignment.Justify), 0, 100, Text(72, 20, "The"), Text(100, 30, "quick")));

        await Assert.That(layer.Text).IsEqualTo("The quick\n");
        var separator = Items(layer)[0].GetProperty("s")[1];
        await Assert.That(separator.GetProperty("t").GetString()).IsEqualTo(" ");
        await Assert.That(separator.GetProperty("x").GetDouble()).IsEqualTo(92);
        await Assert.That(separator.GetProperty("w").GetDouble()).IsEqualTo(8);
        await Assert.That(separator.GetProperty("f").GetInt32()).IsEqualTo(4);
    }

    // A formatting change splits a line into runs that touch; nothing goes between them.
    [Test]
    public async Task ContiguousRuns_GetNoSeparator()
    {
        var layer = Build(Line(Paragraph(), 0, 100, Text(72, 20, "Bold", Font(bold: true)), Text(92, 20, "face")));

        await Assert.That(layer.Text).IsEqualTo("Boldface\n");
    }

    [Test]
    public async Task WhitespaceAtTheBoundary_GetsNoSecondSpace()
    {
        var layer = Build(Line(Paragraph(TextAlignment.Justify), 0, 100, Text(72, 30, "Hello "), Text(110, 25, "world")));

        await Assert.That(layer.Text).IsEqualTo("Hello world\n");
    }

    [Test]
    public async Task SoftWrap_JoinsWithASpace_ParagraphEnd_Breaks()
    {
        var first = Paragraph();
        var second = Paragraph();
        var layer = Build(
            Line(first, 0, 100, Text(72, 20, "one")),
            Line(first, 1, 112, Text(72, 20, "two")),
            Line(second, 0, 130, Text(72, 25, "three")));

        await Assert.That(layer.Text).IsEqualTo("one two\nthree\n");
    }

    [Test]
    public async Task SoftWrapAfterAHyphen_AddsNothing()
    {
        var paragraph = Paragraph();
        var layer = Build(
            Line(paragraph, 0, 100, Text(72, 25, "self-")),
            Line(paragraph, 1, 112, Text(72, 45, "contained")));

        await Assert.That(layer.Text).IsEqualTo("self-contained\n");
    }

    // A table of contents line: the dots are a leader filling a tab's gap, so the gap copies as the tab.
    [Test]
    public async Task Leader_BecomesATab()
    {
        var layer = Build(Line(
            Paragraph(withTab: true),
            0,
            100,
            Text(72, 40, "Chapter"),
            new(112, 300, "", Font(), TabLeader.Dot),
            Text(420, 10, "12")));

        await Assert.That(layer.Text).IsEqualTo("Chapter\t12\n");
    }

    [Test]
    public async Task WideGapInATabbedParagraph_BecomesATab()
    {
        var layer = Build(Line(Paragraph(withTab: true), 0, 100, Text(72, 25, "Name"), Text(200, 30, "Value")));

        await Assert.That(layer.Text).IsEqualTo("Name\tValue\n");
    }

    [Test]
    public async Task PrivateUseListMarker_BecomesABullet()
    {
        var layer = Build(Line(Paragraph(), 0, 100, Text(72, 6, ""), Text(90, 30, "Item")));

        await Assert.That(layer.Text).IsEqualTo("• Item\n");
    }

    [Test]
    public async Task Superscript_CarriesItsDrawnSizeAndShift()
    {
        var superscript = new RunProperties
        {
            FontSizePoints = 12,
            VerticalAlignment = VerticalRunAlignment.Superscript
        };
        var layer = Build(Line(Paragraph(), 0, 100, Text(72, 10, "E"), new(82, 5, "2", superscript, BaselineShift: 4)));

        var span = Items(layer)[0].GetProperty("s")[1];
        await Assert.That(span.GetProperty("z").GetDouble()).IsEqualTo(7.8).Within(0.001);
        await Assert.That(span.GetProperty("d").GetDouble()).IsEqualTo(4);
        await Assert.That(layer.Text).IsEqualTo("E2\n");
    }

    // Cells copy tab-separated with the row ending in a line break — a spreadsheet paste lands in cells —
    // and an empty cell still contributes its tab.
    [Test]
    public async Task TableRow_TabsBetweenCells_EvenEmptyOnes()
    {
        var row = Row(
            Cell(0, [Line(Paragraph(), 0, 100, Text(72, 10, "A"))]),
            Cell(1, []),
            Cell(2, [Line(Paragraph(), 0, 100, Text(272, 10, "C"))]));

        var layer = Build(row, Line(Paragraph(), 0, 140, Text(72, 30, "After")));

        await Assert.That(layer.Text).IsEqualTo("A\t\tC\nAfter\n");
    }

    [Test]
    public async Task ClippedCell_BecomesAClipFrame_WithItsChildrenRelativeToIt()
    {
        var clipped = Cell(0, [Line(Paragraph(), 0, 100, Text(80, 400, "overflowing text"))]) with
        {
            ClipContent = true,
            ClipSpillLeft = 5,
            ClipSpillRight = 15
        };

        var frame = Items(Build(Row(clipped)))[0];

        await Assert.That(frame.GetProperty("k").GetString()).IsEqualTo("c");
        await Assert.That(frame.GetProperty("x").GetDouble()).IsEqualTo(67);
        await Assert.That(frame.GetProperty("w").GetDouble()).IsEqualTo(120);
        await Assert.That(frame.GetProperty("cx").GetInt32()).IsEqualTo(1);
        var child = frame.GetProperty("i")[0];
        await Assert.That(child.GetProperty("s")[0].GetProperty("x").GetDouble()).IsEqualTo(13);
        await Assert.That(child.GetProperty("y").GetDouble()).IsEqualTo(4);
    }

    // A Word exact-height row clips vertically only: ink may overhang the cell sideways.
    [Test]
    public async Task VerticalOnlyClip_KeepsTheCellBox()
    {
        var clipped = Cell(0, [Line(Paragraph(), 0, 100, Text(80, 400, "tall"))]) with
        {
            ClipContent = true,
            ClipSpillLeft = 5,
            ClipHorizontally = false
        };

        var frame = Items(Build(Row(clipped)))[0];

        await Assert.That(frame.GetProperty("x").GetDouble()).IsEqualTo(72);
        await Assert.That(frame.GetProperty("w").GetDouble()).IsEqualTo(100);
        await Assert.That(frame.GetProperty("cx").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task RotatedGroups_NestAsFrames_EachRelativeToItsParent()
    {
        var inner = new PlacedRotatedGroup(210, 320, 40, 20, [Line(Paragraph(), 0, 322, Text(215, 20, "inner"))], 90);
        var outer = new PlacedRotatedGroup(200, 300, 100, 50, [Line(Paragraph(), 0, 305, Text(205, 30, "outer")), inner], -90);

        var frame = Items(Build(outer))[0];

        await Assert.That(frame.GetProperty("k").GetString()).IsEqualTo("r");
        await Assert.That(frame.GetProperty("r").GetDouble()).IsEqualTo(-90);
        var nested = frame.GetProperty("i")[1];
        await Assert.That(nested.GetProperty("x").GetDouble()).IsEqualTo(10);
        await Assert.That(nested.GetProperty("y").GetDouble()).IsEqualTo(20);
        await Assert.That(nested.GetProperty("r").GetDouble()).IsEqualTo(90);
        await Assert.That(nested.GetProperty("i")[0].GetProperty("s")[0].GetProperty("x").GetDouble()).IsEqualTo(5);
    }

    // Paint order puts the footer band before the body; reading order puts it after.
    [Test]
    public async Task FooterLines_MoveAfterTheBody()
    {
        var layer = Build(
            Line(Paragraph(), 0, 750, Text(72, 30, "Footer")),
            Line(Paragraph(), 0, 100, Text(72, 30, "Body")));

        await Assert.That(layer.Text).IsEqualTo("Body\nFooter\n");
    }

    [Test]
    public async Task GapCoveredByAnInlineImage_GetsNoSeparator()
    {
        var line = Line(Paragraph(TextAlignment.Justify), 0, 100, Text(72, 20, "left"), Text(150, 20, "right")) with
        {
            Images = [new(95, 90, 50, 20, null)]
        };

        await Assert.That(Build(line).Text).IsEqualTo("leftright\n");
    }

    [Test]
    public async Task WarpedWordArt_IsOneBox()
    {
        var box = Items(Build(new PlacedWordArt(100, 100, 200, 60, new Visual("Grand Sale"))))[0];

        await Assert.That(box.GetProperty("k").GetString()).IsEqualTo("b");
        await Assert.That(box.GetProperty("t").GetString()).IsEqualTo("Grand Sale");
    }

    // A guard on the layout engine: a new kind of placed item fails here rather than silently dropping
    // its text from the layer. Four kinds carry text; four carry none.
    [Test]
    public async Task EveryPlacedItemKind_IsAccountedFor()
    {
        var kinds = typeof(PlacedItem).Assembly
            .GetTypes()
            .Where(_ => _.IsSubclassOf(typeof(PlacedItem)) && !_.IsAbstract)
            .Select(_ => _.Name)
            .Order()
            .ToList();

        await Assert.That(kinds).IsEquivalentTo(
            new[]
            {
                "PlacedBorder",
                "PlacedImage",
                "PlacedLine",
                "PlacedRotatedGroup",
                "PlacedShading",
                "PlacedShape",
                "PlacedTableRow",
                "PlacedWordArt"
            });
    }

    // The contract with morph-text.js, indented so a change reads as a diff.
    [Test]
    public Task Json_Snapshot()
    {
        var paragraph = Paragraph(TextAlignment.Justify);
        var turned = new PlacedLine(305, 302, 30, 12, 311, Paragraph(), 0, [Text(305, 30, "turned")], []);
        var layer = Build(
            Line(paragraph, 0, 100, Text(72, 20, "The", Font(bold: true)), Text(100, 30, "quick", Font(italic: true))),
            Line(paragraph, 1, 112, Text(72, 25, "brown")),
            Row(Cell(0, [Line(Paragraph(), 0, 140, Text(80, 20, "cell"))]), Cell(1, [])),
            new PlacedRotatedGroup(300, 300, 60, 20, [turned], -90));

        return Verify(JsonSerializer.Serialize(JsonDocument.Parse(layer.Json).RootElement, indented), extension: "json");
    }

    static readonly JsonSerializerOptions indented = new()
    {
        WriteIndented = true
    };

    static PlacedTableRow Row(params PlacedCell[] cells) =>
        new(72, 96, 300, 40, new() { Rows = [] }, 0, false, cells);

    static PlacedCell Cell(int column, IReadOnlyList<PlacedItem> content) =>
        new(72 + column * 100, 96, 100, 40, null, null, content);

    sealed class Visual(string text) : IWordArtVisual
    {
        public string Text => text;
        public double WidthPoints => 200;
        public double HeightPoints => 60;
        public string FontFamily => "Aptos";
        public double FontSizePoints => 36;
        public bool Bold => false;
        public bool Italic => false;
        public string? FillColorHex => null;
        public string? OutlineColorHex => null;
        public double OutlineWidthPoints => 0;
        public bool HasShadow => false;
        public bool HasReflection => false;
        public bool HasGlow => false;
        public WordArtTransform Transform => WordArtTransform.None;
    }
}
