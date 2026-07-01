namespace Morph.Web.Pages;

public partial class Index
{
    const long maxFileSize = 25 * 1024 * 1024;

    // The preview is a fixed-size thumbnail render; the PNG download uses the user-chosen resolution
    // instead (see ImageSettings.Dpi). Kept low so uploading a long document paints quickly.
    const int previewDpi = 110;

    string? sourceName;
    byte[]? docxBytes;
    int pageCount;
    List<string>? previewPages;
    string? errorMessage;
    string? issueUrl;
    string? userAgent;
    OutputFormat target = OutputFormat.Png;
    readonly ImageSettings image = new();
    bool isBusy;
    bool isRendering;
    string? progressLabel;
    string? progressDetail;

    FormatInfo? TargetInfo => ConversionService.Find(target);

    protected override async Task OnInitializedAsync() =>
        userAgent = await JsRuntime.InvokeAsync<string?>("appInfo.userAgent");

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
        Reset();

        var file = args.File;
        sourceName = file.Name;

        if (!ConversionService.CanRead(file.Name))
        {
            // User error rather than a bug, so no "report an issue" prompt.
            errorMessage = $"Can't read '{file.Name}'. Upload a Word .docx file.";
            sourceName = null;
            return;
        }

        try
        {
            BeginPhase("Reading document…");
            await using var stream = file.OpenReadStream(maxFileSize);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            docxBytes = memory.ToArray();

            await RenderPreviewAsync();
        }
        catch (Exception exception)
        {
            ReportError("Could not read the document", exception);
            docxBytes = null;
        }
        finally
        {
            isBusy = false;
        }
    }

    // Loads the document bundled as a static web asset at /sample/sample.docx, then reuses the same
    // read/render pipeline as an uploaded file.
    async Task LoadSampleDocument()
    {
        Reset();
        sourceName = "sample.docx";

        try
        {
            BeginPhase("Downloading sample document…");
            docxBytes = await Http.GetByteArrayAsync("sample/sample.docx");

            await RenderPreviewAsync();
        }
        catch (Exception exception)
        {
            ReportError("Could not load the sample document", exception);
            docxBytes = null;
        }
        finally
        {
            isBusy = false;
        }
    }

    // Rendering a document to page images is CPU-bound. It runs inside Task.Run so the "Rendering
    // preview…" state paints first; on the single-threaded runtime the page is then briefly unresponsive
    // while the pages rasterise.
    async Task RenderPreviewAsync()
    {
        if (docxBytes is not { } bytes)
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
            var pages = await Task.Run(() => RenderPreview(bytes, fontDirectory));
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

    static List<string> RenderPreview(byte[] bytes, string fontDirectory)
    {
        var pages = ConversionService.RenderPngPages(bytes, new() { Dpi = previewDpi }, fontDirectory);
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
        return Task.CompletedTask;
    }

    async Task Download()
    {
        if (docxBytes is not { } bytes)
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
            // The rendered formats (PNG, PDF) resolve fonts against the bundled Aptos faces, materialised
            // into the in-memory filesystem here; the text formats ignore the directory.
            var fontDirectory = await FontStore.EnsureAsync(Http);
            var payload = await Task.Run(() => ConversionService.BuildDownload(bytes, info.Format, image, fontDirectory));
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

    void Reset()
    {
        errorMessage = null;
        issueUrl = null;
        sourceName = null;
        docxBytes = null;
        pageCount = 0;
        previewPages = null;
        isBusy = false;
        isRendering = false;
        progressLabel = null;
        progressDetail = null;
    }
}
