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

    [Test]
    public async Task DocumentParser_ParsesAllW14TextEffects()
    {
        var doc = FeatureCaptureDoc();
        var run = AllRuns(doc).First(_ => _.Properties.Effects != TextEffects.None);
        var allFour = TextEffects.Shadow | TextEffects.Outline | TextEffects.Glow | TextEffects.Reflection;
        await Assert.That(run.Properties.Effects).IsEqualTo(allFour);
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
                case ContentControlElement cc when cc.Runs != null:
                    foreach (var run in cc.Runs) yield return run;
                    break;
            }
        }
    }
}
