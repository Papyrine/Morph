namespace Morph.Web.Pages;

public partial class Index : IDisposable
{
    const long maxFileSize = 25 * 1024 * 1024;

    // The preview is a fixed-size thumbnail render; the PNG download uses the user-chosen resolution
    // instead (see ImageSettings.Dpi). Kept low so uploading a long document paints quickly.
    const int previewDpi = 110;

    // Below this viewport width the result pane never renders (and the conversion feeding it never
    // runs); must match the app.css breakpoint that lays the two panes side by side.
    const int resultPaneMinWidth = 1200;

    string? sourceName;
    InputFormatInfo? sourceInfo;
    byte[]? sourceBytes;
    int pageCount;
    List<string>? previewPages;
    string? errorMessage;
    string? issueUrl;
    string? userAgent;
    OutputFormat target = OutputFormat.Png;
    readonly ImageSettings image = new();
    bool isBusy;
    bool isRendering;
    bool isConvertingResult;
    bool wideViewport;
    string? progressLabel;
    string? progressDetail;
    ResultPreview? result;
    readonly Dictionary<OutputFormat, ResultPreview> resultCache = new();
    DotNetObjectReference<Index>? selfReference;

    FormatInfo? TargetInfo => ConversionService.Find(target);

    protected override async Task OnInitializedAsync()
    {
        // Warm the fonts while the user is still choosing a file: no render can start without them, and
        // fetching them here means the first upload doesn't wait on ~940KB. Deliberately not awaited —
        // nothing on this path needs them yet, and the EnsureAsync each render awaits joins this same
        // download (and surfaces the error if it failed).
        _ = WarmFontsAsync();

        userAgent = await JsRuntime.InvokeAsync<string?>("appInfo.userAgent");
    }

