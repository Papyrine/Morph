/// <summary>
/// No DOCX under <c>Tests\Inputs\</c> may carry a part that nothing in the conversion pipeline
/// reads — see <see cref="DocumentParts"/> for the set. None of them are reachable from
/// <c>word/document.xml</c> content, so their weight buys the corpus nothing and no scenario's
/// rendered output would reveal their return. An Explorer preview picture alone can be most of a
/// package's weight.
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
