using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// A Word document read for its review pane and for editing at once. The viewer wants both after
/// every edit, and each on its own opens the package, normalizes Strict markup and loads every part's
/// DOM — which on a small document costs about as much as parsing and laying it out.
///
/// An edit already has the package open, so under <see cref="Capture"/> an <see cref="Edit"/> reads
/// both from it once it is saved and remembers them against the bytes it returns, and
/// <see cref="Read"/> of those bytes is then free.
/// </summary>
static class DocumentReading
{
    [ThreadStatic]
    static bool capturing;

    // Keyed by the edited file's array: nothing alters one once returned, and an entry goes with it.
    static readonly ConditionalWeakTable<byte[], Reading> remembered = new();

    sealed record Reading(DocumentReview Review, DocumentOutline Outline);

    public static (DocumentReview Review, DocumentOutline Outline) Read(byte[] docx)
    {
        if (remembered.TryGetValue(docx, out var reading))
        {
            return (reading.Review, reading.Outline);
        }

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

    /// <summary>
    /// Has the edits made on this thread until disposed read their results for <see cref="Read"/>.
    /// Only the viewer asks: an edit nobody reads back pays nothing.
    /// </summary>
    public static IDisposable Capture()
    {
        var previous = capturing;
        capturing = true;
        return new Scope(previous);
    }

    sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() =>
            capturing = previous;
    }

    /// <summary>
    /// Opens a copy of <paramref name="docx"/> for writing, makes <paramref name="edit"/> to it, and
    /// returns the edited file — read for <see cref="Read"/> on the way, when captured.
    /// </summary>
    public static byte[] Edit(byte[] docx, Action<WordprocessingDocument> edit)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;

        // A Strict file is read through a rewrite, which the package open here is not.
        var capture = capturing && !StrictToTransitional.IsStrict(stream);
        Reading? reading = null;

        // Captured, the package is saved before it is read and not again after: reading loads parts
        // the edit did not touch, and an autosave would write them back out.
        using (var document = WordprocessingDocument.Open(
                   stream,
                   true,
                   new()
                   {
                       AutoSave = !capture
                   }))
        {
            edit(document);
            if (capture)
            {
                document.Save();
                reading = TryRead(document);
            }
        }

        var bytes = stream.ToArray();
        if (reading != null)
        {
            remembered.AddOrUpdate(bytes, reading);
        }

        return bytes;
    }

    // What cannot be read here is read again from the bytes, where the viewer handles it.
    static Reading? TryRead(WordprocessingDocument document)
    {
        try
        {
            return new(DocumentReview.Read(document, true), DocumentOutline.Read(document, true));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
