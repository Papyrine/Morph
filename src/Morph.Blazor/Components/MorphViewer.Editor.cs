using VerticalPositionValues = DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using Image = SixLabors.ImageSharp.Image;

namespace Morph;

// What MorphEditor's ribbon runs beyond typing: a paragraph's style, its list, its indent and its
// spacing; a page break, a table, a picture or a link put in; a table's rows and columns; text found
// and replaced; a count of the words; the document exported, or a new one begun.
//
// Each acts where the reader is (TargetAsync): in the paragraph open for typing — which is handed in
// first, so the command acts on the file as the reader left it — or else on the text selected across
// the pages. Each is an edit to the file like any other (ReviseAsync): one step of undo, the pages
// laid out again, and the paragraph opened again where the caret was.
public partial class MorphViewer
{
    // The address the blank document a new one starts from is fetched at.
    internal const string BlankAsset = MorphAssets.ContentRoot + "/blank.docx";

    // Space Word's Add Space Before (and After) Paragraph gives.
    const double addedSpace = 12;

    internal static readonly double[] LineSpacings = [1, 1.15, 1.5, 2, 2.5, 3];

    ElementReference replaceInput;
    internal bool replaceOpen;
    bool focusReplace;
    string replaceText = "";
    string? replaceNote;
    int untitled;

    // The bytes as last saved or opened, which is what tells an edit not yet saved.
    byte[]? savedBytes;

    // A save handed to OnSave is under way.
    internal bool saving;

    // The paragraph the ribbon shows the state of: the one open for typing, or where the selection on
    // the pages starts.
    int? caretParagraph;

    internal bool Dirty =>
        sourceBytes is not null &&
        !ReferenceEquals(sourceBytes, savedBytes);

    internal bool ReplaceShown =>
        replaceOpen &&
        editOpen &&
        CanWrite;

    // Where a command acts

    internal async Task<EditTarget?> TargetAsync()
    {
        if (handle is not { } viewer)
        {
            return null;
        }

        int[] place;
        try
        {
            place = await viewer.EditTargetAsync();
        }
        catch (JSDisconnectedException)
        {
            return null;
        }

        if (FromEditor(place ?? []) is { } typing)
        {
            return typing;
        }

        return FromSelection();
    }

    // The place the script names: in paragraphs of the file as it now stands.
    EditTarget? FromEditor(int[] place)
    {
        var count = outline.Paragraphs.Count;
        if (place.Length != 8 ||
            place.Any(_ => _ < 0) ||
            place[0] >= count ||
            place[1] >= count ||
            place[4] >= count ||
            place[6] >= count ||
            place[0] > place[1])
        {
            return null;
        }

        var caret = Math.Min(place[3], outline.Paragraphs[place[2]].Length);
        SourcePosition? start = null;
        SourcePosition? end = null;
        if (place[4] != place[6] ||
            place[5] != place[7])
        {
            start = outline.Paragraphs[place[4]].PositionAt(place[5], ending: false);
            end = outline.Paragraphs[place[6]].PositionAt(place[7], ending: true);
        }

        return new(place[0], place[1], place[2], caret, start, end, Typing: true)
        {
            Quote = Text(place[4], place[5], place[6], place[7]),
            StartParagraph = place[4],
            StartOffset = place[5]
        };
    }

    // The text between two places, a line to each paragraph, as the outline reads it.
    string Text(int firstParagraph, int from, int lastParagraph, int to)
    {
        var builder = new StringBuilder();
        for (var ordinal = firstParagraph; ordinal <= lastParagraph && ordinal < outline.Paragraphs.Count; ordinal++)
        {
            if (ordinal > firstParagraph)
            {
                builder.Append('\n');
            }

            var at = 0;
            var start = ordinal == firstParagraph ? from : 0;
            var end = ordinal == lastParagraph ? to : int.MaxValue;
            foreach (var unit in outline.Paragraphs[ordinal].Units)
            {
                var span = unit.Editable ? unit.Text.Length : 1;
                if (unit.Editable)
                {
                    for (var index = 0; index < unit.Text.Length; index++)
                    {
                        if (at + index >= start &&
                            at + index < end)
                        {
                            builder.Append(unit.Text[index]);
                        }
                    }
                }

                at += span;
            }
        }

        return builder.ToString();
    }

    // A comment on what is selected: in the paragraph open for typing, or across the pages.
    internal async Task CommentAsync()
    {
        if (!CanComment ||
            revising)
        {
            return;
        }

        if (selection.Length == 4)
        {
            if (!InBody(reviewMap.Before(selection[0], selection[1])))
            {
                editNote = bodyOnly;
                return;
            }

            await OpenReviewAsync();
            await BeginCommentAsync();
            return;
        }

        if (await TargetAsync() is not {HasSelection: true, Start: { } start, End: { } end} target)
        {
            editNote = "Select the text to comment on first.";
            return;
        }

        if (!InBody(start))
        {
            editNote = bodyOnly;
            return;
        }

        await OpenReviewAsync();
        draft = new(ReviewDraftKind.Comment)
        {
            Start = start,
            End = end,
            Quote = Excerpt(target.Quote),
            Ranges = []
        };
        activeKey = null;
        focusDraft = true;
        await PushReviewAsync(false);
    }

    Task OpenReviewAsync()
    {
        if (!reviewOpen)
        {
            return ToggleReviewAsync();
        }

        return Task.CompletedTask;
    }

