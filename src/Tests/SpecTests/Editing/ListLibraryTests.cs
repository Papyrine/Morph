using DocumentFormat.OpenXml.Wordprocessing;
using static EditDocuments;
using static ReviewDocuments;

// Home > Bullets' and Numbering's libraries, and numbering restarted or continued, against what Word
// wrote for the same (Word-probed 2026-10-23, see DocumentEditor.Lists.cs).
public class ListLibraryTests
{
    static byte[] Document(int paragraphs) =>
        Build(string.Concat(Enumerable.Range(1, paragraphs).Select(_ => P(R($"Item {_}")))));

    static string Numbering(byte[] docx) =>
        Read(docx, _ => _.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!.OuterXml);

    // Each paragraph's list and level, as numId.ilvl.
    static string Lists(byte[] docx) =>
        Read(docx, _ => string.Join(' ', _.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(paragraph =>
        {
            if (paragraph.ParagraphProperties?.NumberingProperties is {NumberingId.Val.Value: var id} numbering)
            {
                return $"{id}.{numbering.NumberingLevelReference?.Val?.Value ?? 0}";
            }

            return "-";
        })));

    static string FirstLevel(byte[] docx, int numberingId) =>
        Read(docx, _ =>
        {
            var numbering = _.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;
            var abstractId = numbering.Elements<NumberingInstance>().First(instance => instance.NumberID!.Value == numberingId).AbstractNumId!.Val!.Value;
            var definition = numbering.Elements<AbstractNum>().First(each => each.AbstractNumberId!.Value == abstractId);
            return Bare(definition.Elements<Level>().First(level => level.LevelIndex!.Value == 0).OuterXml);
        });

    // Word-probed: the fifth bullet, Wingdings' arrow.
    [Test]
    public async Task ABulletFromTheLibrary_IsItsFirstLevel()
    {
        var arrow = ListTemplates.Bullets.First(_ => _.Name == "Arrow");

        var edited = DocumentEditor.SetListStyle(Document(2), [0, 1], arrow, Plain);

        await Assert.That(Lists(edited)).IsEqualTo("1.0 1.0");
        var level = FirstLevel(edited, 1);
        await Assert.That(level).StartsWith(
            "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\" /><w:numFmt w:val=\"bullet\" /><w:lvlText w:val=\"\" /><w:lvlJc w:val=\"left\" />" +
            "<w:pPr><w:ind w:left=\"720\" w:hanging=\"360\" /></w:pPr><w:rPr><w:rFonts ");
        foreach (var attribute in new[] {"w:ascii=\"Wingdings\"", "w:hAnsi=\"Wingdings\"", "w:hint=\"default\""})
        {
            await Assert.That(level).Contains(attribute);
        }
        await Assert.That(DocumentEditor.ListStyleOf(edited, 0)).IsEqualTo(arrow);
    }

    // Word-probed: upper-case roman stands right of its place and hangs 180 twips.
    [Test]
    public async Task ANumberFromTheLibrary_IsItsFirstLevel_AndItsDeeperLevelsAreTheDefaults()
    {
        var roman = ListTemplates.Numbers.First(_ => _.Name == "Upper-case roman");

        var edited = DocumentEditor.SetListStyle(Document(1), [0], roman, Plain);

        await Assert.That(FirstLevel(edited, 1)).IsEqualTo(
            "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\" /><w:numFmt w:val=\"upperRoman\" /><w:lvlText w:val=\"%1.\" /><w:lvlJc w:val=\"right\" /><w:pPr><w:ind w:left=\"720\" w:hanging=\"180\" /></w:pPr></w:lvl>");
        var plain = DocumentEditor.SetList(Document(1), [0], ListKind.Number, Plain);
        await Assert.That(Numbering(edited).Split("<w:lvl w:ilvl=\"1\"")[1]).IsEqualTo(Numbering(plain).Split("<w:lvl w:ilvl=\"1\"")[1]);
    }

    // On a list's paragraph, the item changes the list: every paragraph of it, and no new list.
    [Test]
    public async Task ALibraryItem_OnAList_ChangesTheList()
    {
        var listed = DocumentEditor.SetList(Document(3), [0, 1, 2], ListKind.Number, Plain);
        var letters = ListTemplates.Numbers.First(_ => _.Name == "Letters with parentheses");

        var edited = DocumentEditor.SetListStyle(listed, [1], letters, Plain);

        await Assert.That(Lists(edited)).IsEqualTo("1.0 1.0 1.0");
        await Assert.That(FirstLevel(edited, 1)).Contains("<w:numFmt w:val=\"lowerLetter\" /><w:lvlText w:val=\"%1)\" />");
        await Assert.That(DocumentEditor.ListStyleOf(edited, 2)).IsEqualTo(letters);
    }

    // A paragraph beside a list of the same look joins it; beside one of another look, it starts its own.
    [Test]
    public async Task ALibraryItem_JoinsAListOfItsLookBesideIt()
    {
        var check = ListTemplates.Bullets.First(_ => _.Name == "Check mark");
        var first = DocumentEditor.SetListStyle(Document(3), [0], check, Plain);

        var joined = DocumentEditor.SetListStyle(first, [1], check, Plain);
        var other = DocumentEditor.SetListStyle(first, [1], ListTemplates.Bullets[0], Plain);

        await Assert.That(Lists(joined)).IsEqualTo("1.0 1.0 -");
        await Assert.That(Lists(other)).IsEqualTo("1.0 2.0 -");
    }

    // Word-probed: Restart at 1 is a new w:num over a copy of the definition, from the paragraph on.
    [Test]
    public async Task RestartNumbering_IsANewListFromTheParagraphOn()
    {
        var listed = DocumentEditor.SetList(Document(4), [0, 1, 2, 3], ListKind.Number, Plain);

        var restarted = DocumentEditor.RestartNumbering(listed, 2, Plain);

        await Assert.That(Lists(restarted)).IsEqualTo("1.0 1.0 2.0 2.0");
        await Assert.That(FirstLevel(restarted, 2)).IsEqualTo(FirstLevel(restarted, 1));
        var numbering = Read(restarted, _ => _.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!);
        await Assert.That(numbering.Elements<AbstractNum>().Select(_ => _.Nsid!.Val!.Value).Distinct().Count()).IsEqualTo(2);
        await Assert.That(numbering.OuterXml).DoesNotContain("lvlOverride");
    }

    // Continue Numbering puts the restarted part back in the list before it.
    [Test]
    public async Task ContinueNumbering_JoinsTheListBefore()
    {
        var listed = DocumentEditor.SetList(Document(4), [0, 1, 2, 3], ListKind.Number, Plain);
        var restarted = DocumentEditor.RestartNumbering(listed, 2, Plain);

        var continued = DocumentEditor.ContinueNumbering(restarted, 2, Plain);

        await Assert.That(Lists(continued)).IsEqualTo("1.0 1.0 1.0 1.0");
        await Assert.That(Lists(DocumentEditor.ContinueNumbering(listed, 0, Plain))).IsEqualTo(Lists(listed));
    }

    // Restart and continue keep each paragraph's level.
    [Test]
    public async Task RestartNumbering_KeepsTheLevels()
    {
        var listed = DocumentEditor.SetList(Document(3), [0, 1, 2], ListKind.Number, Plain);
        var deeper = DocumentEditor.Indent(listed, [2], 1, Plain);

        var restarted = DocumentEditor.RestartNumbering(deeper, 1, Plain);

        await Assert.That(Lists(restarted)).IsEqualTo("1.0 2.0 2.1");
    }
}
