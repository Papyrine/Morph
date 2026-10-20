namespace Morph;

// The editor keeping the reader's work (MorphEditor's alone, by Editing): while the document has edits
// not saved, the page asks before it is left, and the file is kept as a draft in the browser's own
// storage (morph-draft.js), which the start page offers back after a reload, a crash or a closed tab.
// The draft goes once the document is saved, or its edits are discarded. There is one draft: the last
// document edited and not saved.
public partial class MorphViewer
{
    // Whether the draft in storage is this document's, which saving it then takes away. A draft left
    // by an earlier visit, or by a document opened since, is left for the start page to offer.
    bool draftOwned;

    // The draft the start page offers, and when it was written, in the reader's time.
    internal DraftInfo? recovery;
    internal string? recoveryTime;
    internal string? recoveryNote;

    internal async Task FindDraftAsync()
    {
        if (!Editing ||
            handle is not { } viewer)
        {
            return;
        }

        try
        {
            recovery = await viewer.DescribeDraftAsync();
            if (recovery is { } found)
            {
                var zone = TimeSpan.FromMinutes(await viewer.ZoneOffsetAsync());
                recoveryTime = DateTimeOffset.FromUnixTimeMilliseconds((long) found.SavedAt)
                    .ToOffset(zone)
                    .ToString("d MMM 'at' HH:mm", CultureInfo.InvariantCulture);
            }
        }
        catch (JSException)
        {
            recovery = null;
        }
        catch (JSDisconnectedException)
        {
            return;
        }

        StateHasChanged();
    }

    async Task DocumentOpenedAsync(ViewerHandle viewer)
    {
        if (!Editing)
        {
            return;
        }

        draftOwned = false;
        recovery = null;
        recoveryNote = null;
        await viewer.SetUnsavedAsync(Dirty);
    }

    async Task DocumentRevisedAsync()
    {
        if (!Editing ||
            handle is not { } viewer)
        {
            return;
        }

        if (Dirty &&
            sourceBytes is { } bytes)
        {
            await viewer.KeepDraftAsync(fileName ?? "Document.docx", bytes);
            draftOwned = true;
            await viewer.SetUnsavedAsync(true);
            return;
        }

        // Undone back to what was saved.
        await DropDraftAsync();
        await viewer.SetUnsavedAsync(false);
    }

    async Task DocumentSavedAsync()
    {
        if (!Editing)
        {
            return;
        }

        await DropDraftAsync();
        if (handle is { } viewer)
        {
            await viewer.SetUnsavedAsync(false);
        }
    }

    internal async Task DropDraftAsync()
    {
        if (!draftOwned ||
            handle is not { } viewer)
        {
            return;
        }

        draftOwned = false;
        await viewer.DropDraftAsync();
    }

    // Opens the draft as the document it was: edited, and not saved.
    internal async Task RecoverAsync()
    {
        if (handle is not { } viewer ||
            recovery is not { } found)
        {
            return;
        }

        byte[]? bytes = null;
        try
        {
            bytes = await viewer.ReadDraftAsync();
        }
        catch (JSException)
        {
        }

        if (bytes is not {Length: > 0})
        {
            recovery = null;
            recoveryNote = "The unsaved document could not be read back.";
            return;
        }

        try
        {
            var fonts = fontDirectory ?? await FontStore.EnsureAsync(Http);
            await OpenAsync(bytes, found.Name, fonts);
        }
        catch (Exception exception)
        {
            ReportError("Could not recover the document", exception);
            return;
        }

        if (!ReferenceEquals(sourceBytes, bytes))
        {
            return;
        }

        // What it was saved as is not known here: it is edited until it is saved again.
        savedBytes = null;
        draftOwned = true;
        await viewer.SetUnsavedAsync(true);
    }

    internal Task DiscardRecoveryAsync()
    {
        recovery = null;
        draftOwned = true;
        return DropDraftAsync();
    }
}
