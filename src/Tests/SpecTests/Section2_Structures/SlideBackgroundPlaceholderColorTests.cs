using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

/// <summary>
/// Covers a slide background whose colour is a literal <c>phClr</c>.
///
/// <c>phClr</c> is the placeholder a theme style substitutes a caller's colour into, so it is only
/// meaningful inside <c>fmtScheme</c>. A deck that names it as an actual colour — most often
/// <c>&lt;p:bgRef idx="1001"&gt;&lt;a:schemeClr val="phClr"/&gt;&lt;/p:bgRef&gt;</c>, where the
/// substitution colour is the very placeholder being substituted — has nothing to substitute.
/// PowerPoint falls back to DrawingML's default colour and paints the slide solid black.
///
/// <see cref="ThemeColors.ResolveColor(string, ColorTransforms)"/> used to return null for it, which
/// took the whole background down with it: <c>SlideShapeParser.ResolveBackground</c> drops the
/// element when its colour does not resolve, so the slide rendered on the bare white canvas. A deck
/// PowerPoint shows as solid black came out clean, which hides the defect rather than showing it.
/// </summary>
public class SlideBackgroundPlaceholderColorTests
{
    [Test]
    public async Task PlaceholderColorResolvesToBlack() =>
        await Assert.That(new ThemeColors().ResolveColor("phClr")).IsEqualTo("000000");

    // Case-insensitively, like every other name in the map — the OOXML spelling is phClr.
    [Test]
    public async Task PlaceholderColorIsCaseInsensitive() =>
        await Assert.That(new ThemeColors().ResolveColor("PHCLR")).IsEqualTo("000000");

    // p:bgRef is handed to ExtractSolidFillColor directly rather than through an a:solidFill, so the
    // resolution has to work off the reference element itself.
    [Test]
    public async Task BackgroundReferenceWithPlaceholderColorIsBlack()
    {
        var reference = BackgroundReference(A.SchemeColorValues.PhColor);

        var color = ShapeParser.ExtractSolidFillColor(reference, new());

        await Assert.That(color).IsEqualTo("000000");
    }

    // The fallback must not swallow the ordinary path: a bgRef naming a real scheme colour still
    // resolves to that colour, not to black.
    [Test]
    public async Task BackgroundReferenceWithRealSchemeColorIsUnaffected()
    {
        var reference = BackgroundReference(A.SchemeColorValues.Accent2);
        var theme = new ThemeColors();

        var color = ShapeParser.ExtractSolidFillColor(reference, theme);

        await Assert.That(color).IsEqualTo(theme.Accent2);
    }

    /// <summary>
    /// End-to-end through the deck parser, which is where the null actually did its damage: the unit
    /// tests above would still pass if <c>ResolveBackground</c> went back to dropping a background
    /// whose colour it could not resolve.
    ///
    /// The fixture clones a corpus deck and replaces only the first slide's <c>p:bg</c>, so it
    /// differs from a passing scenario in exactly one thing. <c>p:bg</c> resolves slide → layout →
    /// master with the first declaration winning, so setting it on the slide cannot be shadowed by
    /// whatever the deck's own master declares.
    /// </summary>
    [Test]
    public async Task PlaceholderColorBackgroundIsParsedAsBlack_EndToEnd()
    {
        using var stream = DeckWithBackground(A.SchemeColorValues.PhColor);
        var deck = new PowerPointDocument(stream);

        // The background is emitted first for each slide, bottom of the paint order.
        var background = deck.Document.Elements[0] as FloatingShapeElement;

        await Assert.That(background).IsNotNull();
        await Assert.That(background!.FillColorHex).IsEqualTo("000000");
        // Full bleed, so a black background really does cover the slide.
        await Assert.That(background.WidthPoints).IsEqualTo(deck.Document.PageSettings.WidthPoints);
        await Assert.That(background.HeightPoints).IsEqualTo(deck.Document.PageSettings.HeightPoints);
    }

    static P.BackgroundStyleReference BackgroundReference(A.SchemeColorValues color) =>
        new(
            new A.SchemeColor
            {
                Val = color
            })
        {
            Index = 1001
        };

    static MemoryStream DeckWithBackground(A.SchemeColorValues color)
    {
        var stream = new MemoryStream();
        using (var source = File.OpenRead(
                   Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "powerpoint", "business-cards-horizontal-layout", "input.pptx")))
        {
            source.CopyTo(stream);
        }

        stream.Position = 0;
        using (var document = PresentationDocument.Open(stream, true))
        {
            var slide = document.PresentationPart!.SlideParts.First().Slide!;
            slide.CommonSlideData!.Background = new(BackgroundReference(color));
        }

        stream.Position = 0;
        return stream;
    }
}
