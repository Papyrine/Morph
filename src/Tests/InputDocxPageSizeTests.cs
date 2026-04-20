using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Every <c>input.docx</c> scenario under <c>Tests\Inputs\</c> must declare an explicit
/// <c>w:sectPr/w:pgSz</c> with both <c>w:w</c> and <c>w:h</c>. If it doesn't,
/// <see cref="DocumentParser.ExtractPageSettings"/> silently falls back to
/// <c>DefaultPageSize</c>, which is derived from <c>RegionInfo.CurrentRegion</c> — so
/// scenarios without pgSz render at different sizes on US vs non-US hosts, and the
/// "expected" reference PNGs produced by <see cref="RenderExpectedTests"/> (via Word
/// COM on someone's machine) stop matching the Morph-rendered output on CI.
/// </summary>
public class InputDocxPageSizeTests
{
    [Test]
    public async Task EveryInputDocxDeclaresPageSize()
    {
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        var missing = new List<string>();

        foreach (var docxPath in Directory.EnumerateFiles(inputsDir, "input.docx", SearchOption.AllDirectories))
        {
            if (!HasExplicitPageSize(docxPath))
            {
                missing.Add(Path.GetRelativePath(inputsDir, docxPath));
            }
        }

        await Assert.That(missing).IsEmpty()
            .Because(
                $"{missing.Count} input.docx file(s) have no explicit w:pgSz and will render at " +
                $"the host's locale-default paper size:{Environment.NewLine}  " +
                string.Join($"{Environment.NewLine}  ", missing));
    }

    static bool HasExplicitPageSize(string docxPath)
    {
        using var document = WordprocessingDocument.Open(docxPath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body == null)
        {
            return false;
        }

        // pgSz can appear on the body's trailing SectionProperties or on any
        // paragraph-level ParagraphProperties/SectionProperties (mid-document section breaks).
        foreach (var sectionProps in body.Descendants<SectionProperties>())
        {
            var pageSize = sectionProps.GetFirstChild<PageSize>();
            if (pageSize?.Width?.HasValue == true && pageSize.Height?.HasValue == true)
            {
                return true;
            }
        }

        return false;
    }
}
