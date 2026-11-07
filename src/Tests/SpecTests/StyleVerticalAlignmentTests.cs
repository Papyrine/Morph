using static ReviewDocuments;

/// <summary>
/// Superscript and subscript set by a style (w:vertAlign in its w:rPr) raise or lower the runs in it, as
/// Word draws them: a note's own number in Footnote Reference, a character style of a document's own. The
/// style's run properties once left it out, and every such run stood on the baseline — the body's note
/// citations alone were raised, by a rule of their own.
/// </summary>
public class StyleVerticalAlignmentTests
{
    [Test]
    public async Task ACharacterStyle_RaisesAndLowersItsRuns()
    {
        const string styles =
            "<w:style w:type=\"character\" w:styleId=\"Raised\"><w:name w:val=\"Raised\"/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"RaisedToo\"><w:name w:val=\"Raised too\"/><w:basedOn w:val=\"Raised\"/></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"Lowered\"><w:name w:val=\"Lowered\"/><w:rPr><w:vertAlign w:val=\"subscript\"/></w:rPr></w:style>";
        var docx = Build(
            P(R("plain"), "<w:r><w:rPr><w:rStyle w:val=\"Raised\"/></w:rPr><w:t>up</w:t></w:r>",
                "<w:r><w:rPr><w:rStyle w:val=\"RaisedToo\"/></w:rPr><w:t>also</w:t></w:r>",
                "<w:r><w:rPr><w:rStyle w:val=\"Lowered\"/></w:rPr><w:t>down</w:t></w:r>",
                "<w:r><w:rPr><w:rStyle w:val=\"Raised\"/><w:vertAlign w:val=\"baseline\"/></w:rPr><w:t>level</w:t></w:r>"),
            styles: styles);

        await Assert.That(Placements(docx)).IsEqualTo("plain:Baseline up:Superscript also:Superscript down:Subscript level:Baseline");
    }

    // The number a footnote's own text starts with, in Footnote Reference, as Word's Insert Footnote writes it.
    [Test]
    public async Task ANotesOwnNumber_IsRaised()
    {
        var blank = File.ReadAllBytes(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Morph.Blazor", "wwwroot", "blank.docx"));
        var typed = DocumentEditor.Rewrite(blank, 0, [new([new(-1, "Alpha one.")])], EditDocuments.Plain).Document;
        var noted = DocumentEditor.InsertFootnote(typed, 0, 10, EditDocuments.Plain).Document;

        using var stream = new MemoryStream(noted);
        var note = (ParagraphElement) DocumentConverter.Parse(stream, null).Footnotes[0].Elements[0];

        await Assert.That(note.Runs[0].Text).IsEqualTo("1");
        await Assert.That(note.Runs[0].Properties.VerticalAlignment).IsEqualTo(VerticalRunAlignment.Superscript);
    }

    static string Placements(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        var paragraph = (ParagraphElement) DocumentConverter.Parse(stream, null).Elements[0];
        return string.Join(" ", paragraph.Runs.Select(_ => $"{_.Text}:{_.Properties.VerticalAlignment}"));
    }
}
