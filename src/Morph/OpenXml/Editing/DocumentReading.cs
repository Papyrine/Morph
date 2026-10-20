using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// A Word document read for its review pane and for editing at once. The viewer wants both after
/// every edit, and each on its own opens the package, normalizes Strict markup and loads every part's
/// DOM — which on a small document costs about as much as parsing and laying it out.
/// </summary>
static class DocumentReading
{
    public static (DocumentReview Review, DocumentOutline Outline) Read(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        var normalized = StrictToTransitional.Normalize(stream);
        try
        {
            using var document = WordprocessingDocument.Open(normalized, false);
            var editable = ReferenceEquals(normalized, stream);
            return (DocumentReview.Read(document, editable), DocumentOutline.Read(document, editable));
        }
        finally
        {
            if (!ReferenceEquals(normalized, stream))
            {
                normalized.Dispose();
            }
        }
    }
}
