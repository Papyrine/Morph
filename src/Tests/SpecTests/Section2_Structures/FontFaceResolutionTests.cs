extern alias ImageSharp;

/// <summary>
/// The three painters draw a run in the same face. Each rule here was a place where one of them picked a
/// different one: a bold request diverted from the family it named, a face that ships only inside a
/// collection, and a style re-derived from the words in a family's name.
/// </summary>
public class FontFaceResolutionTests
{
    static PdfRenderContext PdfContext() =>
        new(new(), compatibility: null, fontWidthScale: 1, fontFallback: null, ProjectFonts.Directory);

    static string FileOf(string family, bool bold, bool italic = false)
    {
        using var context = PdfContext();
        return Path.GetFileName(context.GetFace(family, bold, italic).Source!);
    }

    static bool PdfSimulatesBold(string family, bool bold)
    {
        using var context = PdfContext();
        return BoldSimulation.For(bold, context.GetFace(family, bold, false).Metrics) != SyntheticBold.None;
    }

    // Word emboldens the family a run names (_probe_synbold): Franklin Gothic Book drawn bold is the Book
    // face with the bold simulation, never Franklin Gothic Demi.
    [Test]
    [Arguments("Franklin Gothic Book", "Franklin_Gothic_Book_400.ttf")]
    [Arguments("Franklin Gothic Medium", "Franklin_Gothic_Medium_400.ttf")]
    [Arguments("Calibri Light", "Calibri_300.ttf")]
    public async Task A_bold_request_keeps_the_family_it_names(string family, string file)
    {
        await Assert.That(FileOf(family, bold: true)).IsEqualTo(file);

        var candidates = FontHelpers.GetCandidateNames(family);
        await Assert.That(candidates.Original).IsEqualTo(family);
    }

    [Test]
    public async Task A_face_that_is_not_bold_is_simulated_in_the_pdf_and_a_bold_one_is_not()
    {
        await Assert.That(PdfSimulatesBold("Franklin Gothic Book", true)).IsTrue();
        await Assert.That(PdfSimulatesBold("Franklin Gothic Book", false)).IsFalse();
        await Assert.That(PdfSimulatesBold("Arial", true)).IsFalse();
        await Assert.That(PdfSimulatesBold("Arial Black", true)).IsFalse();
    }

    // Cambria's regular face is bundled only inside cambria.ttc; its bold is a file of its own. The PDF
    // resolver indexed files alone and drew all of Cambria bold (cards/02).
    [Test]
    public async Task A_face_inside_a_collection_is_served_to_the_pdf()
    {
        await Assert.That(FileOf("Cambria", bold: false)).IsEqualTo("cambria.ttc|0");
        await Assert.That(FileOf("Cambria", bold: true)).IsEqualTo("Cambria_700.ttf");

        using var context = PdfContext();
        var metrics = context.GetFace("Cambria", false, false).Metrics;

        // The face's own weight, glyphs to draw with.
        await Assert.That(metrics.Weight).IsEqualTo(400);
        await Assert.That(metrics.AdvanceWidths.Count).IsGreaterThan(1000);
    }

    // newsletters/07's body is "AvenirNext LT Pro Medium": the resolver scores the Regular of its face
    // closest (Calibri's, since the name falls back as Word's does), and ImageSharp had drawn the Bold
    // because "Medium" reads as a bold word.
    [Test]
    [Arguments("AvenirNext LT Pro Medium", false)]
    [Arguments("Avenir Next LT Pro Light", false)]
    [Arguments("Avenir Next LT Pro", false)]
    [Arguments("Franklin Gothic Demi", false)]
    public async Task ImageSharp_draws_the_face_the_resolver_picked(string family, bool bold)
    {
        using var context = new ImageSharpRenderContext(new(), 96, fontDirectory: ProjectFonts.Directory);

        await Assert.That(context.GetFontForFamily(family, 12f, false, false).IsBold).IsEqualTo(bold);
    }

    [Test]
    public async Task ImageSharp_still_draws_a_real_bold_and_a_real_italic()
    {
        using var context = new ImageSharpRenderContext(new(), 96, fontDirectory: ProjectFonts.Directory);

        await Assert.That(context.GetFontForFamily("Avenir Next LT Pro", 12f, true, false).IsBold).IsTrue();
        await Assert.That(context.GetFontForFamily("Arial", 12f, true, true).IsBold).IsTrue();
        await Assert.That(context.GetFontForFamily("Arial", 12f, true, true).IsItalic).IsTrue();
        await Assert.That(context.GetFontForFamily("Arial", 12f, false, true).IsItalic).IsTrue();

        // No bold face in the family: the regular one, which the painter then emboldens.
        await Assert.That(context.GetFontForFamily("Franklin Gothic Book", 12f, true, false).IsBold).IsFalse();
    }

    [Test]
    public async Task A_variable_font_and_its_weight_are_read_from_the_file()
    {
        var bahnschrift = FontMetricsReader.Read(Path.Combine(ProjectFonts.Directory, "Bahnschrift_400.ttf"))!;
        var arialBold = FontMetricsReader.Read(Path.Combine(ProjectFonts.Directory, "Arial_700.ttf"))!;

        await Assert.That(bahnschrift.IsVariable).IsTrue();
        await Assert.That(bahnschrift.Weight).IsEqualTo(400);
        await Assert.That(arialBold.IsVariable).IsFalse();
        await Assert.That(arialBold.Weight).IsEqualTo(700);
    }
}
