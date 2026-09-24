namespace Morph;

/// <summary>
/// Shows a Word, Excel or PowerPoint file the way a browser shows a PDF — without converting it to anything.
/// Pages render on demand as they scroll into view, sharpening when zoomed, and every word on them is
/// selectable, copyable and findable. The toolbar mirrors a browser PDF viewer: a thumbnail sidebar, page
/// navigation, zoom presets and fit modes, rotation, find, presentation mode, printing, download and open.
///
/// The file comes from <see cref="Source"/> (with <see cref="FileName"/>), from <see cref="Url"/>, or from
/// the user — the Open button, a file dropped on the viewer, or a bundled sample. It is parsed and laid out
/// once; after that only the pages in view are painted, one at a time, so a long document opens fast and
/// stays responsive on the single-threaded WebAssembly runtime.
///
/// The viewer fills its container: give it a height (the stylesheet defaults to 80vh). The host needs the
/// same setup as <see cref="MorphConverter"/> — <c>AddMorph()</c>, a base-addressed <see cref="HttpClient"/>
/// and the stylesheet.
/// </summary>
public partial class MorphViewer : IAsyncDisposable
{
    /// <summary>The file to show. Changing the reference opens the new file. Needs <see cref="FileName"/>.</summary>
    [Parameter]
    public byte[]? Source { get; set; }

    /// <summary>
    /// The file's name: its extension says which format <see cref="Source"/> is (the bytes alone cannot),
    /// and it names the file the Download button saves.
    /// </summary>
    [Parameter]
    public string? FileName { get; set; }

    /// <summary>A file to fetch and show, when <see cref="Source"/> is not set. Fetched with the injected <see cref="HttpClient"/>.</summary>
    [Parameter]
    public string? Url { get; set; }

    /// <summary>Whether the toolbar offers Open (and a dropped file opens). Default true.</summary>
    [Parameter]
    public bool ShowOpenFile { get; set; } = true;

    /// <summary>Whether the empty viewer offers the bundled sample document, workbook and deck. Default false.</summary>
    [Parameter]
    public bool ShowSamples { get; set; }

    /// <summary>Whether the toolbar offers Download, which saves the original file. Default true.</summary>
    [Parameter]
    public bool ShowDownload { get; set; } = true;

    /// <summary>Whether the toolbar offers Print. Default true.</summary>
    [Parameter]
    public bool ShowPrint { get; set; } = true;

    /// <summary>The zoom a file opens at. Default <see cref="ViewerZoom.Auto"/>.</summary>
    [Parameter]
    public ViewerZoom InitialZoom { get; set; } = ViewerZoom.Auto;

    /// <summary>The 1-based page (or slide) a file opens at. Default 1.</summary>
    [Parameter]
    public int InitialPage { get; set; } = 1;

    /// <summary>Resolution pages are printed at. Default 150; lowered automatically for a very long document.</summary>
    [Parameter]
    public int PrintDpi { get; set; } = 150;

    /// <summary>
    /// The highest resolution a page renders at when zoomed in, in DPI. Default 288 — sharp at 200% on a
    /// high-density screen without holding enormous images in the browser's memory.
    /// </summary>
    [Parameter]
    public int MaxRenderDpi { get; set; } = 288;

