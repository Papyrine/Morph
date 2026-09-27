namespace Morph;

// Review: a Word document's comments and tracked changes, read and edited in a pane beside the pages.
//
// The document stays a file throughout. Every edit is made to its bytes (ReviewEditor), the result is
// parsed and laid out like any file that is opened, and the script swaps the new pages in under the
// reader. That costs a layout per edit and buys the one thing a viewer must not get wrong: what is on
// screen is exactly what the downloaded file holds. Undo is the previous bytes.
public partial class MorphViewer
{
    /// <summary>
    /// Whether the toolbar offers the review pane, which lists a Word document's comments and tracked
    /// changes. Default true.
    /// </summary>
    [Parameter]
    public bool ShowReview { get; set; } = true;

    /// <summary>
    /// Whether the review pane is open from the start for a document that has comments or tracked
    /// changes. Default false.
    /// </summary>
    [Parameter]
    public bool OpenReview { get; set; }

    /// <summary>
    /// Whether the document can only be read. The review pane still lists comments and tracked changes;
    /// it offers no way to change them. Default false. A document's own protection is honoured either way.
    /// </summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// The name new comments are signed with. Left null, the pane asks the reader for theirs and signs
    /// with "Guest" until they give one.
    /// </summary>
    [Parameter]
    public string? Author { get; set; }

    /// <summary>
    /// Called with the file after every edit — a comment added, a change accepted, an undo. The same
    /// bytes Download saves. Handing them back as <see cref="Source"/> does not reopen the file.
    /// </summary>
    [Parameter]
    public EventCallback<byte[]> OnDocumentChanged { get; set; }

    const string guest = "Guest";

    // Undo keeps whole files, so it is bounded by what they weigh rather than by how many there are.
    const long undoBudget = 64L * 1024 * 1024;

    const int excerptLength = 280;

    ElementReference draftInput;
    DocumentReview review = DocumentReview.Empty;
    ReviewMap reviewMap = ReviewMap.Empty;
    string[] pageTexts = [];
    IReadOnlyList<ReviewEntry> entries = [];
    ReviewFilter reviewFilter;
    bool reviewOpen;
    bool revising;
    string? activeKey;
    string? revealKey;
    string? reviewNote;
    string authorName = "";
    ReviewDraft? draft;
    bool focusDraft;

    // What the reader has selected on the pages, as the script last reported it: first page, start,
    // last page, end — or empty.
    int[] selection = [];

    readonly List<byte[]> undo = [];
    readonly List<byte[]> redo = [];
    string? fontDirectory;
    byte[]? emitted;

    bool IsWord => sourceInfo?.Format == InputFormat.Docx;

    bool CanComment => !ReadOnly && review.AllowsComments;

    bool CanResolve => !ReadOnly && review.AllowsResolving;

    bool CanEdit => CanComment || CanResolve;

    // What is left to deal with: every change, and every thread not yet resolved.
    int OpenItems => review.Changes.Count + review.Comments.Count(_ => !_.Resolved);

    string OpenItemsLabel
    {
        get
        {
            if (OpenItems > 99)
            {
                return "99+";
            }

            return OpenItems.ToString(CultureInfo.InvariantCulture);
        }
    }

    string Signature
    {
        get
        {
            if (Author is {Length: > 0})
            {
                return Author;
            }

            if (authorName.Trim() is {Length: > 0} name)
            {
                return name;
            }

            return guest;
        }
    }

    void ResetReview()
    {
        review = DocumentReview.Empty;
        reviewMap = ReviewMap.Empty;
        pageTexts = [];
        entries = [];
        activeKey = null;
        revealKey = null;
        reviewNote = null;
        draft = null;
        selection = [];
        undo.Clear();
        redo.Clear();
    }

    void BuildEntries()
    {
        var list = new List<ReviewEntry>();
        if (reviewFilter != ReviewFilter.Changes)
        {
            list.AddRange(review.Comments.Select(_ => new ReviewEntry(_)));
        }

        if (reviewFilter != ReviewFilter.Comments)
        {
            list.AddRange(review.Changes.Select(_ => new ReviewEntry(_)));
        }

        // Down the document; what has no place on a page — a change in a header — follows.
        entries = list.OrderBy(Place).ToList();
        if (activeKey != null &&
            entries.All(_ => _.Key != activeKey))
        {
            activeKey = null;
        }

        static int Place(ReviewEntry entry)
        {
            if (entry.Anchor < 0)
            {
                return int.MaxValue;
            }

            return entry.Anchor;
        }
    }

