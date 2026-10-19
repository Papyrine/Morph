/// <summary>
/// The styles Word builds in, as Word writes them into a document the first time one is used: an edit
/// that applies Heading 1 to a document that has never had one adds this definition, as Word does,
/// rather than naming a style the document does not define.
///
/// Word-probed 2026-10-18 (Word 16 over COM, a blank document from the default Normal template, each
/// style applied and the package saved): every definition below is that styles.xml's, less the
/// <c>w:rsid</c> each carried. A style comes with its linked character style (<c>w:link</c>) and with
/// whatever it is based on, so a definition never names one the document lacks.
/// </summary>
static class BuiltInStyles
{
    const string w = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    const string majorFonts = "<w:rFonts w:asciiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:hAnsiTheme=\"majorHAnsi\" w:cstheme=\"majorBidi\"/>";
    const string majorEastAsia = "<w:rFonts w:eastAsiaTheme=\"majorEastAsia\" w:cstheme=\"majorBidi\"/>";
    const string accent = "<w:color w:val=\"0F4761\" w:themeColor=\"accent1\" w:themeShade=\"BF\"/>";
    const string grey = "<w:color w:val=\"595959\" w:themeColor=\"text1\" w:themeTint=\"A6\"/>";
    const string italic = "<w:i/><w:iCs/>";

    // A paragraph style Word links a character style to: its id, its name, where the gallery puts it,
    // what Word says about showing it, and its paragraph and run properties.
    readonly record struct Linked(string Id, string Name, int Priority, string Visibility, string Paragraph, string Run);

    static readonly Linked[] linked =
    [
        Heading(1, "", "<w:spacing w:before=\"360\" w:after=\"80\"/>", majorFonts + accent + "<w:sz w:val=\"40\"/><w:szCs w:val=\"40\"/>"),
        Heading(2, "<w:unhideWhenUsed/>", "<w:spacing w:before=\"160\" w:after=\"80\"/>", majorFonts + accent + "<w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/>"),
        Heading(3, "<w:unhideWhenUsed/>", "<w:spacing w:before=\"160\" w:after=\"80\"/>", majorEastAsia + accent + "<w:sz w:val=\"28\"/><w:szCs w:val=\"28\"/>"),
        Heading(4, "<w:unhideWhenUsed/>", "<w:spacing w:before=\"80\" w:after=\"40\"/>", majorEastAsia + italic + accent),
        Heading(5, "<w:semiHidden/><w:unhideWhenUsed/>", "<w:spacing w:before=\"80\" w:after=\"40\"/>", majorEastAsia + accent),
        Heading(6, "<w:semiHidden/><w:unhideWhenUsed/>", "<w:spacing w:before=\"40\" w:after=\"0\"/>", majorEastAsia + italic + grey),
        new("Title", "Title", 10, "", "<w:spacing w:after=\"80\" w:line=\"240\" w:lineRule=\"auto\"/><w:contextualSpacing/>", majorFonts + "<w:spacing w:val=\"-10\"/><w:kern w:val=\"28\"/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/>"),
        new("Subtitle", "Subtitle", 11, "", "<w:numPr><w:ilvl w:val=\"1\"/></w:numPr>", majorEastAsia + grey + "<w:spacing w:val=\"15\"/><w:sz w:val=\"28\"/><w:szCs w:val=\"28\"/>"),
        new("Quote", "Quote", 29, "", "<w:spacing w:before=\"160\"/><w:jc w:val=\"center\"/>", italic + "<w:color w:val=\"404040\" w:themeColor=\"text1\" w:themeTint=\"BF\"/>"),
        new(
            "IntenseQuote",
            "Intense Quote",
            30,
            "",
            "<w:pBdr><w:top w:val=\"single\" w:sz=\"4\" w:space=\"10\" w:color=\"0F4761\" w:themeColor=\"accent1\" w:themeShade=\"BF\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"10\" w:color=\"0F4761\" w:themeColor=\"accent1\" w:themeShade=\"BF\"/></w:pBdr><w:spacing w:before=\"360\" w:after=\"360\"/><w:ind w:left=\"864\" w:right=\"864\"/><w:jc w:val=\"center\"/>",
            italic + accent)
    ];

    static readonly Dictionary<string, string> definitions = Definitions();

    static Linked Heading(int level, string visibility, string spacing, string run) =>
        new($"Heading{level}", $"heading {level}", 9, visibility, $"<w:keepNext/><w:keepLines/>{spacing}<w:outlineLvl w:val=\"{level - 1}\"/>", run);

    static Dictionary<string, string> Definitions()
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Normal"] = $"<w:style {w} w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>",
            ["DefaultParagraphFont"] = $"<w:style {w} w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/><w:uiPriority w:val=\"1\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>",
            ["TableNormal"] = $"<w:style {w} w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>",
            ["NoSpacing"] = $"<w:style {w} w:type=\"paragraph\" w:styleId=\"NoSpacing\"><w:name w:val=\"No Spacing\"/><w:uiPriority w:val=\"1\"/><w:qFormat/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:style>",
            ["ListParagraph"] = $"<w:style {w} w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:uiPriority w:val=\"34\"/><w:qFormat/><w:pPr><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr></w:style>",
            ["Hyperlink"] = $"<w:style {w} w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:rPr><w:color w:val=\"467886\" w:themeColor=\"hyperlink\"/><w:u w:val=\"single\"/></w:rPr></w:style>",
            ["TableGrid"] = $"<w:style {w} w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:basedOn w:val=\"TableNormal\"/><w:uiPriority w:val=\"39\"/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/></w:tblBorders></w:tblPr></w:style>"
        };

        foreach (var style in linked)
        {
            definitions[style.Id] =
                $"<w:style {w} w:type=\"paragraph\" w:styleId=\"{style.Id}\"><w:name w:val=\"{style.Name}\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:link w:val=\"{style.Id}Char\"/><w:uiPriority w:val=\"{style.Priority}\"/>{style.Visibility}<w:qFormat/><w:pPr>{style.Paragraph}</w:pPr><w:rPr>{style.Run}</w:rPr></w:style>";

            // Word capitalises a heading's name here, though not the style's own.
            var name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(style.Name);
            definitions[$"{style.Id}Char"] =
                $"<w:style {w} w:type=\"character\" w:customStyle=\"1\" w:styleId=\"{style.Id}Char\"><w:name w:val=\"{name} Char\"/><w:basedOn w:val=\"DefaultParagraphFont\"/><w:link w:val=\"{style.Id}\"/><w:uiPriority w:val=\"{style.Priority}\"/><w:rPr>{style.Run}</w:rPr></w:style>";
        }

        return definitions;
    }

    /// <summary>
    /// The paragraph styles an editor offers whether or not the document defines them, in the order
    /// Word's style gallery shows them.
    /// </summary>
    public static IReadOnlyList<string> Gallery { get; } =
    [
        "Normal",
        "NoSpacing",
        "Heading1",
        "Heading2",
        "Heading3",
        "Title",
        "Subtitle",
        "Quote",
        "IntenseQuote",
        "ListParagraph"
    ];

    public static bool Has(string styleId) =>
        definitions.ContainsKey(styleId);

    /// <summary>A fresh copy of a built-in style's definition; null when Word builds in no such style.</summary>
    public static Style? Definition(string styleId)
    {
        if (definitions.TryGetValue(styleId, out var xml))
        {
            return new(xml);
        }

        return null;
    }
}