    EditTarget? FromSelection()
    {
        if (selection.Length != 4)
        {
            return null;
        }

        var paragraphs = reviewMap.Runs(selection[0], selection[1], selection[2], selection[3])
            .Select(_ => outline.ParagraphOf(_)?.Ordinal)
            .OfType<int>()
            .Distinct()
            .Order()
            .ToList();
        var start = reviewMap.Before(selection[0], selection[1]);
        var end = reviewMap.After(selection[2], selection[3] - 1);
        if (paragraphs.Count == 0 ||
            Place(end) is not var (paragraph, offset))
        {
            return null;
        }

        return new(paragraphs[0], paragraphs[^1], paragraph.Ordinal, offset, start, end, Typing: false);
    }

    // A command with nowhere to act says so.
    internal async Task<EditTarget?> RequireAsync(string note = "Click in the document first, or select some of its text.")
    {
        if (!editOpen ||
            !CanWrite ||
            revising)
        {
            return null;
        }

        editNote = null;
        if (await TargetAsync() is { } target)
        {
            return target;
        }

        editNote = note;
        StateHasChanged();
        return null;
    }

    // Hands in what is being typed, so that what is saved, printed or exported is the file as the
    // reader sees it.
    async Task HandInAsync()
    {
        if (!editOpen ||
            handle is not { } viewer)
        {
            return;
        }

        try
        {
            await viewer.EditTargetAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    // An edit that leaves the caret somewhere, and the paragraph open again there.
    async Task EditAtAsync(Func<byte[], EditOptions, EditResult> edit, bool resume = true)
    {
        var options = await EditOptionsAsync();
        EditResult? result = null;
        var edited = await ReviseAsync(
            _ =>
            {
                result = edit(_, options);
                return result.Document;
            },
            relayout: true,
            () => activeKey);
        if (edited &&
            resume &&
            result is {Paragraph: >= 0} landed)
        {
            await ResumeAsync(landed.Paragraph, landed.Offset);
        }
    }

    // An edit to paragraphs as they are, after which the caret goes back where it was.
    internal async Task EditParagraphsAsync(EditTarget target, Func<byte[], EditOptions, byte[]> edit)
    {
        var options = await EditOptionsAsync();
        var edited = await ReviseAsync(_ => edit(_, options), relayout: true, () => activeKey);
        if (edited &&
            target.Typing)
        {
            await ResumeAsync(target.Paragraph, target.Offset);
        }
    }

    // What the ribbon shows of the paragraph the reader is in

    internal ParagraphProperties? CurrentProperties
    {
        get
        {
            if ((session?.Paragraph.Ordinal ?? caretParagraph) is not { } paragraph)
            {
                return null;
            }

            var blocks = editMap.Blocks(paragraph);
            if (blocks.Count == 0)
            {
                return null;
            }

            return blocks[0].Element.Properties;
        }
    }

    EditParagraph? CurrentParagraph
    {
        get
        {
            if ((session?.Paragraph.Ordinal ?? caretParagraph) is { } paragraph &&
                paragraph < outline.Paragraphs.Count)
            {
                return outline.Paragraphs[paragraph];
            }

            return null;
        }
    }

    internal IReadOnlyList<StyleChoice> Styles => outline.Styles;

    // The document's bookmarks as Word's dialog lists them: Word's own hidden ones left out.
    internal IReadOnlyList<BookmarkPlace> Bookmarks =>
        [.. outline.Bookmarks.Where(_ => !_.Hidden)];

    // Word's ruler: the caret's page and paragraph, drawn by the script, whose markers set the
    // paragraph's indents as Word's do.

    internal async Task SetRulerAsync(string? model)
    {
        if (handle is { } viewer)
        {
            await viewer.SetRulerAsync(model);
        }
    }

    /// <summary>
    /// Called from JavaScript when a ruler marker is let go: the paragraph's left and right indents and
    /// its first line's (negative, hanging), in points.
    /// </summary>
    [JSInvokable]
    public Task OnRulerIndent(double left, double right, double firstLine) =>
        InvokeAsync(
            async () =>
            {
                if (await RequireAsync() is not { } target)
                {
                    return;
                }

                var now = PropertiesOf(target.Paragraph);
                var format = new ParagraphFormat();
                if (now == null ||
                    Math.Abs(now.LeftIndentPoints - left) > 0.05)
                {
                    format = format with { LeftPoints = left };
                }

                if (now == null ||
                    Math.Abs(now.RightIndentPoints - right) > 0.05)
                {
                    format = format with { RightPoints = right };
                }

                var first = 0.0;
                if (now != null)
                {
                    first = now.FirstLineIndentPoints - now.HangingIndentPoints;
                }

                if (Math.Abs(first - firstLine) > 0.05)
                {
                    format = format with { FirstLinePoints = firstLine };
                }

                if (format != default)
                {
                    await EditParagraphsAsync(target, (_, options) => DocumentEditor.FormatParagraphs(_, target.Paragraphs, format, options));
                }

                StateHasChanged();
            });

    // Bookmarks: a name for the caret's place or the text selected, which a link to #name goes to.

    internal Task AddBookmarkAsync(EditTarget target, string name) =>
        EditAtAsync((_, options) => DocumentEditor.AddBookmark(_, target.Paragraph, target.Offset, target.Start, target.End, name, options));

    internal Task DeleteBookmarkAsync(EditTarget target, string name) =>
        EditParagraphsAsync(target, (_, options) => DocumentEditor.DeleteBookmark(_, name, options));

    // Opens the paragraph a bookmark starts in, the caret where it starts.
    internal Task GoToBookmarkAsync(string name)
    {
        if (outline.Bookmarks.FirstOrDefault(_ => _.Name == name) is not { } place)
        {
            return Task.CompletedTask;
        }

        return ResumeAsync(place.Paragraph, place.Offset);
    }

    internal string CurrentStyle =>
        CurrentProperties?.StyleId ?? CurrentProperties?.ResolvedStyleId ?? outline.DefaultStyle;

    internal string CurrentStyleName =>
        outline.Styles.FirstOrDefault(_ => _.Id == CurrentStyle)?.Name ?? CurrentStyle;

    internal ListKind CurrentList => ListOf(CurrentProperties);

    internal double CurrentLineSpacing
    {
        get
        {
            if (CurrentProperties is {LineSpacingRule: LineSpacingRule.Auto} properties)
            {
                return properties.LineSpacingMultiplier;
            }

            return 0;
        }
    }

    internal bool HasSpaceBefore => CurrentProperties?.SpacingBeforePoints > 0;

    internal bool HasSpaceAfter => CurrentProperties?.SpacingAfterPoints > 0;

    internal bool InTable => CurrentParagraph?.InTable == true;

    internal bool InLink => CurrentParagraph?.Units.Any(_ => _.Linked) == true;

    static ListKind ListOf(ParagraphProperties? properties)
    {
        if (properties?.Numbering is not { } numbering)
        {
            return ListKind.None;
        }

        if (numbering.Format == ListNumberFormat.Bullet)
        {
            return ListKind.Bullet;
        }

        return ListKind.Number;
    }

    ListKind ListOf(int paragraph)
    {
        var blocks = editMap.Blocks(paragraph);
        if (blocks.Count == 0)
        {
            return ListKind.None;
        }

        return ListOf(blocks[0].Element.Properties);
    }

    // Paragraphs

    internal async Task ApplyStyleAsync(string styleId)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.SetStyle(_, target.Paragraphs, styleId, options));
    }

