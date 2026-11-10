namespace Morph;

// An Excel workbook in the editor: MorphEditor shows it as a grid (SheetEditor) rather than as pages, and
// the grid's edits come back here as the editor's own do — the file replaced, a draft kept, the host told —
// so Save, Export and the drafts work on the workbook as the reader sees it. The pages laid out when it was
// opened are what printing reads, so after an edit they are laid out again, once, when they are next wanted.
public partial class MorphViewer
{
    bool pagesStale;

    // Counts the files opened, so the grid knows a new one from this one edited.
    int opens;

    /// <summary>Whether the file is a workbook the editor shows as a grid.</summary>
    internal bool IsSheet =>
        Editing &&
        sourceInfo?.Format == InputFormat.Xlsx;

    internal int OpenCount => opens;

    internal string? FontDirectoryPath => fontDirectory;

    internal byte[]? SourceBytes => sourceBytes;

    int untitledBooks;

    /// <summary>A new, empty workbook, as Excel's File &gt; New starts one.</summary>
    internal async Task NewWorkbookAsync()
    {
        await HandInAsync();
        try
        {
            BeginBusy("Starting a new workbook…");
            var fonts = await FontStore.EnsureAsync(Http);
            await OpenAsync(WorkbookEditor.Blank(), $"Book{++untitledBooks}.xlsx", fonts);
        }
        catch (Exception exception)
        {
            EndBusy();
            ReportError("Could not start a new workbook", exception);
        }
    }

    // MorphEditor's: what is being typed in a cell of the grid, handed in before the file is saved, exported
    // or printed.
    internal virtual Task FinishSheetTypingAsync() =>
        Task.CompletedTask;

    /// <summary>The grid's edit made: the workbook it made is the file now.</summary>
    internal async Task SheetEditedAsync(byte[] bytes)
    {
        if (ReferenceEquals(bytes, sourceBytes))
        {
            return;
        }

        sourceBytes = bytes;
        emitted = bytes;
        pagesStale = true;
        try
        {
            await DocumentRevisedAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }

        await OnDocumentChanged.InvokeAsync(bytes);
        StateHasChanged();
    }

    // The pages laid out again from the workbook as edited, if it has been since they were.
    async Task RefreshPagesAsync()
    {
        if (!pagesStale ||
            sourceBytes is not { } bytes ||
            fontDirectory is not { } fonts ||
            sourceInfo is not { } info ||
            handle is not { } viewer)
        {
            return;
        }

        pagesStale = false;
        var opening = ++generation;
        var opened = await Task.Run(() => Open(bytes, info.Format, fonts, false), lifetime.Token);
        if (opening != generation || disposed)
        {
            opened.Document.Dispose();
            return;
        }

        var previous = document;
        Adopt(opened);
        documentId = opening;
        renderQueue = [];
        previous?.Dispose();
        currentPage = Math.Clamp(currentPage, 1, Math.Max(1, pageCount));
        pageInput = currentPage.ToString(CultureInfo.InvariantCulture);
        await viewer.ReloadAsync(opening, pageSizes, opened.Json);
    }
}
