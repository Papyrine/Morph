using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Where Word's built-in paragraph defaults (line 278/240, 8pt after) are in force, and which styles
/// they stand under. Word-probed 2026-10-11 (<c>_probe_styleline_a</c> to <c>_e</c>): the five
/// packages below are the probe's own and differ only in their styles part's defaults; each
/// paragraph's pitch and after-spacing was read off Word's XPS baselines (12pt Calibri: 17.1pt a line
/// under the built-in multiple against 14.7pt single).
///
/// <list type="bullet">
/// <item>With no docDefaults, or docDefaults that carry no <c>w:pPrDefault</c>, the built-in defaults
/// stand under EVERY style: one based on an undefined Normal, one with no <c>w:basedOn</c>, one that
/// declares only <c>w:spacing/@w:after</c> and one that declares nothing. A style's own
/// <c>w:after</c> replaces the 8pt and leaves the multiple.</item>
/// <item>Defining the default paragraph style changes none of it.</item>
/// <item>An EMPTY <c>w:pPrDefault</c> is single-spaced with nothing after.</item>
/// </list>
/// </summary>
public class BuiltInParagraphDefaultsTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
    const double builtInMultiple = 278 / 240.0;

    const string styles =
        """<w:style w:type="paragraph" w:styleId="S1"><w:name w:val="S One"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="240"/></w:pPr></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="S2"><w:name w:val="S Two"/><w:pPr><w:spacing w:after="240"/></w:pPr></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="S3"><w:name w:val="S Three"/><w:basedOn w:val="S2"/></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="S4"><w:name w:val="S Four"/></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="S5"><w:name w:val="S Five"/><w:rPr><w:b/></w:rPr></w:style>""" +
        """<w:style w:type="paragraph" w:styleId="S6"><w:name w:val="S Six"/><w:basedOn w:val="Normal"/></w:style>""";

    const string normal = """<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>""";
    const string runDefault = """<w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/><w:sz w:val="24"/></w:rPr></w:rPrDefault>""";

    // The probe's five packages, by what stands ahead of the six styles.
    const string noDocDefaults = "";
    const string noDocDefaultsNormalDefined = normal;
    const string runDefaultsOnly = $"<w:docDefaults>{runDefault}</w:docDefaults>";
    const string emptyParagraphDefaults = $"<w:docDefaults>{runDefault}<w:pPrDefault/></w:docDefaults>";
    const string runDefaultsOnlyNormalDefined = $"<w:docDefaults>{runDefault}</w:docDefaults>{normal}";

    static ParagraphProperties Parse(string defaults, string? style)
    {
        var paragraphStyle = style == null ? "" : $"""<w:pPr><w:pStyle w:val="{style}"/></w:pPr>""";
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = [with($"<w:document {wNs}><w:body><w:p>{paragraphStyle}<w:r><w:t>text</w:t></w:r></w:p></w:body></w:document>")];
            main.AddNewPart<StyleDefinitionsPart>().Styles = [with($"<w:styles {wNs}>{defaults}{styles}</w:styles>")];
        }

        stream.Position = 0;
        return ((ParagraphElement) new DocumentParser().Parse(stream).Elements[0]).Properties;
    }

    // What a paragraph of each style leaves after it: S1 and S2 declare 12pt and S3 inherits S2's;
    // the rest take whatever the document default is.
    static double After(string? style, double documentDefault) =>
        style is "S1" or "S2" or "S3" ? 12 : documentDefault;

    [Test]
    [Arguments(noDocDefaults)]
    [Arguments(noDocDefaultsNormalDefined)]
    [Arguments(runDefaultsOnly)]
    [Arguments(runDefaultsOnlyNormalDefined)]
    public async Task The_built_in_defaults_stand_under_every_style(string defaults)
    {
        foreach (var style in new[] {null, "S1", "S2", "S3", "S4", "S5", "S6"})
        {
            var properties = Parse(defaults, style);
            await Assert.That(properties.LineSpacingRule).IsEqualTo(LineSpacingRule.Auto);
            await Assert.That(properties.LineSpacingMultiplier).IsEqualTo(builtInMultiple).Within(0.0001);
            await Assert.That(properties.SpacingAfterPoints).IsEqualTo(After(style, documentDefault: 8));
        }
    }

    // An unstyled paragraph is IN the default paragraph style: Word treats it and one that names
    // that style as the same style for contextual spacing (_probe_ctx, pair k8).
    [Test]
    public async Task An_unstyled_paragraph_resolves_to_the_default_paragraph_style()
    {
        await Assert.That(Parse(noDocDefaultsNormalDefined, null).ResolvedStyleId).IsEqualTo("Normal");
        await Assert.That(Parse(noDocDefaultsNormalDefined, "Normal").ResolvedStyleId).IsEqualTo("Normal");
        await Assert.That(Parse(noDocDefaultsNormalDefined, "S4").ResolvedStyleId).IsEqualTo("S4");
        await Assert.That(Parse(noDocDefaultsNormalDefined, null).StyleId).IsNull();
    }

    [Test]
    public async Task An_empty_pPrDefault_is_single_spaced_with_nothing_after()
    {
        foreach (var style in new[] {null, "S1", "S2", "S3", "S4", "S5", "S6"})
        {
            var properties = Parse(emptyParagraphDefaults, style);
            await Assert.That(properties.LineSpacingMultiplier).IsEqualTo(1.0).Within(0.0001);
            await Assert.That(properties.SpacingAfterPoints).IsEqualTo(After(style, documentDefault: 0));
        }
    }
}