    async Task ToggleReviewAsync()
    {
        if (!IsWord ||
            handle is not { } viewer)
        {
            return;
        }

        reviewOpen = !reviewOpen;
        if (!reviewOpen)
        {
            draft = null;
            activeKey = null;
            reviewNote = null;
        }

        BuildEntries();
        await viewer.SetReviewModeAsync(reviewOpen);
        await PushReviewAsync(false);
    }

    Task OnReviewFilterAsync(ChangeEventArgs args)
    {
        if (Enum.TryParse<ReviewFilter>(args.Value as string, out var chosen))
        {
            reviewFilter = chosen;
        }

        BuildEntries();
        return PushReviewAsync(false);
    }

    void OnAuthorInput(ChangeEventArgs args) =>
        authorName = args.Value as string ?? "";

    // What the script paints: the range of every comment listed and not yet resolved, and over them
    // the item the reader chose or the text a new comment is being written on.
    async Task PushReviewAsync(bool scroll)
    {
        if (handle is not { } viewer ||
            document is null ||
            !reviewOpen)
        {
            return;
        }

        int[] comments = [];
        if (reviewFilter != ReviewFilter.Changes)
        {
            comments = Triples(review.Comments.Where(_ => !_.Resolved).SelectMany(_ => reviewMap.Ranges(_.Runs)));
        }

        var current = Triples(ActiveRanges());
        if (draft is {Kind: ReviewDraftKind.Comment})
        {
            current = draft.Ranges;
        }

        try
        {
            await viewer.SetReviewAsync(documentId, comments, current, scroll);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    IReadOnlyList<TextMatch> ActiveRanges()
    {
        if (entries.FirstOrDefault(_ => _.Key == activeKey) is not { } entry)
        {
            return [];
        }

        var ranges = reviewMap.Ranges(entry.Runs);
        if (ranges.Count == 0 &&
            reviewMap.Place(entry.Anchor) is { } place)
        {
            return [place];
        }

        return ranges;
    }

    static int[] Triples(IEnumerable<TextMatch> ranges)
    {
        var triples = new List<int>();
        foreach (var range in ranges)
        {
            triples.Add(range.Page);
            triples.Add(range.Start);
            triples.Add(range.Length);
        }

        return [.. triples];
    }

    Task SelectEntryAsync(ReviewEntry entry)
    {
        activeKey = entry.Key;
        reviewNote = null;
        return PushReviewAsync(true);
    }

    Task OnCardKeyDownAsync(KeyboardEventArgs args, ReviewEntry entry)
    {
        if (args.Key is "Enter" or " ")
        {
            return SelectEntryAsync(entry);
        }

        return Task.CompletedTask;
    }

    /// <summary>Called from JavaScript with the page text the reader has selected, or an empty array.</summary>
    [JSInvokable]
    public Task OnReviewSelection(int[] range) =>
        InvokeAsync(() =>
        {
            var had = selection.Length == 4;
            selection = range.Length == 4 ? range : [];
            if (had != (selection.Length == 4))
            {
                StateHasChanged();
            }
        });

    /// <summary>Called from JavaScript with the character of a page's text a click landed on.</summary>
    [JSInvokable]
    public Task OnReviewHit(int page, int offset) =>
        InvokeAsync(async () =>
        {
            if (reviewMap.RunAt(page, offset) is not { } run)
            {
                return;
            }

            // The narrowest thing there: a comment on a word inside an inserted paragraph is the comment.
            var hit = entries
                .Where(_ => _.Runs.Contains(run))
                .OrderBy(_ => _.Comment == null)
                .ThenBy(_ => _.Runs.Count)
                .FirstOrDefault();
            if (hit == null ||
                hit.Key == activeKey)
            {
                return;
            }

            activeKey = hit.Key;
            revealKey = hit.Key;
            reviewNote = null;
            await PushReviewAsync(false);
            StateHasChanged();
        });

    // Comments

    async Task BeginCommentAsync()
    {
        reviewNote = null;
        if (selection.Length != 4 ||
            reviewMap.Before(selection[0], selection[1]) is not { } start ||
            reviewMap.After(selection[2], selection[3] - 1) is not { } end ||
            start.Run > end.Run ||
            (start.Run == end.Run && start.Offset > end.Offset))
        {
            reviewNote = "Select some of the document's text to comment on it.";
            return;
        }

        draft = new(ReviewDraftKind.Comment)
        {
            Start = start,
            End = end,
            Quote = Excerpt(SelectedText()),
            Ranges = SelectionTriples()
        };
        activeKey = null;
        focusDraft = true;
        await PushReviewAsync(false);
    }

    string SelectedText()
    {
        var builder = new StringBuilder();
        for (var page = selection[0]; page <= selection[2] && page < pageTexts.Length; page++)
        {
            var text = pageTexts[page];
            var from = page == selection[0] ? Math.Clamp(selection[1], 0, text.Length) : 0;
            var to = page == selection[2] ? Math.Clamp(selection[3], from, text.Length) : text.Length;
            builder.Append(text, from, to - from);
        }

        return builder.ToString();
    }

    int[] SelectionTriples()
    {
        var triples = new List<int>();
        for (var page = selection[0]; page <= selection[2] && page < pageTexts.Length; page++)
        {
            var length = pageTexts[page].Length;
            var from = page == selection[0] ? Math.Clamp(selection[1], 0, length) : 0;
            var to = page == selection[2] ? Math.Clamp(selection[3], from, length) : length;
            if (to > from)
            {
                triples.AddRange([page, from, to - from]);
            }
        }

        return [.. triples];
    }

    void BeginReply(ReviewComment thread)
    {
        reviewNote = null;
        draft = new(ReviewDraftKind.Reply)
        {
            CommentId = thread.Id,
            ThreadId = thread.Id
        };
        activeKey = ReviewEntry.KeyOf(thread);
        focusDraft = true;
    }

    void BeginEdit(ReviewComment thread, ReviewComment comment)
    {
        reviewNote = null;
        draft = new(ReviewDraftKind.Edit)
        {
            CommentId = comment.Id,
            ThreadId = thread.Id,
            Text = comment.Text
        };
        activeKey = ReviewEntry.KeyOf(thread);
        focusDraft = true;
    }

    Task CancelDraftAsync()
    {
        draft = null;
        return PushReviewAsync(false);
    }

    void OnDraftInput(ChangeEventArgs args) =>
        draft?.Text = args.Value as string ?? "";

    Task OnDraftKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            return CancelDraftAsync();
        }

        if (args.Key == "Enter" &&
            (args.CtrlKey || args.MetaKey))
        {
            return PostDraftAsync();
        }

        return Task.CompletedTask;
    }

