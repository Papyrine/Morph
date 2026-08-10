using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Covers <c>DocumentParser.ResolveStyleBorders</c> — a table style's <c>w:tblBorders</c> resolved
/// through the <c>w:basedOn</c> chain, per side.
///
/// The shape that reported it: a government template whose <c>HouseOfRepsTable</c> is based on a
/// <c>ChamberTable</c> carrying top/bottom/insideH, and adds nothing of its own but a
/// <c>w:tblStylePr</c> firstRow fill. Reading only the leaf's own <c>w:tblPr</c> found an empty
/// element and rendered the table with no rules at all, while the coloured header band still
/// painted — so it read as a deliberately borderless table rather than a bug.
/// </summary>
public class TableStyleBorderInheritanceTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    static Style TableStyle(string styleId, string? basedOn, string? borders)
    {
        var basedOnXml = basedOn == null ? "" : $"""<w:basedOn w:val="{basedOn}"/>""";
        var tblPr = $"<w:tblPr>{borders ?? ""}</w:tblPr>";
        return [with($"""<w:style {wNs} w:type="table" w:styleId="{styleId}">{basedOnXml}{tblPr}</w:style>""")];
    }

    static DocumentParser Parser() => new("Arial");

    [Test]
    public async Task EmptyLeafInheritsWholeGridFromBase()
    {
        var chamber = TableStyle("ChamberTable", null,
            $"""
             <w:tblBorders {wNs}>
               <w:top w:val="single" w:color="auto" w:sz="4" w:space="0"/>
               <w:bottom w:val="single" w:color="auto" w:sz="4" w:space="0"/>
               <w:insideH w:val="single" w:color="auto" w:sz="4" w:space="0"/>
             </w:tblBorders>
             """);
        var houseOfReps = TableStyle("HouseOfRepsTable", "ChamberTable", borders: null);

        var (outer, insideH, insideV) = Parser().ResolveStyleBorders(houseOfReps,
            new() {["ChamberTable"] = chamber, ["HouseOfRepsTable"] = houseOfReps});

        await Assert.That(outer.Top.IsVisible).IsTrue();
        await Assert.That(outer.Bottom.IsVisible).IsTrue();
        await Assert.That(insideH.IsVisible).IsTrue();
        // The base declares neither, so neither appears.
        await Assert.That(outer.Left.IsVisible).IsFalse();
        await Assert.That(insideV.IsVisible).IsFalse();
        await Assert.That(outer.Top.WidthPoints).IsEqualTo(0.5);
        await Assert.That(outer.Top.ColorHex).IsEqualTo("000000");
    }

    // Nearest ancestor wins PER SIDE, so a derived style can restate one edge and leave the rest
    // of the base's grid intact.
    [Test]
    public async Task DerivedSideWinsAndTheRestStillInherits()
    {
        var basedOn = TableStyle("Base", null,
            $"""
             <w:tblBorders {wNs}>
               <w:top w:val="single" w:color="FF0000" w:sz="4" w:space="0"/>
               <w:bottom w:val="single" w:color="auto" w:sz="4" w:space="0"/>
             </w:tblBorders>
             """);
        var derived = TableStyle("Derived", "Base",
            $"""
             <w:tblBorders {wNs}>
               <w:top w:val="single" w:color="0000FF" w:sz="24" w:space="0"/>
             </w:tblBorders>
             """);

        var (outer, _, _) = Parser().ResolveStyleBorders(derived,
            new() {["Base"] = basedOn, ["Derived"] = derived});

        await Assert.That(outer.Top.ColorHex).IsEqualTo("0000FF");
        await Assert.That(outer.Top.WidthPoints).IsEqualTo(3);
        await Assert.That(outer.Bottom.IsVisible).IsTrue();
    }

    // A side switched off explicitly is a DECLARATION, so it stops the walk rather than falling
    // through to the base's visible rule.
    [Test]
    public async Task ExplicitNoneStopsTheWalkForThatSide()
    {
        var basedOn = TableStyle("Base", null,
            $"""
             <w:tblBorders {wNs}>
               <w:top w:val="single" w:color="auto" w:sz="4" w:space="0"/>
               <w:bottom w:val="single" w:color="auto" w:sz="4" w:space="0"/>
             </w:tblBorders>
             """);
        var derived = TableStyle("Derived", "Base",
            $"""<w:tblBorders {wNs}><w:top w:val="none"/></w:tblBorders>""");

        var (outer, _, _) = Parser().ResolveStyleBorders(derived,
            new() {["Base"] = basedOn, ["Derived"] = derived});

        await Assert.That(outer.Top.IsVisible).IsFalse();
        await Assert.That(outer.Bottom.IsVisible).IsTrue();
    }

    // A style with no w:tblBorders of its own is transparent, so the merge spans the whole chain.
    [Test]
    public async Task MergesAcrossMultiLevelChain()
    {
        var normal = TableStyle("TableNormal", null,
            $"""<w:tblBorders {wNs}><w:insideV w:val="single" w:color="auto" w:sz="4" w:space="0"/></w:tblBorders>""");
        var chamber = TableStyle("ChamberTable", "TableNormal",
            $"""<w:tblBorders {wNs}><w:top w:val="single" w:color="auto" w:sz="4" w:space="0"/></w:tblBorders>""");
        var houseOfReps = TableStyle("HouseOfRepsTable", "ChamberTable", borders: null);

        var (outer, _, insideV) = Parser().ResolveStyleBorders(houseOfReps,
            new()
            {
                ["TableNormal"] = normal,
                ["ChamberTable"] = chamber,
                ["HouseOfRepsTable"] = houseOfReps
            });

        await Assert.That(outer.Top.IsVisible).IsTrue();
        await Assert.That(insideV.IsVisible).IsTrue();
    }

    // A w:basedOn cycle terminates instead of spinning — the styles part is untrusted input.
    [Test]
    public async Task CyclicBasedOnTerminates()
    {
        var first = TableStyle("First", "Second",
            $"""<w:tblBorders {wNs}><w:top w:val="single" w:color="auto" w:sz="4" w:space="0"/></w:tblBorders>""");
        var second = TableStyle("Second", "First", borders: null);

        var (outer, _, _) = Parser().ResolveStyleBorders(first,
            new() {["First"] = first, ["Second"] = second});

        await Assert.That(outer.Top.IsVisible).IsTrue();
        await Assert.That(outer.Bottom.IsVisible).IsFalse();
    }
}
