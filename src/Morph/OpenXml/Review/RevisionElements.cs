/// <summary>
/// The markup Word records tracked changes in, and the order it is numbered in.
/// <see cref="DocumentReview"/> names a change by the ordinals of its elements and
/// <see cref="ReviewEditor"/> finds them again by the same count, so both go through
/// <see cref="Enumerate"/>.
/// </summary>
static class RevisionElements
{
    const string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// The parts a document's revisions can sit in, in a fixed order: a change's key leads with its
    /// part's index here.
    /// </summary>
    public static List<(ReviewPart Part, OpenXmlPartRootElement Root)> Roots(WordprocessingDocument document)
    {
        var roots = new List<(ReviewPart Part, OpenXmlPartRootElement Root)>();
        if (document.MainDocumentPart is not { } main)
        {
            return roots;
        }

        if (main.Document is { } body)
        {
            roots.Add((ReviewPart.Body, body));
        }

        foreach (var part in main.HeaderParts.OrderBy(_ => _.Uri.OriginalString, StringComparer.Ordinal))
        {
            if (part.Header is { } header)
            {
                roots.Add((ReviewPart.Header, header));
            }
        }

        foreach (var part in main.FooterParts.OrderBy(_ => _.Uri.OriginalString, StringComparer.Ordinal))
        {
            if (part.Footer is { } footer)
            {
                roots.Add((ReviewPart.Footer, footer));
            }
        }

        if (main.FootnotesPart?.Footnotes is { } footnotes)
        {
            roots.Add((ReviewPart.Footnotes, footnotes));
        }

        if (main.EndnotesPart?.Endnotes is { } endnotes)
        {
            roots.Add((ReviewPart.Endnotes, endnotes));
        }

        return roots;
    }

    /// <summary>Every revision element under <paramref name="root"/>, in document order.</summary>
    public static IEnumerable<OpenXmlElement> Enumerate(OpenXmlElement root) =>
        root.Descendants().Where(IsRevision);

    public static bool IsRevision(OpenXmlElement element) =>
        element is
            InsertedRun or DeletedRun or MoveFromRun or MoveToRun or
            Inserted or Deleted or MoveFrom or MoveTo or
            MoveFromRangeStart or MoveFromRangeEnd or MoveToRangeStart or MoveToRangeEnd or
            RunPropertiesChange or ParagraphMarkRunPropertiesChange or ParagraphPropertiesChange or NumberingChange or
            SectionPropertiesChange or
            TablePropertiesChange or TablePropertyExceptionsChange or TableGridChange or
            TableRowPropertiesChange or TableCellPropertiesChange or
            CellInsertion or CellDeletion or CellMerge;

    /// <summary>Whether the element wraps the content it changes: <c>w:ins</c>, <c>w:del</c>, <c>w:moveFrom</c>, <c>w:moveTo</c>.</summary>
    public static bool IsContainer(OpenXmlElement element) =>
        element is InsertedRun or DeletedRun or MoveFromRun or MoveToRun;

    /// <summary>
    /// Whether the element marks a paragraph's own mark as inserted, deleted or moved — the same
    /// element marks a table row when it sits in <c>w:trPr</c>.
    /// </summary>
    public static bool IsParagraphMark(OpenXmlElement element) =>
        element is Inserted or Deleted or MoveFrom or MoveTo &&
        element.Parent is ParagraphMarkRunProperties;

    public static string? Author(OpenXmlElement element) =>
        Attribute(element, "author");

    public static string? Id(OpenXmlElement element) =>
        Attribute(element, "id");

    /// <summary>
    /// The element's <c>w:date</c> at face value. Word writes the author's local clock and stamps it
    /// <c>Z</c> regardless, so converting it to this machine's time zone would only move it further
    /// from what the author saw.
    /// </summary>
    public static DateTime? Date(OpenXmlElement element) =>
        ParseDate(Attribute(element, "date"));

    // As Word reads it (probed, Word 16): "12:00:00Z" and "12:00:00" are both 12:00, and a date that
    // does state an offset, "12:00:00+02:00", is its UTC time, 10:00 — not the 12:00 it was written at.
    public static DateTime? ParseDate(string? value)
    {
        if (value is null ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return null;
        }

        return DateTime.SpecifyKind(date.UtcDateTime, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// A moment as Word writes a revision's or a comment's <c>w:date</c>: the author's own clock,
    /// stamped <c>Z</c> though it is not UTC — which is how Word reads it back.
    /// </summary>
    public static string Stamp(DateTimeOffset date) =>
        date.DateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string? Attribute(OpenXmlElement element, string localName)
    {
        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName == localName &&
                attribute.NamespaceUri == wordNamespace)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="element"/> is still part of the tree under <paramref name="root"/>.</summary>
    public static bool IsAttached(OpenXmlElement element, OpenXmlElement root)
    {
        for (var current = element; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes ordinals as a change key: the part's index, then the ordinals as ranges.</summary>
    public static string Key(int part, IReadOnlyList<int> ordinals)
    {
        var sorted = ordinals.Distinct().Order().ToList();
        var builder = new StringBuilder();
        builder.Append(part.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        for (var index = 0; index < sorted.Count; index++)
        {
            var first = sorted[index];
            var last = first;
            while (index + 1 < sorted.Count && sorted[index + 1] == last + 1)
            {
                last = sorted[++index];
            }

            if (builder[^1] != ':')
            {
                builder.Append(',');
            }

            builder.Append(first.ToString(CultureInfo.InvariantCulture));
            if (last != first)
            {
                builder.Append('-');
                builder.Append(last.ToString(CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    /// <summary>Reads a key back. A malformed key names nothing.</summary>
    public static bool TryParseKey(string key, out int part, out List<int> ordinals)
    {
        part = -1;
        ordinals = [];
        var colon = key.IndexOf(':');
        if (colon <= 0 ||
            !int.TryParse(key.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out part))
        {
            return false;
        }

        foreach (var range in key[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = range.IndexOf('-');
            var firstText = dash < 0 ? range : range[..dash];
            var lastText = dash < 0 ? range : range[(dash + 1)..];
            if (!int.TryParse(firstText, NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
                !int.TryParse(lastText, NumberStyles.None, CultureInfo.InvariantCulture, out var last) ||
                last < first)
            {
                ordinals = [];
                return false;
            }

            for (var ordinal = first; ordinal <= last; ordinal++)
            {
                ordinals.Add(ordinal);
            }
        }

        return ordinals.Count > 0;
    }
}
