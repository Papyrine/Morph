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

    [Test]
    public async Task DocumentParser_CapturesSimpleFields()
    {
        // field_codes_simple/01 uses two w:fldSimple entries (PAGE and NUMPAGES) on the same paragraph.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "field_codes_simple", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.FieldCodes.Count).IsEqualTo(2);
        await Assert.That(doc.FieldCodes.Any(_ => _.Keyword == "PAGE")).IsTrue();
        await Assert.That(doc.FieldCodes.Any(_ => _.Keyword == "NUMPAGES")).IsTrue();
    }

    static string FontsDirectory => Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static IEnumerable<Run> AllRuns(IEnumerable<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            if (element is ParagraphElement paragraph)
            {
                foreach (var run in paragraph.Runs)
                {
                    yield return run;
                }
            }
            else if (element is TableElement table)
            {
                foreach (var run in table.Rows.SelectMany(_ => _.Cells).SelectMany(_ => AllRuns(_.Content)))
                {
                    yield return run;
                }
            }
        }
    }

    [Test]
    public async Task SimpleField_PageAndNumPages_TaggedAndFlagsTotalNeeded()
    {
        // field_codes_simple/01: two w:fldSimple fields (PAGE, NUMPAGES) in the body.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "field_codes_simple", "01", "input.docx");
        var doc = new DocumentParser().Parse(inputFile);

        var pageFields = AllRuns(doc.Elements).Select(_ => _.PageField).ToList();
        await Assert.That(pageFields).Contains(PageFieldKind.Page);
        await Assert.That(pageFields).Contains(PageFieldKind.NumberOfPages);

        // NUMPAGES needs the document total, so the converters must run a counting pass.
        await Assert.That(doc.RequiresTotalPageCount).IsTrue();
    }

    [Test]
    public async Task ComplexField_PageInFooter_Tagged()
    {
        // page_numbers footer: "Page <PAGE> of <NUMPAGES>" built from complex w:fldChar fields.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "page_numbers", "input.docx");
        var doc = new DocumentParser().Parse(inputFile);

        await Assert.That(doc.Footer).IsNotNull();
        var footerFields = AllRuns(doc.Footer!.Elements).Select(_ => _.PageField).ToList();
        await Assert.That(footerFields).Contains(PageFieldKind.Page);
        await Assert.That(footerFields).Contains(PageFieldKind.NumberOfPages);
        await Assert.That(doc.RequiresTotalPageCount).IsTrue();
    }

    [Test]
    public async Task PageOnlyField_DoesNotRequireTotal()
    {
        // business-plans/10 footer is a lone PAGE field — the current page number is known during
        // the normal pass, so no counting pass is needed.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "business-plans", "10", "input.docx");
        var doc = new DocumentParser().Parse(inputFile);

        await Assert.That(AllRuns(doc.Footer!.Elements).Any(_ => _.PageField == PageFieldKind.Page)).IsTrue();
        await Assert.That(AllRuns(doc.Footer!.Elements).Any(_ => _.PageField is PageFieldKind.NumberOfPages or PageFieldKind.SectionPages)).IsFalse();
        await Assert.That(doc.RequiresTotalPageCount).IsFalse();
    }

    [Test]
    public async Task NoFields_DoesNotRequireTotal()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");
        var doc = new DocumentParser().Parse(inputFile);

        await Assert.That(doc.RequiresTotalPageCount).IsFalse();
        await Assert.That(AllRuns(doc.Elements).All(_ => _.PageField == PageFieldKind.None)).IsTrue();
    }

    [Test]
    public async Task NumPagesField_RendersRealTotal_ImageSharp()
    {
        // field_codes_simple/01's NUMPAGES cached value is 3, but the document is a single page;
        // the counting pass must produce a 1-page render (Word shows "Page 1 of 1").
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "field_codes_simple", "01", "input.docx");
        var options = new ImageExportOptions {Dpi = 150, FontDirectory = FontsDirectory, DeterministicRendering = true};

        var pages = new ImageSharpDocumentConverter().ConvertToImageData(inputFile, options);

        await Assert.That(pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PageNumbersFooter_RendersBothPages_ImageSharp()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "page_numbers", "input.docx");
        var options = new ImageExportOptions {Dpi = 150, FontDirectory = FontsDirectory, DeterministicRendering = true};

        var pages = new ImageSharpDocumentConverter().ConvertToImageData(inputFile, options);

        await Assert.That(pages.Count).IsEqualTo(2);
    }
}
