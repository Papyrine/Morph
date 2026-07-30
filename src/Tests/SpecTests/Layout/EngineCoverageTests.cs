/// <summary>
/// The capability predicate that routes the cutover: block/table/column documents — plus non-wrapping
/// top-level floating shapes and images — are covered by the layout engine; the rest (WordArt, wrapping
/// floats, section breaks, non-paragraph cell content) fall back to the production renderers until a
/// later emission slice admits them.
/// </summary>
public class EngineCoverageTests
{
    [Test]
    [Arguments("multiple_pages", true)]
    [Arguments("table_default_style", true)]
    [Arguments("dot_points", true)]
    // Non-wrapping top-level floats are admitted: a behind/in-front floating shape and a wrap-none floating image.
    [Arguments("pct_pos_offset", true)]
    [Arguments("agendas-minutes/01", true)]
    // Section breaks are admitted: the Fragmenter paginates every kind and each page carries its own geometry.
    [Arguments("section_break_next_page", true)]
    [Arguments("section_break_continuous", true)]
    // An inline shape group (a grouped drawing in a run) is admitted — the painters draw it.
    [Arguments("inline_shape_arrows", true)]
    // A table with a nested (simple) table in a cell is admitted — the Fragmenter lays the nested table out inline.
    [Arguments("complex_tables", true)]
    // Block-level content controls are admitted — each renders as its synthetic paragraph (its resolved value).
    [Arguments("content_control_inline", true)]
    // A WordArt text-warp element is a separate block/floating element the engine does not emit yet.
    [Arguments("wordart", false)]
    // A floating image that wraps text (WrapType.Square) still needs flow exclusions the engine lacks.
    [Arguments("image_wrap_square", false)]
    public async Task Covers_admits_block_table_column_and_rejects_art(string relative, bool covered)
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", relative.Replace('/', Path.DirectorySeparatorChar), "input.docx");
        await using var stream = File.OpenRead(input);
        var document = new DocumentParser().Parse(stream);

        await Assert.That(EngineCoverage.Covers(document)).IsEqualTo(covered);
    }
}
