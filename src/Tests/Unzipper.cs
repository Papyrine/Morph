public class Unzipper
{
    static readonly UTF8Encoding utf8NoBom = new(false);

    [Test]
    [Explicit]
    public async Task Run()
    {
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        foreach (var docx in Directory.GetFiles(inputsDir, "input.docx", SearchOption.AllDirectories))
        {
            var inputDirectory = Path.Combine(Path.GetDirectoryName(docx)!, "input");
            Console.WriteLine(inputDirectory);
            if (Directory.Exists(inputDirectory))
            {
                Directory.Delete(inputDirectory, true);
            }

            Directory.CreateDirectory(inputDirectory);
            await using var stream = File.OpenRead(docx);
            await using var archive = new ZipArchive(stream);
            await archive.ExtractToDirectoryAsync(inputDirectory);
            NormalizeTextFiles(inputDirectory);
        }
    }

    /// <summary>
    /// Normalizes extracted XML/rels files to LF line endings and UTF-8 without BOM,
    /// matching .gitattributes (text=auto eol=lf). Without this, re-running the
    /// Unzipper produces CRLF files (as stored in the ZIP) that git reports as
    /// modified with no content changes.
    /// </summary>
    static void NormalizeTextFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".rels", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            content = content.ReplaceLineEndings("\n");
            File.WriteAllBytes(file, utf8NoBom.GetBytes(content));
        }
    }
}
