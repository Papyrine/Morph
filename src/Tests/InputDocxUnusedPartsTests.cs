/// <summary>
/// No DOCX under <c>Tests\Inputs\</c> may carry a part that nothing in the conversion pipeline
/// reads — see <see cref="DocumentParts"/> for the set. None of them are reachable from
/// <c>word/document.xml</c> content, so their weight buys the corpus nothing and no scenario's
/// rendered output would reveal their return.
///
/// The corpus was stripped in two passes: 46 packages carrying an Explorer preview picture
/// (7,154,153 bytes — <c>cards/05</c> alone was 91% preview, 3.85 MB down to 350 KB), then 166
/// carrying a glossary and/or custom XML (2,336,581 bytes). 9,490,734 bytes in total, 14% of the
/// 67,188,577 the corpus occupied beforehand, none of it load-bearing.
///
/// This guard exists because that weight arrives silently: a template downloaded from the Word
/// gallery brings whatever its author saved, and nothing about how it renders would say so.
/// </summary>
public class InputDocxUnusedPartsTests
{
    [Test]
    public async Task NoInputDocxCarriesUnusedParts()
    {
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word");
        var carriers = new List<string>();

        foreach (var docxPath in Directory.EnumerateFiles(inputsDir, "*.docx", SearchOption.AllDirectories))
        {
            var present = DocumentCleaner.Find(docxPath);
            if (present == DocumentParts.None)
            {
                continue;
            }

            var size = new FileInfo(docxPath).Length;
            carriers.Add($"{Path.GetRelativePath(inputsDir, docxPath)} — {present} ({size:N0} bytes)");
        }

        await Assert.That(carriers).IsEmpty()
            .Because(
                $"{carriers.Count} input DOCX file(s) carry parts nothing renders. " +
                $"Strip with DocumentCleaner.Remove(path):{Environment.NewLine}  " +
                string.Join($"{Environment.NewLine}  ", carriers));
    }
}