    /// <summary>Largest file the viewer will read, in bytes. Default 25 MB.</summary>
    [Parameter]
    public long MaxFileSize { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Whether an unexpected failure offers a pre-filled GitHub issue link against the Morph repository.
    /// </summary>
    [Parameter]
    public bool ShowIssueLink { get; set; } = true;

    /// <summary>Extra CSS classes for the root element, alongside the <c>viewer</c> class.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Any other attribute, splatted onto the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    // A page image's pixel budget: A4 at 288 DPI is 7.7 megapixels, and a poster-sized sheet drops its DPI
    // rather than asking the browser to hold a hundred-megapixel decode.
    const int maxPagePixels = 16_000_000;

    // A print run's total, so a very long document prints at a lower resolution rather than exhausting
    // memory — roughly 180 A4 pages at 150 DPI.
    const double maxPrintPixels = 400_000_000;

    static readonly double[] zoomPresets = [0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4];

    ElementReference root;
    ElementReference findInput;
    DotNetObjectReference<MorphViewer>? selfReference;
    readonly TaskCompletionSource<ViewerHandle> attached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    ViewerHandle? handle;

    PagedDocument? document;
    TextSearch? search;
    int generation;
    int documentId;
    byte[]? sourceBytes;
    string? fileName;
    InputFormatInfo? sourceInfo;
    int pageCount;
    double[] pageSizes = [];

    // The view as the script last reported it.
    int currentPage = 1;
    string pageInput = "1";
    double scale = 1;
    string zoomMode = "auto";
    bool sidebarOpen;
    bool presenting;

    bool findOpen;
    bool focusFind;
    string findQuery = "";
    bool matchCase;
    bool searchPending;
    IReadOnlyList<TextMatch> matches = [];
    int currentMatch = -1;
    CancelSource? searchDelay;

    int[] renderQueue = [];
    bool rendering;

    bool busy;
    string? progressLabel;
    string? progressDetail;
    bool printing;
    CancelSource? printCancel;

    string? errorMessage;
    string? issueUrl;
    string? userAgent;
    byte[]? openedSource;
    string? openedUrl;
    bool disposed;

    // Cancelled on dispose, so a render loop parked between pages stops at once.
    readonly CancelSource lifetime = new();

    bool HasDocument => document is not null;

    string RootClass => Class is {Length: > 0} extra ? $"viewer {extra}" : "viewer";

    string PageNoun => sourceInfo?.PageNoun ?? "page";

    string PageNounTitle => char.ToUpperInvariant(PageNoun[0]) + PageNoun[1..];

    string ZoomValue =>
        zoomMode != "custom"
            ? zoomMode
            : zoomPresets.FirstOrDefault(_ => Math.Abs(_ - scale) < 0.001) is > 0 and var preset
                ? preset.ToString(CultureInfo.InvariantCulture)
                : "custom";

    string FindStatus =>
        findQuery.Trim().Length == 0 || searchPending
            ? ""
            : matches.Count == 0
                ? "No results"
                : $"{currentMatch + 1} of {matches.Count}{(matches.Count == TextSearch.MaxMatches ? "+" : "")}";

    protected override async Task OnInitializedAsync()
    {
        // Warm the fonts while nothing is open yet — see MorphConverter. Deliberately not awaited.
        _ = WarmFontsAsync();
        userAgent = await Interop.UserAgentAsync();
    }

    async Task WarmFontsAsync()
    {
        try
        {
            await FontStore.EnsureAsync(Http);
        }
        catch
        {
            // The open path retries and reports it against the file that needed it.
        }
    }

    protected override Task OnParametersSetAsync()
    {
        if (Source is { } bytes)
        {
            if (!ReferenceEquals(bytes, openedSource))
            {
                openedSource = bytes;
                _ = OpenSourceAsync(bytes, FileName);
            }
        }
        else if (Url is { } url && url != openedUrl)
        {
            openedUrl = url;
            _ = OpenUrlAsync(url);
        }

        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            selfReference = DotNetObjectReference.Create(this);
            try
            {
                handle = await Interop.AttachViewerAsync(root, selfReference, MaxRenderDpi, maxPagePixels);
                attached.TrySetResult(handle);
            }
            catch (Exception exception)
            {
                attached.TrySetException(exception);
                ReportError("Could not start the viewer", exception);
                StateHasChanged();
            }
        }

        if (focusFind)
        {
            focusFind = false;
            await findInput.FocusAsync();
        }
    }

