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
            Quote = Text(place[4], place[5], place[6], place[7])
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
            await OpenReviewAsync();
            await BeginCommentAsync();
            return;
        }

        if (await TargetAsync() is not {HasSelection: true, Start: { } start, End: { } end} target)
        {
            editNote = "Select the text to comment on first.";
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

    async Task OpenReviewAsync()
    {
        if (!reviewOpen)
        {
            await ToggleReviewAsync();
        }
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
    async Task<EditTarget?> RequireAsync(string note = "Click in the document first, or select some of its text.")
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
    async Task EditAtAsync(EditTarget target, Func<byte[], EditOptions, EditResult> edit, bool resume = true)
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
    async Task EditParagraphsAsync(EditTarget target, Func<byte[], EditOptions, byte[]> edit)
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

    ParagraphProperties? CurrentProperties
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

        await EditAtAsync(target, (_, options) => DocumentEditor.InsertPageBreak(_, target.Paragraph, target.Offset, options));
    }

    internal async Task InsertTableAsync(int rows, int columns)
    {
        if (await RequireAsync("Click in the document where the table is to go.") is not { } target)
        {
            return;
        }

        await EditAtAsync(target, (_, options) => DocumentEditor.InsertTable(_, target.Paragraph, target.Offset, rows, columns, options));
    }

    internal async Task EditTableAsync(TableEdit change)
    {
        if (await RequireAsync("Click in a table first.") is not { } target)
        {
            return;
        }

        await EditAtAsync(target, (_, options) => DocumentEditor.EditTable(_, target.Paragraph, change, options));
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

            await EditAtAsync(target, (_, options) => DocumentEditor.InsertPicture(_, target.Paragraph, target.Offset, picture, options));
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

    internal async Task LinkAsync(string address)
    {
        if (linkTarget is not {Start: { } start, End: { } end} target)
        {
            return;
        }

        linkTarget = null;
        if (!address.StartsWith('#') &&
            !address.Contains("://", StringComparison.Ordinal) &&
            !address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            address = $"https://{address}";
        }

        await EditParagraphsAsync(target, (_, options) => DocumentEditor.Link(_, start, end, address, options));
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

    internal async Task OpenReplaceAsync()
    {
        replaceOpen = true;
        focusReplace = findQuery.Trim().Length > 0;
        replaceNote = null;
        await OpenFindAsync();
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
        var page = matches[current].Page;
        var options = await EditOptionsAsync();
        if (await ReviseAsync(_ => DocumentEditor.Replace(_, [range], text, options), relayout: true, () => activeKey))
        {
            replaceNote = null;

            // The match after the one replaced is the first from where it was.
            currentMatch = matches.Count == 0 ? -1 : Math.Min(current, matches.Count - 1);
            if (currentMatch >= 0 &&
                matches[currentMatch].Page < page)
            {
                currentMatch = FirstMatchFrom(page);
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

    // Counting

    internal DocumentCount Count()
    {
        var words = 0;
        var characters = 0;
        var spaced = 0;
        var paragraphs = 0;
        var lines = 0;
        foreach (var paragraph in outline.Paragraphs)
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
}
