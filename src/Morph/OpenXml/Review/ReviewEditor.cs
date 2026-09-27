using OoxmlComment = DocumentFormat.OpenXml.Wordprocessing.Comment;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlFieldCode = DocumentFormat.OpenXml.Wordprocessing.FieldCode;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using OoxmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OoxmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using W15 = DocumentFormat.OpenXml.Office2013.Word;
using W16Cid = DocumentFormat.OpenXml.Office2019.Word.Cid;
using W16Cex = DocumentFormat.OpenXml.Office2021.Word.CommentsExt;

/// <summary>
/// The edits a reviewer makes: accepting and rejecting tracked changes, and adding, answering, rewording,
/// resolving and deleting comments. Each takes a document's bytes and returns the edited document's;
/// the input is never touched, which is what makes undo a matter of keeping the old array.
///
/// Changes are named by the keys <see cref="DocumentReview"/> read from the same bytes, and places by
/// <see cref="SourcePosition"/>s in the coordinates of <see cref="SourceRuns"/>.
/// </summary>
static class ReviewEditor
{
    const string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string commentTextStyle = "CommentText";
    const string commentReferenceStyle = "CommentReference";

    /// <summary>Accepts or rejects the changes with the given keys.</summary>
    public static byte[] Resolve(byte[] docx, IEnumerable<string> keys, bool accept)
    {
        var ordinalsByPart = new Dictionary<int, HashSet<int>>();
        foreach (var key in keys)
        {
            if (!RevisionElements.TryParseKey(key, out var part, out var ordinals))
            {
                continue;
            }

            if (!ordinalsByPart.TryGetValue(part, out var set))
            {
                ordinalsByPart[part] = set = [];
            }

            set.UnionWith(ordinals);
        }

        return Edit(docx, document => Resolve(document, ordinalsByPart, accept));
    }

    /// <summary>Accepts or rejects every change in the document.</summary>
    public static byte[] ResolveAll(byte[] docx, bool accept) =>
        Edit(docx, document => Resolve(document, null, accept));

    /// <summary>
    /// Attaches a new comment to the text from <paramref name="start"/> to <paramref name="end"/>,
    /// splitting the runs those fall inside.
    /// </summary>
    public static byte[] AddComment(byte[] docx, SourcePosition start, SourcePosition end, string author, string text, DateTimeOffset date) =>
        Edit(docx, document => AddComment(document, start, end, author, text, date));

    /// <summary>Answers a comment. A reply to a reply joins the same thread: Word's threads are flat.</summary>
    public static byte[] Reply(byte[] docx, string commentId, string author, string text, DateTimeOffset date) =>
        Edit(docx, document => Reply(document, commentId, author, text, date));

    /// <summary>Replaces a comment's text, keeping its author, its date and its place in its thread.</summary>
    public static byte[] EditComment(byte[] docx, string commentId, string text) =>
        Edit(docx, document => EditComment(document, commentId, text));

    /// <summary>Marks the thread a comment belongs to as resolved, or reopens it.</summary>
    public static byte[] SetResolved(byte[] docx, string commentId, bool resolved) =>
        Edit(docx, document => SetResolved(document, commentId, resolved));

    /// <summary>Deletes a comment — with its replies, when it starts a thread.</summary>
    public static byte[] DeleteComment(byte[] docx, string commentId) =>
        Edit(docx, document => DeleteComment(document, commentId));

