/// <summary>
/// The .NET side of one viewer's JavaScript controller (<c>attachViewer</c> in morph-viewer.js). The script
/// owns the page elements, the scroll position and the zoom — geometry has to change in the same frame as
/// a resize — while .NET owns the document: it renders the pages the script asks for and pushes them back.
///
/// Every push carries the document id <c>load</c> was given, and the script drops anything stamped with an
/// older one, so a render finishing after a new file opened cannot land on the new document's pages.
/// Arguments are primitives, strings and byte arrays only: they serialise without reflection, so a trimmed
/// app keeps working.
/// </summary>
sealed class ViewerHandle(IJSObjectReference controller) : IAsyncDisposable
{
    public ValueTask LoadAsync(int documentId, double[] sizesPoints, string[] textLayers, int pageIndex, string zoom, string pageNoun) =>
        controller.InvokeVoidAsync("load", documentId, sizesPoints, textLayers, pageIndex, zoom, pageNoun);

    // The same document after an edit, laid out again: the pages are swapped in place and the view stays
    // where it was.
    public ValueTask ReloadAsync(int documentId, double[] sizesPoints, string[] textLayers) =>
        controller.InvokeVoidAsync("reload", documentId, sizesPoints, textLayers);

    public ValueTask UnloadAsync() =>
        controller.InvokeVoidAsync("unload");

    public ValueTask SetPageImageAsync(int documentId, int pageIndex, byte[] png, int dpi) =>
        controller.InvokeVoidAsync("setPageImage", documentId, pageIndex, png, dpi);

    public ValueTask SetThumbnailAsync(int documentId, int pageIndex, byte[] png) =>
        controller.InvokeVoidAsync("setThumbnail", documentId, pageIndex, png);

    public ValueTask SetPageErrorAsync(int documentId, int pageIndex, string message) =>
        controller.InvokeVoidAsync("setPageError", documentId, pageIndex, message);

    public ValueTask GoToPageAsync(int pageIndex) =>
        controller.InvokeVoidAsync("goToPage", pageIndex);

    public ValueTask StepPageAsync(int delta) =>
        controller.InvokeVoidAsync("stepPage", delta);

    // A preset name ("auto", "page-fit", "page-width", "page-actual") or a scale.
    public ValueTask SetZoomAsync(string mode) =>
        controller.InvokeVoidAsync("setZoom", mode);

    public ValueTask SetZoomAsync(double scale) =>
        controller.InvokeVoidAsync("setZoom", scale);

    public ValueTask ZoomByAsync(int steps) =>
        controller.InvokeVoidAsync("zoomBy", steps);

    public ValueTask RotateAsync(int degrees) =>
        controller.InvokeVoidAsync("rotate", degrees);

    public ValueTask ToggleSidebarAsync(bool open) =>
        controller.InvokeVoidAsync("toggleSidebar", open);

    // Flattened (page, start, length) triples; current is an index into them, or -1.
    public ValueTask SetFindResultsAsync(int documentId, int[] matches, int current) =>
        controller.InvokeVoidAsync("setFindResults", documentId, matches, current);

    public ValueTask ClearFindAsync() =>
        controller.InvokeVoidAsync("clearFind");

    // While review is on, the script paints what SetReviewAsync sends and reports the reader's selection
    // (OnReviewSelection) and the character a click lands on (OnReviewHit).
    public ValueTask SetReviewModeAsync(bool on) =>
        controller.InvokeVoidAsync("setReviewMode", on);

    // Flattened (page, start, length) triples, as find results are: every comment's range, and the
    // ranges of the item the reader has chosen, which scroll brings into view.
    public ValueTask SetReviewAsync(int documentId, int[] comments, int[] current, bool scroll) =>
        controller.InvokeVoidAsync("setReview", documentId, comments, current, scroll);

    public ValueTask RevealReviewCardAsync(string key) =>
        controller.InvokeVoidAsync("revealReviewCard", key);

    // While editing is on, a click on a page reports where it landed (OnEditHit) rather than
    // choosing a review card. Turning it off hands in whatever is being typed first.
    public ValueTask SetEditModeAsync(bool on) =>
        controller.InvokeVoidAsync("setEditMode", on);

    // Lays an editor over a paragraph: session is EditSession's JSON.
    public ValueTask BeginEditAsync(int documentId, string session) =>
        controller.InvokeVoidAsync("beginEdit", documentId, session);

    // The reader's time zone, in minutes east of UTC.
    public ValueTask<int> ZoneOffsetAsync() =>
        controller.InvokeAsync<int>("zoneOffset");

    public ValueTask BeginPrintAsync(double[] sizesPoints) =>
        controller.InvokeVoidAsync("beginPrint", sizesPoints);

    public ValueTask AddPrintPageAsync(int pageIndex, byte[] png) =>
        controller.InvokeVoidAsync("addPrintPage", pageIndex, png);

    // Resolves once the browser's print dialog has closed and the print pages are cleaned up.
    public ValueTask FinishPrintAsync() =>
        controller.InvokeVoidAsync("finishPrint");

    public ValueTask CancelPrintAsync() =>
        controller.InvokeVoidAsync("cancelPrint");

    public async ValueTask DisposeAsync()
    {
        try
        {
            await controller.InvokeVoidAsync("dispose");
            await controller.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is already gone; nothing is left to tear down on the JS side.
        }
    }
}
