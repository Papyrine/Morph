/// <summary>
/// Step 6 of the layout-engine cutover (docs/layout-engine-proposal.md): the third-backend analogue of
/// <c>EngineSkiaPathTests</c>. <c>ImageSharpDocumentConverter.RenderViaEngine</c> paginates with the
/// backend-independent <c>Fragmenter</c> and draws with <c>ImageSharpPainter</c> — the seam that replaces
/// <c>ImageSharpPageRenderer</c> + <c>PageRendererBase</c> + <c>TextRenderer</c>. Guards that the seam
/// produces a valid PNG per page at the expected count for documents the engine covers. During bring-up the
/// engine path matched production ImageSharp exactly (SSIM-identical) on these docs.
/// </summary>
public class EngineImageSharpPathTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    [Test]
    [Arguments("multiple_pages", 5)]
    [Arguments("even_odd_headers/01", 2)]
    [Arguments("table_default_style", 1)]
    [Arguments("dot_points", 1)]
    // A label sheet: behind-text floating shapes anchored across a table grid, so this drives ImageSharpPainter.PaintShape.
    [Arguments("labels/14", 1)]
    // Inline arrow glyphs: grouped drawings embedded in runs, so this drives ImageSharpPainter.PaintInlineGroup.
    [Arguments("inline_shape_arrows", 1)]
    public async Task Engine_path_renders_a_valid_png_per_page_at_the_expected_count(string relative, int pages)
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", relative.Replace('/', Path.DirectorySeparatorChar), "input.docx");
        await using var stream = File.OpenRead(input);
        var document = new DocumentParser().Parse(stream);

        var rendered = new List<byte[]>();
        var count = ImageSharpDocumentConverter.RenderViaEngine(
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
