namespace Morph;

/// <summary>
/// The library's whole JavaScript surface, loaded as ES modules from this package's static web assets.
/// Registered by <see cref="MorphServiceCollectionExtensions.AddMorph"/>; public because a custom UI built
/// over <see cref="ConversionService"/> needs the same browser plumbing <see cref="MorphConverter"/> uses.
///
/// Modules rather than <c>window.*</c> globals on purpose: a consuming app then needs no
/// <c>&lt;script&gt;</c> tag of its own, nothing is added to the global scope to collide with the host's
/// own code, and each fetch is deferred until a component actually needs it. Each import is started once
/// and awaited by every later call.
/// </summary>
public sealed class MorphInterop(IJSRuntime jsRuntime) : IAsyncDisposable
{
    Task<IJSObjectReference>? import;
    Task<IJSObjectReference>? textImport;
    Task<IJSObjectReference>? viewerImport;

    Task<IJSObjectReference> Module =>
        import ??= Import(MorphAssets.Script);

    Task<IJSObjectReference> TextModule =>
        textImport ??= Import(MorphAssets.TextScript);

    Task<IJSObjectReference> ViewerModule =>
        viewerImport ??= Import(MorphAssets.ViewerScript);

    Task<IJSObjectReference> Import(string path) =>
        jsRuntime.InvokeAsync<IJSObjectReference>("import", $"./{path}").AsTask();

    /// <summary>Hands bytes to the browser as a file download.</summary>
    public async Task DownloadAsync(string fileName, string contentType, byte[] bytes)
    {
        var module = await Module;
        await module.InvokeVoidAsync("download", fileName, contentType, bytes);
    }

    /// <summary>
    /// Wraps bytes in a blob URL an <c>&lt;iframe&gt;</c> can load — the browser's built-in PDF viewer
    /// needs a real URL, and an HTML result needs a document of its own. The caller owns the URL and must
    /// <see cref="RevokeObjectUrlAsync"/> it when done.
    /// </summary>
    public async Task<string> CreateObjectUrlAsync(string contentType, byte[] bytes)
    {
        var module = await Module;
        return await module.InvokeAsync<string>("createObjectUrl", contentType, bytes);
    }

    /// <summary>Releases a URL handed out by <see cref="CreateObjectUrlAsync"/>.</summary>
    public async Task RevokeObjectUrlAsync(string url)
    {
        var module = await Module;
        await module.InvokeVoidAsync("revokeObjectUrl", url);
    }

    /// <summary>
    /// Builds a page's selectable text layer into <paramref name="element"/>: transparent, positioned text
    /// over the page image, which the browser can select, copy and find. The element must be an empty,
    /// absolutely positioned box exactly covering the image, carrying the <c>text-layer</c> class that
    /// the stylesheet sizes it by — the layer scales with that box, so it needs no update on resize or zoom.
    /// Blazor must render the element empty: the script owns its children.
    /// </summary>
    public async Task RenderTextLayerAsync(ElementReference element, PageTextLayer layer)
    {
        var module = await TextModule;
        await module.InvokeVoidAsync("renderTextLayer", element, layer.Json);
    }

    /// <summary>Empties an element <see cref="RenderTextLayerAsync"/> built a layer into.</summary>
    public async Task ClearTextLayerAsync(ElementReference element)
    {
        var module = await TextModule;
        await module.InvokeVoidAsync("clearTextLayer", element);
    }

    // The viewer's controller over the element MorphViewer renders; see morph-viewer.js for the calls.
    internal async Task<ViewerHandle> AttachViewerAsync<T>(ElementReference root, DotNetObjectReference<T> callbacks, int maxDpi, int maxPagePixels)
        where T : class
    {
        var module = await ViewerModule;
        var controller = await module.InvokeAsync<IJSObjectReference>("attachViewer", root, callbacks, maxDpi, maxPagePixels);
        return new(controller);
    }

    /// <summary>
    /// Reports whether the viewport is at least <paramref name="minWidth"/> CSS pixels wide, and calls
    /// back at every later crossing of that threshold. The callback target is <paramref name="reference"/>,
    /// whose component must expose a <c>[JSInvokable] Task OnViewportWideChanged(bool)</c>.
    /// </summary>
    public async Task<bool> WatchWideAsync<T>(DotNetObjectReference<T> reference, int minWidth)
        where T : class
    {
        var module = await Module;
        return await module.InvokeAsync<bool>("watchWide", reference, minWidth);
    }

    /// <summary>The browser's user-agent string, for a bug report's environment block.</summary>
    public async Task<string?> UserAgentAsync()
    {
        var module = await Module;
        return await module.InvokeAsync<string?>("userAgent");
    }

    /// <summary>Releases the imported JavaScript modules.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var pending in new[] { import, textImport, viewerImport })
        {
            if (pending is null)
            {
                continue;
            }

            try
            {
                var module = await pending;
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit/page is already gone, so there is nothing left to dispose on the JS side.
            }
        }
    }
}
