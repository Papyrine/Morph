extern alias ImageSharp;

/// <summary>
/// The three painters draw a run in the same face. Each rule here was a place where one of them picked a
/// different one: a bold request diverted from the family it named, a face that ships only inside a
/// collection, and a style re-derived from the words in a family's name.
/// </summary>
public class FontFaceResolutionTests
{
    static string FileOf(string family, bool bold, bool italic = false)
    {
        PdfFontResolver.Register(ProjectFonts.Directory);
        return Path.GetFileName(PdfFontResolver.Instance.ResolveTypeface(family, bold, italic)!.FaceName);
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
        PdfFontResolver.Register(ProjectFonts.Directory);

        await Assert.That(PdfFontResolver.Instance.ResolveTypeface("Franklin Gothic Book", true, false)!.MustSimulateBold).IsTrue();
        await Assert.That(PdfFontResolver.Instance.ResolveTypeface("Franklin Gothic Book", false, false)!.MustSimulateBold).IsFalse();
        await Assert.That(PdfFontResolver.Instance.ResolveTypeface("Arial", true, false)!.MustSimulateBold).IsFalse();
        await Assert.That(PdfFontResolver.Instance.ResolveTypeface("Arial Black", true, false)!.MustSimulateBold).IsFalse();
    }

    // Cambria's regular face is bundled only inside cambria.ttc; its bold is a file of its own. The PDF
    // resolver indexed files alone and drew all of Cambria bold (cards/02).
    [Test]
    public async Task A_face_inside_a_collection_is_served_to_the_pdf()
    {
        await Assert.That(FileOf("Cambria", bold: false)).IsEqualTo("cambria.ttc|0");
        await Assert.That(FileOf("Cambria", bold: true)).IsEqualTo("Cambria_700.ttf");

        var face = PdfFontResolver.Instance.ResolveTypeface("Cambria", false, false)!.FaceName;
        var bytes = PdfFontResolver.Instance.GetFont(face)!;
        var metrics = FontMetricsReader.Read(new MemoryStream(bytes))!;

        // A font file of its own: an sfnt header, the face's own weight, glyphs to draw with.
        await Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes)).IsEqualTo(0x00010000u);
        await Assert.That(metrics.Weight).IsEqualTo(400);
        await Assert.That(metrics.AdvanceWidths.Count).IsGreaterThan(1000);
    }

    [Test]
    public async Task A_collection_member_is_lifted_out_whole()
    {
        var collection = File.ReadAllBytes(Path.Combine(ProjectFonts.Directory, "cambria.ttc"));
        var first = FontCollectionFace.Extract(collection, 0)!;
        var second = FontCollectionFace.Extract(collection, 1)!;

        var fromCollection = FontMetricsReader.Read(new MemoryStream(collection), 1)!;
        var standalone = FontMetricsReader.Read(new MemoryStream(second))!;
        await Assert.That(standalone.UnitsPerEm).IsEqualTo(fromCollection.UnitsPerEm);
        await Assert.That(standalone.AdvanceWidths.Count).IsEqualTo(fromCollection.AdvanceWidths.Count);
        await Assert.That(standalone.AdvanceUnits('M')).IsEqualTo(fromCollection.AdvanceUnits('M'));

        // The whole-file checksum a font carries in its head table comes out right for the new file.
        await Assert.That(Checksum(first)).IsEqualTo(0xB1B0AFBAu);
        await Assert.That(Checksum(second)).IsEqualTo(0xB1B0AFBAu);
    }

    [Test]
    public async Task What_is_not_a_collection_member_is_refused()
    {
        var collection = File.ReadAllBytes(Path.Combine(ProjectFonts.Directory, "cambria.ttc"));
        var single = File.ReadAllBytes(Path.Combine(ProjectFonts.Directory, "Cambria_700.ttf"));

        await Assert.That(FontCollectionFace.Extract(collection, 2)).IsNull();
        await Assert.That(FontCollectionFace.Extract(collection, -1)).IsNull();
        await Assert.That(FontCollectionFace.Extract(single, 0)).IsNull();
        await Assert.That(FontCollectionFace.Extract(collection.AsSpan(0, 40), 0)).IsNull();
    }

    static uint Checksum(byte[] bytes)
    {
        var sum = 0u;
        for (var index = 0; index + 3 < bytes.Length; index += 4)
        {
            sum += System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index));
        }

        return sum;
    }

    // newsletters/07's body is "AvenirNext LT Pro Medium": the resolver scores the bundled Regular closest,
    // and ImageSharp drew the Bold because "Medium" reads as a bold word.
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