    async Task OpenSourceAsync(byte[] bytes, string? name)
    {
        if (name is null)
        {
            errorMessage = "Set FileName alongside Source: its extension says whether the bytes are a .docx, .xlsx or .pptx.";
            issueUrl = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        try
        {
            var fontDirectory = await FontStore.EnsureAsync(Http);
            await OpenAsync(bytes, name, fontDirectory);
        }
        catch (Exception exception)
        {
            ReportError($"Could not open '{name}'", exception);
            await InvokeAsync(StateHasChanged);
        }
    }

    async Task OpenUrlAsync(string url)
    {
        var name = FileName ?? Path.GetFileName(url.Split('?', '#')[0]);
        try
        {
            BeginBusy($"Downloading {name}…");
            var bytes = await Http.GetByteArrayAsync(url);
            var fontDirectory = await FontStore.EnsureAsync(Http);
            await OpenAsync(bytes, name, fontDirectory);
        }
        catch (Exception exception)
        {
            EndBusy();
            ReportError($"Could not load '{url}'", exception);
            StateHasChanged();
        }
    }

    async Task OnFileSelectedAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (ConversionService.Detect(file.Name) is null)
        {
            // User error rather than a bug, so no "report an issue" prompt.
            errorMessage = UnreadableMessage(file.Name);
            issueUrl = null;
            return;
        }

        try
        {
            BeginBusy($"Reading {file.Name}…");
            await using var stream = file.OpenReadStream(MaxFileSize);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var fontDirectory = await FontStore.EnsureAsync(Http);
            await OpenAsync(memory.ToArray(), file.Name, fontDirectory);
        }
        catch (Exception exception)
        {
            EndBusy();
            ReportError($"Could not read '{file.Name}'", exception);
        }
    }

    async Task LoadSampleAsync(InputFormatInfo info)
    {
        try
        {
            BeginBusy($"Downloading sample {info.DisplayName}…");
            var bytes = await Http.GetByteArrayAsync(info.SampleAsset);
            var fontDirectory = await FontStore.EnsureAsync(Http);
            await OpenAsync(bytes, info.SampleFileName, fontDirectory);
        }
        catch (Exception exception)
        {
            EndBusy();
            ReportError($"Could not load the sample {info.DisplayName}", exception);
        }
    }

    static string UnreadableMessage(string name) =>
        $"Can't read '{name}'. Open a Word .docx, Excel .xlsx or PowerPoint .pptx file.";

