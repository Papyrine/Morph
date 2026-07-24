/// <summary>
/// Tests for the run-property fields added during the recent capture pass:
/// w:kern, w14:ligatures, w:smallCaps, w14:shadow / textOutline / glow / reflection, w:rtl.
/// </summary>
public class RunPropertyCaptureTests
{
    [Test]
    public async Task RunProperties_NewCaptureDefaults_AreCorrect()
    {
        var props = new RunProperties();

        await Assert.That(props.SmallCaps).IsFalse();
        await Assert.That(props.KerningMinFontSizePoints).IsEqualTo(0);
        await Assert.That(props.Ligatures).IsEqualTo(LigatureMode.Standard);
        await Assert.That(props.Effects).IsEqualTo(TextEffects.None);
        await Assert.That(props.IsRightToLeft).IsFalse();
    }

    static ParsedDocument FeatureCaptureDoc()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "feature_capture", "01", "input.docx");
        return new DocumentParser().Parse(inputFile);
    }

    [Test]
    public async Task DocumentParser_ParsesKerningThreshold()
    {
        var doc = FeatureCaptureDoc();
        var kerned = AllRuns(doc).Any(_ => _.Properties.KerningMinFontSizePoints == 12);
        await Assert.That(kerned).IsTrue();
    }

    [Test]
    public async Task DocumentParser_ParsesLigatureMode_None()
    {
        var doc = FeatureCaptureDoc();
        var none = AllRuns(doc).Any(_ => _.Properties.Ligatures == LigatureMode.None);
        await Assert.That(none).IsTrue();
    }

    [Test]
    public async Task DocumentParser_ParsesSmallCaps()
    {
        var doc = FeatureCaptureDoc();
        var hasSmallCaps = AllRuns(doc).Any(_ => _.Properties.SmallCaps);
        await Assert.That(hasSmallCaps).IsTrue();
    }

    /// <summary>
    /// The fixture declares <c>w14:shadow</c>, <c>w14:textOutline</c>, <c>w14:glow</c> and
    /// <c>w14:reflection</c> all BARE — no attributes, no children — and Word renders the run
    /// plain: its own reference for this scenario draws "All features" as unadorned small caps.
    /// So a bare effect is inert, NOT an instruction to apply Word's UI defaults. Reading mere
    /// presence as "on" drew a shadow copy of the heading in ImageSharp and a glow in Skia.
    /// Defaults still fill the gaps once an effect declares anything at all — see
    /// <c>RunEffectsTests</c>, which builds non-bare elements.
    /// </summary>
    [Test]
    public async Task DocumentParser_BareW14TextEffects_AreInert()
    {
        var doc = FeatureCaptureDoc();
        var run = AllRuns(doc).First(_ => _.Properties.SmallCaps);

        await Assert.That(run.Properties.Effects).IsEqualTo(TextEffects.None);
        await Assert.That(run.Properties.Outline).IsNull();
        await Assert.That(run.Properties.Shadow).IsNull();
        await Assert.That(run.Properties.Glow).IsNull();
        await Assert.That(run.Properties.HasReflection).IsFalse();
    }

    [Test]
    public async Task DocumentParser_ParsesRunRtl()
    {
        var doc = FeatureCaptureDoc();
        var rtl = AllRuns(doc).Any(_ => _.Properties.IsRightToLeft);
        await Assert.That(rtl).IsTrue();
    }

    static IEnumerable<Run> AllRuns(ParsedDocument doc) => Walk(doc.Elements);

    static IEnumerable<Run> Walk(IEnumerable<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case ParagraphElement para:
                    foreach (var run in para.Runs) yield return run;
                    break;
                case TableElement table:
                    foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                    foreach (var run in Walk(cell.Content))
                        yield return run;
                    break;
                case ContentControlElement {Runs: not null} cc:
                    foreach (var run in cc.Runs) yield return run;
                    break;
            }
        }
    }
}