    static byte[] Edit(byte[] docx, Action<WordprocessingDocument> edit)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            edit(document);
        }

        return stream.ToArray();
    }

    // Tracked changes

    // What an edit removed along the way, to tidy up after.
    sealed class Removals
    {
        public HashSet<string> CommentIds { get; } = new(StringComparer.Ordinal);
    }

    static void Resolve(WordprocessingDocument document, Dictionary<int, HashSet<int>>? ordinalsByPart, bool accept)
    {
        var removals = new Removals();
        var roots = RevisionElements.Roots(document);
        for (var index = 0; index < roots.Count; index++)
        {
            var root = roots[index].Root;
            var targets = RevisionElements.Enumerate(root).ToList();
            if (ordinalsByPart != null)
            {
                if (!ordinalsByPart.TryGetValue(index, out var ordinals))
                {
                    continue;
                }

                targets = targets.Where((_, ordinal) => ordinals.Contains(ordinal)).ToList();
            }

            // In file order. Settling one revision can take others with it — the deletion inside a
            // rejected insertion, the formatting change on a paragraph whose mark went — and those
            // are no longer part of the document when their turn comes.
            foreach (var target in targets)
            {
                if (!RevisionElements.IsAttached(target, root))
                {
                    continue;
                }

                if (RevisionElements.IsParagraphMark(target))
                {
                    ApplyMark(target, accept);
                }
                else
                {
                    Apply(target, accept, removals);
                }
            }
        }

        DropOrphanedComments(document, removals);
    }

    static void Apply(OpenXmlElement element, bool accept, Removals removals)
    {
        switch (element)
        {
            case InsertedRun or MoveToRun:
                if (accept)
                {
                    Unwrap(element);
                }
                else
                {
                    Remove(element, removals);
                }

                break;

            case DeletedRun or MoveFromRun:
                if (accept)
                {
                    Remove(element, removals);
                }
                else
                {
                    RestoreDeletedText(element);
                    Unwrap(element);
                }

                break;

            case MoveFromRangeStart or MoveFromRangeEnd or MoveToRangeStart or MoveToRangeEnd or NumberingChange:
                element.Remove();
                break;

            case Inserted or Deleted when element.Parent is TableRowProperties {Parent: OoxmlTableRow row}:
                element.Remove();
                if ((element is Inserted) != accept)
                {
                    RemoveRow(row, removals);
                }

                break;

            case CellInsertion or CellDeletion when element.Parent is OoxmlTableCellProperties {Parent: OoxmlTableCell cell}:
                element.Remove();
                if ((element is CellInsertion) != accept)
                {
                    RemoveCell(cell, removals);
                }

                break;

            case CellMerge merge:
                if (!accept)
                {
                    RestoreMerge(merge);
                }

                element.Remove();
                break;

            case RunPropertiesChange or
                ParagraphMarkRunPropertiesChange or
                ParagraphPropertiesChange or
                SectionPropertiesChange or
                TablePropertiesChange or
                TablePropertyExceptionsChange or
                TableGridChange or
                TableRowPropertiesChange or
                TableCellPropertiesChange:
                if (accept)
                {
                    element.Remove();
                }
                else
                {
                    RestorePrevious(element);
                }

                break;
        }
    }

    // A mark that goes — a deletion accepted, an insertion rejected — takes the paragraph break with
    // it; one that stays only loses its revision.
    static void ApplyMark(OpenXmlElement mark, bool accept)
    {
        var paragraph = mark.Ancestors<Paragraph>().FirstOrDefault();
        var goes = (mark is Deleted or MoveFrom) == accept;
        mark.Remove();
        if (goes && paragraph != null)
        {
            MergeWithNext(paragraph);
        }
    }

    static void Unwrap(OpenXmlElement container)
    {
        var parent = container.Parent!;
        foreach (var child in container.ChildElements.ToList())
        {
            child.Remove();
            parent.InsertBefore(child, container);
        }

        container.Remove();
    }

    // Takes content out of the document. A comment whose reference mark is in it goes with it
    // (DropOrphanedComments). One that only starts or ends in it keeps the rest of its range: the mark
    // moves out to where the content was, rather than leave a range with one end.
    static void Remove(OpenXmlElement element, Removals removals)
    {
        foreach (var descendant in element.Descendants().ToList())
        {
            switch (descendant)
            {
                case CommentReference {Id.Value: { } id}:
                    removals.CommentIds.Add(id);
                    break;
                case CommentRangeStart or CommentRangeEnd:
                    descendant.Remove();
                    element.InsertBeforeSelf(descendant);
                    break;
            }
        }

        element.Remove();
    }

    // A deletion's text is w:delText (and a deleted field's code w:delInstrText); text that is back
    // in the document is w:t again.
    static void RestoreDeletedText(OpenXmlElement container)
    {
        foreach (var deleted in container.Descendants<DeletedText>().ToList())
        {
            var text = new Text(deleted.Text);
            if (deleted.Space?.Value is { } space)
            {
                text.Space = space;
            }

            deleted.Parent!.ReplaceChild(text, deleted);
        }

        foreach (var deleted in container.Descendants<DeletedFieldCode>().ToList())
        {
            var code = new OoxmlFieldCode(deleted.Text);
            if (deleted.Space?.Value is { } space)
            {
                code.Space = space;
            }

            deleted.Parent!.ReplaceChild(code, deleted);
        }
    }

    // Puts back the properties a change replaced. The change element holds them, less what the
    // schema keeps beside it rather than inside it — a paragraph's mark properties and section break,
    // a section's header and footer references, the revision marks on a mark, a row or a cell —
    // which therefore stay as they are. Where those belong relative to the restored properties
    // follows the schema's order.
    static void RestorePrevious(OpenXmlElement change)
    {
        var properties = change.Parent!;
        var keptFirst = change is ParagraphMarkRunPropertiesChange or SectionPropertiesChange;
        foreach (var child in properties.ChildElements.ToList())
        {
            if (!ReferenceEquals(child, change) && !Stays(change, child))
            {
                child.Remove();
            }
        }

        var restored = change.FirstChild?.ChildElements.Select(_ => _.CloneNode(true)).ToList() ?? [];
        if (keptFirst)
        {
            foreach (var property in restored)
            {
                properties.InsertBefore(property, change);
            }
        }
        else
        {
            for (var index = restored.Count - 1; index >= 0; index--)
            {
                properties.PrependChild(restored[index]);
            }
        }

        change.Remove();
    }

    static bool Stays(OpenXmlElement change, OpenXmlElement property) =>
        change switch
        {
            ParagraphPropertiesChange => property is ParagraphMarkRunProperties or SectionProperties,
            ParagraphMarkRunPropertiesChange => property is Inserted or Deleted or MoveFrom or MoveTo,
            SectionPropertiesChange => property is HeaderReference or FooterReference,
            TableRowPropertiesChange => property is Inserted or Deleted,
            TableCellPropertiesChange => property is CellInsertion or CellDeletion or CellMerge,
            _ => false
        };

    // w:cellMerge records the cell's vertical merge before the change in w:vMergeOrig.
    static void RestoreMerge(CellMerge merge)
    {
        if (merge.Parent is not OoxmlTableCellProperties properties)
        {
            return;
        }

        properties.RemoveAllChildren<VerticalMerge>();
        if (merge.VerticalMergeOriginal?.Value is not { } original)
        {
            return;
        }

        var restored = new VerticalMerge();
        if (original == VerticalMergeRevisionValues.Restart)
        {
            restored.Val = MergedCellValues.Restart;
        }

        properties.InsertBefore(restored, merge);
    }

    static void RemoveRow(OoxmlTableRow row, Removals removals)
    {
        var table = row.Parent;
        Remove(row, removals);
        if (table is Table &&
            !table.Elements<OoxmlTableRow>().Any())
        {
            RemoveTable(table);
        }
    }

    static void RemoveCell(OoxmlTableCell cell, Removals removals)
    {
        var row = cell.Parent;
        Remove(cell, removals);
        if (row is OoxmlTableRow emptied &&
            !emptied.Elements<OoxmlTableCell>().Any())
        {
            RemoveRow(emptied, removals);
        }
    }

    static void RemoveTable(OpenXmlElement table)
    {
        var parent = table.Parent;
        table.Remove();

        // A cell has to end in a paragraph; the table that went may have been all that followed one.
        if (parent is OoxmlTableCell cell &&
            cell.LastChild is not Paragraph)
        {
            cell.AppendChild(new Paragraph());
        }
    }

    // Removes the break at the end of a paragraph, which joins it to the paragraph after. A paragraph's
    // formatting lives in its mark, so the joined paragraph keeps the FOLLOWING paragraph's: that is
    // the mark left standing. The first paragraph's content moves to the head of the second, and
    // everything that hung off the first one's mark — its properties, a revision on the mark, a section
    // break — goes with the mark.
    //
    // Word-probed 2026-09-27 (Word 16, Revisions.AcceptAll / RejectAll over COM): a centred "first "
    // whose mark is a tracked deletion, followed by a right-aligned "second", is one RIGHT-aligned
    // paragraph "first second" once the deletion is accepted, and the same pair with the mark a tracked
    // insertion is the same paragraph once it is rejected. The opposite rule — the first paragraph's
    // formatting standing for both — was written first and was wrong.
    static void MergeWithNext(Paragraph paragraph)
    {
        if (NextBlock(paragraph) is not Paragraph next)
        {
            return;
        }

        var after = (OpenXmlElement?) next.ParagraphProperties;
        foreach (var child in paragraph.ChildElements.Where(_ => _ is not OoxmlParagraphProperties).ToList())
        {
            child.Remove();
            if (after == null)
            {
                next.PrependChild(child);
            }
            else
            {
                next.InsertAfter(child, after);
            }

            after = child;
        }

        paragraph.Remove();
    }

    // The block after a paragraph, looking past the range marks that can sit between blocks.
    static OpenXmlElement? NextBlock(OpenXmlElement element)
    {
        var next = element.NextSibling();
        while (next != null && IsRangeMark(next))
        {
            next = next.NextSibling();
        }

        return next;
    }

    static bool IsRangeMark(OpenXmlElement element) =>
        element is
            BookmarkStart or BookmarkEnd or
            CommentRangeStart or CommentRangeEnd or
            MoveFromRangeStart or MoveFromRangeEnd or MoveToRangeStart or MoveToRangeEnd or
            PermStart or PermEnd or
            ProofError;

    // A comment whose reference went with rejected or deleted text has nothing left to be about.
    static void DropOrphanedComments(WordprocessingDocument document, Removals removals)
    {
        if (removals.CommentIds.Count == 0 ||
            document.MainDocumentPart?.Document is not { } root)
        {
            return;
        }

        var referenced = root.Descendants<CommentReference>()
            .Select(_ => _.Id?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        foreach (var id in removals.CommentIds)
        {
            if (!referenced.Contains(id))
            {
                DeleteComment(document, id);
            }
        }
    }

    // Comments

    static void AddComment(WordprocessingDocument document, SourcePosition start, SourcePosition end, string author, string text, DateTimeOffset date)
    {
        var main = MainPart(document);
        var runs = main.Document!.Descendants<OoxmlRun>().ToList();
        if (runs.Count == 0)
        {
            throw new InvalidOperationException("The document has no text to comment on.");
        }

        if (end.Run < start.Run ||
            (end.Run == start.Run && end.Offset < start.Offset))
        {
            (start, end) = (end, start);
        }

        var startRun = runs[Math.Clamp(start.Run, 0, runs.Count - 1)];
        var endRun = runs[Math.Clamp(end.Run, 0, runs.Count - 1)];
        var id = NextCommentId(main);

        // The end first: splitting for the start leaves the end's run holding only the text before
        // the start, so the end has to have found its place by then.
        var rangeEnd = new CommentRangeEnd
        {
            Id = id
        };
        InsertAt(endRun, end.Offset, rangeEnd);
        AfterRevisions(rangeEnd).InsertAfterSelf(ReferenceRun(main, id));
        InsertAt(
            startRun,
            start.Offset,
            new CommentRangeStart
            {
                Id = id
            });

        AppendComment(main, id, author, text, date);
    }

    // Puts a range mark between two positions of a run: ahead of the run, after it, or in the split
    // made for it.
    static void InsertAt(OoxmlRun run, int offset, OpenXmlElement mark)
    {
        if (Split(run, offset) is { } tail)
        {
            tail.InsertBeforeSelf(mark);
        }
        else
        {
            run.InsertAfterSelf(mark);
        }
    }

    /// <summary>
    /// Divides a run so that one begins at <paramref name="offset"/>, and returns that run: the run
    /// itself when the offset is its start, null when it is its end, and otherwise a new run holding
    /// what followed the offset, with the same properties, placed after the original.
    /// </summary>
    internal static OoxmlRun? Split(OoxmlRun run, int offset)
    {
        if (offset <= 0)
        {
            return run;
        }

        if (offset >= SourceRuns.Length(run))
        {
            return null;
        }

        var tail = (OoxmlRun) run.CloneNode(false);
        if (run.RunProperties is { } properties)
        {
            tail.AppendChild(properties.CloneNode(true));
        }

        var position = 0;
        foreach (var child in run.ChildElements.ToList())
        {
            if (child is OoxmlRunProperties)
            {
                continue;
            }

            var length = SourceRuns.Length(child);
            if (position >= offset)
            {
                child.Remove();
                tail.AppendChild(child);
            }
            else if (position + length > offset &&
                     child is TextType text)
            {
                // Both halves keep their edge spaces: what the parser would have trimmed is already
                // gone from the text the offset counts.
                var content = SourceRuns.EffectiveText(text.Text, text.Space?.Value);
                var rest = (TextType) text.CloneNode(false);
                rest.Text = content[(offset - position)..];
                rest.Space = SpaceProcessingModeValues.Preserve;
                text.Text = content[..(offset - position)];
                text.Space = SpaceProcessingModeValues.Preserve;
                tail.AppendChild(rest);
            }

            position += length;
        }

        run.InsertAfterSelf(tail);
        return tail;
    }

    // A comment's reference mark is not part of the revision its range ends in: it goes after.
    static OpenXmlElement AfterRevisions(OpenXmlElement element)
    {
        var place = element;
        while (place.Parent is { } parent && RevisionElements.IsContainer(parent))
        {
            place = parent;
        }

        return place;
    }

    static OoxmlRun ReferenceRun(MainDocumentPart main, string id)
    {
        var run = new OoxmlRun();
        if (HasStyle(main, commentReferenceStyle))
        {
            run.AppendChild(
                new OoxmlRunProperties(
                    new RunStyle
                    {
                        Val = commentReferenceStyle
                    }));
        }

        run.AppendChild(
            new CommentReference
            {
                Id = id
            });
        return run;
    }

    static void Reply(WordprocessingDocument document, string commentId, string author, string text, DateTimeOffset date)
    {
        var main = MainPart(document);
        var thread = Thread(main, commentId);
        var root = thread[0];
        var rootId = root.Id!.Value!;
        var id = NextCommentId(main);

        // A reply covers exactly what its thread covers: its range marks go beside the thread's own,
        // and its reference mark after the last of the thread's.
        var body = main.Document!;
        if (body.Descendants<CommentRangeStart>().FirstOrDefault(_ => _.Id?.Value == rootId) is { } rangeStart &&
            body.Descendants<CommentRangeEnd>().FirstOrDefault(_ => _.Id?.Value == rootId) is { } rangeEnd)
        {
            rangeStart.InsertAfterSelf(
                new CommentRangeStart
                {
                    Id = id
                });
            rangeEnd.InsertAfterSelf(
                new CommentRangeEnd
                {
                    Id = id
                });
        }

        var threadIds = thread.Select(_ => _.Id!.Value!).ToHashSet(StringComparer.Ordinal);
        if (body.Descendants<CommentReference>().LastOrDefault(_ => _.Id?.Value is { } value && threadIds.Contains(value)) is {Parent: OoxmlRun lastReference})
        {
            lastReference.InsertAfterSelf(ReferenceRun(main, id));
        }

        var reply = AppendComment(main, id, author, text, date);
        var extended = Extended(main);
        var rootEntry = Entry(extended, root);
        var entry = Entry(extended, reply);
        entry.ParaIdParent = rootEntry.ParaId!.Value;
        entry.Done = rootEntry.Done?.Value == true;
    }

    static void EditComment(WordprocessingDocument document, string commentId, string text)
    {
        var main = MainPart(document);
        var comment = Find(main, commentId);

        // The thread knows the comment by its last paragraph's id, which the new text has to keep.
        var threadId = DocumentReview.ThreadParagraphId(comment);
        comment.RemoveAllChildren();
        AppendText(main, comment, text);
        if (threadId != null)
        {
            comment.Elements<Paragraph>().Last().ParagraphId = threadId;
        }
    }

    static void SetResolved(WordprocessingDocument document, string commentId, bool resolved)
    {
        var main = MainPart(document);
        var extended = Extended(main);
        foreach (var comment in Thread(main, commentId))
        {
            Entry(extended, comment).Done = resolved;
        }
    }

    static void DeleteComment(WordprocessingDocument document, string commentId)
    {
        var main = MainPart(document);
        if (main.WordprocessingCommentsPart?.Comments is not { } comments ||
            comments.Elements<OoxmlComment>().FirstOrDefault(_ => _.Id?.Value == commentId) is not { } comment)
        {
            RemoveMarks(main, [commentId]);
            return;
        }

        var thread = Thread(main, commentId);
        var doomed = ReferenceEquals(thread[0], comment) ? thread : [comment];
        var ids = doomed.Select(_ => _.Id!.Value!).ToHashSet(StringComparer.Ordinal);
        var paragraphIds = doomed
            .SelectMany(_ => _.Descendants<Paragraph>())
            .Select(_ => _.ParagraphId?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in main.WordprocessingCommentsExPart?.CommentsEx?.Elements<W15.CommentEx>().ToList() ?? [])
        {
            if (entry.ParaId?.Value is { } paraId && paragraphIds.Contains(paraId))
            {
                entry.Remove();
            }
        }

        var durableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in main.WordprocessingCommentsIdsPart?.CommentsIds?.Elements<W16Cid.CommentId>().ToList() ?? [])
        {
            if (entry.ParaId?.Value is { } paraId && paragraphIds.Contains(paraId))
            {
                if (entry.DurableId?.Value is { } durableId)
                {
                    durableIds.Add(durableId);
                }

                entry.Remove();
            }
        }

        foreach (var entry in main.WordCommentsExtensiblePart?.CommentsExtensible?.Elements<W16Cex.CommentExtensible>().ToList() ?? [])
        {
            if (entry.DurableId?.Value is { } durableId && durableIds.Contains(durableId))
            {
                entry.Remove();
            }
        }

        foreach (var item in doomed)
        {
            item.Remove();
        }

        RemoveMarks(main, ids);
    }

    static void RemoveMarks(MainDocumentPart main, HashSet<string> ids)
    {
        var body = main.Document!;
        foreach (var mark in body.Descendants().Where(_ => MarkId(_) is { } id && ids.Contains(id)).ToList())
        {
            if (mark is CommentReference {Parent: OoxmlRun run} &&
                run.ChildElements.All(_ => _ is OoxmlRunProperties || ReferenceEquals(_, mark)))
            {
                run.Remove();
            }
            else
            {
                mark.Remove();
            }
        }
    }

    static string? MarkId(OpenXmlElement element) =>
        element switch
        {
            CommentRangeStart start => start.Id?.Value,
            CommentRangeEnd end => end.Id?.Value,
            CommentReference reference => reference.Id?.Value,
            _ => null
        };

    static MainDocumentPart MainPart(WordprocessingDocument document)
    {
        if (document.MainDocumentPart is {Document: not null} main)
        {
            return main;
        }

        throw new InvalidOperationException("Document has no main part");
    }

    static OoxmlComment Find(MainDocumentPart main, string commentId)
    {
        var comment = main.WordprocessingCommentsPart?.Comments?
            .Elements<OoxmlComment>()
            .FirstOrDefault(_ => _.Id?.Value == commentId);
        if (comment != null)
        {
            return comment;
        }

        throw new InvalidOperationException($"The document has no comment '{commentId}'.");
    }

    // The thread a comment belongs to: the comment that started it, then the replies in file order.
    static List<OoxmlComment> Thread(MainDocumentPart main, string commentId)
    {
        var comment = Find(main, commentId);
        var all = main.WordprocessingCommentsPart!.Comments!.Elements<OoxmlComment>().ToList();
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in main.WordprocessingCommentsExPart?.CommentsEx?.Elements<W15.CommentEx>() ?? [])
        {
            if (entry.ParaId?.Value is { } paraId &&
                entry.ParaIdParent?.Value is { } parent)
            {
                parents[paraId] = parent;
            }
        }

        string? RootOf(OoxmlComment item)
        {
            var paraId = DocumentReview.ThreadParagraphId(item);

            // Bounded: a malformed file could name parents in a circle.
            for (var depth = 0; paraId != null && depth < all.Count && parents.TryGetValue(paraId, out var parent); depth++)
            {
                paraId = parent;
            }

            return paraId;
        }

        var rootId = RootOf(comment);
        if (rootId == null)
        {
            return [comment];
        }

        var thread = all
            .Where(_ => string.Equals(RootOf(_), rootId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => !string.Equals(DocumentReview.ThreadParagraphId(_), rootId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (thread.Count == 0)
        {
            return [comment];
        }

        return thread;
    }

    static W15.CommentsEx Extended(MainDocumentPart main)
    {
        var part = main.WordprocessingCommentsExPart ?? main.AddNewPart<WordprocessingCommentsExPart>();
        if (part.CommentsEx is { } existing)
        {
            return existing;
        }

        var created = new W15.CommentsEx();
        part.CommentsEx = created;
        return created;
    }

    // The commentsExtended entry for a comment, made if it has none — which means giving the
    // comment's last paragraph the id the entry names it by, if it has none either.
    static W15.CommentEx Entry(W15.CommentsEx extended, OoxmlComment comment)
    {
        var paragraph = comment.Elements<Paragraph>().LastOrDefault();
        if (paragraph == null)
        {
            paragraph = new();
            comment.AppendChild(paragraph);
        }

        var comments = (Comments) comment.Parent!;
        var paraId = paragraph.ParagraphId?.Value;
        if (paraId == null)
        {
            paraId = NextParagraphId(comments, comment.Id?.Value);
            paragraph.ParagraphId = paraId;
        }

        var entry = extended.Elements<W15.CommentEx>()
            .FirstOrDefault(_ => string.Equals(_.ParaId?.Value, paraId, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            return entry;
        }

        entry = new()
        {
            ParaId = paraId,
            Done = false
        };
        extended.AppendChild(entry);
        return entry;
    }

    static OoxmlComment AppendComment(MainDocumentPart main, string id, string author, string text, DateTimeOffset date)
    {
        var part = main.WordprocessingCommentsPart ?? main.AddNewPart<WordprocessingCommentsPart>();
        var comments = part.Comments;
        if (comments == null)
        {
            comments = new();
            part.Comments = comments;
        }

        var comment = new OoxmlComment
        {
            Id = id,
            Author = author,
            Initials = Initials(author)
        };

        // Word's own form: the author's clock, stamped Z though it is not UTC. Word reads it back at
        // face value, so the UTC time here would show as hours ago to anyone east of Greenwich.
        comment.SetAttribute(new("w", "date", wordNamespace, Stamp(date.DateTime)));
        comments.AppendChild(comment);
        AppendText(main, comment, text);

        // A document that keeps thread state keeps it for every comment.
        if (main.WordprocessingCommentsExPart?.CommentsEx is { } extended)
        {
            Entry(extended, comment);
        }

        StampUtc(main, comment, date);
        return comment;
    }

    static string Stamp(DateTime date) =>
        date.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // The moment itself, which w:date cannot say. Word keeps it apart, as it does thread state:
    // commentsIds gives the comment's last paragraph a durable id, and commentsExtensible dates that.
    static void StampUtc(MainDocumentPart main, OoxmlComment comment, DateTimeOffset date)
    {
        if (DocumentReview.ThreadParagraphId(comment) is not { } paraId)
        {
            return;
        }

        var idsPart = main.WordprocessingCommentsIdsPart ?? main.AddNewPart<WordprocessingCommentsIdsPart>();
        var ids = idsPart.CommentsIds;
        if (ids == null)
        {
            ids = new();
            idsPart.CommentsIds = ids;
        }

        var extensiblePart = main.WordCommentsExtensiblePart ?? main.AddNewPart<WordCommentsExtensiblePart>();
        var extensible = extensiblePart.CommentsExtensible;
        if (extensible == null)
        {
            extensible = new();
            extensiblePart.CommentsExtensible = extensible;
        }

        var durableId = NextDurableId(ids, extensible, comment.Id?.Value);
        ids.AppendChild(
            new W16Cid.CommentId
            {
                ParaId = paraId,
                DurableId = durableId
            });
        extensible.AppendChild(
            new W16Cex.CommentExtensible
            {
                DurableId = durableId,
                DateUtc = new()
                {
                    InnerText = Stamp(date.UtcDateTime)
                }
            });
    }

    // A paragraph per line; the first carries the mark that ties the comment to its reference.
    static void AppendText(MainDocumentPart main, OoxmlComment comment, string text)
    {
        var comments = (Comments) comment.Parent!;
        var styled = HasStyle(main, commentTextStyle);
        var first = true;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var paragraph = new Paragraph();
            comment.AppendChild(paragraph);
            paragraph.ParagraphId = NextParagraphId(comments, comment.Id?.Value);
            if (styled)
            {
                paragraph.AppendChild(
                    new OoxmlParagraphProperties(
                        new ParagraphStyleId
                        {
                            Val = commentTextStyle
                        }));
            }

            if (first)
            {
                var mark = new OoxmlRun();
                if (HasStyle(main, commentReferenceStyle))
                {
                    mark.AppendChild(
                        new OoxmlRunProperties(
                            new RunStyle
                            {
                                Val = commentReferenceStyle
                            }));
                }

                mark.AppendChild(new AnnotationReferenceMark());
                paragraph.AppendChild(mark);
                first = false;
            }

            if (line.Length > 0)
            {
                paragraph.AppendChild(
                    new OoxmlRun(
                        new Text(line)
                        {
                            Space = SpaceProcessingModeValues.Preserve
                        }));
            }
        }
    }

    static bool HasStyle(MainDocumentPart main, string styleId) =>
        main.StyleDefinitionsPart?.Styles?.Elements<Style>().Any(_ => _.StyleId?.Value == styleId) == true;

    // One more than the highest id in use, by a comment or by a mark left behind by one.
    static string NextCommentId(MainDocumentPart main)
    {
        var highest = -1L;
        var ids = (main.WordprocessingCommentsPart?.Comments?.Elements<OoxmlComment>().Select(_ => _.Id?.Value) ?? [])
            .Concat(main.Document!.Descendants().Select(MarkId));
        foreach (var id in ids)
        {
            if (long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                highest = Math.Max(highest, value);
            }
        }

        return (highest + 1).ToString(CultureInfo.InvariantCulture);
    }

    // A w14:paraId is eight hex digits below 0x80000000, unique within its part. Derived from the
    // comment's id rather than drawn at random, so the same edit to the same file writes the same bytes.
    static string NextParagraphId(Comments comments, string? commentId) =>
        NextHexId(
            comments.Descendants<Paragraph>().Select(_ => _.ParagraphId?.Value),
            commentId,
            0x10000000L);

    // A durable id has a paragraph id's form, and the same bound.
    static string NextDurableId(W16Cid.CommentsIds ids, W16Cex.CommentsExtensible extensible, string? commentId) =>
        NextHexId(
            ids.Elements<W16Cid.CommentId>().Select(_ => _.DurableId?.Value)
                .Concat(extensible.Elements<W16Cex.CommentExtensible>().Select(_ => _.DurableId?.Value)),
            commentId,
            0x20000000L);

    static string NextHexId(IEnumerable<string?> taken, string? commentId, long origin)
    {
        var used = taken
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        long.TryParse(commentId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed);
        var candidate = origin + (Math.Abs(seed) % 0x00FFFFFF) * 0x10;
        while (true)
        {
            var id = (candidate % 0x7FFFFFFF).ToString("X8", CultureInfo.InvariantCulture);
            if (!used.Contains(id))
            {
                return id;
            }

            candidate++;
        }
    }

    static string Initials(string author)
    {
        var initials = new StringBuilder();
        foreach (var word in author.Split([' ', '\t', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (char.IsLetterOrDigit(word[0]))
            {
                initials.Append(char.ToUpperInvariant(word[0]));
            }

            if (initials.Length == 3)
            {
                break;
            }
        }

        return initials.ToString();
    }
}
