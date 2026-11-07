using OoxmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;

/// <summary>
/// Gives every paragraph and table row a <c>w14:paraId</c> of its own, as Word does. Coauthoring names a
/// paragraph by it across edits and between the people editing, where the editor's ordinals move with
/// every paragraph added above. A paragraph or row without one, or with one an earlier one in document
/// order already has, gets a new one, random and below 0x80000000 as [MS-DOCX] 2.6.2.5 has it.
///
/// Under <see cref="Stamping"/>, <see cref="DocumentReading.Edit"/> stamps the parts whose tree the edit
/// loaded before it saves, so a part the edit never touched is not written again. A session stamps every
/// part once, when it starts (<see cref="StampAll"/>). Paragraphs under <c>mc:Fallback</c> are left as they
/// are: they are the copy of a text box for readers older than Word 2010, and carry the ids of the
/// <c>mc:Choice</c> they copy.
/// </summary>
static class ParagraphIds
{
    const string w14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    const string mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    [ThreadStatic]
    static bool stamping;

    /// <summary>Whether the edits made on this thread stamp ids.</summary>
    public static bool IsStamping => stamping;

    /// <summary>Has the edits made on this thread stamp the parts they load until disposed.</summary>
    public static IDisposable Stamping()
    {
        var previous = stamping;
        stamping = true;
        return new Scope(previous);
    }

    sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() =>
            stamping = previous;
    }

    /// <summary>Stamps every part of the document that holds paragraphs, loading each.</summary>
    public static byte[] StampAll(byte[] docx) =>
        DocumentReading.Edit(docx, _ => Stamp(_, loadedOnly: false));

    /// <summary>Stamps the parts of the document whose tree is loaded.</summary>
    public static void Stamp(WordprocessingDocument document) =>
        Stamp(document, loadedOnly: true);

    /// <summary>The id of a paragraph or a table row, or null.</summary>
    public static string? Of(OpenXmlElement element) =>
        element switch
        {
            Paragraph paragraph => paragraph.ParagraphId?.Value,
            OoxmlTableRow row => row.ParagraphId?.Value,
            _ => null
        };

    static void Stamp(WordprocessingDocument document, bool loadedOnly)
    {
        if (document.MainDocumentPart is not { } main)
        {
            return;
        }

        // Comments first, so that a comment's paragraphs keep the ids commentsExtended names them by.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in Parts(main))
        {
            if (loadedOnly &&
                !part.IsRootElementLoaded)
            {
                continue;
            }

            if (part.RootElement is { } root)
            {
                Stamp(root, seen);
            }
        }
    }

    // The parts that hold paragraphs, comments first.
    static IEnumerable<OpenXmlPart> Parts(MainDocumentPart main)
    {
        if (main.WordprocessingCommentsPart is { } comments)
        {
            yield return comments;
        }

        yield return main;
        foreach (var header in main.HeaderParts)
        {
            yield return header;
        }

        foreach (var footer in main.FooterParts)
        {
            yield return footer;
        }

        if (main.FootnotesPart is { } footnotes)
        {
            yield return footnotes;
        }

        if (main.EndnotesPart is { } endnotes)
        {
            yield return endnotes;
        }
    }

    static void Stamp(OpenXmlElement root, HashSet<string> seen)
    {
        var stamped = false;
        foreach (var element in Blocks(root))
        {
            if (Of(element) is { } id &&
                seen.Add(id))
            {
                continue;
            }

            var fresh = Fresh(seen);
            if (element is Paragraph paragraph)
            {
                paragraph.ParagraphId = fresh;
            }
            else if (element is OoxmlTableRow row)
            {
                row.ParagraphId = fresh;
            }

            stamped = true;
        }

        if (stamped)
        {
            Declare(root);
        }
    }

    // The paragraphs and rows in document order, past the copies under mc:Fallback.
    static IEnumerable<OpenXmlElement> Blocks(OpenXmlElement element)
    {
        foreach (var child in element.ChildElements)
        {
            if (child is AlternateContentFallback)
            {
                continue;
            }

            if (child is Paragraph or OoxmlTableRow)
            {
                yield return child;
            }

            foreach (var nested in Blocks(child))
            {
                yield return nested;
            }
        }
    }

    static string Fresh(HashSet<string> seen)
    {
        while (true)
        {
            var id = Random.Shared.Next(1, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture);
            if (seen.Add(id))
            {
                return id;
            }
        }
    }

    // The part's root declares w14, and marks it ignorable, as Word writes it.
    static void Declare(OpenXmlElement root)
    {
        if (root.LookupNamespace("w14") == null)
        {
            root.AddNamespaceDeclaration("w14", w14);
        }

        if (root.LookupNamespace("mc") == null)
        {
            root.AddNamespaceDeclaration("mc", mc);
        }

        var tokens = (root.MCAttributes?.Ignorable?.Value ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Contains("w14"))
        {
            return;
        }

        root.MCAttributes ??= new();
        root.MCAttributes.Ignorable = string.Join(' ', [.. tokens, "w14"]);
    }
}
