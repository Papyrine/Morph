/// <summary>
/// Tests for w:fldChar / w:instrText parsing.
/// </summary>
public class FieldCodesTests
{
    [Test]
    public async Task FieldCode_Keyword_StripsArguments()
    {
        var field = new FieldCode { Instruction = "PAGE \\* MERGEFORMAT", Result = "1" };
        await Assert.That(field.Keyword).IsEqualTo("PAGE");
    }

    [Test]
    public async Task FieldCode_Keyword_HandlesEmpty()
    {
        var field = new FieldCode { Instruction = "", Result = "" };
        await Assert.That(field.Keyword).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DocumentParser_CapturesPageField()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "newsletters", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.FieldCodes).IsNotEmpty();
        await Assert.That(doc.FieldCodes.Any(_ => _.Keyword == "PAGE")).IsTrue();
    }

    [Test]
    public async Task DocumentParser_NoFields_EmptyList()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.FieldCodes).IsEmpty();
    }
}
