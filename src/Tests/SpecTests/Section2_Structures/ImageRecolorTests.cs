/// <summary>
/// Covers <see cref="ImageRecolor"/>, the shared recipe the three painters and the HTML export
/// recolour a picture from. The parse side is <see cref="BlipColorEffectParseTests"/>; this is what
/// happens to the pixels once an effect has been read.
/// </summary>
public class ImageRecolorTests
{
    [Test]
    // The whole corpus bar five scenarios has no picture effect at all, so this is the case that
    // must stay null — a null Recolor is every painter's untouched fast path.
    public async Task No_effect_yields_no_recolour() =>
        await Assert.That(ImageRecolor.For(BlipColorEffect.None, "AF0C0B", null)).IsNull();

    [Test]
    public async Task Grayscale_is_the_bare_luminance_row()
    {
        var (red, green, blue) = ImageRecolor.For(BlipColorEffect.Grayscale, null, null)!.Rows();

        // Rec. 709, the coefficients ImageSharp's Grayscale() uses and the model the duotone ramp
        // was written against. Every output channel gets the same row, which is what makes it grey.
        foreach (var row in new[] {red, green, blue})
        {
            await Assert.That(row.Red).IsEqualTo(0.2126f).Within(0.0001f);
            await Assert.That(row.Green).IsEqualTo(0.7152f).Within(0.0001f);
            await Assert.That(row.Blue).IsEqualTo(0.0722f).Within(0.0001f);
            await Assert.That(row.Offset).IsEqualTo(0f).Within(0.0001f);
        }
    }

    [Test]
    public async Task Duotone_maps_luminance_onto_the_dark_to_light_ramp()
    {
        // brochures/02's recolour: Word's gallery pairs a dark red with white. Black maps to the
        // dark end and white to the light end, which is what makes the photo read red rather than
        // blue — the divergence that opened this finding.
        var recolor = ImageRecolor.For(BlipColorEffect.Duotone, "AF0C0B", "FFFFFF")!;

        await AssertMaps(recolor, from: (0, 0, 0), to: (0xAF / 255f, 0x0C / 255f, 0x0B / 255f));
        await AssertMaps(recolor, from: (1, 1, 1), to: (1, 1, 1));
    }

    [Test]
    public async Task Duotone_light_end_need_not_be_white()
    {
        // letters/02 pairs prstClr black with a tinted accent, so neither end can be assumed.
        var recolor = ImageRecolor.For(BlipColorEffect.Duotone, "000000", "FFA380")!;

        await AssertMaps(recolor, from: (0, 0, 0), to: (0, 0, 0));
        await AssertMaps(recolor, from: (1, 1, 1), to: (1, 0xA3 / 255f, 0x80 / 255f));
    }

    [Test]
    public async Task Duotone_with_no_resolved_colour_falls_back_to_greyscale()
    {
        var (red, _, _) = ImageRecolor.For(BlipColorEffect.Duotone, null, null)!.Rows();

        await Assert.That(red.Red).IsEqualTo(0.2126f).Within(0.0001f);
        await Assert.That(red.Offset).IsEqualTo(0f).Within(0.0001f);
    }

    [Test]
    public async Task Washout_lightens_every_channel_without_mixing_them()
    {
        // Word's washout is brightness +70% then contrast -50%, which composes to c × 0.85 + 0.25:
        // black lifts to a quarter grey and everything above ~0.88 saturates to white.
        var recolor = ImageRecolor.For(BlipColorEffect.Washout, null, null)!;

        await AssertMaps(recolor, from: (0, 0, 0), to: (0.25f, 0.25f, 0.25f));
        await AssertMaps(recolor, from: (0.5f, 0.5f, 0.5f), to: (0.675f, 0.675f, 0.675f));

        // No cross-channel terms: a washed-out picture keeps its hues, it does not grey out.
        var (red, _, _) = recolor.Rows();
        await Assert.That(red.Green).IsEqualTo(0f).Within(0.0001f);
        await Assert.That(red.Blue).IsEqualTo(0f).Within(0.0001f);
    }

    // Asserts the transform a painter applies: each output channel is its row's weights against the
    // source RGB plus the row's constant.
    static async Task AssertMaps(ImageRecolor recolor, (float Red, float Green, float Blue) from, (float Red, float Green, float Blue) to)
    {
        var (red, green, blue) = recolor.Rows();

        await Assert.That(Channel(red)).IsEqualTo(to.Red).Within(0.0005f);
        await Assert.That(Channel(green)).IsEqualTo(to.Green).Within(0.0005f);
        await Assert.That(Channel(blue)).IsEqualTo(to.Blue).Within(0.0005f);

        float Channel(ImageRecolor.Row row) =>
            row.Red * from.Red + row.Green * from.Green + row.Blue * from.Blue + row.Offset;
    }
}
