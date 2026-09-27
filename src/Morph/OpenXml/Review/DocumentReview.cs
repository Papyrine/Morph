using OoxmlComment = DocumentFormat.OpenXml.Wordprocessing.Comment;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OoxmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using W15 = DocumentFormat.OpenXml.Office2013.Word;

/// <summary>
/// A Word document's comments and tracked changes, read for a reviewer: each comment with its thread and
/// the text it is attached to, each change gathered from the elements Word wrote it as, and both tied to
/// the main-part runs they cover (<see cref="SourceRuns"/>) so a viewer can find them on a page.
/// <see cref="ReviewEditor"/> makes the edits; every edit is followed by a fresh read.
///
/// Separate from <see cref="DocumentParser"/> on purpose. The parser builds what gets drawn, and what it
/// keeps of a revision is how the run looks; this reads the markup itself — authors, threads, the
/// formatting a change replaced — none of which rendering needs.
/// </summary>
sealed class DocumentReview
{
    /// <summary>The comment threads, in the order their anchors appear in the document.</summary>
    public required IReadOnlyList<ReviewComment> Comments { get; init; }

    /// <summary>The tracked changes in document order, the main part's first.</summary>
    public required IReadOnlyList<ReviewChange> Changes { get; init; }

    /// <summary>
    /// Whether <see cref="ReviewEditor"/> can edit the package. An ISO 29500 Strict document is read
    /// through a Transitional copy, and saving that copy would quietly change the file's conformance.
    /// </summary>
    public required bool Editable { get; init; }

    /// <summary>Whether the document's protection lets a reviewer add, change and remove comments.</summary>
    public required bool AllowsComments { get; init; }

    /// <summary>Whether the document's protection lets a reviewer accept and reject changes.</summary>
    public required bool AllowsResolving { get; init; }

    public static DocumentReview Empty { get; } = new()
    {
        Comments = [],
        Changes = [],
        Editable = false,
        AllowsComments = false,
        AllowsResolving = false
    };

    public static DocumentReview Read(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        return Read(stream);
    }

    public static DocumentReview Read(Stream docx)
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

    static DocumentReview Read(WordprocessingDocument document, bool editable)
    {
        var changes = new List<(int Part, int Order, ReviewChange Change)>();
        Reader? body = null;
        var roots = RevisionElements.Roots(document);
        for (var index = 0; index < roots.Count; index++)
        {
            var (part, root) = roots[index];
            var reader = new Reader(index, part, root);
            reader.Read();
            if (part == ReviewPart.Body)
            {
                body = reader;
            }

            changes.AddRange(reader.Changes().Select(_ => (index, _.Order, _.Change)));
        }

        var (allowsComments, allowsResolving) = Permissions(document);
        return new()
        {
            Comments = ReadComments(document, body),
            Changes = changes
                .OrderBy(_ => _.Part)
                .ThenBy(_ => _.Change.Anchor)
                .ThenBy(_ => _.Order)
                .Select(_ => _.Change)
                .ToList(),
            Editable = editable,
            AllowsComments = editable && allowsComments,
            AllowsResolving = editable && allowsResolving
        };
    }

    // w:documentProtection restricts editing only while it is enforced. Read-only and forms protection
    // allow no review edits; comments protection allows comments alone; tracked-changes protection
    // allows comments, and forbids settling the very changes it exists to collect.
    static (bool Comments, bool Resolving) Permissions(WordprocessingDocument document)
    {
        var protection = document.MainDocumentPart?.DocumentSettingsPart?.Settings?.GetFirstChild<DocumentProtection>();
        if (protection?.Edit?.Value is not { } edit ||
            protection.Enforcement?.Value != true)
        {
            return (true, true);
        }

        if (edit == DocumentProtectionValues.Comments ||
            edit == DocumentProtectionValues.TrackedChanges)
        {
            return (true, false);
        }

        if (edit == DocumentProtectionValues.None)
        {
            return (true, true);
        }

        return (false, false);
    }

