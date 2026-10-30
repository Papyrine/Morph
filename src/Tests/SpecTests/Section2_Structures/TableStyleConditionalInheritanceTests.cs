using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Covers <c>DocumentParser.ResolveStyleConditionals</c> — a table style's <c>w:tblStylePr</c> regions
/// resolved through the <c>w:basedOn</c> chain, a region's <c>w:tcPr</c> and <c>w:rPr</c> each taken whole
/// from the nearest style that declares it (Word-probed, <c>_probe_tblinherit</c>). A derived style that
/// restates only a region's run properties keeps the base's cell properties for it, and vice versa.
/// </summary>
public class TableStyleConditionalInheritanceTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    static Style TableStyle(string styleId, string? basedOn, string blocks)
    {
        var basedOnXml = basedOn == null ? "" : $"""<w:basedOn w:val="{basedOn}"/>""";
        return [with($"""<w:style {wNs} w:type="table" w:styleId="{styleId}">{basedOnXml}<w:tblPr/>{blocks}</w:style>""")];
    }

    static DocumentParser Parser() => new("Arial");

    [Test]
    public async Task DerivedRegionReplacesEachBlockItDeclares()
    {
        var basedOn = TableStyle("Base", null,
            """
            <w:tblStylePr w:type="firstRow">
              <w:rPr><w:b/><w:sz w:val="28"/><w:color w:val="FF0000"/></w:rPr>
              <w:tcPr>
                <w:tcBorders>
                  <w:top w:val="single" w:sz="24" w:color="000000"/>
                  <w:bottom w:val="single" w:sz="24" w:color="000000"/>
                </w:tcBorders>
                <w:shd w:val="clear" w:color="auto" w:fill="CCCCCC"/>
              </w:tcPr>
            </w:tblStylePr>
            <w:tblStylePr w:type="lastRow">
              <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="EEEEEE"/></w:tcPr>
            </w:tblStylePr>
            """);
        var derived = TableStyle("Derived", "Base",
            """
            <w:tblStylePr w:type="firstRow">
              <w:rPr><w:color w:val="0000FF"/></w:rPr>
              <w:tcPr>
                <w:tcBorders><w:bottom w:val="nil"/></w:tcBorders>
                <w:shd w:val="clear" w:color="auto" w:fill="112233"/>
              </w:tcPr>
            </w:tblStylePr>
            """);

        var conditionals = Parser().ResolveStyleConditionals(derived,
            new() {["Base"] = basedOn, ["Derived"] = derived})!;

        var firstRow = conditionals[TableStyleOverrideValues.FirstRow];
        await Assert.That(firstRow.BackgroundColorHex).IsEqualTo("112233");
        await Assert.That(firstRow.RunColorHex).IsEqualTo("0000FF");
        // The derived rPr replaces the base's: its bold and size go.
        await Assert.That(firstRow.RunProperties!.Bold).IsNotEqualTo(true);
        await Assert.That(firstRow.RunProperties.FontSizePoints).IsNull();
        // So does its tcPr: the base's top rule goes with it.
        await Assert.That(firstRow.Borders!.Top.IsVisible).IsFalse();
        await Assert.That(firstRow.Borders.Bottom.IsVisible).IsFalse();

        // A region only the base declares is inherited whole.
        await Assert.That(conditionals[TableStyleOverrideValues.LastRow].BackgroundColorHex).IsEqualTo("EEEEEE");
    }

    [Test]
    public async Task BlockTheDerivedRegionLeavesOutIsInherited()
    {
        var basedOn = TableStyle("Base", null,
            """
            <w:tblStylePr w:type="firstRow">
              <w:rPr><w:sz w:val="40"/></w:rPr>
              <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="FF0000"/></w:tcPr>
            </w:tblStylePr>
            """);
        var derived = TableStyle("Derived", "Base",
            """<w:tblStylePr w:type="firstRow"><w:rPr><w:b/></w:rPr></w:tblStylePr>""");

        var firstRow = Parser().ResolveStyleConditionals(derived,
            new() {["Base"] = basedOn, ["Derived"] = derived})![TableStyleOverrideValues.FirstRow];

        await Assert.That(firstRow.BackgroundColorHex).IsEqualTo("FF0000");
        await Assert.That(firstRow.RunProperties!.Bold).IsTrue();
        await Assert.That(firstRow.RunProperties.FontSizePoints).IsNull();
    }
}
