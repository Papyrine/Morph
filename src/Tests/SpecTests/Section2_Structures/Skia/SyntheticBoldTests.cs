extern alias Skia;
using SkiaSharp;
using SkiaTextRenderer = Skia::TextRenderer;

/// <summary>
/// A bold run whose resolved face is not itself bold must still render bold.
///
/// Two ways in. The family may ship no bold member — "Franklin Gothic Book" bundles only the 400
/// face and its italic — or the family NAME may carry a lighter weight, which deliberately
/// outranks the bold flag for face selection (see <c>FontHelpers.ResolveTargetWeight</c>: a bold
/// run in "Segoe UI Semilight" must still resolve Semilight rather than jumping to a different
/// family member). Either way Word draws the run bold.
///
/// The old gate only emboldened when the NAME-derived target weight exceeded the face by 200+, so
/// "Franklin Gothic Book" resolved a target of 400 against a 400 face and never fired — resumes/07's
/// "Company, location" and its SKILLS labels rendered at normal weight against Word's bold.
/// </summary>
public class SyntheticBoldTests
{
    [Test]
    [Arguments("Franklin Gothic Book")] // weight word in the name, and no bold face bundled
    [Arguments("Segoe UI Semilight")] // name weight deliberately outranks bold for face selection
    [Arguments("Arial")] // control: a family that does bundle a real bold face
    public async Task BoldRun_RendersHeavierThanRegular(string fontFamily)
    {
        var regular = InkPixels(fontFamily, bold: false);
        var bold = InkPixels(fontFamily, bold: true);

        await Assert.That(bold).IsGreaterThan(regular);
    }

    static int InkPixels(string fontFamily, bool bold)
    {
        var pageSettings = new PageSettings
        {
            WidthPoints = 300,
            HeightPoints = 120,
            MarginTop = 10,
            MarginBottom = 10,
            MarginLeft = 10,
            MarginRight = 10
        };

        using var context = new SkiaRenderContext(pageSettings, 150, fontDirectory: ProjectFonts.Directory);
        var textRenderer = new SkiaTextRenderer(context);
        using var bitmap = new SKBitmap(context.PageWidthPixels, context.PageHeightPixels);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new()
                {
                    Text = "Company, location",
                    Properties = new() {FontFamily = fontFamily, FontSizePoints = 24, Bold = bold}
                }
            ]
        };
        textRenderer.RenderParagraph(canvas, paragraph);

        var ink = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    ink++;
                }
            }
        }

        return ink;
    }
}
