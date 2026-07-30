/// <summary>
/// The capability predicate that routes the PDF cutover (Phase B): block/table/column documents are
/// covered by the layout engine, art-bearing ones fall back to PdfTextEngine until a Phase C emission
/// slice admits them.
/// </summary>
public class EngineCoverageTests
{
    [Test]
    [Arguments("multiple_pages", true)]
    [Arguments("table_default_style", true)]
    [Arguments("dot_points", true)]
    [Arguments("wordart", false)]
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
