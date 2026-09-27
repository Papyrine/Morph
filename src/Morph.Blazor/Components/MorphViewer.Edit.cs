namespace Morph;

// Editing: a Word document's text, changed where it stands on the page.
//
// The reader clicks a paragraph and the script lays an editor over it — the paragraph's own text, in
// the face the page drew it in. What is typed there stays in the browser until the reader moves on;
// then the paragraph as it now reads comes back here, is written into the file (DocumentEditor), and
// the file is laid out again like any edit the review pane makes. So typing is as quick as the
// browser, and the pages are never anything but what the file says.
//
// Formatting and deleting also work on text selected across the pages, with no editor open: those
// go straight to the file.
public partial class MorphViewer
{
    /// <summary>
    /// Whether the toolbar offers editing for a Word document: typing into its paragraphs, and
    /// formatting and aligning its text. Default true. <see cref="ReadOnly"/>, and a document's own
    /// protection, turn it off whatever this says.
    /// </summary>
    [Parameter]
    public bool ShowEdit { get; set; } = true;

    DocumentOutline outline = DocumentOutline.Empty;
    EditMap editMap = EditMap.Empty;
    EditSession? session;
    int sessions;
    bool editOpen;
    string? editNote;

    bool CanWrite => !ReadOnly && IsWord && outline.AllowsEditing;

    // Whether edits are recorded as tracked changes.
    bool Tracking => outline.Tracking || outline.ForcesTracking;

    string TrackTitle
    {
        get
        {
            if (outline.ForcesTracking)
            {
                return "This document has every change tracked";
            }

            if (Tracking)
            {
                return "Changes are tracked: stop tracking";
            }

            return "Track changes";
        }
    }

    void ResetEdit()
    {
        outline = DocumentOutline.Empty;
        editMap = EditMap.Empty;
        session = null;
        editNote = null;
    }

    async Task ToggleEditAsync()
    {
        if (!IsWord ||
            handle is not { } viewer)
        {
            return;
        }

        editNote = null;
        if (editOpen)
        {
            // The script hands in what is being typed before it lets go.
            await viewer.SetEditModeAsync(false);
            editOpen = false;
            session = null;
            return;
        }

        if (!CanWrite)
        {
            return;
        }

        editOpen = true;
        await viewer.SetEditModeAsync(true);
    }

    /// <summary>
    /// Called from JavaScript when the reader clicks a page, or selects text on one, while editing:
    /// the point in points from the page's top left, and the selection in offsets into the page's
    /// text — equal for a click, and -1 where the click found no text.
    /// </summary>
    [JSInvokable]
    public Task OnEditHit(int page, double x, double y, int start, int end) =>
        InvokeAsync(
            async () =>
            {
                if (!editOpen ||
                    !CanWrite ||
                    revising ||
                    handle is not { } viewer)
                {
                    return;
                }

                editNote = null;
                if (Locate(page, x, y, start, end) is var (paragraph, block, from, to))
                {
                    await BeginAsync(viewer, paragraph, block, from, to);
                }

                StateHasChanged();
            });

    // Which paragraph a click or a selection is in, and where in it.
    (EditParagraph Paragraph, EditBlock Block, int From, int To)? Locate(int page, double x, double y, int start, int end)
    {
        var under = editMap.At(page, x, y);
        if (start >= 0 &&
            end > start)
        {
            // Text selected: the paragraph it is in, if it is in one.
            if (Place(reviewMap.Before(page, start)) is not var (first, from) ||
                Place(reviewMap.After(page, end - 1)) is not var (last, to) ||
                first.Ordinal != last.Ordinal)
            {
                return null;
            }

            if (Block(first.Ordinal, page) is not { } selected)
            {
                return null;
            }

            return (first, selected, Math.Min(from, to), Math.Max(from, to));
        }

        // A click: the paragraph whose box it is in, at the character it was nearest — if that is
        // one of the paragraph's. Blank paper beside a short line is still the line's paragraph.
        var near = Nearest(page, start);
        if (under == null)
        {
            if (near is not var (paragraph, offset) ||
                Block(paragraph.Ordinal, page) is not { } found)
            {
                return null;
            }

            return (paragraph, found, offset, offset);
        }

        if (under.Paragraph >= outline.Paragraphs.Count)
        {
            return null;
        }

        var clicked = outline.Paragraphs[under.Paragraph];
        var caret = y < under.Top + under.Height / 2 ? 0 : clicked.Length;
        if (near is var (nearest, place) &&
            nearest.Ordinal == clicked.Ordinal)
        {
            caret = place;
        }

        return (clicked, under, caret, caret);
    }

