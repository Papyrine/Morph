/// <summary>
/// The files the sample starts with.
/// </summary>
static class SampleFiles
{
    // Morph's own samples, under the names the host lists them by.
    static (string Seed, string Name)[] files =
    [
        ("sample.docx", "Report.docx"),
        ("sample.xlsx", "Workbook.xlsx"),
        ("sample.pptx", "Deck.pptx")
    ];

    /// <summary>
    /// Fills an empty folder with a document, a workbook and a deck, so that there are files to open.
    /// </summary>
    public static void Seed(string folder)
    {
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFiles(folder).Any())
        {
            return;
        }

        foreach (var (seed, name) in files)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "seed", seed), Path.Combine(folder, name));
        }
    }
}
