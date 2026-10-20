/// <summary>
/// A Word document read for someone about to edit its text: every paragraph of the body, the headers
/// and footers and the notes, with what it holds (<see cref="EditParagraph"/>), and what the document
/// allows.
///
/// Like <see cref="DocumentReview"/> this reads the markup rather than the model the parser builds:
/// an edit is made to the markup, and has to name what it changes in the markup's own terms.
/// <see cref="DocumentEditor"/> makes the edits; every edit is followed by a fresh read.
/// </summary>
sealed class DocumentOutline
{
    Dictionary<int, int> paragraphOfRun = [];

    /// <summary>
    /// The paragraphs of the body, then of the headers and footers, then of the notes
    /// (<see cref="SourceRuns.Stories"/>), each at the index that is its ordinal.
    /// </summary>
    public required IReadOnlyList<EditParagraph> Paragraphs { get; init; }

    /// <summary>
    /// Whether the document's text can be edited: the package is one <see cref="DocumentEditor"/>
    /// can save (see <see cref="DocumentReview.Editable"/>), and its protection allows it.
    /// </summary>
    public required bool AllowsEditing { get; init; }

    /// <summary>Whether edits are recorded as tracked changes: the document's own setting.</summary>
    public required bool Tracking { get; init; }

    /// <summary>Whether the document's protection has every edit tracked, whatever the setting.</summary>
    public required bool ForcesTracking { get; init; }

    /// <summary>The paragraph styles an editor offers for the document (<see cref="StyleCatalog"/>).</summary>
    public IReadOnlyList<StyleChoice> Styles { get; init; } = [];

    /// <summary>The style a paragraph that names none is in.</summary>
    public string DefaultStyle { get; init; } = "Normal";

    /// <summary>The fonts the document's font table names, in its order, less the vertical (<c>@</c>) forms.</summary>
    public IReadOnlyList<string> Fonts { get; init; } = [];

    public static DocumentOutline Empty { get; } = new()
    {
        Paragraphs = [],
        AllowsEditing = false,
        Tracking = false,
        ForcesTracking = false
    };

    /// <summary>The paragraph a run of the main part belongs to, if it is a run of a paragraph's own text.</summary>
    public EditParagraph? ParagraphOf(int run)
    {
        if (paragraphOfRun.TryGetValue(run, out var paragraph))
        {
            return Paragraphs[paragraph];
        }

        return null;
    }

    public static DocumentOutline Read(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        return Read(stream);
    }

    public static DocumentOutline Read(Stream docx)
    {
        var normalized = StrictToTransitional.Normalize(docx);
        try
        {
            using var document = WordprocessingDocument.Open(normalized, false);
            return Read(document, ReferenceEquals(normalized, docx));
        }
        finally
        {
            if (!ReferenceEquals(normalized, docx))
            {
                normalized.Dispose();
            }
        }
    }

    public static DocumentOutline Read(WordprocessingDocument document, bool editable)
    {
        if (document.MainDocumentPart?.Document is not { })
        {
            return Empty;
        }

        var stories = RevisionElements.Roots(document);
        var runs = SourceRuns.IndexAll(stories.Select(_ => _.Root));
        var styles = new Dictionary<string, Style>(StringComparer.Ordinal);
        foreach (var style in document.MainDocumentPart.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is { } id)
            {
                styles.TryAdd(id, style);
            }
        }

        var defaultStyle = styles.Values
            .FirstOrDefault(_ => _.Type?.Value == StyleValues.Paragraph && _.Default?.Value == true)?.StyleId?.Value ?? "Normal";
        var paragraphs = new List<EditParagraph>();
        var unindents = DocumentEditor.Unindents(document);
        var paragraphOfRun = new Dictionary<int, int>();
        var read = stories.SelectMany(story => ParagraphContent.ReadAll(story.Root).Select(_ => (story.Part, _.Paragraph, _.Units)));
        foreach (var (part, paragraph, units) in read)
        {
            var ordinal = paragraphs.Count;
            foreach (var (run, _, _) in ParagraphContent.Runs(paragraph))
            {
                paragraphOfRun[runs[run]] = ordinal;
            }

            paragraphs.Add(
                new(ordinal, units.Select(_ => Unit(_, runs)).ToList())
                {
                    Part = part,
                    HasNext = DocumentEditor.NextBlock(paragraph) is Paragraph,
                    HasPrevious = DocumentEditor.PreviousBlock(paragraph) is Paragraph,
                    InTable = paragraph.Ancestors<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Any(),
                    ListLevel = ListLevel(paragraph, styles, defaultStyle),
                    Indented = unindents(paragraph),
                    NextStyle = NextStyle(paragraph, styles)
                });
        }