    // As Word's list buttons do: on, unless every paragraph is in a list of the kind already.
    internal async Task ToggleListAsync(ListKind kind)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        var listed = target.Paragraphs.All(_ => ListOf(_) == kind);
        var chosen = listed ? ListKind.None : kind;
        await EditParagraphsAsync(target, (_, options) => DocumentEditor.SetList(_, target.Paragraphs, chosen, options));
    }

    // Enter in an empty paragraph of a list ends the list there, as Word's does: the paragraph is
    // left out of it, at no indent.
    internal async Task EndListAsync()
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.SetList(_, [target.Paragraph], ListKind.None, options));
    }

    // Backspace at the start of a list's paragraph takes its number off, and at an indented one's a
    // step of its indent; with neither, it joins the paragraph to the one before, as anywhere else.
    internal async Task UnindentAsync()
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        if (outline.Paragraphs[target.Paragraph].Indented)
        {
            await EditParagraphsAsync(target, (_, options) => DocumentEditor.Unindent(_, target.Paragraph, options));
            return;
        }

        if (target.Paragraph == 0 ||
            !outline.Paragraphs[target.Paragraph].HasPrevious)
        {
            return;
        }

        var options = await EditOptionsAsync();
        EditResult? result = null;
        var joined = await ReviseAsync(
            _ =>
            {
                result = DocumentEditor.Join(_, target.Paragraph - 1, options);
                return result.Document;
            },
            relayout: true,
            () => activeKey);
        if (joined &&
            result is {Paragraph: >= 0} met)
        {
            await ResumeAsync(met.Paragraph, met.Offset);
        }
    }

    // Pasting HTML: what it keeps is PastedHtml's to say, and the text goes in where the caret was,
    // which is where the editor is opened again — after it.
    internal async Task PasteAsync(string html, string text)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        var content = PastedHtml.Read(html);
        if (content.Count == 0)
        {
            content = text.Replace("\r\n", "\n").Split('\n').Select(_ => new PastedParagraph([new(_)])).ToList();
        }

        var options = await EditOptionsAsync();
        EditResult? result = null;
        var pasted = await ReviseAsync(
            _ =>
            {
                result = DocumentEditor.Paste(_, target.Paragraph, target.Offset, content, options);
                return result.Document;
            },
            relayout: true,
            () => activeKey);
        if (pasted &&
            result is { } end)
        {
            await ResumeAsync(end.Paragraph, end.Offset);
        }
    }

    // Tab and Shift+Tab in a list: the list's first paragraph moves the whole list, any other a level.
    internal async Task TabInListAsync(int steps)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.TabInList(_, target.Paragraphs, steps, options));
    }

    // Enter at the end of a heading: the paragraph it began is in the file, in the style the heading
    // names next (DocumentEditor.Rewrite gives it that), and typing goes on in it.
    internal async Task FollowOnAsync()
    {
        if (await TargetAsync() is {Typing: true} target)
        {
            await ResumeAsync(target.Paragraph, target.Offset);
        }
    }

    internal async Task IndentAsync(int steps)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.Indent(_, target.Paragraphs, steps, options));
    }

    internal async Task SpaceAsync(ParagraphSpacing spacing)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.Space(_, target.Paragraphs, spacing, options));
    }

    internal Task ToggleSpaceBeforeAsync()
    {
        var before = addedSpace;
        if (HasSpaceBefore)
        {
            before = 0;
        }

        return SpaceAsync(new(BeforePoints: before));
    }

    internal Task ToggleSpaceAfterAsync()
    {
        var after = addedSpace;
        if (HasSpaceAfter)
        {
            after = 0;
        }

        return SpaceAsync(new(AfterPoints: after));
    }

    // Inserting

    internal async Task InsertPageBreakAsync()
    {
        if (await RequireAsync("Click in the document where the page is to break.") is not { } target)
        {
            return;
        }

        await EditAtAsync((_, options) => DocumentEditor.InsertPageBreak(_, target.Paragraph, target.Offset, options));
    }

    internal async Task InsertTableAsync(int rows, int columns)
    {
        if (await RequireAsync("Click in the document where the table is to go.") is not { } target)
        {
            return;
        }

        await EditAtAsync((_, options) => DocumentEditor.InsertTable(_, target.Paragraph, target.Offset, rows, columns, options));
    }

    internal async Task EditTableAsync(TableEdit change)
    {
        if (await RequireAsync("Click in a table first.") is not { } target)
        {
            return;
        }

        await EditAtAsync((_, options) => DocumentEditor.EditTable(_, target.Paragraph, change, options));
    }

    // Headers, footers and footnotes. A header or footer is opened for typing, made if the section
    // has none; a footnote is put at the caret and its note opened.
    internal async Task EditHeaderAsync(bool footer)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditAtAsync((_, options) => DocumentEditor.EditHeader(_, target.Paragraph, footer, options));
    }

    internal async Task InsertFootnoteAsync()
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditAtAsync((_, options) => DocumentEditor.InsertFootnote(_, target.Paragraph, target.Offset, options));
    }

    internal async Task InsertEndnoteAsync()
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditAtAsync((_, options) => DocumentEditor.InsertEndnote(_, target.Paragraph, target.Offset, options));
    }

    // A section's page: margins, which way up, the paper, and breaks between sections. They act on the
    // section the caret is in.

    // Word's margin presets, in twips: top, right, bottom, left.
    internal static readonly (string Name, int Top, int Right, int Bottom, int Left)[] MarginPresets =
    [
        ("Normal", 1440, 1440, 1440, 1440),
        ("Narrow", 720, 720, 720, 720),
        ("Moderate", 1440, 1080, 1440, 1080),
        ("Wide", 1440, 2880, 1440, 2880)
    ];

    // Word's paper sizes, upright, in twips.
    internal static readonly (string Name, int Width, int Height)[] PaperSizes =
    [
        ("Letter", 12240, 15840),
        ("Legal", 12240, 20160),
        ("Executive", 10440, 15120),
        ("A3", 16838, 23811),
        ("A4", 11906, 16838),
        ("A5", 8391, 11906)
    ];

    byte[]? pageBytes;
    int pageParagraph = -1;
    PageSetup? page;

    // The page of the section the caret is in, read once for each file and paragraph.
    internal PageSetup? CurrentPage
    {
        get
        {
            var paragraph = session?.Paragraph.Ordinal ?? caretParagraph ?? 0;
            if (sourceBytes is not { } bytes ||
                !IsWord)
            {
                return null;
            }

            if (!ReferenceEquals(bytes, pageBytes) ||
                paragraph != pageParagraph)
            {
                pageBytes = bytes;
                pageParagraph = paragraph;
                try
                {
                    page = DocumentEditor.PageSetupOf(bytes, paragraph);
                }
                catch (Exception)
                {
                    page = null;
                }
            }

            return page;
        }
    }

    internal async Task SetMarginsAsync(int top, int right, int bottom, int left)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.SetMargins(_, target.Paragraph, top, right, bottom, left, options));
    }

    internal async Task SetOrientationAsync(bool landscape)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.SetOrientation(_, target.Paragraph, landscape, options));
    }

    internal async Task SetPaperSizeAsync(int width, int height)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.SetPaperSize(_, target.Paragraph, width, height, options));
    }

    internal async Task InsertSectionBreakAsync(bool continuous)
    {
        if (await RequireAsync() is not { } target)
        {
            return;
        }

        await EditAtAsync((_, options) => DocumentEditor.InsertSectionBreak(_, target.Paragraph, target.Offset, continuous, options));
    }

    // A picture sized: the one selected, or the one just before the caret.
    const string pictureNote = "Click a picture first.";

    internal async Task ResizePictureAsync(double width, double height)
    {
        if (await RequireAsync(pictureNote) is not { } target)
        {
            return;
        }

        var (paragraph, offset) = PictureAt(target);
        await EditParagraphsAsync(target, (_, options) => DocumentEditor.ResizePicture(_, paragraph, offset, width, height, options));
    }

    // Back to the size its image was made for, as when it was put in.
    internal async Task ResetPictureAsync()
    {
        if (await RequireAsync(pictureNote) is not { } target ||
            sourceBytes is not { } bytes)
        {
            return;
        }

        var (paragraph, offset) = PictureAt(target);
        if (DocumentEditor.PictureData(bytes, paragraph, offset) is not var (data, _) ||
            Picture(data, "") is not { } natural)
        {
            editNote = "That picture's size could not be read.";
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.ResizePicture(_, paragraph, offset, natural.WidthPoints, natural.HeightPoints, options));
    }

    static (int Paragraph, int Offset) PictureAt(EditTarget target)
    {
        if (target is { HasSelection: true, StartParagraph: >= 0 })
        {
            return (target.StartParagraph, target.StartOffset);
        }

        return (target.Paragraph, Math.Max(0, target.Offset - 1));
    }

    // A table's look and its cells: borders, shading, merging and splitting, widths. They act on the
    // table the caret is in, and shading and merging on the cells from where a selection starts to
    // where it ends.

    // Word's border widths, in eighths of a point.
    internal static readonly (int Size, string Name)[] BorderSizes = [(4, "½ pt"), (8, "1 pt"), (12, "1½ pt"), (18, "2¼ pt"), (24, "3 pt")];

    internal int BorderSize { get; set; } = 4;

    internal string LastShading { get; set; } = "D9D9D9";

    const string tableNote = "Click in a table first.";

    internal async Task SetTableBordersAsync(BorderPreset preset)
    {
        if (await RequireAsync(tableNote) is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.SetTableBorders(_, target.Paragraph, preset, BorderSize, options));
    }

    internal async Task ShadeCellsAsync(string? fill)
    {
        if (fill != null)
        {
            LastShading = fill;
        }

        if (await RequireAsync(tableNote) is not { } target)
        {
            return;
        }

        var (first, last) = Cells(target);
        await EditParagraphsAsync(target, (_, options) => DocumentEditor.ShadeCells(_, first, last, fill, options));
    }

    internal async Task MergeCellsAsync()
    {
        if (await RequireAsync(tableNote) is not { } target)
        {
            return;
        }

        var (first, last) = Cells(target);
        await EditAtAsync((_, options) => DocumentEditor.MergeCells(_, first, last, options));
    }

    internal async Task SplitCellAsync(int columns, int rows)
    {
        if (await RequireAsync(tableNote) is not { } target)
        {
            return;
        }

        await EditAtAsync((_, options) => DocumentEditor.SplitCell(_, target.Paragraph, columns, rows, options));
    }

    // A width in centimetres, as the ribbon takes it: 567 twips to the centimetre.
    internal async Task SetColumnWidthAsync(double centimetres)
    {
        if (centimetres <= 0 ||
            await RequireAsync(tableNote) is not { } target)
        {
            return;
        }

        var twips = (int) Math.Round(centimetres * 1440 / 2.54);
        await EditParagraphsAsync(target, (_, options) => DocumentEditor.SetColumnWidth(_, target.Paragraph, twips, options));
    }

    internal async Task DistributeColumnsAsync()
    {
        if (await RequireAsync(tableNote) is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.DistributeColumns(_, target.Paragraph, options));
    }

    internal async Task FitTableAsync(TableFit fit)
    {
        if (await RequireAsync(tableNote) is not { } target)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.FitTable(_, target.Paragraph, fit, options));
    }

    // Where a selection of cells starts and ends, in paragraphs; with none, the caret's cell.
    static (int First, int Last) Cells(EditTarget target)
    {
        if (target is
            {
                HasSelection: true,
                StartParagraph: >= 0
            })
        {
            return (Math.Min(target.StartParagraph, target.Paragraph), Math.Max(target.StartParagraph, target.Paragraph));
        }

        if (target.First != target.Last)
        {
            return (target.First, target.Last);
        }

        return (target.Paragraph, target.Paragraph);
    }

    // Where a picture goes is taken when the reader asks for one, before the file picker opens: by the
    // time a file is picked, focus has been elsewhere.
    Task<EditTarget?>? pictureTarget;

    internal void BeginPicture() =>
        pictureTarget = TargetAsync();

    internal async Task InsertPictureAsync(InputFileChangeEventArgs args)
    {
        var target = pictureTarget is { } pending ? await pending : await TargetAsync();
        pictureTarget = null;
        if (!editOpen ||
            !CanWrite)
        {
            return;
        }

        if (target == null)
        {
            editNote = "Click in the document where the picture is to go.";
            return;
        }

        try
        {
            await using var stream = args.File.OpenReadStream(MaxFileSize);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            if (Picture(bytes, args.File.Name) is not { } picture)
            {
                editNote = $"'{args.File.Name}' is not a picture this can put in a document: choose a PNG, JPEG, GIF or BMP.";
                return;
            }

            await EditAtAsync((_, options) => DocumentEditor.InsertPicture(_, target.Paragraph, target.Offset, picture, options));
        }
        catch (IOException exception)
        {
            ReportError($"Could not read '{args.File.Name}'", exception);
        }
    }

    // A picture's size is its pixels at the resolution it says it was made for, 96 DPI when it says
    // none — as Word sizes a picture it inserts.
    static NewPicture? Picture(byte[] bytes, string name)
    {
        ImageInfo info;
        try
        {
            info = Image.Identify(bytes);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return null;
        }

        if (info.Metadata.DecodedImageFormat?.DefaultMimeType is not ("image/png" or "image/jpeg" or "image/gif" or "image/bmp") ||
            info.Width <= 0 ||
            info.Height <= 0)
        {
            return null;
        }

        var dpiX = Dpi(info.Metadata.HorizontalResolution, info.Metadata.ResolutionUnits);
        var dpiY = Dpi(info.Metadata.VerticalResolution, info.Metadata.ResolutionUnits);
        return new(
            bytes,
            info.Metadata.DecodedImageFormat.DefaultMimeType,
            info.Width * 72d / dpiX,
            info.Height * 72d / dpiY,
            Path.GetFileNameWithoutExtension(name));

        static double Dpi(double resolution, PixelResolutionUnit unit)
        {
            var dpi = unit switch
            {
                PixelResolutionUnit.PixelsPerInch => resolution,
                PixelResolutionUnit.PixelsPerCentimeter => resolution * 2.54,
                PixelResolutionUnit.PixelsPerMeter => resolution * 0.0254,
                _ => 0
            };
            if (dpi is < 24 or > 2400)
            {
                return 96;
            }

            return dpi;
        }
    }

    // Links: the dialog asks for the address, so the place is taken when it opens.
    internal EditTarget? linkTarget;

    internal async Task<bool> BeginLinkAsync()
    {
        if (await RequireAsync("Select the text to make a link.") is not { } target)
        {
            return false;
        }

        if (!target.HasSelection &&
            !InLink)
        {
            editNote = "Select the text to make a link.";
            StateHasChanged();
            return false;
        }

        linkTarget = target;
        return true;
    }

    internal Task LinkAsync(string address)
    {
        if (linkTarget is not
            {
                Start: { } start,
                End: { } end
            } target)
        {
            return Task.CompletedTask;
        }

        linkTarget = null;
        if (!address.StartsWith('#') &&
            !address.Contains("://", StringComparison.Ordinal) &&
            !address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            address = $"https://{address}";
        }

        return EditParagraphsAsync(target, (_, options) => DocumentEditor.Link(_, start, end, address, options));
    }

    internal async Task UnlinkAsync()
    {
        var target = linkTarget ?? await RequireAsync();
        linkTarget = null;
        if (target == null)
        {
            return;
        }

        var start = target.Start ?? CaretPosition(target);
        var end = target.End ?? start;
        if (start is not { } from ||
            end is not { } to)
        {
            return;
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.Unlink(_, from, to, options));
    }

    SourcePosition? CaretPosition(EditTarget target)
    {
        if (target.Paragraph >= outline.Paragraphs.Count)
        {
            return null;
        }

        var paragraph = outline.Paragraphs[target.Paragraph];
        return paragraph.PositionAt(target.Offset, ending: true) ?? paragraph.PositionAt(target.Offset, ending: false);
    }

    // Find and replace

    internal Task OpenReplaceAsync()
    {
        replaceOpen = true;
        focusReplace = findQuery.Trim().Length > 0;
        replaceNote = null;
        return OpenFindAsync();
    }

    void OnReplaceInput(ChangeEventArgs args) =>
        replaceText = args.Value as string ?? "";

    Task OnReplaceKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter":
                return ReplaceOneAsync();
            case "Escape":
                return CloseFindAsync();
        }

        return Task.CompletedTask;
    }

    // A match as a stretch of one paragraph; null for one that runs on from one paragraph to the next.
    TextRange? RangeOf(TextMatch match)
    {
        if (match.Length <= 0 ||
            Place(reviewMap.Before(match.Page, match.Start)) is not var (first, from) ||
            Place(reviewMap.After(match.Page, match.Start + match.Length - 1)) is not var (last, to) ||
            first.Ordinal != last.Ordinal ||
            to <= from)
        {
            return null;
        }

        return new(first.Ordinal, from, to);
    }

    async Task ReplaceOneAsync()
    {
        if (!CanWrite ||
            revising ||
            matches.Count == 0)
        {
            return;
        }

        await HandInAsync();
        var current = Math.Clamp(currentMatch, 0, matches.Count - 1);
        if (RangeOf(matches[current]) is not { } range)
        {
            replaceNote = "That match runs from one paragraph into the next, and is left as it is.";
            await StepMatchAsync(1);
            return;
        }

        var text = replaceText;
        var replaced = matches[current];
        var options = await EditOptionsAsync();
        if (await ReviseAsync(_ => DocumentEditor.Replace(_, [range], text, options), relayout: true, () => activeKey))
        {
            replaceNote = null;

            // On to the first match after what was put in, as Word's Replace goes: the text put in
            // can match itself — "e" replaced with "E", the case not matched — and is not found again.
            var after = replaced.Start + text.Length;
            currentMatch = -1;
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                if (match.Page > replaced.Page ||
                    (match.Page == replaced.Page && match.Start >= after))
                {
                    currentMatch = index;
                    break;
                }
            }

            if (currentMatch < 0 &&
                matches.Count > 0)
            {
                currentMatch = 0;
            }

            if (handle is { } viewer)
            {
                await PushFindAsync(viewer);
            }
        }
    }

    async Task ReplaceAllAsync()
    {
        if (!CanWrite ||
            revising ||
            matches.Count == 0)
        {
            return;
        }

        await HandInAsync();
        var ranges = matches.Select(RangeOf).OfType<TextRange>().ToList();
        var skipped = matches.Count - ranges.Count;
        if (ranges.Count == 0)
        {
            replaceNote = "Every match runs from one paragraph into the next, and none was replaced.";
            return;
        }

        var text = replaceText;
        var options = await EditOptionsAsync();
        if (await ReviseAsync(_ => DocumentEditor.Replace(_, ranges, text, options), relayout: true, () => activeKey))
        {
            replaceNote = $"Replaced {ranges.Count}{(ranges.Count == TextSearch.MaxMatches ? "+" : "")}";
            if (skipped > 0)
            {
                replaceNote += $"; {skipped} running across paragraphs left as they were";
            }
        }
    }

    const string bodyOnly = "Comments go on the body's text, not a header's, a footer's or a note's.";

    // Whether a place is in the body, where comments are anchored; one the pages could not place is
    // taken as in it.
    bool InBody(SourcePosition? place) =>
        place is not { } known ||
        outline.ParagraphOf(known.Run)?.Part is null or ReviewPart.Body;

    // Counting

    internal DocumentCount Count()
    {
        var words = 0;
        var characters = 0;
        var spaced = 0;
        var paragraphs = 0;
        var lines = 0;
        // The body's words, as Word's status bar counts them: not the headers', footers' or notes'.
        foreach (var paragraph in outline.Paragraphs.Where(_ => _.Part == ReviewPart.Body))
        {
            var text = new StringBuilder();
            foreach (var unit in paragraph.Units)
            {
                if (unit.Kind is EditUnitKind.Text or EditUnitKind.Field)
                {
                    text.Append(unit.Text);
                }
            }

            var counted = DocumentCount.Of(text.ToString());
            words += counted.Words;
            characters += counted.Characters;
            spaced += counted.CharactersWithSpaces;
            if (counted.Words > 0)
            {
                paragraphs++;
            }

            lines += editMap.Blocks(paragraph.Ordinal).Sum(_ => _.Lines.Count);
        }

        return new(words, characters, spaced, paragraphs, lines, pageCount);
    }

    // What is selected on the pages, counted: null when nothing is.
    internal DocumentCount? CountSelected()
    {
        if (selection.Length != 4)
        {
            return null;
        }

        return DocumentCount.Of(SelectedText());
    }

    // Saving and exporting

    internal async Task ExportAsync(OutputFormat format)
    {
        if (sourceInfo is not { } info ||
            fontDirectory is not { } fonts ||
            ConversionService.Find(format) is not { } output)
        {
            return;
        }

        await HandInAsync();
        if (sourceBytes is not { } bytes)
        {
            return;
        }

        try
        {
            BeginBusy($"Exporting {output.DisplayName}…");
            await Task.Delay(1, lifetime.Token);
            var payload = await Task.Run(() => ConversionService.BuildDownload(bytes, info.Format, format, new(), fonts), lifetime.Token);
            var name = Path.ChangeExtension(fileName ?? info.SampleFileName, payload.Extension);
            await Interop.DownloadAsync(name, payload.ContentType, payload.Bytes);
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (Exception exception)
        {
            ReportError($"Could not export the {output.DisplayName}", exception);
        }
        finally
        {
            EndBusy();
            if (!disposed)
            {
                StateHasChanged();
            }
        }
    }

    // A new document

    internal async Task NewDocumentAsync()
    {
        await HandInAsync();
        try
        {
            BeginBusy("Starting a new document…");
            var bytes = await Http.GetByteArrayAsync(BlankAsset);
            var fonts = await FontStore.EnsureAsync(Http);
            await OpenAsync(bytes, $"Document{++untitled}.docx", fonts);
            if (editOpen &&
                outline.Paragraphs.Count > 0)
            {
                await ResumeAsync(0, 0);
            }
        }
        catch (Exception exception)
        {
            EndBusy();
            ReportError("Could not start a new document", exception);
        }
    }

    // Character formatting: what the Font group of the ribbon sets. It acts on the text selected, or
    // else on the word the caret is in, as Word's does; and the paragraph is open again afterwards
    // with the same text selected.

    // Word's font size list.
    internal static readonly double[] FontSizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    internal static readonly string[] CommonFonts =
    [
        "Aptos", "Aptos Display", "Arial", "Calibri", "Calibri Light", "Cambria", "Candara", "Consolas", "Constantia",
        "Corbel", "Courier New", "Georgia", "Segoe UI", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana"
    ];

    // Word's standard colours, the row under its theme colours, and a row of greys.
    internal static readonly (string Hex, string Name)[] TextColors =
    [
        ("C00000", "Dark red"), ("FF0000", "Red"), ("FFC000", "Orange"), ("FFFF00", "Yellow"), ("92D050", "Light green"),
        ("00B050", "Green"), ("00B0F0", "Light blue"), ("0070C0", "Blue"), ("002060", "Dark blue"), ("7030A0", "Purple"),
        ("000000", "Black"), ("404040", "Dark grey"), ("7F7F7F", "Grey"), ("BFBFBF", "Light grey"), ("FFFFFF", "White")
    ];

    // Word's highlight colours, by the names the file writes them under.
    internal static readonly (string Value, string Hex, string Name)[] Highlights =
    [
        ("yellow", "FFFF00", "Yellow"), ("green", "00FF00", "Bright green"), ("cyan", "00FFFF", "Turquoise"),
        ("magenta", "FF00FF", "Pink"), ("blue", "0000FF", "Blue"), ("red", "FF0000", "Red"),
        ("darkBlue", "000080", "Dark blue"), ("darkCyan", "008080", "Teal"), ("darkGreen", "008000", "Green"),
        ("darkMagenta", "800080", "Violet"), ("darkRed", "800000", "Dark red"), ("darkYellow", "808000", "Dark yellow"),
        ("darkGray", "808080", "Grey 50%"), ("lightGray", "C0C0C0", "Grey 25%"), ("black", "000000", "Black")
    ];

    // Where the paragraph was opened, as a place whose character before it is the one the ribbon
    // shows the look of.
    int caretOffset;

    internal string LastColor { get; set; } = "FF0000";

    internal string LastHighlight { get; set; } = "yellow";

    internal IReadOnlyList<string> DocumentFonts => outline.Fonts;

    // The look of the text the caret is in: that of the character before it, or of the first at a
    // paragraph's start.
    RunProperties? CurrentRun
    {
        get
        {
            if (CurrentParagraph is not { } paragraph)
            {
                return null;
            }

            return RunAt(paragraph, caretOffset);
        }
    }

    // The look of the character before a place in a paragraph, or else of its first text.
    RunProperties? RunAt(EditParagraph paragraph, int offset)
    {
        var at = 0;
        RunProperties? found = null;
        foreach (var unit in paragraph.Units)
        {
            var span = unit.Editable ? unit.Text.Length : 1;
            if (unit.Editable &&
                editMap.Format(unit.Run) is { } format)
            {
                found ??= format;
                if (offset > at &&
                    offset <= at + span)
                {
                    return format;
                }
            }

            at += span;
        }

        return found;
    }

    // The look of the first character of the text between two places of a paragraph.
    internal RunProperties? LookOf(int paragraph, int from)
    {
        if (paragraph >= outline.Paragraphs.Count)
        {
            return null;
        }

        return RunAt(outline.Paragraphs[paragraph], from + 1);
    }

    internal string CurrentFont => CurrentRun?.FontFamily ?? DefaultFontSettings.DefaultFont;

    internal double CurrentSize => CurrentRun?.FontSizePoints ?? 11;

    internal bool IsSuperscript => CurrentRun?.VerticalAlignment == VerticalRunAlignment.Superscript;

    internal bool IsSubscript => CurrentRun?.VerticalAlignment == VerticalRunAlignment.Subscript;

    // Word's Grow Font and Shrink Font: to the next size on the list, or past its ends a point at a time.
    internal static double Step(double size, int steps)
    {
        if (steps > 0)
        {
            foreach (var each in FontSizes)
            {
                if (each > size + 0.01)
                {
                    return each;
                }
            }

            return Math.Min(1638, Math.Floor(size) + 1);
        }

        for (var index = FontSizes.Length - 1; index >= 0; index--)
        {
            if (FontSizes[index] < size - 0.01)
            {
                return FontSizes[index];
            }
        }

        return Math.Max(1, Math.Ceiling(size) - 1);
    }

    // What character formatting acts on: the text selected — in the paragraph open for typing, or
    // across the pages — or the word the caret is in.
    internal async Task<(EditTarget Target, SourcePosition Start, SourcePosition End, int From, int To)?> TextAsync()
    {
        const string note = "Select the text to format, or click in a word.";
        if (await RequireAsync(note) is not { } target)
        {
            return null;
        }

        if (target is {HasSelection: true, Start: { } start, End: { } end})
        {
            return (target, start, end, target.StartOffset, target.Offset);
        }

        if (Word(target) is var (from, to) &&
            outline.Paragraphs[target.Paragraph].PositionAt(from, ending: false) is { } first &&
            outline.Paragraphs[target.Paragraph].PositionAt(to, ending: true) is { } last)
        {
            return (target with {StartParagraph = target.Paragraph}, first, last, from, to);
        }

        editNote = note;
        StateHasChanged();
        return null;
    }

    // The word a caret stands inside: letters and digits either side of it.
    (int From, int To)? Word(EditTarget target)
    {
        if (target.Paragraph >= outline.Paragraphs.Count)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var unit in outline.Paragraphs[target.Paragraph].Units)
        {
            text.Append(unit.Editable ? unit.Text : "￼");
        }

        var caret = target.Offset;
        if (caret <= 0 ||
            caret >= text.Length ||
            !Letter(text[caret - 1]) ||
            !Letter(text[caret]))
        {
            return null;
        }

        var from = caret;
        while (from > 0 &&
               Letter(text[from - 1]))
        {
            from--;
        }

        var to = caret;
        while (to < text.Length &&
               Letter(text[to]))
        {
            to++;
        }

        return (from, to);

        static bool Letter(char character) =>
            char.IsLetterOrDigit(character) || character is '\'' or '’';
    }

    async Task FormatTextAsync(Func<SourcePosition, SourcePosition, EditOptions, Func<byte[], byte[]>> edit)
    {
        if (await TextAsync() is { } text)
        {
            await FormatTextAsync(text, edit);
        }
    }

    // Formats text taken before, as a dialog takes it when it opens.
    internal async Task FormatTextAsync((EditTarget Target, SourcePosition Start, SourcePosition End, int From, int To) text, Func<SourcePosition, SourcePosition, EditOptions, Func<byte[], byte[]>> edit)
    {
        var (target, start, end, from, to) = text;
        var options = await EditOptionsAsync();
        if (!await ReviseAsync(edit(start, end, options), relayout: true, () => activeKey) ||
            !target.Typing)
        {
            return;
        }

        // Formatting changes no text: the paragraph opens again with the same text selected.
        if (target.StartParagraph == target.Paragraph &&
            handle is { } viewer &&
            target.Paragraph < outline.Paragraphs.Count &&
            Block(target.Paragraph, -1) is { } block)
        {
            await BeginAsync(viewer, outline.Paragraphs[target.Paragraph], block, from, to);
            return;
        }

        await ResumeAsync(target.Paragraph, target.Offset);
    }

    // The look of a paragraph and of a run as the pages drew them, which the Paragraph and Font
    // dialogs start from.
    internal ParagraphProperties? PropertiesOf(int paragraph)
    {
        var blocks = editMap.Blocks(paragraph);
        if (blocks.Count == 0)
        {
            return null;
        }

        return blocks[0].Element.Properties;
    }

    internal Task FormatAsync(RunFormat format) =>
        FormatTextAsync((start, end, options) => _ => DocumentEditor.Format(_, start, end, format, options));

    internal Task SetFontAsync(string font) =>
        FormatAsync(new(Font: font));

    internal Task SetSizeAsync(double size) =>
        FormatAsync(new(SizePoints: size));

    internal Task GrowAsync(int steps) =>
        SetSizeAsync(Step(CurrentSize, steps));

    internal Task SetColorAsync(string color)
    {
        if (color != "auto")
        {
            LastColor = color;
        }

        return FormatAsync(new(Color: color));
    }

    internal Task SetHighlightAsync(string highlight)
    {
        if (highlight != "none")
        {
            LastHighlight = highlight;
        }

        return FormatAsync(new(Highlight: highlight));
    }

    // As Word's buttons do: on, unless the text the caret is in is that already.
    internal Task ToggleSuperscriptAsync()
    {
        var vertical = VerticalPositionValues.Superscript;
        if (IsSuperscript)
        {
            vertical = VerticalPositionValues.Baseline;
        }

        return FormatAsync(new(Vertical: vertical));
    }

    internal Task ToggleSubscriptAsync()
    {
        var vertical = VerticalPositionValues.Subscript;
        if (IsSubscript)
        {
            vertical = VerticalPositionValues.Baseline;
        }

        return FormatAsync(new(Vertical: vertical));
    }

    internal Task ClearFormattingAsync() =>
        FormatTextAsync((start, end, options) => _ => DocumentEditor.ClearFormatting(_, start, end, options));
}