    async Task PostDraftAsync()
    {
        if (draft is not { } posting ||
            posting.Text.Trim() is not {Length: > 0} text)
        {
            return;
        }

        var author = Signature;
        var known = review.Comments.SelectMany(_ => _.Replies.Prepend(_)).Select(_ => _.Id).ToHashSet(StringComparer.Ordinal);
        if (posting.Kind == ReviewDraftKind.Edit)
        {
            await ReviseAsync(_ => ReviewEditor.EditComment(_, posting.CommentId!, text), relayout: false, () => activeKey);
            return;
        }

        var now = await ReaderClockAsync();
        if (posting.Kind == ReviewDraftKind.Reply)
        {
            await ReviseAsync(_ => ReviewEditor.Reply(_, posting.CommentId!, author, text, now), relayout: true, () => ThreadOfNew(known));
            return;
        }

        await ReviseAsync(_ => ReviewEditor.AddComment(_, posting.Start, posting.End, author, text, now), relayout: true, () => ThreadOfNew(known));
    }

    // The time on the reader's own clock, which only the browser knows. .NET's is the server's under
    // Blazor Server, and UTC in a WebAssembly app published without time zone data, as Morph.Web is —
    // and a comment dated in UTC reads in Word as made that many hours ago.
    async Task<DateTimeOffset> ReaderClockAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (handle == null)
        {
            return now;
        }