        var (allows, forces) = Permissions(document);
        return new()
        {
            Paragraphs = paragraphs,
            AllowsEditing = editable && allows,
            Tracking = document.MainDocumentPart.DocumentSettingsPart?.Settings?.GetFirstChild<TrackRevisions>() is { } tracking &&
                       tracking.Val?.Value != false,
            ForcesTracking = forces,
            Styles = StyleCatalog.Read(document.MainDocumentPart),
            Fonts = document.MainDocumentPart.FontTablePart?.Fonts?.Elements<Font>()
                .Select(_ => _.Name?.Value)
                .OfType<string>()
                .Where(_ => _.Length > 0 && !_.StartsWith('@'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [],
            DefaultStyle = defaultStyle,
            paragraphOfRun = paragraphOfRun
        };
    }

    // The level of the list a paragraph is in: its own numbering's, or else its style's. A list id
    // of 0 says the paragraph is in none, whatever its style says.
    static int? ListLevel(Paragraph paragraph, Dictionary<string, Style> styles, string defaultStyle)
    {
        var own = paragraph.ParagraphProperties?.NumberingProperties;
        var id = own?.NumberingId?.Val?.Value;
        var level = own?.NumberingLevelReference?.Val?.Value;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? defaultStyle;
        while (seen.Add(styleId) &&
               styles.TryGetValue(styleId, out var style))
        {
            var inherited = style.StyleParagraphProperties?.NumberingProperties;
            id ??= inherited?.NumberingId?.Val?.Value;
            level ??= inherited?.NumberingLevelReference?.Val?.Value;
            if (style.BasedOn?.Val?.Value is not { } basedOn)
            {
                break;
            }

            styleId = basedOn;
        }

        if (id is not > 0)
        {
            return null;
        }

        return level ?? 0;
    }

    // The style w:next names for the paragraph's own, when it is another. w:next is the style's
    // own, not inherited through w:basedOn, and a paragraph that names no style follows itself.
    static string? NextStyle(Paragraph paragraph, Dictionary<string, Style> styles)
    {
        if (paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value is not { } styleId ||
            !styles.TryGetValue(styleId, out var style) ||
            style.NextParagraphStyle?.Val?.Value is not { } next ||
            next == styleId)
        {
            return null;
        }

        return next;
    }

    static EditUnit Unit(ParagraphContent.Unit unit, Dictionary<DocumentFormat.OpenXml.Wordprocessing.Run, int> runs) =>
        new(unit.Kind, runs[unit.Run], unit.Start, unit.Text.ToString())
        {
            WidthPoints = unit.WidthPoints,
            HeightPoints = unit.HeightPoints,
            Linked = unit.Run.Ancestors<Hyperlink>().Any()
        };

    // w:documentProtection restricts editing only while it is enforced. Tracked-changes protection
    // allows any edit so long as it is tracked; every other kind allows none to the text.
    static (bool Allows, bool ForcesTracking) Permissions(WordprocessingDocument document)
    {
        var protection = document.MainDocumentPart?.DocumentSettingsPart?.Settings?.GetFirstChild<DocumentProtection>();
        if (protection?.Edit?.Value is not { } edit ||
            protection.Enforcement?.Value != true ||
            edit == DocumentProtectionValues.None)
        {
            return (true, false);
        }

        if (edit == DocumentProtectionValues.TrackedChanges)
        {
            return (true, true);
        }

        return (false, false);
    }
}
