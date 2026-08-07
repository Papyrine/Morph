/// <summary>
/// The per-conversion <c>FontWidthScale</c> (<see cref="PdfExportOptions.FontWidthScale"/> /
/// <see cref="ImageExportOptions.FontWidthScale"/>, the knob production's <c>RenderContextBase</c> multiplies
/// glyph advances by) is now honoured by the engine measurer — docs/layout-engine.md, step 5 Phase A.
/// The <see cref="CanonicalTextMeasurer"/> previously ignored it, so a conversion that set
/// <c>FontWidthScale != 1</c> got scaled text from production but unscaled text from the engine — a latent
/// divergence once raster defaults to the engine. These guard that the scale widens advances linearly and that
/// the measurer threads it from its constructor.
/// </summary>
public class CanonicalFontWidthScaleTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static FontMetrics Aptos() =>
        FontMetricsReader.Read(Path.Combine(fontsDirectory, "Aptos_400.ttf"))
        ?? throw new InvalidOperationException("Could not read Aptos metrics.");

    [Test]
    public async Task FontWidthScale_widens_the_measured_advance_linearly()
    {
        var metrics = Aptos();
        const string text = "The quick brown fox jumps over the lazy dog, and then over it once more.";

        var unscaled = CanonicalTextMeasurer.MeasureWidthPoints(metrics, text, 11);
        var scaled = CanonicalTextMeasurer.MeasureWidthPoints(metrics, text, 11, 1.08);

        await Assert.That(scaled).IsGreaterThan(unscaled);
        // Linear widening, to within the once-per-line pen-position rounding (< half a device pixel).
        await Assert.That(scaled).IsEqualTo(unscaled * 1.08).Within(0.7);
    }

    [Test]
    public async Task Scale_of_one_is_the_unscaled_advance()
    {
        var metrics = Aptos();
        await Assert.That(CanonicalTextMeasurer.MeasureWidthPoints(metrics, "Accountant", 11))
            .IsEqualTo(CanonicalTextMeasurer.MeasureWidthPoints(metrics, "Accountant", 11));
    }

    [Test]
    public async Task The_measurer_threads_the_scale_from_its_constructor()
    {
        static FontMetrics Resolve(string family, bool bold, bool italic) => Aptos();
        var properties = new RunProperties
        {
            FontFamily = "Aptos",
            FontSizePoints = 11
        };

        var narrow = new CanonicalParagraphMeasurer(Resolve).MeasureRunWidth("Accountant", properties);
        var wider = new CanonicalParagraphMeasurer(Resolve, 1.08).MeasureRunWidth("Accountant", properties);

        await Assert.That(wider).IsGreaterThan(narrow);
        await Assert.That((double) wider).IsEqualTo(narrow * 1.08).Within(0.7);
    }
}
