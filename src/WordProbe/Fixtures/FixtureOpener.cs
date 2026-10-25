// A fixture is opened as an unsaved copy — "new from existing" — so there is nothing to close-save and
// the user's Normal template is never the source. Documents.Add(Template) is tried first; when Word
// refuses a .docx there, a temporary copy is opened read-write and closed without saving.
static class FixtureOpener
{
    public static bool UseCopyFallback { get; set; }

    public static Word.Document Open(Word.Application application, string fixturePath)
    {
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException($"Fixture not found: {fixturePath}");
        }

        if (!UseCopyFallback)
        {
            try
            {
                var added = application.Documents.Add(fixturePath, false, Word.WdNewDocumentType.wdNewBlankDocument, true);
                return added;
            }
            catch (COMException exception)
            {
                Log.Warn($"Documents.Add(Template: .docx) refused ({exception.Message}); opening copies instead.");
                UseCopyFallback = true;
            }
        }

        Directory.CreateDirectory(Paths.Temp);
        var copy = Path.Combine(Paths.Temp, $"{Path.GetFileNameWithoutExtension(fixturePath)}-{Guid.NewGuid():N}.docx");
        File.Copy(fixturePath, copy, overwrite: true);
        return application.Documents.Open(copy, ConfirmConversions: false, ReadOnly: false, AddToRecentFiles: false, Visible: true);
    }
}