    async Task WarmFontsAsync()
    {
        try
        {
            await FontStore.EnsureAsync(Http);
        }
        catch
        {
            // Swallowed on purpose: a failure here has no UI to report to and must not fault an
            // unobserved task. The render path retries and reports it against the action that needed it.
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // Watch the viewport against the side-by-side breakpoint; the callback fires on every crossing.
        selfReference = DotNetObjectReference.Create(this);
        wideViewport = await JsRuntime.InvokeAsync<bool>("viewport.watchWide", selfReference, resultPaneMinWidth);
    }

    [JSInvokable]
    public Task OnViewportWideChanged(bool wide) =>
        InvokeAsync(async () =>
        {
            wideViewport = wide;
            // Widening may reveal the pane for a target selected while narrow, never yet converted.
            await EnsureResultPreviewAsync();
            StateHasChanged();
        });

    // Switches the visible phase and repaints. The conversion that follows is wrapped in Task.Run, which
    // yields once so this busy state paints before the (single-threaded) compute begins.
    void BeginPhase(string label, string? detail = null)
    {
        progressLabel = label;
        progressDetail = detail;
        isBusy = true;
        StateHasChanged();
    }

    async Task OnFileSelected(InputFileChangeEventArgs args)
    {
        await ResetAsync();

        var file = args.File;
        sourceName = file.Name;

        // Extension is the only reliable signal: the browser's reported MIME type varies by OS and is
        // frequently empty for Office files.
        if (ConversionService.Detect(file.Name) is not { } detected)
        {
            // User error rather than a bug, so no "report an issue" prompt.
            errorMessage = $"Can't read '{file.Name}'. Upload a Word .docx, Excel .xlsx or PowerPoint .pptx file.";
            sourceName = null;
            return;
        }

        sourceInfo = detected;

        try
        {
            BeginPhase($"Reading {detected.DisplayName}…");
            await using var stream = file.OpenReadStream(maxFileSize);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            sourceBytes = memory.ToArray();

            await RenderPreviewAsync();
            await EnsureResultPreviewAsync();
        }
        catch (Exception exception)
        {
            ReportError($"Could not read the {detected.DisplayName}", exception);
            sourceBytes = null;
        }
        finally
        {
            isBusy = false;
        }
    }

    // Loads the file bundled as a static web asset at /sample/sample.docx (or .xlsx / .pptx), then reuses
    // the same read/render pipeline as an uploaded file.
    async Task LoadSample(InputFormatInfo info)
    {
        await ResetAsync();
        sourceInfo = info;
        sourceName = info.SampleFileName;

        try
        {
            BeginPhase($"Downloading sample {info.DisplayName}…");
            sourceBytes = await Http.GetByteArrayAsync(info.SampleAsset);

            await RenderPreviewAsync();
            await EnsureResultPreviewAsync();
        }
        catch (Exception exception)
        {
            ReportError($"Could not load the sample {info.DisplayName}", exception);
            sourceBytes = null;
        }
        finally
        {
            isBusy = false;
        }
    }

    // Rendering to page images is CPU-bound. It runs inside Task.Run so the "Rendering preview…" state
    // paints first; on the single-threaded runtime the page is then briefly unresponsive while the pages
    // rasterise.
    async Task RenderPreviewAsync()
    {
        if (sourceBytes is not { } bytes ||
            sourceInfo is not { } info)
        {
            previewPages = null;
            return;
        }

        isRendering = true;
        BeginPhase("Rendering preview…");
        try
        {
            // Rendering needs the bundled fonts materialised into the in-memory filesystem first.
            var fontDirectory = await FontStore.EnsureAsync(Http);
            var pages = await Task.Run(() => RenderPreview(bytes, info.Format, fontDirectory));
            previewPages = pages;
            pageCount = pages.Count;
            progressDetail = null;
        }
        finally
        {
            isBusy = false;
            isRendering = false;
        }
    }

    static List<string> RenderPreview(byte[] bytes, InputFormat source, string fontDirectory)
    {
        var pages = ConversionService.RenderPngPages(bytes, source, new() { Dpi = previewDpi }, fontDirectory);
        var urls = new List<string>(pages.Count);
        foreach (var page in pages)
        {
            urls.Add($"data:image/png;base64,{Convert.ToBase64String(page)}");
        }

        return urls;
    }

    Task OnTargetChanged(OutputFormat format)
    {
        target = format;
        // The pane must not keep showing the previous format while (or if) the new one converts.
        result = null;
        return EnsureResultPreviewAsync();
    }

    // Converts the loaded source to the selected format for the result pane. Skipped when the pane
    // can't show: no source, PNG selected (the page preview already is the PNG), or a narrow viewport
    // (the pane never renders there, so converting would be wasted work). Results are cached per format
    // for the life of the source, so flipping between formats re-shows instantly.
    async Task EnsureResultPreviewAsync()
    {
        // The selection can move on while a conversion runs (the dropdown stays enabled), so loop:
        // each pass re-reads the current target and converts it, until the finished result matches
        // the still-selected format.
        while (true)
        {
            if (sourceBytes is not { } bytes ||
                sourceInfo is not { } source ||
                !wideViewport ||
                isBusy ||
                TargetInfo is not { } info ||
                info.Format == OutputFormat.Png)
            {
                return;
            }

            if (resultCache.TryGetValue(info.Format, out var cached))
            {
                result = cached;
                return;
            }

            isConvertingResult = true;
            BeginPhase($"Converting to {info.DisplayName}…");
            try
            {
                var fontDirectory = await FontStore.EnsureAsync(Http);
                var payload = await Task.Run(() => ConversionService.BuildDownload(bytes, source.Format, info.Format, image, fontDirectory));
                var converted = await BuildResultPreviewAsync(info.Format, payload);

                // The awaits above can interleave with a new upload (the file input stays active); a result
                // for the replaced source must not be cached or shown against the new one.
                if (!ReferenceEquals(sourceBytes, bytes))
                {
                    await RevokeAsync(converted);
                    return;
                }

                resultCache[info.Format] = converted;
                if (target == info.Format)
                {
                    result = converted;
                }
            }
            catch (Exception exception)
            {
                ReportError($"Could not convert to {info.DisplayName}", exception);
            }
            finally
            {
                isBusy = false;
                isConvertingResult = false;
            }

            // If the target is still this format we're done; otherwise it moved mid-conversion — loop
            // to catch up to the new selection.
            if (target == info.Format)
            {
                return;
            }
        }
    }

    // Markdown and plain text show inline; PDF and HTML load into an <iframe> via a blob URL — the
    // browser's PDF viewer needs a real URL, and an HTML result needs a document of its own.
    async Task<ResultPreview> BuildResultPreviewAsync(OutputFormat format, DownloadPayload payload)
    {
        if (format is OutputFormat.Markdown or OutputFormat.Text)
        {
            var text = Encoding.UTF8.GetString(payload.Bytes);
            var imagesElided = false;
            if (format == OutputFormat.Markdown)
            {
                // Embedded images are megabytes of base64 that would drown the pane; the download
                // still gets the full bytes. The caption notes the swap when it happens.
                imagesElided = MarkdownPreview.HasElidableImages(text);
                text = MarkdownPreview.ElideImages(text);
            }

            return new(payload.Bytes, text, null, imagesElided);
        }

        var url = await JsRuntime.InvokeAsync<string>(
            "resultPreview.createUrl",
            payload.ContentType,
            Convert.ToBase64String(payload.Bytes));
        return new(payload.Bytes, null, url, false);
    }

    async Task RevokeAsync(ResultPreview preview)
    {
        if (preview.Url is { } url)
        {
            await JsRuntime.InvokeVoidAsync("resultPreview.revokeUrl", url);
        }
    }

    async Task Download()
    {
        if (sourceBytes is not { } bytes ||
            sourceInfo is not { } source)
        {
            return;
        }

        var info = TargetInfo;
        if (info is null)
        {
            return;
        }

        BeginPhase(target == OutputFormat.Png ? "Rendering image…" : $"Converting to {info.DisplayName}…");
        try
        {
            DownloadPayload payload;
            if (resultCache.TryGetValue(info.Format, out var cached))
            {
                // The result pane already produced exactly these bytes; don't convert twice.
                payload = new(cached.Bytes, info.Extension, info.ContentType);
            }
            else
            {
                // The rendered formats (PNG, PDF) resolve fonts against the bundled Aptos faces,
                // materialised into the in-memory filesystem here; the text formats ignore the directory.
                var fontDirectory = await FontStore.EnsureAsync(Http);
                payload = await Task.Run(() => ConversionService.BuildDownload(bytes, source.Format, info.Format, image, fontDirectory));
            }

            var baseName = Path.GetFileNameWithoutExtension(sourceName) ?? "document";
            await FileDownloadService.DownloadAsync($"{baseName}{payload.Extension}", payload.ContentType, payload.Bytes);
        }
        catch (Exception exception)
        {
            ReportError($"Could not convert to {info.DisplayName}", exception);
        }
        finally
        {
            isBusy = false;
        }
    }

    void ReportError(string action, Exception exception)
    {
        errorMessage = $"{action}: {exception.Message}";
        issueUrl = IssueLauncher.ForException(action, exception, AppInfo.Environment(userAgent));
    }

    async Task ResetAsync()
    {
        errorMessage = null;
        issueUrl = null;
        sourceName = null;
        sourceInfo = null;
        sourceBytes = null;
        pageCount = 0;
        previewPages = null;
        isBusy = false;
        isRendering = false;
        isConvertingResult = false;
        progressLabel = null;
        progressDetail = null;
        result = null;
        foreach (var preview in resultCache.Values)
        {
            await RevokeAsync(preview);
        }

        resultCache.Clear();
    }

    public void Dispose() =>
        selfReference?.Dispose();

    // One converted output, ready for the result pane: the exact bytes a download would produce, plus
    // either the text to show inline (Markdown, plain text) or the blob URL an <iframe> loads (PDF, HTML).
    sealed record ResultPreview(byte[] Bytes, string? Text, string? Url, bool ImagesElided);
}
