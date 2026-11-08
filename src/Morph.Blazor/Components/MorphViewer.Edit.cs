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

    // The session open when an edit's pages replaced its own: still to be handed in, perhaps.
    EditSession? superseded;
    int sessions;
    bool editOpen;
    internal string? editNote;

    // The page (0-based) the reader was last at in the document: where the paragraph last opened was,
    // or where the text last selected on the pages ends. What a header's paragraph is opened again on.
    int readerPage = -1;

    // The document (documentId) the script's place for a ribbon command is in the ordinals of: the one the
    // paragraph last open was opened in, or handed in to; -1 before one has been.
    int targetDocument = -1;

    // The paragraph last handed in, as what its typing took in and left (CaretPlace.Typing) and how many
    // paragraphs Enter added to it: what a place read off the file before it is moved by (Moved).
    (CaretPlace Before, CaretPlace After, int Added)? handedIn;

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
        typedSelection = false;
        boxUndo = false;
        boxRedo = false;
        readerPage = -1;
        targetDocument = -1;
        handedIn = null;
        superseded = null;
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
                    handle is not { } viewer)
                {
                    return;
                }

                // A click on pages about to be replaced is on text that may be about to move: said, not
                // guessed at.
                if (revising)
                {
                    Busy();
                    return;
                }

                editNote = null;
                // The reader has gone on elsewhere: that is not where the last edit left them.
                placingAfter = false;
                if (Locate(page, x, y, start, end) is var (paragraph, block, from, to))
                {
                    await BeginAsync(viewer, paragraph, block, from, to);
                }

                StateHasChanged();
            });

    // Which paragraph a click or a selection is in, and where in it. On blank paper a click finds the
    // nearest paragraph (blank); a probe (OnEditProbe) asks only what is at the point.
    (EditParagraph Paragraph, EditBlock Block, int From, int To)? Locate(int page, double x, double y, int start, int end, bool blank = true)
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
        if (under == null &&
            !blank)
        {
            return near is var (atPoint, offsetAtPoint) && Block(atPoint.Ordinal, page) is { } found
                ? (atPoint, found, offsetAtPoint, offsetAtPoint)
                : null;
        }

        if (under == null)
        {
            // Blank paper in no paragraph's box: the body's nearest paragraph, as Word's caret goes to
            // the nearest line. The browser's nearest character cannot say which that is — below the
            // last paragraph Chrome named the end of the first line — so it counts only in that
            // paragraph. A header's or a footer's character it names keeps its paragraph.
            if (near is var (named, inStoryOffset) &&
                named.Part != ReviewPart.Body &&
                Block(named.Ordinal, page) is { } inStory)
            {
                return (named, inStory, inStoryOffset, inStoryOffset);
            }

            if (editMap.Nearest(page, x, y, _ => _.Paragraph < outline.Paragraphs.Count && outline.Paragraphs[_.Paragraph].Part == ReviewPart.Body) is not { } closest)
            {
                return null;
            }

            var paragraph = outline.Paragraphs[closest.Paragraph];
            var offset = near is var (inParagraph, at) && inParagraph.Ordinal == paragraph.Ordinal
                ? at
                : y < closest.Top + closest.Height / 2 ? 0 : paragraph.Length;
            return (paragraph, closest, offset, offset);
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

    // Where a paragraph is opened again, or selected again: a header's or a footer's on the page the
    // reader was on — it is drawn on every page that shows it, and its first is seldom the reader's —
    // or the nearest that shows it; anything else where it starts, as a caret there reaches its lines.
    EditBlock? Reopened(int paragraph, int page)
    {
        if (!Repeated(paragraph) ||
            page < 0)
        {
            return Block(paragraph, -1);
        }

        return editMap.Blocks(paragraph)
            .OrderBy(_ => Math.Abs(_.Page - page))
            .FirstOrDefault();
    }

    bool Repeated(int paragraph) =>
        paragraph >= 0 &&
        paragraph < outline.Paragraphs.Count &&
        outline.Paragraphs[paragraph].Part is ReviewPart.Header or ReviewPart.Footer;

    async Task BeginAsync(ViewerHandle viewer, EditParagraph paragraph, EditBlock block, int from, int to)
    {
        // Coauthoring, the paragraph is asked of the session first, and may have moved.
        if (await EnterAsync(paragraph, block) is not var (entered, landed))
        {
            StateHasChanged();
            return;
        }

        paragraph = entered;
        block = landed;
        from = Math.Min(from, paragraph.Length);
        to = Math.Min(to, paragraph.Length);
        readerPage = block.Page;
        targetDocument = documentId;
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
            await viewer.BeginEditAsync(documentId, session.Json(from, to, Tracking, Signature));
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    /// <summary>
    /// Called from JavaScript when the reader is done with a paragraph: what it now reads as, and
    /// whether it is then to be joined to the paragraph before it (1) or after it (2) — which is
    /// what Backspace at its start and Delete at its end ask for — or the caret is to go on to the end of
    /// the paragraph before it (3) or the start of the one after (4), as Left and Right at its edges do.
    /// </summary>
    [JSInvokable]
    public Task OnEditCommit(int id, string payload, int then) =>
        InvokeAsync(
            async () =>
            {
                try
                {
                    await CommitEditAsync(id, payload, then);
                }
                finally
                {
                    // Coauthoring, a paragraph handed in and not opened again is free for the others.
                    ExitIfClosed();
                }
            });

    async Task CommitEditAsync(int id, string payload, int then)
    {
        // The session may have been overtaken by another edit's pages, which came while its box
        // was still on its way in: it is handed in all the same, if its paragraph is still there.
        var overtaken = session?.Id != id &&
                        superseded?.Id == id;
        var open = overtaken ? superseded : session;
        if (open == null ||
            open.Id != id ||
            !CanWrite)
        {
            return;
        }

        session = null;
        superseded = null;
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

        // Handed in while another edit is under way — the box lost the focus to a button that
        // made one: it waits for that edit rather than being refused, and is made if the file
        // that edit left still has the paragraph where it was, as it was opened.
        var ordinal = open.Paragraph.Ordinal;
        if (revising ||
            overtaken)
        {
            await SettledAsync();

            // Coauthoring, others' edits may have moved it: found by its id, which the session keeps unique.
            if (coauthor != null &&
                open.Paragraph.Id is { } paragraphId &&
                OrdinalOf(paragraphId) is { } moved)
            {
                ordinal = moved;
            }

            if (ordinal >= outline.Paragraphs.Count ||
                !Same(outline.Paragraphs[ordinal], open.Paragraph))
            {
                editNote = "The document changed while that paragraph was being handed in, so what was typed in it was not kept: type it again.";
                StateHasChanged();
                return;
            }
        }

        // Undone, a join leaves the caret where the key was pressed: the joined paragraph's start for
        // Backspace, its end for Delete (WordProbe R-U-2, U-04).
        var (before, after) = CaretPlace.Typing(open.Paragraph, content);
        beforeEdit = join switch
        {
            JoinDirection.Previous => CaretPlace.At(ordinal, 0),
            JoinDirection.Next => CaretPlace.At(ordinal, open.Paragraph.Length),
            _ => before
        };
        EditResult? result = null;
        handedIn = null;
        var edited = await ReviseAsync(
            _ =>
            {
                result = DocumentEditor.Rewrite(_, ordinal, content, options, join);
                return result.Document;
            },
            relayout: true,
            () => activeKey);
        if (!edited)
        {
            return;
        }

        // The ordinals of the file the script names its place in, for a command that asks.
        targetDocument = documentId;
        if (join == JoinDirection.None)
        {
            handedIn = (before, after, content.Count - 1);
        }

        if (join != JoinDirection.None &&
            result is {Paragraph: >= 0} joined)
        {
            await ResumeAsync(joined.Paragraph, joined.Offset);
            return;
        }

        PlacedAfter(after);
        if (then is 3 or 4)
        {
            // The paragraph handed in is now as many paragraphs as Enter split it into.
            await CrossAsync(then == 3 ? ordinal : ordinal + content.Count - 1, then == 3 ? -1 : 1);
        }
    }

    // Whether a paragraph of the file now is the one opened for typing: the same things in the same
    // order, which is all a hand-in's items name (NewItem.Unit).
    static bool Same(EditParagraph now, EditParagraph opened) =>
        now.Units.Count == opened.Units.Count &&
        now.Units.Zip(opened.Units).All(_ => _.First.Kind == _.Second.Kind &&
                                              _.First.Text == _.Second.Text);

    // Where a place in the file as it was before the last hand-in is in the file that hand-in made: a
    // paragraph after it moved on by the paragraphs Enter made, and a place in it after what was typed
    // moved on by what was typed. Null when the last edit was not a hand-in that says.
    internal (int Paragraph, int Offset)? Moved(int paragraph, int offset)
    {
        if (handedIn is not var (before, after, added))
        {
            return null;
        }

        if (paragraph < before.StartParagraph)
        {
            return (paragraph, offset);
        }

        if (paragraph > before.StartParagraph)
        {
            return (paragraph + added, offset);
        }

        if (offset < before.StartOffset)
        {
            return (paragraph, offset);
        }

        if (offset >= before.EndOffset)
        {
            return (after.EndParagraph, after.EndOffset + offset - before.EndOffset);
        }

        return (after.EndParagraph, after.EndOffset);
    }

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

                // A key pressed while the pages are laid out again waits for them, then acts where the
                // reader is in them.
                await SettledAsync();
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
                    !CanWrite)
                {
                    return;
                }

                // Waits for an edit under way: where it goes is asked of the script afterwards.
                await SettledAsync();
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
                    !CanWrite)
                {
                    return;
                }

                // Waits for an edit under way: where it goes is asked of the script afterwards.
                await SettledAsync();
                editNote = null;
                await ResizePictureAsync(width, height);
                StateHasChanged();
            });

    /// <summary>
    /// Called from JavaScript for Left at the start (-1) or Right at the end (1) of a paragraph left as it
    /// was: the caret goes on to the end of the paragraph before or the start of the one after.
    /// </summary>
    [JSInvokable]
    public Task OnEditStep(int paragraph, int direction) =>
        InvokeAsync(
            async () =>
            {
                if (!editOpen ||
                    !CanWrite)
                {
                    return;
                }

                // The paragraph named is one of the file being replaced.
                if (revising)
                {
                    Busy();
                    return;
                }

                placingAfter = false;
                await CrossAsync(paragraph, direction);
                StateHasChanged();
            });

    // Left and Right cross a paragraph mark one place at a time, as Word's do (WordProbe R-N-1): to the end
    // of the paragraph before in the same story, or the start of the one after, across a table's cells too.
    Task CrossAsync(int paragraph, int direction)
    {
        if (paragraph < 0 ||
            paragraph >= outline.Paragraphs.Count ||
            (direction < 0 ? outline.Paragraphs[paragraph].PreviousInStory : outline.Paragraphs[paragraph].NextInStory) is not { } to)
        {
            return Task.CompletedTask;
        }

        return ResumeAsync(to, direction < 0 ? outline.Paragraphs[to].Length : 0);
    }

    /// <summary>
    /// Called from JavaScript when the reader leaves a paragraph with nothing to hand in — left as it was,
    /// or a box the page replaced before it was handed in.
    /// </summary>
    [JSInvokable]
    public Task OnEditCancel(int id) =>
        InvokeAsync(
            () =>
            {
                if (session?.Id == id)
                {
                    session = null;
                }

                if (superseded?.Id == id)
                {
                    superseded = null;
                }

                ExitIfClosed();
            });

    // Goes on editing where an edit left off: the caret at offset, or the text up to `to` selected — a
    // header's or a footer's on the page given, or else the page the reader was last at.
    internal Task ResumeAsync(int paragraph, int offset, int? to = null, int? page = null)
    {
        var end = Math.Max(offset, to ?? offset);
        PlacedAfter(CaretPlace.Between(paragraph, offset, end));
        if (!editOpen ||
            handle is not { } viewer ||
            paragraph >= outline.Paragraphs.Count ||
            Reopened(paragraph, page ?? readerPage) is not { } block)
        {
            return Task.CompletedTask;
        }

        var length = outline.Paragraphs[paragraph].Length;
        return BeginAsync(viewer, outline.Paragraphs[paragraph], block, Math.Min(offset, length), Math.Min(end, length));
    }

    // The caret or the selection undo or redo puts back: one paragraph's is opened for typing there, and one
    // across paragraphs is selected on the pages.
    Task PlaceAsync(CaretPlace place)
    {
        if (!editOpen)
        {
            return Task.CompletedTask;
        }

        if (place.StartParagraph == place.EndParagraph)
        {
            return ResumeAsync(place.StartParagraph, place.StartOffset, place.EndOffset);
        }

        return SelectAsync(place);
    }

    // Where a command found the reader: the text selected, or the caret.
    CaretPlace PlaceOf(EditTarget target)
    {
        if (target.HasSelection &&
            Place(target.Start) is var (first, from) &&
            Place(target.End) is var (last, to))
        {
            return new(first.Ordinal, from, last.Ordinal, to);
        }

        return CaretPlace.At(target.Paragraph, target.Offset);
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

                await SettledAsync();
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
            async () =>
            {
                // A menu leaves the paragraph open under it; a dialog took the reader away from it.
                var dialog = EscapeCancelsDialog;
                CloseMenus();
                StateHasChanged();
                if (dialog)
                {
                    await ReturnToDocumentAsync();
                }
            });

    // MorphEditor's: whether Escape now closes a dialog rather than a menu. The viewer has neither.
    internal virtual bool EscapeCancelsDialog => false;

    /// <summary>
    /// Called from JavaScript for a Ctrl+click on a page while editing, which follows a link as Word's
    /// does rather than opening the paragraph: the address of the link the character at an offset of the
    /// page's text is, "#name" for one to a bookmark; null where there is none.
    /// </summary>
    [JSInvokable]
    public async Task<string?> OnEditLinkAt(int page, int offset)
    {
        string? address = null;
        await InvokeAsync(
            () =>
            {
                if (editOpen &&
                    reviewMap.RunAt(page, offset) is { } run)
                {
                    address = editMap.Link(run);
                }
            });
        return address;
    }

    /// <summary>
    /// Called from JavaScript for a link to a bookmark followed (a table of contents' entry): the paragraph
    /// being typed is handed in, and the caret goes to where the bookmark starts.
    /// </summary>
    [JSInvokable]
    public Task OnEditGoToBookmark(string name) =>
        InvokeAsync(
            async () =>
            {
                if (!editOpen)
                {
                    return;
                }

                await FollowLinkAsync($"#{name}");
                StateHasChanged();
            });

    /// <summary>
    /// Called from JavaScript when Escape is pressed in the ribbon's Font or Size box, which has put
    /// back what it showed: its list closes, and the reader goes back to where they were — the
    /// paragraph open again with the caret or the text selected, or the text selected on the pages —
    /// as a command run from the ribbon leaves them.
    /// </summary>
    [JSInvokable]
    public Task OnRibbonEscape() =>
        InvokeAsync(
            async () =>
            {
                CloseMenus();
                StateHasChanged();
                await ReturnToDocumentAsync();
            });

    // Back where the reader left the document, once something that took them away from it is done
    // with — Escape in the ribbon's boxes, a dialog cancelled — as a command run from the ribbon
    // leaves them: the paragraph open again with the caret or the text selected, or the text selected
    // on the pages again. Without it the focus is nowhere, and what is typed reaches nothing.
    internal async Task ReturnToDocumentAsync()
    {
        if (!editOpen)
        {
            return;
        }

        await SettledAsync();
        if (await TargetAsync() is not { } target)
        {
            return;
        }

        if (!target.Typing)
        {
            await ReselectAsync(target);
            return;
        }

        if (target.HasSelection &&
            target.StartParagraph == target.Paragraph)
        {
            var from = Math.Min(target.StartOffset, target.Offset);
            var to = Math.Max(target.StartOffset, target.Offset);
            await ResumeAsync(target.Paragraph, from, to, target.Page);
            return;
        }

        await ResumeAsync(target.Paragraph, target.Offset, page: target.Page);
    }

    // MorphEditor's: the viewer has no right-click menu, and no menus of its own to close.
    internal virtual void OpenContextMenu(double x, double y)
    {
    }

    internal virtual void CloseMenus()
    {
    }

    // The pages are not laid out again, so the paragraph being typed stays open: a hand-in racing this
    // waits for it (OnEditCommit), and finds its paragraph where it was.
    internal Task SetTrackingAsync(bool on)
    {
        if (outline.ForcesTracking)
        {
            return Task.CompletedTask;
        }

        beforeEdit = null;
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
        if (target != null)
        {
            beforeEdit = PlaceOf(target);
        }

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
        if (target != null)
        {
            beforeEdit = PlaceOf(target);
        }

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
    internal Task ReselectAsync(EditTarget target)
    {
        if (target.Typing ||
            !target.HasSelection ||
            target.StartParagraph < 0)
        {
            return Task.CompletedTask;
        }

        return SelectAsync(new(target.StartParagraph, target.StartOffset, target.Paragraph, target.Offset), target.Page);
    }

    // Selects text on the pages, between two places in the paragraphs: a header's or a footer's on the
    // page given, or else the page the reader was last at.
    async Task SelectAsync(CaretPlace place, int? page = null)
    {
        PlacedAfter(place);
        var near = page ?? readerPage;
        if (place.EndParagraph >= outline.Paragraphs.Count ||
            place.StartParagraph >= outline.Paragraphs.Count ||
            outline.Paragraphs[place.StartParagraph].PositionAt(place.StartOffset, ending: false) is not { } start ||
            outline.Paragraphs[place.EndParagraph].PositionAt(place.EndOffset, ending: true) is not { } end ||
            reviewMap.Locate(start, after: false, Repeated(place.StartParagraph) ? near : -1) is not var (firstPage, from) ||
            reviewMap.Locate(end, after: true, Repeated(place.EndParagraph) ? near : -1) is not var (lastPage, to) ||
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

    /// <summary>Called from JavaScript for a character typed with text selected on the pages: it replaces the text.</summary>
    [JSInvokable]
    public Task OnEditTypeOver(string text) =>
        InvokeAsync(
            async () =>
            {
                // Typed over text on pages being replaced: said, since the text may be about to move.
                if (revising)
                {
                    Busy();
                    return;
                }

                if (!editOpen ||
                    !CanWrite ||
                    Selected() is not var (start, end))
                {
                    StateHasChanged();
                    return;
                }

                // Undone, what was typed over is selected again (WordProbe R-U-2).
                if (FromSelection() is { } target)
                {
                    beforeEdit = PlaceOf(target);
                }

                editNote = null;
                var options = await EditOptionsAsync();
                EditResult? result = null;
                var edited = await ReviseAsync(
                    _ =>
                    {
                        result = DocumentEditor.TypeOver(_, start, end, text, options);
                        return result.Document;
                    },
                    relayout: true,
                    () => activeKey);
                if (edited &&
                    result is {Paragraph: >= 0} typed)
                {
                    await ResumeAsync(typed.Paragraph, typed.Offset);
                }

                StateHasChanged();
            });

    internal async Task DeleteAsync()
    {
        if (Selected() is not var (start, end))
        {
            return;
        }

        // Undone, the text deleted is selected again (WordProbe R-U-2).
        if (FromSelection() is { } target)
        {
            beforeEdit = PlaceOf(target);
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
