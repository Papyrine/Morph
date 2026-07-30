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
    [Arguments("wordart", false)]
    // A floating image that wraps text (WrapType.Square) still needs flow exclusions the engine lacks.
    [Arguments("image_wrap_square", false)]
    [Arguments("inline_shape_arrows", false)]
    public async Task Covers_admits_block_table_column_and_rejects_art(string relative, bool covered)
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", relative.Replace('/', Path.DirectorySeparatorChar), "input.docx");
        await using var stream = File.OpenRead(input);
        var document = new DocumentParser().Parse(stream);

        await Assert.That(EngineCoverage.Covers(document)).IsEqualTo(covered);
    }
}
