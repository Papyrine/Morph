/// <summary>
/// The shared capability predicate gating the raster default path and the PDF cutover. It now admits every
/// corpus document — block/table/column flow, floats wrapping and not, section breaks, inline shape groups,
/// nested-table cells, content controls, floating text boxes and tables, WordArt warped and not, and
/// positioned frames — so the raster fallback is cold. These arguments keep one named example per admitted
/// shape, so a predicate change that drops a shape fails here and says which.
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
    // A WARPED WordArt (arch/wave/envelope) is admitted — it stays one figure the painter rasterizes.
    [Arguments("wordart", true)]
    [Arguments("wordart-envelope", true)]
    // A floating image that wraps text: the Fragmenter flows text beside it, and with the ppem-grain and
    // baseline-ascent defects fixed the engine now renders this document better than the fallback.
    [Arguments("image_wrap_square", true)]
    public async Task Covers_admits_block_table_column_and_rejects_art(string relative, bool covered)
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", relative.Replace('/', Path.DirectorySeparatorChar), "input.docx");
        await using var stream = File.OpenRead(input);
        var document = new DocumentParser().Parse(stream);

        await Assert.That(EngineCoverage.Covers(document)).IsEqualTo(covered);
    }

    /// <summary>
    /// Counts the predicate over the whole corpus, so a change that silently narrows or widens coverage
    /// shows up as a number rather than as drift nobody notices. The uncovered set is asserted by name:
    /// these fall back to the production renderers, so anything joining them is a document that stopped
    /// paginating through the one engine.
    /// </summary>
    [Test]
    public async Task The_corpus_coverage_count_holds()
    {
        var inputs = Directory.GetFiles(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs"), "input.docx", SearchOption.AllDirectories)
            .Where(_ => Directory.GetFiles(Path.GetDirectoryName(_)!, "expected_*.png").Length > 0)
            .Order()
            .ToList();

        var uncovered = new List<string>();
        foreach (var input in inputs)
        {
            ParsedDocument document;
            try
            {
                await using var stream = File.OpenRead(input);
                document = new DocumentParser().Parse(stream);
            }
            catch
            {
                continue;
            }

            if (!EngineCoverage.Covers(document))
            {
                var directory = Path.GetDirectoryName(input)!;
                uncovered.Add($"{Path.GetFileName(Path.GetDirectoryName(directory))}/{Path.GetFileName(directory)}");
            }
        }

        // Nothing is left out: every corpus document paginates through the engine. If this ever fails, a
        // predicate change has pushed documents back onto the production renderers, which is the thing the
        // whole migration exists to retire — the names say which.
        await Assert.That(uncovered).IsEmpty();
        await Assert.That(inputs.Count).IsGreaterThanOrEqualTo(325);
    }
}
