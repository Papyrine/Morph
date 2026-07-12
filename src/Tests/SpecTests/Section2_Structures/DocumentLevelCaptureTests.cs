/// <summary>
/// Tests for document-level captures added during the recent pass:
/// footnotes, endnotes, embedded OLE objects, and DocumentFeatures presence flags.
/// </summary>
public class DocumentLevelCaptureTests
{
    static string DocumentCaptureFile => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "document_capture", "01", "input.docx");
    static string AllCapsFile => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

    [Test]
    public async Task Footnote_RequiresIdAndText()
    {
        var fn = new Footnote { Id = 1, Text = "x" };
        await Assert.That(fn.Id).IsEqualTo(1);
        await Assert.That(fn.Text).IsEqualTo("x");
    }

    [Test]
    public async Task Endnote_RequiresIdAndText()
    {
        var en = new Endnote { Id = 1, Text = "y" };
        await Assert.That(en.Id).IsEqualTo(1);
        await Assert.That(en.Text).IsEqualTo("y");
    }

    [Test]
    public async Task EmbeddedObject_DefaultsAreNull()
    {
        var ole = new EmbeddedObject();
        await Assert.That(ole.ProgId).IsNull();
        await Assert.That(ole.RelationshipId).IsNull();
    }

    [Test]
    public async Task DocumentFeatures_DefaultsAllFalse()
    {
        var f = new DocumentFeatures();
        await Assert.That(f.HasCharts).IsFalse();
        await Assert.That(f.HasSmartArt).IsFalse();
        await Assert.That(f.HasMath).IsFalse();
        await Assert.That(f.HasWatermarks).IsFalse();
        await Assert.That(f.HasGradientFills).IsFalse();
        await Assert.That(f.HasBezierShapes).IsFalse();
        await Assert.That(f.Has3dEffects).IsFalse();
        await Assert.That(f.HasConnectors).IsFalse();
        await Assert.That(f.HasDuotoneEffects).IsFalse();
    }

    [Test]
    public async Task DocumentParser_ParsesFootnotes()
    {
        var doc = new DocumentParser().Parse(DocumentCaptureFile);

        await Assert.That(doc.Footnotes.Count).IsEqualTo(1);
        await Assert.That(doc.Footnotes[0].Id).IsEqualTo(1);
        await Assert.That(doc.Footnotes[0].Text).IsEqualTo("This is a footnote.");
    }

    [Test]
    public async Task DocumentParser_ParsesEndnotes()
    {
        var doc = new DocumentParser().Parse(DocumentCaptureFile);

        await Assert.That(doc.Endnotes.Count).IsEqualTo(1);
        await Assert.That(doc.Endnotes[0].Id).IsEqualTo(1);
        await Assert.That(doc.Endnotes[0].Text).IsEqualTo("This is an endnote.");
    }

    [Test]
    public async Task DocumentParser_DetectsHasMath()
    {
        var doc = new DocumentParser().Parse(DocumentCaptureFile);
        await Assert.That(doc.Features.HasMath).IsTrue();
    }

    [Test]
    public async Task DocumentParser_NoFootnotes_EmptyList()
    {
        var doc = new DocumentParser().Parse(AllCapsFile);
        await Assert.That(doc.Footnotes).IsEmpty();
        await Assert.That(doc.Endnotes).IsEmpty();
    }

    [Test]
    public async Task DocumentParser_DetectsHasGradientFills()
    {
        // business/04 ships gradient-filled decorative shapes.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "business", "04", "input.docx");
        var doc = new DocumentParser().Parse(inputFile);
        await Assert.That(doc.Features.HasGradientFills).IsTrue();
    }

    [Test]
    public async Task DocumentParser_DetectsHasDuotoneEffects()
    {
        // newsletters/01 uses a:duotone on its decorative imagery.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "newsletters", "01", "input.docx");
        var doc = new DocumentParser().Parse(inputFile);
        await Assert.That(doc.Features.HasDuotoneEffects).IsTrue();
    }

    [Test]
    public async Task DocumentParser_DetectsHasBezierShapes()
    {
        // agendas-minutes/05 ships custom Bezier path geometry.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "agendas-minutes", "05", "input.docx");
        var doc = new DocumentParser().Parse(inputFile);
        await Assert.That(doc.Features.HasBezierShapes).IsTrue();
    }

    [Test]
    public async Task DocumentParser_DetectsHasCharts()
    {
        // business-plans/12 embeds a chart.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "business-plans", "12", "input.docx");
        var doc = new DocumentParser().Parse(inputFile);
        await Assert.That(doc.Features.HasCharts).IsTrue();
    }
}
