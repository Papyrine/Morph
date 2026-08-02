/// <summary>
/// The shared capability predicate gating the raster default path and the PDF cutover: block/table/column
/// documents — plus non-wrapping floats, section breaks, inline shape groups, nested-table cells, content
/// controls, floating text boxes and tables, and unwarped WordArt — are covered by the layout engine; what
/// is left (warped WordArt, wrapping floats, a positioned frame) falls back to the production
/// renderers until a later emission slice admits it.
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
    // A non-wrapping floating text box is admitted — its box chrome + content lay out inside the box.
    [Arguments("cards/13", true)]
    // A floating table (w:tblpPr) is admitted — the Fragmenter positions it and reuses the nested-table layout.
    [Arguments("letters/01", true)]
    // Unwarped WordArt is admitted — a block one (business/06's LOGO box) and one in a cell (menus/03's labels).
    [Arguments("business/06", true)]
    [Arguments("menus/03", true)]
    // A positioned text frame (w:framePr) is admitted — it auto-sizes and paints at its anchored position.
    [Arguments("agendas-minutes/14", true)]
    // A WARPED WordArt (arch/wave/envelope) is still excluded — the warp geometry is not emitted yet.
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