    // The place a caret at an offset of a page's text is: after the character before it, or before
    // the character after it, whichever the page's text can say.
    (EditParagraph Paragraph, int Offset)? Nearest(int page, int offset)
    {
        if (offset < 0)
        {
            return null;
        }

        if (reviewMap.RunAt(page, offset) != null)
        {
            return Place(reviewMap.Before(page, offset));
        }

        if (offset > 0 &&
            reviewMap.RunAt(page, offset - 1) != null)
        {
            return Place(reviewMap.After(page, offset - 1));
        }

        return null;
    }

    (EditParagraph Paragraph, int Offset)? Place(SourcePosition? position)
    {
        if (position is not { } found ||
            outline.ParagraphOf(found.Run) is not { } paragraph ||
            paragraph.OffsetOf(found) is not { } offset)
        {
            return null;
        }

        return (paragraph, offset);
    }

    // The paragraph's lines on a page, or where it starts when it has none there.
    EditBlock? Block(int paragraph, int page)
    {
        var blocks = editMap.Blocks(paragraph);
        foreach (var block in blocks)
        {
            if (block.Page == page)
            {
                return block;
            }
        }

        if (blocks.Count > 0)
        {
            return blocks[0];
        }

        return null;
    }

    async Task BeginAsync(ViewerHandle viewer, EditParagraph paragraph, EditBlock block, int from, int to)
    {
        session = new(++sessions, paragraph, block, editMap);
        try
        {
            await viewer.BeginEditAsync(documentId, session.Json(from, to));
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    /// <summary>
    /// Called from JavaScript when the reader is done with a paragraph: what it now reads as, and
    /// whether it is then to be joined to the paragraph before it (1) or after it (2) — which is
    /// what Backspace at its start and Delete at its end ask for.
    /// </summary>
    [JSInvokable]
    public Task OnEditCommit(int id, string payload, int then) =>
        InvokeAsync(
            async () =>
            {
                if (session is not { } open ||
                    open.Id != id ||
                    !CanWrite)
                {
                    return;
                }

                session = null;
                if (open.Read(payload) is not { } content)
                {
                    editNote = "That edit could not be read, and was not made.";
                    StateHasChanged();
                    return;
                }

                var join = then switch
                {
                    1 => JoinDirection.Previous,
                    2 => JoinDirection.Next,
                    _ => JoinDirection.None
                };
                var options = await EditOptionsAsync();
                EditResult? result = null;
                var edited = await ReviseAsync(
                    _ =>
                    {
                        result = DocumentEditor.Rewrite(_, open.Paragraph.Ordinal, content, options, join);
                        return result.Document;
                    },
                    relayout: true,
                    () => activeKey);
                if (edited &&
                    join != JoinDirection.None &&
                    result is {Paragraph: >= 0} joined)
                {
                    await ResumeAsync(joined.Paragraph, joined.Offset);
                }
            });

    /// <summary>Called from JavaScript when the reader abandons what they were typing.</summary>
    [JSInvokable]
    public Task OnEditCancel(int id) =>
        InvokeAsync(
            () =>
            {
                if (session?.Id == id)
                {
                    session = null;
                }
            });

    // Goes on editing where an edit left off.
    async Task ResumeAsync(int paragraph, int offset)
    {
        if (!editOpen ||
            handle is not { } viewer ||
            paragraph >= outline.Paragraphs.Count ||
            Block(paragraph, -1) is not { } block)
        {
            return;
        }

        await BeginAsync(viewer, outline.Paragraphs[paragraph], block, offset, offset);
    }

    async Task<EditOptions> EditOptionsAsync() =>
        new(Signature, await ReaderClockAsync(), Tracking);

    /// <summary>
    /// Called from JavaScript for a toolbar button or a key pressed while no paragraph is open: the
    /// command applies to the text selected on the pages.
    /// </summary>
    [JSInvokable]
    public Task OnEditCommand(string command) =>
        InvokeAsync(
            async () =>
            {
                if (!editOpen ||
                    !CanWrite ||
                    revising)
                {
                    return;
                }

                editNote = null;
                await RunAsync(command);
                StateHasChanged();
            });

    Task RunAsync(string command)
    {
        switch (command)
        {
            case "undo":
                return UndoAsync();
            case "redo":
                return RedoAsync();
            case "track":
                return SetTrackingAsync(!outline.Tracking);
            case "bold":
                return FormatAsync(_ => _.Bold, _ => new(Bold: _));
            case "italic":
                return FormatAsync(_ => _.Italic, _ => new(Italic: _));
            case "underline":
                return FormatAsync(_ => _.Underline, _ => new(Underline: _));
            case "strike":
                return FormatAsync(_ => _.Strikethrough, _ => new(Strike: _));
            case "align-left":
                return AlignAsync(TextAlignment.Left);
            case "align-center":
                return AlignAsync(TextAlignment.Center);
            case "align-right":
                return AlignAsync(TextAlignment.Right);
            case "align-justify":
                return AlignAsync(TextAlignment.Justify);
            case "delete":
                return DeleteAsync();
        }

        return Task.CompletedTask;
    }

    Task SetTrackingAsync(bool on)
    {
        if (outline.ForcesTracking)
        {
            return Task.CompletedTask;
        }

        return ReviseAsync(_ => DocumentEditor.SetTracking(_, on), relayout: false, () => activeKey);
    }

    // The text selected on the pages, as two places in the document.
    (SourcePosition Start, SourcePosition End)? Selected()
    {
        if (selection.Length != 4 ||
            reviewMap.Before(selection[0], selection[1]) is not { } start ||
            reviewMap.After(selection[2], selection[3] - 1) is not { } end ||
            start.Run > end.Run ||
            (start.Run == end.Run && start.Offset >= end.Offset))
        {
            editNote = "Select some of the document's text first, or click in a paragraph to type in it.";
            return null;
        }

        return (start, end);
    }

    // As a word processor's buttons do: on, unless all of what is selected already is.
    async Task FormatAsync(Func<RunProperties, bool> has, Func<bool, RunFormat> format)
    {
        if (Selected() is not var (start, end))
        {
            return;
        }

        var runs = reviewMap.Runs(selection[0], selection[1], selection[2], selection[3]);
        var all = runs.Count > 0 && runs.All(_ => editMap.Format(_) is { } properties && has(properties));
        var options = await EditOptionsAsync();
        await ReviseAsync(_ => DocumentEditor.Format(_, start, end, format(!all), options), relayout: true, () => activeKey);
    }

    async Task AlignAsync(TextAlignment alignment)
    {
        if (Selected() == null)
        {
            return;
        }

        var paragraphs = reviewMap.Runs(selection[0], selection[1], selection[2], selection[3])
            .Select(_ => outline.ParagraphOf(_)?.Ordinal)
            .OfType<int>()
            .Distinct()
            .ToList();
        if (paragraphs.Count == 0)
        {
            return;
        }

        var options = await EditOptionsAsync();
        await ReviseAsync(_ => DocumentEditor.Align(_, paragraphs, alignment, options), relayout: true, () => activeKey);
    }

    async Task DeleteAsync()
    {
        if (Selected() is not var (start, end))
        {
            return;
        }

        var options = await EditOptionsAsync();
        EditResult? result = null;
        var edited = await ReviseAsync(
            _ =>
            {
                result = DocumentEditor.Delete(_, start, end, options);
                return result.Document;
            },
            relayout: true,
            () => activeKey);
        if (edited &&
            result is {Paragraph: >= 0} left)
        {
            await ResumeAsync(left.Paragraph, left.Offset);
        }
    }
}
