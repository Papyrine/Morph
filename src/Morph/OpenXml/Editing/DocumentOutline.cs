/// <summary>
/// A Word document read for someone about to edit its text: every paragraph of the main part with
/// what it holds (<see cref="EditParagraph"/>), and what the document allows. Headers, footers and
/// notes are other parts, and are not read.
///
/// Like <see cref="DocumentReview"/> this reads the markup rather than the model the parser builds:
/// an edit is made to the markup, and has to name what it changes in the markup's own terms.
/// <see cref="DocumentEditor"/> makes the edits; every edit is followed by a fresh read.
/// </summary>
sealed class DocumentOutline
{
    Dictionary<int, int> paragraphOfRun = [];

    /// <summary>The main part's paragraphs, each at the index that is its ordinal.</summary>
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

    static DocumentOutline Read(WordprocessingDocument document, bool editable)
    {
        if (document.MainDocumentPart?.Document is not { } root)
        {
            return Empty;
        }

        var runs = SourceRuns.Index(root);
        var paragraphs = new List<EditParagraph>();
        var paragraphOfRun = new Dictionary<int, int>();
        foreach (var (paragraph, units) in ParagraphContent.ReadAll(root))
        {
            var ordinal = paragraphs.Count;
            foreach (var (run, _, _) in ParagraphContent.Runs(paragraph))
            {
                paragraphOfRun[runs[run]] = ordinal;
            }

            paragraphs.Add(
                new(ordinal, units.Select(_ => Unit(_, runs)).ToList())
                {
                    HasNext = DocumentEditor.NextBlock(paragraph) is Paragraph,
                    HasPrevious = DocumentEditor.PreviousBlock(paragraph) is Paragraph
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
            paragraphOfRun = paragraphOfRun
        };
    }

    static EditUnit Unit(ParagraphContent.Unit unit, Dictionary<DocumentFormat.OpenXml.Wordprocessing.Run, int> runs) =>
        new(unit.Kind, runs[unit.Run], unit.Start, unit.Text.ToString())
        {
            WidthPoints = unit.WidthPoints,
            HeightPoints = unit.HeightPoints
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
