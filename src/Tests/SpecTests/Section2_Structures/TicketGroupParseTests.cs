/// <summary>
/// cards/02's tickets are one behind-text group per ticket — a notched <c>plaque</c>, five-point stars on
/// a ribbon, and a fixed-size code text box — anchored in the body's first paragraph above the ticket
/// table. Three parse rules went wrong on it.
/// </summary>
public class TicketGroupParseTests
{
    static async Task<ParsedDocument> Parse()
    {
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "cards", "02", "input.docx"));
        return new DocumentParser().Parse(stream);
    }

    [Test]
    public async Task Solid_filled_presets_beyond_rect_and_ellipse_carry_their_contours()
    {
        var shapes = (await Parse()).Elements.OfType<FloatingShapeElement>().ToList();

        // Six orange stars (three per ticket) and the two plaque tickets drew as their bounding boxes.
        await Assert.That(shapes.Count(_ => _.FillColorHex == "ED7D31" && _.Subpaths is { Count: > 0 })).IsEqualTo(6);
        await Assert.That(shapes.Count(_ => _.FillColorHex == "EDEBE7" && _.WidthPoints > 270 && _.Subpaths is { Count: > 0 })).IsEqualTo(2);
    }

    [Test]
    public async Task A_paragraph_anchoring_only_floating_art_and_a_text_box_keeps_its_mark_line()
    {
        var elements = (await Parse()).Elements;
        var table = elements.Select((element, index) => (element, index)).First(_ => _.element is TableElement).index;

        // The anchor paragraph's mark (spacing after 0) takes a line before the first ticket table.
        var mark = elements.Take(table).OfType<ParagraphElement>().Single();
        await Assert.That(mark.Runs.Count).IsEqualTo(0);
        await Assert.That(mark.IsAnchorOnlyMark).IsFalse();
    }

    [Test]
    public async Task A_fixed_size_text_box_hides_its_overflow()
    {
        var codeBoxes = (await Parse()).Elements.OfType<FloatingTextBoxElement>().ToList();

        await Assert.That(codeBoxes.Count).IsEqualTo(2);
        await Assert.That(codeBoxes.All(_ => _.HidesOverflow)).IsTrue();
    }
}
