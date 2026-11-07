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

    /// <summary>
    /// Saves the file where it came from, as a WOPI host's storage (<see cref="MorphWopi"/>). Given one,
    /// Save and Ctrl+S hand it the file instead of downloading it, and the file counts as saved once it
    /// returns; a failure it throws is shown, and the file stays edited. Download still saves a copy.
    /// </summary>
    [Parameter]
    public EventCallback<byte[]> OnSave { get; set; }

    internal const string guest = "Guest";

    // Undo keeps whole files, so it is bounded by what they weigh rather than by how many there are.
    const long undoBudget = 64L * 1024 * 1024;

    const int excerptLength = 280;

    ElementReference draftInput;
    internal DocumentReview review = DocumentReview.Empty;
    ReviewMap reviewMap = ReviewMap.Empty;
    string[] pageTexts = [];
    IReadOnlyList<ReviewEntry> entries = [];
    ReviewFilter reviewFilter;
    internal bool reviewOpen;
    internal bool revising;
    string? activeKey;
    string? revealKey;
    string? reviewNote;
    internal string authorName = "";
    ReviewDraft? draft;
    bool focusDraft;

    // What the reader has selected on the pages, as the script last reported it: first page, start,
    // last page, end — or empty.
    int[] selection = [];

    readonly List<HistoryStep> undo = [];
    readonly List<HistoryStep> redo = [];

    // Where the next edit acts, as the command making it found the reader: what its undo step puts back.
    CaretPlace? beforeEdit;

    // An edit was just made, and where it leaves the reader (ResumeAsync, SelectAsync) is what redo puts back.
    bool placingAfter;
    string? fontDirectory;
    byte[]? emitted;

    internal bool IsWord => sourceInfo?.Format == InputFormat.Docx;

    internal bool CanComment => !ReadOnly &&
                                review.AllowsComments;

    internal bool CanResolve => !ReadOnly &&
                                review.AllowsResolving;

    bool CanEdit => CanComment || CanResolve;

    // What is left to deal with: every change, and every thread not yet resolved.
    internal int OpenItems => review.Changes.Count + review.Comments.Count(_ => !_.Resolved);

    internal string OpenItemsLabel
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

    internal async Task ToggleReviewAsync()
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

    internal void OnAuthorInput(ChangeEventArgs args) =>
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
            if (selection.Length == 4)
            {
                readerPage = selection[2];
            }

            if (selection.Length == 4 &&
                Place(reviewMap.Before(selection[0], selection[1])) is var (first, _))
            {
                caretParagraph = first.Ordinal;
            }

            // The editor counts what is selected, so it hears of every change.
            if (had != (selection.Length == 4) ||
                Editing)
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

    Task BeginCommentAsync()
    {
        reviewNote = null;
        if (selection.Length != 4 ||
            reviewMap.Before(selection[0], selection[1]) is not { } start ||
            reviewMap.After(selection[2], selection[3] - 1) is not { } end ||
            start.Run > end.Run ||
            (start.Run == end.Run &&
             start.Offset > end.Offset))
        {
            reviewNote = "Select some of the document's text to comment on it.";
            return Task.CompletedTask;
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
        return PushReviewAsync(false);
    }

    string SelectedText()
    {
        var builder = new StringBuilder();
        for (var page = selection[0];
             page <= selection[2] &&
             page < pageTexts.Length;
             page++)
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
        for (var page = selection[0];
             page <= selection[2] &&
             page < pageTexts.Length;
             page++)
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

        // A new comment is on places in the file as it was when its text was selected, which a hand-in
        // of the paragraph being typed renumbers: it is asked for again rather than put on other text.
        if (await HandInFirstAsync() &&
            posting.Kind == ReviewDraftKind.Comment)
        {
            reviewNote = "The document changed as the paragraph being typed was handed in: select the text to comment on again.";
            draft = null;
            await PushReviewAsync(false);
            return;
        }

        var author = Signature;
        var known = review.Comments
            .SelectMany(_ => _.Replies.Prepend(_))
            .Select(_ => _.Id)
            .ToHashSet(StringComparer.Ordinal);
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

    // A comment's id is the file's own, which handing in the paragraph being typed leaves as it is.
    async Task SetResolvedAsync(ReviewComment thread, bool resolved)
    {
        await HandInFirstAsync();
        await ReviseAsync(_ => ReviewEditor.SetResolved(_, thread.Id, resolved), relayout: false, () => ReviewEntry.KeyOf(thread));
    }

    async Task DeleteCommentAsync(ReviewComment comment)
    {
        await HandInFirstAsync();
        await ReviseAsync(_ => ReviewEditor.DeleteComment(_, comment.Id), relayout: true, () => activeKey);
    }

    // Tracked changes

    // Settles a change and moves to the one after it, as Word's Accept and Reject do.
    async Task ResolveAsync(ReviewEntry entry, bool accept)
    {
        var read = review.Changes;
        var change = entry.Change!;
        var index = entries.ToList().IndexOf(entry);
        if (await HandInFirstAsync())
        {
            // A change's key names elements of the file it was read from, which the hand-in renumbered:
            // the change is found again as the same one of its kind, by the same author, on the same text.
            if (Refound(read, change) is not { } now)
            {
                reviewNote = "The document changed as the paragraph being typed was handed in, and that change is no longer there.";
                return;
            }

            change = now;
            index = entries.ToList().FindIndex(_ => _.Change == now);
        }

        var key = change.Key;
        await ReviseAsync(
            _ => ReviewEditor.Resolve(_, [key], accept),
            relayout: true,
            () =>
            {
                var remaining = entries.Where(_ => _.Change != null).ToList();
                if (remaining.Count == 0)
                {
                    return null;
                }

                // Its place in the list is where the change after it now stands.
                var after = entries.Skip(Math.Max(index, 0)).FirstOrDefault(_ => _.Change != null) ?? remaining[0];
                return after.Key;
            },
            scroll: true);
    }

    // The change of a review read before an edit, in the review read after it: the one at the same place
    // among those alike in kind, author, date, part and text.
    ReviewChange? Refound(IReadOnlyList<ReviewChange> before, ReviewChange change)
    {
        var place = before.Where(_ => Alike(_, change)).ToList().IndexOf(change);
        var alike = review.Changes.Where(_ => Alike(_, change)).ToList();
        if (place < 0 ||
            place >= alike.Count)
        {
            return null;
        }

        return alike[place];

        static bool Alike(ReviewChange first, ReviewChange second) =>
            first.Kind == second.Kind &&
            first.Author == second.Author &&
            first.Date == second.Date &&
            first.Part == second.Part &&
            first.Text == second.Text;
    }

    // Enabled with nothing to resolve, as Word's is, and then does nothing.
    internal async Task ResolveAllAsync(bool accept)
    {
        // What is being typed is handed in first, and then resolved with the rest.
        await HandInFirstAsync();
        if (review.Changes.Count == 0)
        {
            return;
        }

        await ReviseAsync(_ => ReviewEditor.ResolveAll(_, accept), relayout: true, () => null);
    }

    // Review > Previous and Next: the change before or after the one the pane has active, in the
    // document's order and round from the end to the start, the pane opened to it.
    internal async Task StepChangeAsync(int direction)
    {
        var changes = entries.Where(_ => _.Change != null).ToList();
        if (changes.Count == 0)
        {
            editNote = "The document has no tracked changes.";
            return;
        }

        var index = changes.FindIndex(_ => _.Key == activeKey);
        if (index < 0)
        {
            index = direction > 0 ? 0 : changes.Count - 1;
        }
        else
        {
            index = (index + direction + changes.Count) % changes.Count;
        }

        await OpenReviewAsync();
        revealKey = changes[index].Key;
        await SelectEntryAsync(changes[index]);
    }

    // What the editor's Undo and Redo are greyed out by (EditorState.CanUndo): the file's history, or a
    // paragraph open for typing, whose own history is the script's.
    internal bool CanUndo => undo.Count > 0 || session != null;

    internal bool CanRedo => redo.Count > 0 || session != null;

    // Text is selected across the pages.
    internal bool PagesSelected => selection.Length == 4;

    internal Task UndoAsync() =>
        StepAsync(undo, redo, undoing: true);

    internal Task RedoAsync() =>
        StepAsync(redo, undo, undoing: false);

    // Undo puts the caret or the selection back where the undone edit began, and redo where it left them
    // (WordProbe R-U-2): the file a step restores is the one its places were taken in.
    async Task StepAsync(List<HistoryStep> from, List<HistoryStep> to, bool undoing)
    {
        // What is being typed is a step of its own, which undo then takes back first.
        await HandInFirstAsync();
        if (from.Count == 0 ||
            sourceBytes is not { } bytes)
        {
            return;
        }

        if (revising)
        {
            Busy();
            return;
        }

        var step = from[^1];
        beforeEdit = null;
        placingAfter = false;

        // The step moves from one list to the other only once the file it restores is the one shown.
        if (await ShowAsync(
                () => Stepped(step, bytes),
                relayout: true,
                () => activeKey,
                scroll: false,
                () =>
                {
                    var index = from.LastIndexOf(step);
                    if (index >= 0)
                    {
                        from.RemoveAt(index);
                    }

                    Keep(to, step with
                    {
                        Bytes = bytes,
                        Shown = sourceBytes
                    });
                }) &&
            (undoing ? step.Before : step.After) is { } place)
        {
            await PlaceAsync(place);
        }
    }

    static void Keep(List<HistoryStep> history, HistoryStep step)
    {
        history.Add(step);
        var weight = history.Sum(_ => (long) _.Bytes.Length);
        while (history.Count > 1 &&
               weight > undoBudget)
        {
            weight -= history[0].Bytes.Length;
            history.RemoveAt(0);
        }
    }

    // Where the edit just made left the reader, for redo to put them back there.
    void PlacedAfter(CaretPlace place)
    {
        if (placingAfter &&
            undo.Count > 0)
        {
            undo[^1] = undo[^1] with
            {
                After = place
            };
        }

        placingAfter = false;
    }

    // Every edit ends here. select names the card to leave chosen, asked once the edited document has
    // been read, since an edit renumbers what it did not touch.
    async Task<bool> ReviseAsync(Func<byte[], byte[]> edit, bool relayout, Func<string?> select, bool scroll = false)
    {
        if (sourceBytes is not { } bytes)
        {
            return false;
        }

        if (revising)
        {
            Busy();
            return false;
        }

        var before = beforeEdit;
        beforeEdit = null;
        placingAfter = false;
        var edited = await ShowAsync(
            () => edit(bytes),
            relayout,
            select,
            scroll,
            () =>
            {
                Keep(undo, new(bytes, before, null, sourceBytes));
                redo.Clear();
            });
        if (edited)
        {
            placingAfter = true;
        }

        return edited;
    }

    // Shown for a command that arrives while the pages are being laid out again after an edit: what it
    // would act on is about to move, so it is not guessed at.
    internal const string busyNote = "The pages are being laid out again after the last edit: try that again in a moment.";

    void Busy()
    {
        editNote = busyNote;
        reviewNote = busyNote;
        StateHasChanged();
    }

    // Resolved as the edit under way has been shown, for what waits on it.
    TaskCompletionSource? settling;

    // Waits out the edit under way, if there is one, and any that another waiter then began.
    async Task SettledAsync()
    {
        while (revising &&
               settling is { } pending)
        {
            await pending.Task;
        }
    }

    // What is being typed is handed in before an edit that does not itself ask the script where the
    // reader is — a card of the review pane, Accept all — since the pages laid out again would drop it,
    // and its own hand-in, racing this edit as the box loses the focus, would be refused. Says whether
    // that changed the file, which renumbers what was read off it before.
    async Task<bool> HandInFirstAsync()
    {
        var before = sourceBytes;
        await HandInAsync();

        // An edit made from here starts at no caret of its own: a place a command took earlier and did
        // not edit at is not where its undo goes back to.
        beforeEdit = null;
        return !ReferenceEquals(before, sourceBytes);
    }

    // An edit is taken whole or not at all. The file, the pages .NET holds and the review and outline read
    // off it move together, with nothing awaited between them, and `made` — the edit's undo step — goes
    // with them; only then is the draft kept, the script handed the new pages and the host told, and a
    // failure among those leaves an edit that was made, with its undo step, rather than pages over a file
    // they were not laid out from.
    async Task<bool> ShowAsync(Func<byte[]> produce, bool relayout, Func<string?> select, bool scroll, Action? made = null)
    {
        if (handle is not { } viewer ||
            fontDirectory is not { } fonts)
        {
            return false;
        }

        var settled = settling = new(TaskCreationOptions.RunContinuationsAsynchronously);
        revising = true;
        reviewNote = null;
        editNote = null;
        errorMessage = null;
        issueUrl = null;
        StateHasChanged();
        var shown = false;
        try
        {
            // The edit reads the review and the outline off its own package as it saves, which the
            // reading of the edited file below then finds rather than opening the package again.
            var bytes = await Task.Run(
                () =>
                {
                    using var capture = DocumentReading.Capture();
                    using var stamping = Stamping();
                    return produce();
                },
                lifetime.Token);
            OpenedDocument? opened = null;
            var opening = generation;
            if (relayout)
            {
                opening = ++generation;
                opened = await Task.Run(() => Open(bytes, InputFormat.Docx, fonts, true), lifetime.Token);
                if (opening != generation || disposed)
                {
                    opened.Document.Dispose();
                    return false;
                }

                var previous = document;
                superseded = session ?? superseded;
                Adopt(opened);
                documentId = opening;
                sourceBytes = bytes;
                renderQueue = [];
                previous?.Dispose();
                currentPage = Math.Clamp(currentPage, 1, Math.Max(1, pageCount));
                pageInput = currentPage.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var (read, readOutline) = await Task.Run(
                    () =>
                    {
                        try
                        {
                            return DocumentReading.Read(bytes);
                        }
                        catch (Exception)
                        {
                            // As read on their own: a review that cannot be read is this edit's failure.
                            return (DocumentReview.Read(bytes), Outline(bytes, InputFormat.Docx));
                        }
                    },
                    lifetime.Token);

                // A file opened meanwhile is the document now, and this edit was to the one before it.
                if (opening != generation || disposed)
                {
                    return false;
                }

                review = read;
                outline = readOutline;
                sourceBytes = bytes;
            }

            made?.Invoke();
            shown = true;
            emitted = bytes;
            draft = null;
            selection = [];
            BuildEntries();
            activeKey = select();
            BuildEntries();
            revealKey = activeKey;

            try
            {
                if (opened != null)
                {
                    await viewer.ReloadAsync(opening, pageSizes, opened.Json);
                    await viewer.SetPicturesAsync(opening, opened.Pictures);
                    if (findOpen &&
                        findQuery.Trim().Length > 0)
                    {
                        await RunSearchAsync();
                    }
                }

                await PushReviewAsync(scroll &&
                                      activeKey != null);
            }
            finally
            {
                // Kept whatever the script made of the pages, and before the host hears of the edit, so
                // neither can cost the draft.
                await DocumentRevisedAsync();
            }

            await CoauthoredAsync();
            await OnDocumentChanged.InvokeAsync(bytes);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Disposed.
            return shown;
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
            return shown;
        }
        catch (InvalidOperationException exception) when (!shown)
        {
            // An edit the document does not allow, which is for the reader to know and not a fault
            // to report.
            reviewNote = exception.Message;
            editNote = exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            ReportError("Could not update the document", exception);
            return shown;
        }
        finally
        {
            revising = false;
            settled.TrySetResult();
            if (ReferenceEquals(settling, settled))
            {
                settling = null;
            }

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

            if (gap &&
                builder.Length > 0)
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
