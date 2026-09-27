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

        using var json = System.Text.Json.JsonDocument.Parse(session.Json(3, 9));
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

            using var first = System.Text.Json.JsonDocument.Parse(new EditSession(1, outline.Paragraphs[above], blocks[0], map).Json(0, 0));
            using var second = System.Text.Json.JsonDocument.Parse(new EditSession(2, outline.Paragraphs[above], blocks[1], map).Json(0, 0));

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

        using var json = System.Text.Json.JsonDocument.Parse(session.Json(0, 0));
        var units = json.RootElement.GetProperty("u").EnumerateArray()
            .Select(_ => $"{_.GetProperty("k").GetInt32()}{(_.TryGetProperty("s", out var kind) ? " " + kind.GetString() : "")}")
            .ToList();

        await Assert.That(string.Join(" | ", units)).IsEqualTo("0 | 1 note | 1 deleted | 0 | 2");
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
        await Assert.That(content[0].Items[0]).IsEqualTo(new NewItem(0, "Second "));
        await Assert.That(content[0].Items[1]).IsEqualTo(new NewItem(1, "bold", new(Italic: true)));
        await Assert.That(content[1].Items[0]).IsEqualTo(new NewItem(2, " one, and more."));

        // Like the bold run, and no longer bold.
        await Assert.That(content[1].Items[1]).IsEqualTo(new NewItem(1, "!", new(Bold: false)));
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
