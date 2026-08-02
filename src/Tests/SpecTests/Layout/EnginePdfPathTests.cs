using Morph.PDFium;

/// <summary>
/// Phase A of the layout-engine PDF cutover (docs/layout-engine-proposal.md, "The PDF cutover (step 5)").
/// <c>PdfRenderer.RenderViaEngine</c> paginates with the backend-independent <c>Fragmenter</c> and draws
/// with <c>PdfPainter</c>, wired to production font settings through <c>LayoutFonts</c> — the seam that
/// replaces <c>PdfTextEngine</c>. This guards that the seam produces a valid PDF at Word's page count for
/// documents the engine covers. Render fidelity is measured separately by <c>PdfPainterFidelityTests</c>;
/// the SSIM there was reproduced verbatim through this production wiring during Phase A bring-up, so
/// <c>LayoutFonts</c> resolves the same faces as the tests' <c>LayoutTestFonts</c>.
/// </summary>
public class EnginePdfPathTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    [Test]
    [Arguments("multiple_pages", 5)]
    [Arguments("even_odd_headers/01", 2)]
    [Arguments("table_default_style", 1)]
    [Arguments("dot_points", 1)]
    public async Task Engine_path_renders_a_valid_pdf_at_the_expected_page_count(string relative, int pages)
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", relative.Replace('/', Path.DirectorySeparatorChar), "input.docx");
        await using var stream = File.OpenRead(input);
        var document = new DocumentParser().Parse(stream);

        var pdf = PdfRenderer.RenderViaEngine(document, new PdfExportOptions { FontDirectory = fontsDirectory });

        await Assert.That(System.Text.Encoding.ASCII.GetString(pdf, 0, 5)).IsEqualTo("%PDF-");
        using var rendered = PdfiumDocument.Load(pdf);
        await Assert.That(rendered.PageCount).IsEqualTo(pages);
    }

    /// <summary>
    /// The engine path forwards the conversion's own options to the painter, not just to the measurer.
    /// RasterizeWordArt is the observable one: with it off the warp is dropped, so the page carries less
    /// ink. Before the wiring landed, the painter hardcoded its settings and honoured neither this nor the
    /// font width scale / fallback / compatibility the measurer was given — a painter resolving a different
    /// face than the measurer draws text off its measured line.
    /// </summary>
    [Test]
    public async Task The_engine_path_honours_RasterizeWordArt()
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "wordart-envelope", "input.docx");
        await using var stream = File.OpenRead(input);
        var document = new DocumentParser().Parse(stream);

        var withArt = PdfRenderer.RenderViaEngine(document, new() { FontDirectory = fontsDirectory, RasterizeWordArt = true });
        var withoutArt = PdfRenderer.RenderViaEngine(document, new() { FontDirectory = fontsDirectory, RasterizeWordArt = false });

        // Both stay valid single-page PDFs — the warps reserve their height either way, so only the drawing
        // differs — and dropping four rasterized warps makes for a markedly smaller file.
        using var withRendered = PdfiumDocument.Load(withArt);
        using var withoutRendered = PdfiumDocument.Load(withoutArt);
        await Assert.That(withRendered.PageCount).IsEqualTo(withoutRendered.PageCount);
        await Assert.That(withoutArt.Length).IsLessThan(withArt.Length);
    }
}
