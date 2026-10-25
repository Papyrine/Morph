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
//
// The viewer only shows a document. All of this is MorphEditor's, which turns it on (Editing) for a
// Word document that allows it; it lives here because it shares the viewer's document, pages and
// edits from end to end.
public partial class MorphViewer
{
    /// <summary>Whether a Word document's text is edited where it stands: MorphEditor's, not the viewer's.</summary>
    internal virtual bool Editing => false;

    internal DocumentOutline outline = DocumentOutline.Empty;
    EditMap editMap = EditMap.Empty;
    EditSession? session;
    int sessions;
    bool editOpen;
    internal string? editNote;

    internal bool CanWrite => !ReadOnly &&
                              IsWord &&
                              outline.AllowsEditing;

    // Whether edits are recorded as tracked changes.
    internal bool Tracking => outline.Tracking || outline.ForcesTracking;

    internal string TrackTitle
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
        floatingPicture = null;
        inlinePicture = false;
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
        caretParagraph = paragraph.Ordinal;
        // The look shown is that of the character before a caret, or of the first one selected.
        caretOffset = from;
        if (to > from)
        {
            caretOffset = from + 1;
        }

        try
        {
            await viewer.BeginEditAsync(documentId, session.Json(from, to, Tracking));
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

    /// <summary>
    /// Called from JavaScript for a key whose command is carried out here, where the reader is, by the
    /// command's name: Tab and Shift+Tab in a list (<c>indent</c>, <c>outdent</c>), Enter in an empty
    /// paragraph of one (<c>end-list</c>), Backspace at the start of one or of an indented paragraph
    /// (<c>unindent</c>), Enter at the end of a heading (<c>follow-on</c>), Ctrl+Alt+1 to 3
    /// (<c>heading1</c> to <c>heading3</c>), Ctrl+K (<c>link</c>), and the Font and Paragraph groups'
    /// keys (<c>grow</c>, <c>shrink</c>, <c>subscript</c>, <c>superscript</c>, <c>clear-formatting</c>,
    /// <c>bullets</c>, <c>line-single</c>, <c>line-double</c>, <c>line-one-and-half</c>), and Alt+Shift+D,
    /// P and T (<c>date-field</c>, <c>page-field</c>, <c>time-field</c>).
    /// </summary>
    [JSInvokable]
    public Task OnEditKey(string name) =>
        InvokeAsync(
            async () =>
            {
                if (!editOpen)
                {
                    return;
                }

                editNote = null;
                await CommandAsync(name);
                StateHasChanged();
            });

    /// <summary>
    /// Called from JavaScript for HTML pasted, or dropped, into the paragraph being typed: the clipboard's
    /// HTML, and its plain text for when the HTML holds nothing to read.
    /// </summary>
    [JSInvokable]
    public Task OnEditPaste(string html, string text) =>
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
                await PasteAsync(html, text);
                StateHasChanged();
            });

    /// <summary>
    /// Called from JavaScript when the picture selected in the paragraph being typed has been sized
    /// by its handles: its new width and height, in points.
    /// </summary>
    [JSInvokable]
    public Task OnEditResize(double width, double height) =>
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
                await ResizePictureAsync(width, height);
                StateHasChanged();
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
    internal Task ResumeAsync(int paragraph, int offset)
    {
        if (!editOpen ||
            handle is not { } viewer ||
            paragraph >= outline.Paragraphs.Count ||
            Block(paragraph, -1) is not { } block)
        {
            return Task.CompletedTask;
        }

        return BeginAsync(viewer, outline.Paragraphs[paragraph], block, offset, offset);
    }

    async Task<EditOptions> EditOptionsAsync() =>
        new(Signature, await ReaderClockAsync(), Tracking);

    // Overtype, which the Insert key turns on and off in the script: shown in the status bar.
    internal bool overtype;

    /// <summary>Called from JavaScript when the Insert key turns Overtype on or off.</summary>
    [JSInvokable]
    public Task OnOvertype(bool on) =>
        InvokeAsync(
            () =>
            {
                overtype = on;
                StateHasChanged();
            });

    /// <summary>Called from JavaScript with a note for the status bar, as when the clipboard could not be read.</summary>
    [JSInvokable]
    public Task OnEditNote(string note) =>
        InvokeAsync(
            () =>
            {
                editNote = note;
                StateHasChanged();
            });

    /// <summary>
    /// Called from JavaScript for a button the script handles itself (<c>data-edit-command</c>), or its
    /// key, when no paragraph is open to take it: the command applies to the text selected on the pages.
    /// </summary>
    [JSInvokable]
    public Task OnEditCommand(string command) =>
        InvokeAsync(
            async () =>
            {
                if (!editOpen)
                {
                    return;
                }

                editNote = null;
                await CommandAsync(command);
                StateHasChanged();
            });

    // One of the editor's commands, by the name the script gives it: MorphEditor's table, which says when
    // each can run. The viewer has none.
    internal virtual Task CommandAsync(string name) =>
        Task.CompletedTask;

    /// <summary>
    /// Called from JavaScript for a right-click while editing, once what was right-clicked has been
    /// selected — a picture, a place in the paragraph being typed, or the paragraph there opened as a
    /// click opens it: where, in pixels from the editor's top left.
    /// </summary>
    [JSInvokable]
    public Task OnContextMenu(double x, double y) =>
        InvokeAsync(
            () =>
            {
                if (!editOpen)
                {
                    return;
                }

                OpenContextMenu(x, y);
                StateHasChanged();
            });

    /// <summary>
    /// Called from JavaScript for Escape while a menu or a dialog is open: that closes, and the key goes no
    /// further, so the paragraph being typed in keeps what was typed.
    /// </summary>
    [JSInvokable]
    public Task OnMenuEscape() =>
        InvokeAsync(
            () =>
            {
                CloseMenus();
                StateHasChanged();
            });

    // MorphEditor's: the viewer has no right-click menu, and no menus of its own to close.
    internal virtual void OpenContextMenu(double x, double y)
    {
    }

    internal virtual void CloseMenus()
    {
    }

    internal Task SetTrackingAsync(bool on)
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
            (start.Run == end.Run &&
             start.Offset >= end.Offset))
        {
            editNote = "Select some of the document's text first, or click in a paragraph to type in it.";
            return null;
        }

        return (start, end);
    }

    // As a word processor's buttons do: on, unless all of what is selected already is.
    internal async Task FormatAsync(Func<RunProperties, bool> has, Func<bool, RunFormat> format)
    {
        if (Selected() is not var (start, end))
        {
            return;
        }

        var target = FromSelection();

        var runs = reviewMap.Runs(selection[0], selection[1], selection[2], selection[3]);
        var all = runs.Count > 0 &&
                  runs.All(_ => editMap.Format(_) is { } properties &&
                                has(properties));
        var options = await EditOptionsAsync();
        if (await ReviseAsync(_ => DocumentEditor.Format(_, start, end, format(!all), options), relayout: true, () => activeKey) &&
            target != null)
        {
            await ReselectAsync(target);
        }
    }

    internal async Task AlignAsync(TextAlignment alignment)
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

        // Word's alignments toggle against Left: pressed on paragraphs that all have it already, they go
        // back to the left (WordProbe E-89).
        if (alignment != TextAlignment.Left &&
            paragraphs.All(_ => PropertiesOf(_)?.Alignment == alignment))
        {
            alignment = TextAlignment.Left;
        }

        var target = FromSelection();
        var options = await EditOptionsAsync();
        if (await ReviseAsync(_ => DocumentEditor.Align(_, paragraphs, alignment, options), relayout: true, () => activeKey) &&
            target != null)
        {
            await ReselectAsync(target);
        }
    }

    // Formatting text selected on the pages leaves it selected, as Word's does whichever way the change
    // is made (WordProbe P-01, P-03): formatting changes no text, so the same paragraphs and offsets are
    // the same text in the file just made, found again on its pages.
    internal async Task ReselectAsync(EditTarget target)
    {
        if (target.Typing ||
            !target.HasSelection ||
            target.StartParagraph < 0 ||
            target.Paragraph >= outline.Paragraphs.Count ||
            target.StartParagraph >= outline.Paragraphs.Count ||
            outline.Paragraphs[target.StartParagraph].PositionAt(target.StartOffset, ending: false) is not { } start ||
            outline.Paragraphs[target.Paragraph].PositionAt(target.Offset, ending: true) is not { } end ||
            reviewMap.Locate(start, after: false) is not var (firstPage, from) ||
            reviewMap.Locate(end, after: true) is not var (lastPage, to) ||
            handle is not { } viewer)
        {
            return;
        }

        try
        {
            await viewer.SelectTextAsync(firstPage, from, lastPage, to);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    internal async Task DeleteAsync()
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