    static List<ReviewComment> ReadComments(WordprocessingDocument document, Reader? body)
    {
        var main = document.MainDocumentPart!;
        if (body == null ||
            main.WordprocessingCommentsPart?.Comments is not { } comments)
        {
            return [];
        }

        // Threads hang off a comment's LAST paragraph: commentsExtended names it by w14:paraId, and
        // names the parent's the same way.
        var extended = new Dictionary<string, W15.CommentEx>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in main.WordprocessingCommentsExPart?.CommentsEx?.Elements<W15.CommentEx>() ?? [])
        {
            if (entry.ParaId?.Value is { } paraId)
            {
                extended[paraId] = entry;
            }
        }

        var all = new List<(OoxmlComment Source, string Id, string? ParaId)>();
        foreach (var comment in comments.Elements<OoxmlComment>())
        {
            if (comment.Id?.Value is { } id)
            {
                all.Add((comment, id, ThreadParagraphId(comment)));
            }
        }

        var idByParaId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, id, paraId) in all)
        {
            if (paraId != null)
            {
                idByParaId.TryAdd(paraId, id);
            }
        }

        var replies = new Dictionary<string, List<ReviewComment>>(StringComparer.Ordinal);
        var roots = new List<(ReviewComment Comment, int Index)>();
        for (var index = 0; index < all.Count; index++)
        {
            var (source, id, paraId) = all[index];
            W15.CommentEx? entry = null;
            if (paraId != null)
            {
                extended.TryGetValue(paraId, out entry);
            }

            var anchor = body.CommentAnchor(id);
            var comment = new ReviewComment
            {
                Id = id,
                Author = source.Author?.Value,
                Initials = source.Initials?.Value,
                Date = RevisionElements.Date(source),
                Text = CommentText(source),
                Resolved = entry?.Done?.Value == true,
                Quote = anchor.Quote,
                Runs = anchor.Runs,
                Anchor = anchor.Anchor
            };

            if (entry?.ParaIdParent?.Value is { } parentParaId &&
                idByParaId.TryGetValue(parentParaId, out var parentId) &&
                parentId != id)
            {
                if (!replies.TryGetValue(parentId, out var list))
                {
                    replies[parentId] = list = [];
                }

                list.Add(comment);
            }
            else
            {
                roots.Add((comment, index));
            }
        }

        // A reply whose parent is itself a reply hangs off nothing in roots; Word's threads are flat,
        // so that is malformed, and the reply is shown as a thread of its own rather than dropped.
        var rootIds = roots.Select(_ => _.Comment.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (parentId, orphans) in replies.Where(_ => !rootIds.Contains(_.Key)).ToList())
        {
            replies.Remove(parentId);
            roots.AddRange(orphans.Select(_ => (_, int.MaxValue)));
        }

        return roots
            .Select(_ =>
            {
                if (replies.TryGetValue(_.Comment.Id, out var list))
                {
                    return (Comment: _.Comment with {Replies = list}, _.Index);
                }

                return _;
            })
            .OrderBy(_ => Place(_.Comment))
            .ThenBy(_ => _.Index)
            .Select(_ => _.Comment)
            .ToList();
    }

    // A comment the document never refers to has no place in it, and follows the ones that do.
    static int Place(ReviewComment comment)
    {
        if (comment.Anchor < 0)
        {
            return int.MaxValue;
        }

        return comment.Anchor;
    }

    /// <summary>The <c>w14:paraId</c> commentsExtended knows a comment by: its last paragraph's.</summary>
    internal static string? ThreadParagraphId(OoxmlComment comment) =>
        comment.Elements<Paragraph>().LastOrDefault()?.ParagraphId?.Value;

    /// <summary>A comment's text, a line per paragraph.</summary>
    internal static string CommentText(OoxmlComment comment)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var paragraph in comment.Descendants<Paragraph>())
        {
            if (!first)
            {
                builder.Append('\n');
            }

            first = false;
            foreach (var element in paragraph.Descendants())
            {
                switch (element)
                {
                    case Text text:
                        builder.Append(text.Text);
                        break;
                    case TabChar:
                        builder.Append('\t');
                        break;
                    case Break:
                        builder.Append('\n');
                        break;
                }
            }
        }

        return builder.ToString();
    }

    readonly record struct Anchored(IReadOnlyList<int> Runs, int Anchor, string Quote);

    // A change being gathered.
    sealed class Builder(ReviewChangeKind kind, string? author, DateTime? date, int order)
    {
        public ReviewChangeKind Kind { get; } = kind;
        public string? Author { get; } = author;
        public DateTime? Date { get; } = date;
        public int Order { get; } = order;
        public List<int> Elements { get; } = [];
        public List<int> Runs { get; } = [];
        public StringBuilder Text { get; } = new();

        // Where a change that covers no run sits.
        public int Point { get; set; } = -1;

        // A move's two halves read the same text; only the destination's is kept, and the change
        // is placed where the text now is.
        public bool TextFromDestination { get; set; }
        public int Destination { get; set; } = -1;
    }

    // The grouping state of one story: the body's flow of paragraphs, or a text box's.
    sealed class Flow
    {
        // The insertion or deletion the next adjacent one of its kind and author joins.
        public Builder? Current { get; set; }

        public Builder? Formatting { get; set; }
        public int FormattingRun { get; set; } = -2;

        public Builder? ParagraphFormatting { get; set; }
        public Paragraph? FormattedParagraph { get; set; }

        public Builder? Rows { get; set; }
        public OoxmlTableRow? ChangedRow { get; set; }

        // The open w:moveFromRangeStart / w:moveToRangeStart, by w:id, and the move each names.
        public Dictionary<string, string> MoveRanges { get; } = new(StringComparer.Ordinal);
        public string? MoveFromName { get; set; }
        public string? MoveToName { get; set; }

        // The paragraph being walked, and the revisions on its mark: they are written ahead of its
        // runs, and take their place after them.
        public List<OpenXmlElement> Marks { get; } = [];
        public int LastRun { get; set; } = -1;
        public bool HasRun { get; set; }
    }

    sealed class CommentRange
    {
        public List<int> Runs { get; } = [];
        public StringBuilder Quote { get; } = new();
        public Paragraph? LastParagraph { get; set; }
        public int Start { get; set; }
    }

    sealed class Reader(int partIndex, ReviewPart part, OpenXmlElement root)
    {
        readonly Dictionary<OpenXmlElement, int> ordinals = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<OoxmlRun, int> runOrdinals = SourceRuns.Index(root);
        readonly Dictionary<OpenXmlElement, Builder> builderOf = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<string, Builder> moves = new(StringComparer.Ordinal);
        readonly List<Builder> builders = [];
        readonly Stack<Flow> flows = new();

        readonly Dictionary<string, CommentRange> openComments = new(StringComparer.Ordinal);
        readonly Dictionary<string, CommentRange> comments = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> references = new(StringComparer.Ordinal);

        // The highest run ordinal walked so far: the run after it is where a runless change sits.
        int lastRun = -1;
        int fallbackDepth;
        int unnamedMoves;

        Flow Flow => flows.Peek();

        bool Tracked => part == ReviewPart.Body;

        public void Read()
        {
            foreach (var element in RevisionElements.Enumerate(root))
            {
                ordinals[element] = ordinals.Count;
            }

            flows.Push(new());
            Visit(root);
        }

        public IEnumerable<(int Order, ReviewChange Change)> Changes()
        {
            foreach (var builder in builders)
            {
                if (builder.Elements.Count == 0)
                {
                    continue;
                }

                List<int> runs = [];
                var anchor = -1;
                if (Tracked)
                {
                    runs = builder.Runs.Distinct().Order().ToList();
                    anchor = builder.Point;
                    if (builder.Destination >= 0)
                    {
                        anchor = builder.Destination;
                    }
                    else if (runs.Count > 0)
                    {
                        anchor = runs[0];
                    }
                }

                yield return (builder.Order, new()
                {
                    Key = RevisionElements.Key(partIndex, builder.Elements),
                    Kind = builder.Kind,
                    Author = builder.Author,
                    Date = builder.Date,
                    Text = builder.Text.ToString(),
                    Part = part,
                    Runs = runs,
                    Anchor = anchor
                });
            }
        }

        public Anchored CommentAnchor(string id)
        {
            // A range the document never closes still says where the comment starts.
            if (!comments.TryGetValue(id, out var range))
            {
                openComments.TryGetValue(id, out range);
            }

            if (range is {Runs.Count: > 0})
            {
                return new(range.Runs, range.Runs[0], range.Quote.ToString());
            }

            if (references.TryGetValue(id, out var reference))
            {
                return new([], reference, "");
            }

            if (range != null)
            {
                return new([], range.Start, "");
            }

            return new([], -1, "");
        }

        void Visit(OpenXmlElement element)
        {
            switch (element)
            {
                // The VML twin of a drawing: its runs are numbered like any others, but listing its
                // revisions would show every change in a text box twice.
                case AlternateContentFallback:
                    fallbackDepth++;
                    VisitChildren(element);
                    fallbackDepth--;
                    return;

                case TextBoxContent:
                    flows.Push(new());
                    VisitChildren(element);
                    flows.Pop();
                    return;

                case Paragraph paragraph:
                    Flow.Marks.Clear();
                    Flow.HasRun = false;
                    VisitChildren(paragraph);
                    EndParagraph();
                    return;

                case OoxmlRun run:
                    VisitRun(run);
                    VisitChildren(run);
                    return;
            }

            if (fallbackDepth == 0)
            {
                VisitMarkup(element);
            }

            VisitChildren(element);
        }

        void VisitChildren(OpenXmlElement element)
        {
            foreach (var child in element.ChildElements)
            {
                Visit(child);
            }
        }

        void VisitMarkup(OpenXmlElement element)
        {
            switch (element)
            {
                case CommentRangeStart start when Tracked && start.Id?.Value is { } id:
                    openComments[id] = new()
                    {
                        Start = lastRun + 1
                    };
                    break;

                case CommentRangeEnd end when Tracked && end.Id?.Value is { } id:
                    if (openComments.Remove(id, out var closed))
                    {
                        comments.TryAdd(id, closed);
                    }

                    break;

                case CommentReference reference when Tracked && reference.Id?.Value is { } id:
                    references.TryAdd(id, Math.Max(0, lastRun));
                    break;

                case MoveFromRangeStart start:
                    OpenMove(start, from: true);
                    break;

                case MoveToRangeStart start:
                    OpenMove(start, from: false);
                    break;

                case MoveFromRangeEnd end:
                    CloseMove(end, from: true);
                    break;

                case MoveToRangeEnd end:
                    CloseMove(end, from: false);
                    break;

                case InsertedRun or DeletedRun or MoveFromRun or MoveToRun:
                    StartContainer(element);
                    break;

                case Inserted or Deleted or MoveFrom or MoveTo when element.Parent is ParagraphMarkRunProperties:
                    Flow.Marks.Add(element);
                    break;

                case ParagraphMarkRunPropertiesChange:
                    Flow.Marks.Add(element);
                    break;

                case Inserted or Deleted when element.Parent is TableRowProperties {Parent: OoxmlTableRow row}:
                    RowChange(element, row);
                    break;

                case CellInsertion when element.Parent is OoxmlTableCellProperties {Parent: OoxmlTableCell cell}:
                    Standalone(element, ReviewChangeKind.CellInsertion, cell);
                    break;

                case CellDeletion when element.Parent is OoxmlTableCellProperties {Parent: OoxmlTableCell cell}:
                    Standalone(element, ReviewChangeKind.CellDeletion, cell);
                    break;

                case CellMerge when element.Parent is OoxmlTableCellProperties {Parent: OoxmlTableCell cell}:
                    Standalone(element, ReviewChangeKind.CellMerge, cell);
                    break;

                case RunPropertiesChange when element.Parent is {Parent: OoxmlRun run}:
                    RunFormatting(element, run);
                    break;

                case ParagraphPropertiesChange when element.Parent is {Parent: Paragraph paragraph}:
                    ParagraphFormatting(element, paragraph);
                    break;

                case NumberingChange when element.Ancestors<Paragraph>().FirstOrDefault() is { } paragraph:
                    ParagraphFormatting(element, paragraph);
                    break;

                case SectionPropertiesChange:
                    Standalone(element, ReviewChangeKind.SectionFormatting, element.Ancestors<Paragraph>().FirstOrDefault());
                    break;

                case TablePropertiesChange or TableGridChange:
                    Standalone(element, ReviewChangeKind.TableFormatting, element.Ancestors<Table>().FirstOrDefault());
                    break;

                case TablePropertyExceptionsChange or TableRowPropertiesChange:
                    Standalone(element, ReviewChangeKind.TableFormatting, element.Ancestors<OoxmlTableRow>().FirstOrDefault());
                    break;

                case TableCellPropertiesChange:
                    Standalone(element, ReviewChangeKind.TableFormatting, element.Ancestors<OoxmlTableCell>().FirstOrDefault());
                    break;
            }
        }

        Builder NewBuilder(ReviewChangeKind kind, OpenXmlElement element)
        {
            var builder = new Builder(kind, RevisionElements.Author(element), RevisionElements.Date(element), ordinals[element])
            {
                Point = lastRun + 1
            };
            builders.Add(builder);
            return builder;
        }

        void Add(Builder builder, OpenXmlElement element)
        {
            builder.Elements.Add(ordinals[element]);
            builderOf[element] = builder;
        }

        static bool SameHand(Builder builder, ReviewChangeKind kind, OpenXmlElement element) =>
            builder.Kind == kind &&
            string.Equals(builder.Author, RevisionElements.Author(element), StringComparison.Ordinal);

        static bool SameEdit(Builder builder, OpenXmlElement element) =>
            string.Equals(builder.Author, RevisionElements.Author(element), StringComparison.Ordinal) &&
            builder.Date == RevisionElements.Date(element);

        void StartContainer(OpenXmlElement element)
        {
            if (element is MoveFromRun or MoveToRun)
            {
                Add(MoveBuilder(element, element is MoveFromRun), element);
                Flow.Current = null;
                return;
            }

            var kind = element is InsertedRun ? ReviewChangeKind.Insertion : ReviewChangeKind.Deletion;

            // A revision inside another — text one reviewer inserted and another deleted — is a change
            // of its own and leaves the one around it to carry on.
            if (element.Ancestors().TakeWhile(_ => _ is not Paragraph).Any(RevisionElements.IsContainer))
            {
                Add(NewBuilder(kind, element), element);
                return;
            }

            if (Flow.Current is not { } current ||
                !SameHand(current, kind, element))
            {
                Flow.Current = current = NewBuilder(kind, element);
            }

            Add(current, element);
        }

        // A move's halves are tied by the name on the range each sits in. One written without its range
        // stands alone, as the move it says it is.
        Builder MoveBuilder(OpenXmlElement element, bool from)
        {
            var name = from ? Flow.MoveFromName : Flow.MoveToName;
            name ??= $"\0{unnamedMoves++}";
            if (!moves.TryGetValue(name, out var builder))
            {
                moves[name] = builder = NewBuilder(ReviewChangeKind.Move, element);
            }

            if (!from && !builder.TextFromDestination)
            {
                // The destination is where the text now is: it names the change's place and its text.
                builder.TextFromDestination = true;
                builder.Text.Clear();
                builder.Point = lastRun + 1;
            }

            return builder;
        }

        void OpenMove(OpenXmlElement start, bool from)
        {
            if (RevisionElements.Id(start) is not { } id ||
                RevisionElements.Attribute(start, "name") is not { } name)
            {
                return;
            }

            Flow.MoveRanges[(from ? "f" : "t") + id] = name;
            if (from)
            {
                Flow.MoveFromName = name;
            }
            else
            {
                Flow.MoveToName = name;
            }

            if (!moves.TryGetValue(name, out var builder))
            {
                moves[name] = builder = NewBuilder(ReviewChangeKind.Move, start);
            }

            Add(builder, start);
        }

        void CloseMove(OpenXmlElement end, bool from)
        {
            if (RevisionElements.Id(end) is not { } id ||
                !Flow.MoveRanges.Remove((from ? "f" : "t") + id, out var name))
            {
                return;
            }

            if (from)
            {
                Flow.MoveFromName = null;
            }
            else
            {
                Flow.MoveToName = null;
            }

            if (moves.TryGetValue(name, out var builder))
            {
                Add(builder, end);
            }
        }

        void VisitRun(OoxmlRun run)
        {
            var ordinal = runOrdinals[run];
            lastRun = Math.Max(lastRun, ordinal);
            if (fallbackDepth > 0)
            {
                return;
            }

            Flow.LastRun = ordinal;
            Flow.HasRun = true;
            var text = SourceRuns.Text(run);
            foreach (var range in openComments.Values)
            {
                var paragraph = run.Ancestors<Paragraph>().FirstOrDefault();
                if (range.LastParagraph != null &&
                    !ReferenceEquals(range.LastParagraph, paragraph) &&
                    range.Quote.Length > 0)
                {
                    range.Quote.Append('\n');
                }

                range.LastParagraph = paragraph;
                range.Runs.Add(ordinal);
                range.Quote.Append(text);
            }

            var revised = false;
            for (var parent = run.Parent; parent != null && parent is not Paragraph; parent = parent.Parent)
            {
                if (!builderOf.TryGetValue(parent, out var builder) ||
                    !RevisionElements.IsContainer(parent))
                {
                    continue;
                }

                revised = true;
                builder.Runs.Add(ordinal);
                if (parent is MoveToRun && builder.Destination < 0)
                {
                    builder.Destination = ordinal;
                }

                // A move's source half repeats the text its destination reads.
                if (parent is not MoveFromRun || !builder.TextFromDestination)
                {
                    builder.Text.Append(text);
                }
            }

            if (!revised && HasContent(run, text))
            {
                Flow.Current = null;
            }
        }

        static bool HasContent(OoxmlRun run, string text) =>
            text.Length > 0 ||
            run.ChildElements.Any(_ => _ is Break or Drawing or Picture or DocumentFormat.OpenXml.Wordprocessing.EmbeddedObject or SymbolChar);

        // The revisions on the paragraph's mark take their place here, after its runs.
        void EndParagraph()
        {
            if (fallbackDepth > 0)
            {
                return;
            }

            var flow = Flow;
            var markRevised = false;
            foreach (var mark in flow.Marks)
            {
                switch (mark)
                {
                    case ParagraphMarkRunPropertiesChange:
                        MarkFormatting(mark);
                        break;

                    case MoveFrom or MoveTo:
                        markRevised = true;
                        var move = MoveBuilder(mark, mark is MoveFrom);
                        Add(move, mark);
                        if (mark is MoveTo || !move.TextFromDestination)
                        {
                            move.Text.Append('\n');
                        }

                        flow.Current = null;
                        break;

                    default:
                        markRevised = true;
                        var kind = mark is Inserted ? ReviewChangeKind.Insertion : ReviewChangeKind.Deletion;
                        if (flow.Current is not { } current ||
                            !SameHand(current, kind, mark))
                        {
                            flow.Current = current = NewBuilder(kind, mark);
                            if (flow.HasRun)
                            {
                                current.Point = flow.LastRun;
                            }
                        }

                        Add(current, mark);
                        current.Text.Append('\n');
                        break;
                }
            }

            flow.Marks.Clear();

            // An untouched paragraph mark stands between the revisions either side of it.
            if (!markRevised)
            {
                flow.Current = null;
            }
        }

        // Formatting a stretch of text leaves a w:rPrChange in every run of it, and in the paragraph
        // mark of every paragraph it crosses; one edit, so one change.
        void RunFormatting(OpenXmlElement element, OoxmlRun run)
        {
            var ordinal = runOrdinals[run];
            var flow = Flow;
            if (flow.Formatting is not { } formatting ||
                !SameEdit(formatting, element) ||
                ordinal > flow.FormattingRun + 1)
            {
                flow.Formatting = formatting = NewBuilder(ReviewChangeKind.Formatting, element);
            }

            flow.FormattingRun = ordinal;
            Add(formatting, element);
            formatting.Runs.Add(ordinal);
            formatting.Text.Append(SourceRuns.Text(run));
        }

        void MarkFormatting(OpenXmlElement element)
        {
            var flow = Flow;
            if (flow.Formatting is not { } formatting ||
                !SameEdit(formatting, element) ||
                !flow.HasRun ||
                flow.FormattingRun != flow.LastRun)
            {
                flow.Formatting = formatting = NewBuilder(ReviewChangeKind.Formatting, element);
                flow.FormattingRun = flow.HasRun ? flow.LastRun : lastRun;
                if (flow.HasRun)
                {
                    formatting.Point = flow.LastRun;
                }
            }

            Add(formatting, element);
            formatting.Text.Append('\n');
        }

        void ParagraphFormatting(OpenXmlElement element, Paragraph paragraph)
        {
            var flow = Flow;
            if (flow.ParagraphFormatting is not { } formatting ||
                !SameEdit(formatting, element) ||
                (!ReferenceEquals(flow.FormattedParagraph, paragraph) &&
                 !ReferenceEquals(flow.FormattedParagraph, paragraph.PreviousSibling())))
            {
                flow.ParagraphFormatting = formatting = NewBuilder(ReviewChangeKind.ParagraphFormatting, element);
            }

            Add(formatting, element);
            if (!ReferenceEquals(flow.FormattedParagraph, paragraph))
            {
                Cover(formatting, paragraph);
            }

            flow.FormattedParagraph = paragraph;
        }

        void RowChange(OpenXmlElement element, OoxmlTableRow row)
        {
            var kind = element is Inserted ? ReviewChangeKind.RowInsertion : ReviewChangeKind.RowDeletion;
            var flow = Flow;
            if (flow.Rows is not { } rows ||
                !SameHand(rows, kind, element) ||
                !ReferenceEquals(flow.ChangedRow, row.PreviousSibling()))
            {
                flow.Rows = rows = NewBuilder(kind, element);
            }

            flow.ChangedRow = row;
            Add(rows, element);
            Cover(rows, row);
        }

        void Standalone(OpenXmlElement element, ReviewChangeKind kind, OpenXmlElement? scope)
        {
            var builder = NewBuilder(kind, element);
            Add(builder, element);
            if (scope != null)
            {
                Cover(builder, scope);
            }
        }

        // Ties a change to the runs of the paragraph, row, cell or table it applies to.
        void Cover(Builder builder, OpenXmlElement scope)
        {
            // What the change already covers — the row before this one — reads as a line of its own.
            if (builder.Text.Length > 0 &&
                builder.Text[^1] != '\n')
            {
                builder.Text.Append('\n');
            }

            Paragraph? last = null;
            foreach (var run in scope.Descendants<OoxmlRun>())
            {
                if (run.Ancestors<AlternateContentFallback>().Any())
                {
                    continue;
                }

                var paragraph = run.Ancestors<Paragraph>().FirstOrDefault();
                if (last != null &&
                    !ReferenceEquals(last, paragraph) &&
                    builder.Text.Length > 0)
                {
                    builder.Text.Append('\n');
                }

                last = paragraph;
                builder.Runs.Add(runOrdinals[run]);
                builder.Text.Append(SourceRuns.Text(run));
            }
        }
    }
}