    // Every way in ends here. Internal so the tests can hand it a font directory directly: FontStore's
    // in-memory directory only exists inside the browser.
    internal async Task OpenAsync(byte[] bytes, string name, string fontDirectory)
    {
        errorMessage = null;
        issueUrl = null;
        if (ConversionService.Detect(name) is not { } info)
        {
            errorMessage = UnreadableMessage(name);
            await InvokeAsync(StateHasChanged);
            return;
        }

        var opening = ++generation;
        await CloseDocumentAsync();
        BeginBusy($"Opening {info.DisplayName}…");
        try
        {
            // Parse, lay out and read every page's text in one go; the pages themselves paint later, on demand.
            var opened = await Task.Run(() => Open(bytes, info.Format, fontDirectory), lifetime.Token);
            if (opening != generation || disposed)
            {
                opened.Document.Dispose();
                return;
            }

            document = opened.Document;
            search = new(opened.Texts);
            sourceBytes = bytes;
            fileName = name;
            sourceInfo = info;
            pageCount = opened.Document.PageCount;
            pageSizes = opened.Sizes;
            currentPage = Math.Clamp(InitialPage, 1, Math.Max(1, pageCount));
            pageInput = currentPage.ToString(CultureInfo.InvariantCulture);

            var viewer = await attached.Task;
            documentId = opening;
            await viewer.LoadAsync(opening, pageSizes, opened.Json, currentPage - 1, ZoomName(InitialZoom), info.PageNoun);
            if (findOpen && findQuery.Trim().Length > 0)
            {
                await RunSearchAsync();
            }
        }
        catch (Exception exception)
        {
            // Disposal cancels the open; that is not a failure to report.
            if (opening == generation && !disposed)
            {
                ReportError($"Could not open the {info.DisplayName}", exception);
            }
        }
        finally
        {
            if (opening == generation)
            {
                EndBusy();
            }

            if (!disposed)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    static OpenedDocument Open(byte[] bytes, InputFormat format, string fontDirectory)
    {
        var document = PagedDocument.Open(bytes, format, fontDirectory);
        try
        {
            var count = document.PageCount;
            var sizes = new double[count * 2];
            var json = new string[count];
            var texts = new string[count];
            for (var index = 0; index < count; index++)
            {
                sizes[2 * index] = document.WidthPoints(index);
                sizes[2 * index + 1] = document.HeightPoints(index);
                var layer = document.TextLayer(index);
                json[index] = layer.Json;
                texts[index] = layer.Text;
            }

            return new(document, sizes, json, texts);
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    async Task CloseDocumentAsync()
    {
        renderQueue = [];
        printCancel?.Cancel();
        searchDelay?.Cancel();
        matches = [];
        currentMatch = -1;
        if (document is { } open)
        {
            document = null;
            search = null;
            open.Dispose();
            if (handle is { } viewer)
            {
                try
                {
                    await viewer.UnloadAsync();
                }
                catch (JSDisconnectedException)
                {
                    // The page is gone.
                }
            }
        }

        sourceBytes = null;
        fileName = null;
        sourceInfo = null;
        pageCount = 0;
        pageSizes = [];
        documentId = 0;
    }

    static string ZoomName(ViewerZoom zoom) =>
        zoom switch
        {
            ViewerZoom.PageFit => "page-fit",
            ViewerZoom.PageWidth => "page-width",
            ViewerZoom.ActualSize => "page-actual",
            _ => "auto"
        };

    // The render worker. The script sends the pages it wants, most wanted first, replacing its previous
    // wish list each time; this paints the head of the latest list, pushes it, and yields to the browser.

    /// <summary>Called from JavaScript with the pages to render next, as (kind, page, dpi) triples.</summary>
    [JSInvokable]
    public Task OnRenderQueue(int[] jobs) =>
        InvokeAsync(() =>
        {
            renderQueue = jobs;
            StartRenderLoop();
        });

    // Not awaited by the caller: the script's call returns at once, and the loop keeps going until the
    // queue runs dry — which the script refills as the view changes.
    void StartRenderLoop()
    {
        if (rendering || renderQueue.Length < 3)
        {
            return;
        }

        rendering = true;
        _ = RenderLoopAsync();
    }

    async Task RenderLoopAsync()
    {
        try
        {
            while (!disposed && !printing && renderQueue.Length >= 3 && document is { } current && handle is { } viewer)
            {
                // A real macrotask, not Task.Yield: the browser paints and delivers scroll and input events
                // between two pages — which is also what brings the script's next wish list in.
                await Task.Delay(1, lifetime.Token);
                if (disposed || printing || renderQueue.Length < 3 || !ReferenceEquals(current, document))
                {
                    continue;
                }

                var kind = renderQueue[0];
                var page = renderQueue[1];
                var dpi = renderQueue[2];
                renderQueue = renderQueue[3..];
                if (page < 0 || page >= current.PageCount || dpi <= 0)
                {
                    continue;
                }

                var stamp = documentId;
                byte[] png;
                try
                {
                    png = await Task.Run(() => current.RenderPage(page, dpi), lifetime.Token);
                }
                catch (ObjectDisposedException)
                {
                    // Another file opened mid-render.
                    continue;
                }
                catch (Exception exception)
                {
                    if (stamp == documentId && kind == 0)
                    {
                        await viewer.SetPageErrorAsync(stamp, page, $"This {PageNoun} could not be rendered: {exception.Message}");
                    }

                    continue;
                }

                if (stamp != documentId)
                {
                    continue;
                }

                if (kind == 0)
                {
                    await viewer.SetPageImageAsync(stamp, page, png, dpi);
                }
                else
                {
                    await viewer.SetThumbnailAsync(stamp, page, png);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
        finally
        {
            rendering = false;
        }
    }

    /// <summary>Called from JavaScript whenever the view changes: the page in view, the zoom, presentation, the sidebar.</summary>
    [JSInvokable]
    public Task OnViewerState(int page, double viewScale, string mode, bool isPresenting, bool isSidebarOpen) =>
        InvokeAsync(() =>
        {
            currentPage = page;
            pageInput = page.ToString(CultureInfo.InvariantCulture);
            scale = viewScale;
            zoomMode = mode;
            presenting = isPresenting;
            sidebarOpen = isSidebarOpen;
            StateHasChanged();
        });

    /// <summary>Called from JavaScript for Ctrl+F inside the viewer.</summary>
    [JSInvokable]
    public Task OnFindRequested() =>
        InvokeAsync(OpenFindAsync);

    /// <summary>Called from JavaScript for Ctrl+P inside the viewer.</summary>
    [JSInvokable]
    public Task OnPrintRequested() =>
        InvokeAsync(PrintAsync);

    /// <summary>Called from JavaScript for Ctrl+S inside the viewer.</summary>
    [JSInvokable]
    public Task OnSaveRequested() =>
        InvokeAsync(DownloadAsync);

    async Task Call(Func<ViewerHandle, ValueTask> action)
    {
        if (handle is { } viewer && document is not null)
        {
            await action(viewer);
        }
    }

    Task ToggleSidebarAsync() =>
        Call(_ => _.ToggleSidebarAsync(!sidebarOpen));

    Task StepAsync(int delta) =>
        Call(_ => _.StepPageAsync(delta));

    Task ZoomByAsync(int steps) =>
        Call(_ => _.ZoomByAsync(steps));

    Task RotateAsync() =>
        Call(_ => _.RotateAsync(90));

    Task OnPageInputAsync()
    {
        if (int.TryParse(pageInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
            number >= 1 &&
            number <= pageCount)
        {
            return Call(_ => _.GoToPageAsync(number - 1));
        }

        pageInput = currentPage.ToString(CultureInfo.InvariantCulture);

        return Task.CompletedTask;
    }

    Task OnZoomSelectedAsync(ChangeEventArgs args)
    {
        if (args.Value is not string value || value == "custom")
        {
            return Task.CompletedTask;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return Call(_ => _.SetZoomAsync(number));
        }

        return Call(_ => _.SetZoomAsync(value));
    }

    async Task DownloadAsync()
    {
        if (sourceBytes is not { } bytes ||
            sourceInfo is not { } info)
        {
            return;
        }

        try
        {
            await Interop.DownloadAsync(fileName ?? info.SampleFileName, info.ContentType, bytes);
        }
        catch (Exception exception)
        {
            ReportError("Could not download the file", exception);
        }
    }

    // Printing: every page rendered at the print resolution and streamed to the script, which lays them out
    // for print CSS and opens the browser's dialog.
    async Task PrintAsync()
    {
        if (document is not { } current ||
            handle is not { } viewer ||
            printing)
        {
            return;
        }

        printing = true;
        var cancel = new CancelSource();
        printCancel = cancel;
        BeginBusy("Preparing to print…");
        try
        {
            await viewer.BeginPrintAsync(pageSizes);
            var dpi = PrintResolution();
            for (var index = 0; index < current.PageCount && !cancel.IsCancellationRequested; index++)
            {
                var pageIndex = index;
                progressDetail = $"{index + 1} of {current.PageCount}";
                StateHasChanged();
                await Task.Delay(1, cancel.Token);
                var png = await Task.Run(() => current.RenderPage(pageIndex, dpi), cancel.Token);
                await viewer.AddPrintPageAsync(pageIndex, png);
            }

            if (cancel.IsCancellationRequested)
            {
                await CancelScriptPrintAsync(viewer);
            }
            else
            {
                EndBusy();
                StateHasChanged();
                await viewer.FinishPrintAsync();
            }
        }
        catch (OperationCanceledException)
        {
            await CancelScriptPrintAsync(viewer);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
        catch (Exception exception)
        {
            ReportError("Could not print the document", exception);
            await CancelScriptPrintAsync(viewer);
        }
        finally
        {
            printing = false;
            printCancel = null;
            EndBusy();
            if (!disposed)
            {
                StateHasChanged();
                StartRenderLoop();
            }
        }
    }

    int PrintResolution()
    {
        var area = 0d;
        for (var index = 0; index < pageSizes.Length; index += 2)
        {
            area += pageSizes[index] * pageSizes[index + 1];
        }

        var budget = (int) Math.Floor(72 * Math.Sqrt(maxPrintPixels / Math.Max(area, 1)));
        return Math.Clamp(Math.Min(PrintDpi, budget), 36, Math.Max(36, PrintDpi));
    }

    void CancelPrint() =>
        printCancel?.Cancel();

    // Tears down the script's print container. Once the component is disposed the controller has already
    // done that itself, and its handle is no longer callable.
    async Task CancelScriptPrintAsync(ViewerHandle viewer)
    {
        if (disposed)
        {
            return;
        }

        try
        {
            await viewer.CancelPrintAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }

    // Find

    Task ToggleFindAsync()
    {
        if (findOpen)
        {
            return CloseFindAsync();
        }

        return OpenFindAsync();
    }

    Task OpenFindAsync()
    {
        if (!HasDocument)
        {
            return Task.CompletedTask;
        }

        var wasOpen = findOpen;
        findOpen = true;
        focusFind = true;
        StateHasChanged();
        if (!wasOpen && findQuery.Trim().Length > 0)
        {
            return RunSearchAsync();
        }

        return Task.CompletedTask;
    }

    async Task CloseFindAsync()
    {
        findOpen = false;
        searchDelay?.Cancel();
        searchPending = false;
        matches = [];
        currentMatch = -1;
        if (handle is { } viewer)
        {
            await viewer.ClearFindAsync();
        }
    }

    void OnFindInput(ChangeEventArgs args)
    {
        findQuery = args.Value as string ?? "";
        searchDelay?.Cancel();
        var delay = new CancelSource();
        searchDelay = delay;
        searchPending = true;
        _ = SearchAfterDelayAsync(delay.Token);
    }

    // Search as the user types, once they pause.
    async Task SearchAfterDelayAsync(Cancel cancel)
    {
        try
        {
            await Task.Delay(200, cancel);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await InvokeAsync(RunSearchAsync);
    }

    async Task RunSearchAsync()
    {
        searchPending = false;
        if (search is null ||
            handle is not { } viewer)
        {
            return;
        }

        matches = search.Find(findQuery, matchCase);
        currentMatch = matches.Count == 0 ? -1 : FirstMatchFrom(currentPage - 1);
        await PushFindAsync(viewer);
        StateHasChanged();
    }

    int FirstMatchFrom(int pageIndex)
    {
        for (var index = 0; index < matches.Count; index++)
        {
            if (matches[index].Page >= pageIndex)
            {
                return index;
            }
        }

        return 0;
    }

    async Task StepMatchAsync(int delta)
    {
        if (matches.Count == 0 ||
            handle is not { } viewer)
        {
            return;
        }

        currentMatch = (currentMatch + delta + matches.Count) % matches.Count;
        await PushFindAsync(viewer);
    }

    ValueTask PushFindAsync(ViewerHandle viewer)
    {
        var triples = new int[matches.Count * 3];
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            triples[3 * index] = match.Page;
            triples[3 * index + 1] = match.Start;
            triples[3 * index + 2] = match.Length;
        }

        return viewer.SetFindResultsAsync(documentId, triples, currentMatch);
    }

    Task OnFindKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter":
                if (searchPending || matches.Count == 0)
                {
                    searchDelay?.Cancel();
                    return RunSearchAsync();
                }

                return StepMatchAsync(args.ShiftKey ? -1 : 1);

            case "Escape":
                return CloseFindAsync();
        }

        return Task.CompletedTask;
    }

    Task OnMatchCaseAsync(ChangeEventArgs args)
    {
        matchCase = args.Value is true;
        return RunSearchAsync();
    }

    void BeginBusy(string label)
    {
        busy = true;
        progressLabel = label;
        progressDetail = null;
        StateHasChanged();
    }

    void EndBusy()
    {
        busy = false;
        progressLabel = null;
        progressDetail = null;
    }

    void ReportError(string action, Exception exception)
    {
        errorMessage = $"{action}: {exception.Message}";
        issueUrl = ShowIssueLink ? IssueLauncher.ForException(action, exception, MorphInfo.Environment(userAgent)) : null;
    }

    /// <summary>Releases the document, the script's controller and the images it holds.</summary>
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await lifetime.CancelAsync();
        renderQueue = [];
        searchDelay?.Cancel();
        printCancel?.Cancel();
        attached.TrySetCanceled();
        if (handle is { } viewer)
        {
            await viewer.DisposeAsync();
        }

        selfReference?.Dispose();
        document?.Dispose();
        document = null;
    }

    sealed record OpenedDocument(PagedDocument Document, double[] Sizes, string[] Json, string[] Texts);
}
