/// <summary>
/// Tests for the paragraph-property fields added during the recent capture pass:
/// w:framePr/w:dropCap and w:bidi.
/// </summary>
public class ParagraphPropertyCaptureTests
{
    [Test]
    public async Task ParagraphProperties_NewCaptureDefaults_AreCorrect()
    {
        var props = new ParagraphProperties();

        await Assert.That(props.DropCap).IsEqualTo(DropCapPosition.None);
        await Assert.That(props.DropCapLines).IsEqualTo(0);
        await Assert.That(props.IsRightToLeft).IsFalse();
    }

    [Test]
    public async Task DocumentParser_ParsesDropCap()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "feature_capture", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var dropPara = doc.Elements.OfType<ParagraphElement>().FirstOrDefault(_ => _.Properties.DropCap != DropCapPosition.None);
        await Assert.That(dropPara).IsNotNull();
        await Assert.That(dropPara!.Properties.DropCap).IsEqualTo(DropCapPosition.Drop);
        await Assert.That(dropPara.Properties.DropCapLines).IsEqualTo(3);
    }

    [Test]
    public async Task DocumentParser_ParsesParagraphRtl()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "feature_capture", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var rtlPara = doc.Elements.OfType<ParagraphElement>().Any(_ => _.Properties.IsRightToLeft);
        await Assert.That(rtlPara).IsTrue();
    }
}
