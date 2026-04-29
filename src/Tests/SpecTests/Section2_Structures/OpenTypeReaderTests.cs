/// <summary>
/// Verifies <see cref="OpenTypeReader"/> against real font files bundled in <c>src/Fonts</c>.
/// </summary>
public class OpenTypeReaderTests
{
    [Test]
    public async Task SegoeSemilight_FullNameAndWeightRecovered()
    {
        var path = Path.Combine(ProjectFonts.Directory, "Segoe_UI_350.ttf");
        var faces = OpenTypeReader.ReadFaces(path).ToList();

        await Assert.That(faces.Count).IsEqualTo(1);
        var (face, names) = faces[0];

        // The OS/2 weight class on segoeuisl.ttf is 350 — this is the whole point of the
        // refactor. SkiaSharp's SKTypeface.FromFamilyName collapses it to "Segoe UI"
        // weight 400 on Windows; reading the file directly avoids that.
        await Assert.That(face.Weight).IsEqualTo(350);
        await Assert.That(face.Italic).IsFalse();

        // Both the Family ("Segoe UI") and the Full Name ("Segoe UI Semilight") get
        // indexed, so a Word doc that asks for either resolves to this file.
        await Assert.That(names).Contains("Segoe UI", StringComparer.OrdinalIgnoreCase);
        await Assert.That(names).Contains("Segoe UI Semilight", StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public async Task SegoeRegular_BaseFamilyAndRegularWeight()
    {
        var path = Path.Combine(ProjectFonts.Directory, "segoeui.ttf");
        var faces = OpenTypeReader.ReadFaces(path).ToList();

        await Assert.That(faces.Count).IsEqualTo(1);
        var (face, names) = faces[0];

        await Assert.That(face.Weight).IsEqualTo(400);
        await Assert.That(face.Italic).IsFalse();
        await Assert.That(names).Contains("Segoe UI", StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public async Task SegoeBoldItalic_ItalicFlagSet()
    {
        var path = Path.Combine(ProjectFonts.Directory, "segoeuiz.ttf");
        var faces = OpenTypeReader.ReadFaces(path).ToList();

        await Assert.That(faces.Count).IsEqualTo(1);
        var (face, _) = faces[0];

        await Assert.That(face.Italic).IsTrue();
        await Assert.That(face.Weight).IsGreaterThanOrEqualTo(700);
    }

    [Test]
    public async Task LatoLight_WeightThree_HundredRecovered()
    {
        var path = Path.Combine(ProjectFonts.Directory, "Lato_Light_300.ttf");
        var faces = OpenTypeReader.ReadFaces(path).ToList();

        await Assert.That(faces.Count).IsEqualTo(1);
        await Assert.That(faces[0].Face.Weight).IsEqualTo(300);
    }

    [Test]
    public async Task OpenSansRegular_Woff2_MetadataReadViaPartialBrotliDecode()
    {
        // Exercises the WOFF2 path: parse the (uncompressed) header + table directory,
        // then partial-decompress only enough of the Brotli payload to reach name/OS/2.
        var path = Path.Combine(ProjectFonts.Directory, "OpenSans-Regular.woff2");
        var faces = OpenTypeReader.ReadFaces(path).ToList();

        await Assert.That(faces.Count).IsEqualTo(1);
        var (face, names) = faces[0];

        await Assert.That(face.Weight).IsEqualTo(400);
        await Assert.That(face.Italic).IsFalse();
        await Assert.That(names).Contains("Open Sans", StringComparer.OrdinalIgnoreCase);
    }
}
