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
    static readonly ConditionalWeakTable<byte[], Reading> remembered = [];

    sealed record Reading(DocumentReview Review, DocumentOutline Outline);

    public static (DocumentReview Review, DocumentOutline Outline) Read(byte[] docx)
    {
        if (remembered.TryGetValue(docx, out var reading))
        {
            return (reading.Review, reading.Outline);
        }

        if (Borrow(docx, _ => (DocumentReview.Read(_, true), DocumentOutline.Read(_, true)), out var borrowed))
        {
            return borrowed;
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

    // The package of the file the last captured edit on this thread returned, or the viewer opened
    // (Prepare), still open. The viewer's next edit is made to that very array, so it starts from the
    // tree the last one built rather than opening the file and reading its body's XML again, and its
    // parse and the reading of its review and outline read that tree too (Borrow): on the WebAssembly
    // runtime the business plan's body takes ~580ms to read, once for the edit and once for the parse,
    // of a hand-in of ~2.3s. Only the latest is kept; an edit to any other array opens its file afresh,
    // and one that fails lets the package go, since its tree may no longer be the file's. Per thread:
    // the browser has one, and where there are more an edit and the parse after it find the package
    // only when they run on the same one.
    [ThreadStatic]
    static Package? live;

    sealed class Package(byte[] bytes, MemoryStream stream, WordprocessingDocument document) : IDisposable
    {
        public byte[] Bytes { get; } = bytes;
        public MemoryStream Stream { get; } = stream;
        public WordprocessingDocument Document { get; } = document;

        public void Dispose()
        {
            Document.Dispose();
            Stream.Dispose();
        }
    }

    /// <summary>
    /// Opens a copy of <paramref name="docx"/> for writing, makes <paramref name="edit"/> to it, and
    /// returns the edited file — read for <see cref="Read"/> on the way, when captured. Captured, the
    /// package is kept open for the next edit to the file returned, and made to that file it is the
    /// package edited, rather than a copy opened afresh.
    /// </summary>
    public static byte[] Edit(byte[] docx, Action<WordprocessingDocument> edit)
    {
        if (capturing &&
            Take(docx) is { } held)
        {
            return EditCaptured(held, edit);
        }

        var stream = Copy(docx);

        // A Strict file is read through a rewrite, which the package open here is not.
        if (capturing &&
            !StrictToTransitional.IsStrict(stream))
        {
            return EditCaptured(new(docx, stream, OpenCaptured(stream)), edit);
        }

        using (stream)
        {
            using (var document = WordprocessingDocument.Open(stream, true))
            {
                edit(document);
            }

            return stream.ToArray();
        }
    }

    // A copy of the file to open for writing, its entries marked to be deflated fast when saved.
    static MemoryStream Copy(byte[] docx)
    {
        var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;
        FastDeflate.Mark(stream.GetBuffer().AsSpan(0, docx.Length));
        return stream;
    }

    // Captured, the package is saved before it is read and not again after: reading loads parts the
    // edit did not touch, and an autosave would write them back out.
    static WordprocessingDocument OpenCaptured(MemoryStream stream) =>
        WordprocessingDocument.Open(
            stream,
            true,
            new()
            {
                AutoSave = false
            });

    /// <summary>
    /// Opens <paramref name="docx"/> for editing ahead of its first edit, and keeps the package as a
    /// captured edit keeps its own: the parse of the file (<see cref="Borrow"/>), the reading of its
    /// review and outline (<see cref="Read"/>) and its first captured edit then read one tree, where
    /// each would otherwise read the body's XML for itself. Nothing is kept for a Strict file, which
    /// is read through a rewrite, or for one that cannot be opened for writing; each is then read as
    /// it would have been, and what is wrong with it is reported there.
    /// </summary>
    public static void Prepare(byte[] docx)
    {
        if (live is { } held &&
            ReferenceEquals(held.Bytes, docx))
        {
            return;
        }

        var stream = Copy(docx);
        WordprocessingDocument? document = null;
        try
        {
            if (!StrictToTransitional.IsStrict(stream))
            {
                document = OpenCaptured(stream);
            }
        }
        catch (Exception)
        {
            // Left to the reading of the file to report.
        }

        if (document == null)
        {
            stream.Dispose();
            return;
        }

        Keep(new(docx, stream, document));
    }

    /// <summary>Lets the package kept for <paramref name="docx"/> go, if there is one.</summary>
    public static void Release(byte[] docx) =>
        Take(docx)?.Dispose();

    // The edit made and saved, the review and the outline read off the package, and the package kept
    // open for the next edit to the bytes returned.
    static byte[] EditCaptured(Package package, Action<WordprocessingDocument> edit)
    {
        try
        {
            edit(package.Document);
            package.Document.Save();
            var reading = TryRead(package.Document);
            var bytes = package.Stream.ToArray();
            if (reading != null)
            {
                remembered.AddOrUpdate(bytes, reading);
            }

            Keep(new(bytes, package.Stream, package.Document));
            return bytes;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="read"/> on the package the edit that returned <paramref name="docx"/> still
    /// holds open, whose tree is that file's, so the viewer's parse of the file it is about to show
    /// need not read the body's XML again. False, with nothing run, when there is none (the file did
    /// not come from a captured edit, or another has been made since). The reader must leave the
    /// package as it found it; one that throws lets it go.
    /// </summary>
    public static bool Borrow<T>(byte[] docx, Func<WordprocessingDocument, T> read, [MaybeNullWhen(false)] out T result)
    {
        if (Take(docx) is not { } held)
        {
            result = default;
            return false;
        }

        try
        {
            result = read(held.Document);
        }
        catch
        {
            held.Dispose();
            throw;
        }

        Keep(held);
        return true;
    }

    // The live package when it is the file's own, taken out while it is used.
    static Package? Take(byte[] docx)
    {
        if (live is not { } held ||
            !ReferenceEquals(held.Bytes, docx))
        {
            return null;
        }

        live = null;
        return held;
    }

    static void Keep(Package package)
    {
        if (live is { } previous &&
            !ReferenceEquals(previous.Document, package.Document))
        {
            previous.Dispose();
        }

        live = package;
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
