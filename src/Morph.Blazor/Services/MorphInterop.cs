namespace Morph;

/// <summary>
/// The library's whole JavaScript surface, loaded as an ES module from this package's static web assets.
/// Registered by <see cref="MorphServiceCollectionExtensions.AddMorph"/>; public because a custom UI built
/// over <see cref="ConversionService"/> needs the same browser plumbing <see cref="MorphConverter"/> uses.
///
/// A module rather than <c>window.*</c> globals on purpose: a consuming app then needs no
/// <c>&lt;script&gt;</c> tag of its own, nothing is added to the global scope to collide with the host's
/// own code, and the fetch is deferred until a component actually needs it. The import is started once
/// and awaited by every later call.
/// </summary>
public sealed class MorphInterop(IJSRuntime jsRuntime) : IAsyncDisposable
{
    Task<IJSObjectReference>? import;

    Task<IJSObjectReference> Module =>
        import ??= jsRuntime.InvokeAsync<IJSObjectReference>("import", $"./{MorphAssets.Script}").AsTask();

    /// <summary>Hands bytes to the browser as a file download.</summary>
    public async Task DownloadAsync(string fileName, string contentType, byte[] bytes)
    {
        var module = await Module;
        await module.InvokeVoidAsync("download", fileName, contentType, Convert.ToBase64String(bytes));
    }

    /// <summary>
    /// Wraps bytes in a blob URL an <c>&lt;iframe&gt;</c> can load — the browser's built-in PDF viewer
    /// needs a real URL, and an HTML result needs a document of its own. The caller owns the URL and must
    /// <see cref="RevokeObjectUrlAsync"/> it when done.
    /// </summary>
    public async Task<string> CreateObjectUrlAsync(string contentType, byte[] bytes)
    {
        var module = await Module;
        return await module.InvokeAsync<string>("createObjectUrl", contentType, Convert.ToBase64String(bytes));
    }

    /// <summary>Releases a URL handed out by <see cref="CreateObjectUrlAsync"/>.</summary>
    public async Task RevokeObjectUrlAsync(string url)
    {
        var module = await Module;
        await module.InvokeVoidAsync("revokeObjectUrl", url);
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

    /// <summary>Releases the imported JavaScript module.</summary>
    public async ValueTask DisposeAsync()
    {
        if (import is null)
        {
            return;
        }

        try
        {
            var module = await import;
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit/page is already gone, so there is nothing left to dispose on the JS side.
        }
    }
}
