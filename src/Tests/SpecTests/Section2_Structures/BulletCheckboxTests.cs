/// <summary>
/// Covers the Wingdings checkbox pair in <c>DocumentParser.MapBulletPuaToUnicode</c>. A Word
/// template that renders a questionnaire draws its boxes as list markers, so the pair has to survive
/// the hop from Wingdings' private-use codepoints onto the Unicode ones <c>Bullets.ttf</c> carries —
/// and <c>Bullets.ttf</c> is what actually draws the marker, whatever font the numbering names.
/// </summary>
public class BulletCheckboxTests
{
    // Wingdings 0xA8 and 0xFE, shifted into the F000 private-use block the way Word stores them.
    const string emptyBox = "\uF0A8";
    const string checkedBox = "\uF0FE";

    [Test]
    public async Task WingdingsCheckboxesBecomeBallotBoxes()
    {
        var markers = Markers(("Wingdings", emptyBox), ("Wingdings", checkedBox));

        await Assert.That(markers[0]).IsEqualTo("☐");
        await Assert.That(markers[1]).IsEqualTo("☒");
    }

    // The same codepoint means something else in Symbol, which is why the lookup branches on the
    // font before the character. 0xA8 is a hollow circle there, not a box.
    [Test]
    public async Task TheSameCodepointInSymbolStaysACircle()
    {
        var markers = Markers(("Symbol", emptyBox));

        await Assert.That(markers[0]).IsEqualTo("○");
    }

    static List<string> Markers(params (string Font, string LevelText)[] levels)
    {
        var parser = new DocumentParser();
        using var stream = new MemoryStream(BuildDocx(levels));
        return parser.Parse(stream)
            .Elements
            .OfType<ParagraphElement>()
            .Where(_ => _.Properties.Numbering != null)
            .Select(_ => _.Properties.Numbering!.Text)
            .ToList();
    }

    // One bullet list per level, each with its own numbering definition, and one paragraph in each.
    static byte[] BuildDocx((string Font, string LevelText)[] levels)
    {
        var abstracts = new StringBuilder();
        var nums = new StringBuilder();
        var paragraphs = new StringBuilder();
        for (var index = 0; index < levels.Length; index++)
        {
            var (font, levelText) = levels[index];
            var id = index + 1;
            abstracts.Append(
                $"""
                 <w:abstractNum w:abstractNumId="{id}">
                   <w:lvl w:ilvl="0">
                     <w:numFmt w:val="bullet"/>
                     <w:lvlText w:val="{levelText}"/>
                     <w:rPr><w:rFonts w:ascii="{font}" w:hAnsi="{font}"/></w:rPr>
                   </w:lvl>
                 </w:abstractNum>
                 """);
            nums.Append($"""<w:num w:numId="{id}"><w:abstractNumId w:val="{id}"/></w:num>""");
            paragraphs.Append(
                $"""
                 <w:p>
                   <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="{id}"/></w:numPr></w:pPr>
                   <w:r><w:t>Option {id}</w:t></w:r>
                 </w:p>
                 """);
        }

        return BuildZip(
            ("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
                </Types>
                """),
            ("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """),
            ("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
                </Relationships>
                """),
            ("word/numbering.xml",
                $"""
                 <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                 <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                   {abstracts}{nums}
                 </w:numbering>
                 """),
            ("word/document.xml",
                $"""
                 <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                 <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                   <w:body>{paragraphs}</w:body>
                 </w:document>
                 """));
    }

    static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