        var minutes = await handle.ZoneOffsetAsync();
        return now.ToOffset(TimeSpan.FromMinutes(minutes));
    }

    // The card of the comment an edit added: the thread holding the one id that was not there before.
    string? ThreadOfNew(HashSet<string> known)
    {
        foreach (var thread in review.Comments)
        {
            if (thread.Replies.Prepend(thread).Any(_ => !known.Contains(_.Id)))
            {
                return ReviewEntry.KeyOf(thread);
            }
        }

        return activeKey;
    }

    Task SetResolvedAsync(ReviewComment thread, bool resolved) =>
        ReviseAsync(_ => ReviewEditor.SetResolved(_, thread.Id, resolved), relayout: false, () => ReviewEntry.KeyOf(thread));

    Task DeleteCommentAsync(ReviewComment comment) =>
        ReviseAsync(_ => ReviewEditor.DeleteComment(_, comment.Id), relayout: true, () => activeKey);

    // Tracked changes

    // Settles a change and moves to the one after it, as Word's Accept and Reject do.
    Task ResolveAsync(ReviewEntry entry, bool accept)
    {
        var index = entries.ToList().IndexOf(entry);
        var key = entry.Change!.Key;
        return ReviseAsync(
            _ => ReviewEditor.Resolve(_, [key], accept),
            relayout: true,
            () =>
            {
                var changes = entries.Where(_ => _.Change != null).ToList();
                if (changes.Count == 0)
                {
                    return null;
                }

                // Its place in the list is where the change after it now stands.
                var after = entries.Skip(index).FirstOrDefault(_ => _.Change != null) ?? changes[0];
                return after.Key;
            },
            scroll: true);
    }

    Task ResolveAllAsync(bool accept) =>
        ReviseAsync(_ => ReviewEditor.ResolveAll(_, accept), relayout: true, () => null);

    Task UndoAsync() =>
        StepAsync(undo, redo);

    Task RedoAsync() =>
        StepAsync(redo, undo);

    async Task StepAsync(List<byte[]> from, List<byte[]> to)
    {
        if (from.Count == 0 ||
            sourceBytes is not { } bytes ||
            revising)
        {
            return;
        }

        var restored = from[^1];
        from.RemoveAt(from.Count - 1);
        Keep(to, bytes);
        await ShowAsync(() => restored, relayout: true, () => activeKey, scroll: false);
    }

    static void Keep(List<byte[]> history, byte[] bytes)
    {
        history.Add(bytes);
        var weight = history.Sum(_ => (long) _.Length);
        while (history.Count > 1 && weight > undoBudget)
        {
            weight -= history[0].Length;
            history.RemoveAt(0);
        }
    }

    // Every edit ends here. select names the card to leave chosen, asked once the edited document has
    // been read, since an edit renumbers what it did not touch.
    async Task ReviseAsync(Func<byte[], byte[]> edit, bool relayout, Func<string?> select, bool scroll = false)
    {
        if (sourceBytes is not { } bytes ||
            revising)
        {
            return;
        }

        var edited = await ShowAsync(() => edit(bytes), relayout, select, scroll);
        if (edited)
        {
            Keep(undo, bytes);
            redo.Clear();
        }
    }

    async Task<bool> ShowAsync(Func<byte[]> produce, bool relayout, Func<string?> select, bool scroll)
    {
        if (handle is not { } viewer ||
            fontDirectory is not { } fonts)
        {
            return false;
        }

        revising = true;
        reviewNote = null;
        errorMessage = null;
        issueUrl = null;
        StateHasChanged();
        try
        {
            var bytes = await Task.Run(produce, lifetime.Token);
            if (relayout)
            {
                var opening = ++generation;
                var opened = await Task.Run(() => Open(bytes, InputFormat.Docx, fonts), lifetime.Token);
                if (opening != generation || disposed)
                {
                    opened.Document.Dispose();
                    return false;
                }

                var previous = document;
                Adopt(opened);
                documentId = opening;
                renderQueue = [];
                previous?.Dispose();
                currentPage = Math.Clamp(currentPage, 1, Math.Max(1, pageCount));
                pageInput = currentPage.ToString(CultureInfo.InvariantCulture);
                await viewer.ReloadAsync(opening, pageSizes, opened.Json);
                if (findOpen && findQuery.Trim().Length > 0)
                {
                    await RunSearchAsync();
                }
            }
            else
            {
                review = await Task.Run(() => DocumentReview.Read(bytes), lifetime.Token);
            }

            sourceBytes = bytes;
            draft = null;
            selection = [];
            BuildEntries();
            activeKey = select();
            BuildEntries();
            revealKey = activeKey;
            await PushReviewAsync(scroll && activeKey != null);
            emitted = bytes;
            await OnDocumentChanged.InvokeAsync(bytes);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Disposed.
            return false;
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
            return false;
        }
        catch (Exception exception)
        {
            ReportError("Could not update the document", exception);
            return false;
        }
        finally
        {
            revising = false;
            if (!disposed)
            {
                StateHasChanged();
            }
        }
    }

    // What the pane shows

    static string Excerpt(string text)
    {
        var builder = new StringBuilder(Math.Min(text.Length, excerptLength + 1));
        var gap = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                gap = true;
                continue;
            }

            if (gap && builder.Length > 0)
            {
                builder.Append(' ');
            }

            gap = false;
            builder.Append(ch);
            if (builder.Length >= excerptLength)
            {
                builder.Append('…');
                break;
            }
        }

        return builder.ToString();
    }

    static string Initials(string? author)
    {
        var initials = new StringBuilder();
        foreach (var word in (author ?? "").Split([' ', '\t', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (char.IsLetterOrDigit(word[0]))
            {
                initials.Append(char.ToUpperInvariant(word[0]));
            }

            if (initials.Length == 2)
            {
                break;
            }
        }

        if (initials.Length == 0)
        {
            return "?";
        }

        return initials.ToString();
    }

    static string When(DateTime? date)
    {
        if (date is not { } value)
        {
            return "";
        }

        return value.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
    }

    static string KindLabel(ReviewChange change)
    {
        var several = change.Text.Contains('\n');
        var insertedRows = several ? "Inserted rows" : "Inserted row";
        var deletedRows = several ? "Deleted rows" : "Deleted row";
        return change.Kind switch
        {
            ReviewChangeKind.Insertion => "Inserted",
            ReviewChangeKind.Deletion => "Deleted",
            ReviewChangeKind.Move => "Moved",
            ReviewChangeKind.Formatting => "Formatted",
            ReviewChangeKind.ParagraphFormatting => "Paragraph formatted",
            ReviewChangeKind.SectionFormatting => "Page setup changed",
            ReviewChangeKind.TableFormatting => "Table formatted",
            ReviewChangeKind.RowInsertion => insertedRows,
            ReviewChangeKind.RowDeletion => deletedRows,
            ReviewChangeKind.CellInsertion => "Inserted cell",
            ReviewChangeKind.CellDeletion => "Deleted cell",
            ReviewChangeKind.CellMerge => "Merged cells",
            _ => "Changed"
        };
    }

    // A change that is only a paragraph's mark has no text to quote.
    static string ChangeText(ReviewChange change)
    {
        var excerpt = Excerpt(change.Text);
        if (excerpt.Length == 0 &&
            change.Text.Contains('\n') &&
            change.Kind is ReviewChangeKind.Insertion or ReviewChangeKind.Deletion or ReviewChangeKind.Move)
        {
            return "paragraph break";
        }

        return excerpt;
    }

    static string? PartLabel(ReviewChange change) =>
        change.Part switch
        {
            ReviewPart.Header => "In a header",
            ReviewPart.Footer => "In a footer",
            ReviewPart.Footnotes => "In a footnote",
            ReviewPart.Endnotes => "In an endnote",
            _ => null
        };

    string EmptyReviewText =>
        reviewFilter switch
        {
            ReviewFilter.Comments => "This document has no comments.",
            ReviewFilter.Changes => "This document has no tracked changes.",
            _ => "This document has no comments or tracked changes."
        };

    string CardClass(ReviewEntry entry, string kind)
    {
        var name = $"review-card {kind}";
        if (entry.Key == activeKey)
        {
            name += " review-active";
        }

        if (entry.Comment is {Resolved: true})
        {
            name += " review-resolved";
        }

        return name;
    }
}
