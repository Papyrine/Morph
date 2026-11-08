using static EditFixtures;

// One paragraph open for editing: what the script is given to show, and the reading of what it
// hands back.
public class EditSessionTests
{
    static EditSession Open(byte[] docx, int paragraph)
    {
        using var document = PagedDocument.Open(docx, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var outline = DocumentOutline.Read(docx);
        var map = document.Edits(outline);
        return new(7, outline.Paragraphs[paragraph], map.Blocks(paragraph)[0], map);
    }

    [Test]
    public async Task TheScript_IsGivenTheParagraphsText_InItsRuns_AsThePageDrewThem()
    {
        var session = Open(Paragraphs, 1);

        using var json = JsonDocument.Parse(session.Json(3, 9));
        var root = json.RootElement;

        await Assert.That(root.GetProperty("id").GetInt32()).IsEqualTo(7);
        await Assert.That(root.GetProperty("page").GetInt32()).IsEqualTo(0);
        await Assert.That(root.GetProperty("align").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("from").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("to").GetInt32()).IsEqualTo(9);
        await Assert.That(root.GetProperty("previous").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("next").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("x").GetDouble()).IsEqualTo(72).Within(0.01);
        await Assert.That(root.GetProperty("line").GetDouble()).IsGreaterThan(0);

        var units = root.GetProperty("u").EnumerateArray().Select(_ => $"{_.GetProperty("k").GetInt32()} '{_.GetProperty("t").GetString()}' {_.GetProperty("f").GetInt32()}").ToList();
        await Assert.That(string.Join(" | ", units)).IsEqualTo("0 'Second ' 0 | 0 'bold' 1 | 0 ' one.' 0");
    }

    // Under tracking the script is told which text the tracking author inserted, which deleting takes
    // out rather than strikes (WordProbe R-R-1, R-R-6): not another author's, and nothing untracked.
    [Test]
    public async Task UnderTracking_TheAuthorsOwnInsertion_IsMarked()
    {
        var docx = Build(
            P(
                "",
                R("Kept "),
                "<w:ins w:id=\"1\" w:author=\"Ann Lee\" w:date=\"2026-01-01T00:00:00Z\">", R("mine"), "</w:ins>",
                R(" and "),
                "<w:ins w:id=\"2\" w:author=\"Bob\" w:date=\"2026-01-01T00:00:00Z\">", R("his"), "</w:ins>"));
        var session = Open(docx, 0);

        static string Owned(string json)
        {
            using var parsed = JsonDocument.Parse(json);
            return string.Join(
                " | ",
                parsed.RootElement.GetProperty("u").EnumerateArray()
                    .Select(_ => $"{_.GetProperty("t").GetString()}{(_.EnumerateObject().Any(property => property.Name == "o") ? " o" : "")}"));
        }

        await Assert.That(Owned(session.Json(0, 0, true, "Ann Lee"))).IsEqualTo("Kept  | mine o |  and  | his");
        await Assert.That(Owned(session.Json(0, 0, false, "Ann Lee"))).IsEqualTo("Kept  | mine |  and  | his");
    }

    // A paragraph that runs over a page break is a block on each page. Opened on the second, the
    // editor shows the lines that are there, the ones before them scrolled out of sight.
    [Test]
    public async Task TheRestOfAParagraph_IsOpenedWhereItIs_WithWhatCameBeforeSkipped()
    {
        var lengthy = string.Join(' ', Enumerable.Repeat("A sentence of a paragraph long enough to run from the foot of one page to the head of the next.", 12));

        // Enough lines above it to bring the paragraph to the foot of the page, however many that is.
        for (var above = 10; above < 40; above++)
        {
            var docx = Build(
                string.Concat(Enumerable.Range(0, above).Select(_ => P("", R($"Line {_}")))) +
                P("<w:widowControl w:val=\"0\"/><w:ind w:firstLine=\"360\"/>", R(lengthy)));
            using var document = PagedDocument.Open(docx, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
            var outline = DocumentOutline.Read(docx);
            var map = document.Edits(outline);
            var blocks = map.Blocks(above);
            if (blocks.Count != 2)
            {
                continue;
            }

            await Assert.That(blocks[0].Page).IsEqualTo(0);
            await Assert.That(blocks[1].Page).IsEqualTo(1);

            using var first = JsonDocument.Parse(new EditSession(1, outline.Paragraphs[above], blocks[0], map).Json(0, 0));
            using var second = JsonDocument.Parse(new EditSession(2, outline.Paragraphs[above], blocks[1], map).Json(0, 0));

            await Assert.That(first.RootElement.GetProperty("skip").GetDouble()).IsEqualTo(0);
            await Assert.That(first.RootElement.GetProperty("indent").GetDouble()).IsEqualTo(18).Within(0.01);
            await Assert.That(second.RootElement.GetProperty("skip").GetDouble()).IsEqualTo(blocks[0].Height).Within(0.01);
            await Assert.That(second.RootElement.GetProperty("indent").GetDouble()).IsEqualTo(0);
            await Assert.That(second.RootElement.GetProperty("page").GetInt32()).IsEqualTo(1);
            await Assert.That(second.RootElement.GetProperty("y").GetDouble()).IsEqualTo(72).Within(0.01);
            await Assert.That(second.RootElement.GetProperty("x").GetDouble()).IsEqualTo(72).Within(0.01);
            return;
        }

        Assert.Fail("No number of lines above it put the paragraph across a page break.");
    }

    [Test]
    public async Task WhatIsNotText_IsShown_AndSaidToBeWhatItIs()
    {
        var docx = Build(
            P(
                "",
                R("See"),
                "<w:r><w:footnoteReference w:id=\"2\"/></w:r>",
                "<w:del w:id=\"1\" w:author=\"Bob\" w:date=\"2025-04-25T10:00:00Z\"><w:r><w:delText xml:space=\"preserve\"> not</w:delText></w:r></w:del>",
                R(" this."),
                "<w:r><w:rPr><w:vanish/></w:rPr><w:t>unseen</w:t></w:r>"));
        var session = Open(docx, 0);

        using var json = JsonDocument.Parse(session.Json(0, 0));
        var units = json.RootElement.GetProperty("u").EnumerateArray()
            .Select(_ => $"{_.GetProperty("k").GetInt32()}{(_.TryGetProperty("s", out var kind) ? " " + kind.GetString() : "")}{(_.TryGetProperty("h", out _) ? " hidden" : "")}")
            .ToList();

        // Hidden text is text still: typed over and handed back as text, so that the script and the file
        // count the same places; it is only marked as text the page did not draw.
        await Assert.That(string.Join(" | ", units)).IsEqualTo("0 | 1 note | 1 deleted | 0 | 0 hidden");
    }

    // A footer's page number reads, in the open box, what the page drew: the file holds what the field
    // read when it was put in (1), and a footer opened on the second page once showed that.
    [Test]
    public async Task APageField_ShowsThePagesOwnNumber()
    {
        var plain = new EditOptions("", DateTimeOffset.MinValue, false);
        var docx = Build(P("", R("One")) + P("", "<w:r><w:br w:type=\"page\"/></w:r>", R("Two")));
        var footer = DocumentEditor.EditHeader(docx, 0, true, plain);
        var typed = DocumentEditor.Rewrite(footer.Document, footer.Paragraph, [new([new(-1, "Page  of ")])], plain).Document;
        var numbered = DocumentEditor.InsertPageNumber(typed, footer.Paragraph, 5, false, plain).Document;
        var counted = DocumentEditor.InsertPageNumber(numbered, footer.Paragraph, 10, true, plain).Document;

        using var document = PagedDocument.Open(counted, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var outline = DocumentOutline.Read(counted);
        var map = document.Edits(outline);
        var blocks = map.Blocks(footer.Paragraph);
        await Assert.That(blocks.Count).IsEqualTo(2);

        string Shown(EditBlock block)
        {
            using var json = JsonDocument.Parse(new EditSession(1, outline.Paragraphs[footer.Paragraph], block, map).Json(0, 0));
            return string.Concat(json.RootElement.GetProperty("u").EnumerateArray().Select(_ => _.GetProperty("t").GetString()));
        }

        await Assert.That(Shown(blocks[0])).IsEqualTo("Page 1 of 2");
        await Assert.That(Shown(blocks[1])).IsEqualTo("Page 2 of 2");
    }

    // Hidden text was once handed to the script as a thing that is not text, which the script handed
    // back as one place where the file counted six, and the rewrite refused: the typing was lost.
    [Test]
    public async Task AParagraphWithHiddenText_IsHandedBack_AndRewritten()
    {
        var docx = Build(
            P(
                "",
                R("See "),
                "<w:r><w:rPr><w:vanish/></w:rPr><w:t>unseen</w:t></w:r>",
                R(" this.")));
        var session = Open(docx, 0);

        var content = session.Read("""[{"a":null,"i":[[0,"See x",0],[1,"unseen",0],[2," this.",0]]}]""");
        await Assert.That(content).IsNotNull();

        var result = DocumentEditor.Rewrite(docx, 0, content!, new("", DateTimeOffset.MinValue, false));
        var text = string.Concat(DocumentOutline.Read(result.Document).Paragraphs[0].Units.Select(_ => _.Text));
        await Assert.That(text).IsEqualTo("See xunseen this.");
    }

    [Test]
    public async Task WhatTheScriptHandsBack_IsReadAsTheParagraphsItBecame()
    {
        var session = Open(Paragraphs, 1);

        // "Second " as it was, "bold" made italic as well, a new paragraph, and its end right-aligned.
        var content = session.Read("""[{"a":null,"i":[[0,"Second ",0],[1,"bold",3]]},{"a":2,"i":[[2," one, and more.",0],[1,"!",0]]}]""")!;

        await Assert.That(content.Count).IsEqualTo(2);
        await Assert.That(content[0].Alignment).IsNull();
        await Assert.That(content[1].Alignment).IsEqualTo(TextAlignment.Right);
        await Assert.That(content[0].Items[0]).IsEqualTo(new(0, "Second "));
        await Assert.That(content[0].Items[1]).IsEqualTo(new(1, "bold", new(Italic: true)));
        await Assert.That(content[1].Items[0]).IsEqualTo(new(2, " one, and more."));

        // Like the bold run, and no longer bold.
        await Assert.That(content[1].Items[1]).IsEqualTo(new(1, "!", new(Bold: false)));
    }

    [Test]
    [Arguments("")]
    [Arguments("{}")]
    [Arguments("[]")]
    [Arguments("[{\"i\":[[9,\"no such unit\",0]]}]")]
    [Arguments("[{\"i\":[[\"x\"]]}]")]
    [Arguments("[{\"a\":1}]")]
    public async Task WhatIsNotASessionsToHandBack_IsNotRead(string payload)
    {
        var session = Open(Paragraphs, 1);

        await Assert.That(session.Read(payload)).IsNull();
    }
}
