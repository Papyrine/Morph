/// <summary>
/// Step 6 of the layout-engine cutover (docs/layout-engine-proposal.md): the raster analogue of
/// <c>EnginePdfPathTests</c>. <c>SkiaDocumentConverter.RenderViaEngine</c> paginates with the
/// backend-independent <c>Fragmenter</c> and draws with <c>SkiaPainter</c> — the seam that replaces
/// <c>SkiaPageRenderer</c> + <c>PageRendererBase</c> + <c>TextRenderer</c>. This guards that the seam
/// produces a valid PNG per page at the expected page count for documents the engine covers. Fidelity
/// (SSIM vs Word) is measured separately; during bring-up the engine path matched production Skia to within
/// 0.002 on these docs.
/// </summary>
public class EngineSkiaPathTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    [Test]
    [Arguments("multiple_pages", 5)]
    [Arguments("even_odd_headers/01", 2)]
    [Arguments("table_default_style", 1)]
    [Arguments("dot_points", 1)]
    // A label sheet: behind-text floating shapes anchored across a table grid, so this drives SkiaPainter.PaintShape.
    [Arguments("labels/14", 1)]
    // A menu card stacked from outline-only rectangles over a filled one — drives PaintShape's outline STROKE
    // path (the rule paint must stroke, not fill, or the green border floods the card; menus/09 green-bg fix).
    [Arguments("menus/09", 1)]
    // Inline arrow glyphs: grouped drawings embedded in runs, so this drives SkiaPainter.PaintInlineGroup.
    [Arguments("inline_shape_arrows", 1)]
    public async Task Engine_path_renders_a_valid_png_per_page_at_the_expected_count(string relative, int pages)
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", relative.Replace('/', Path.DirectorySeparatorChar), "input.docx");
        await using var stream = File.OpenRead(input);
        var document = new DocumentParser().Parse(stream);

        var rendered = new List<byte[]>();
        var count = SkiaDocumentConverter.RenderViaEngine(
            document,
            new ImageExportOptions { Dpi = 150, FontDirectory = fontsDirectory },
            write =>
            {
                using var memory = new MemoryStream();
                write(memory);
                rendered.Add(memory.ToArray());
            });

        await Assert.That(count).IsEqualTo(pages);
        await Assert.That(rendered.Count).IsEqualTo(pages);
        // PNG signature: 0x89 'P' 'N' 'G'.
        await Assert.That(rendered[0].Length > 100 && rendered[0][0] == 0x89 && rendered[0][1] == (byte) 'P').IsTrue();
    }
}
