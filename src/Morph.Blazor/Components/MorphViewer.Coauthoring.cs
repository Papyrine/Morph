namespace Morph;

// Coauthoring: several people editing one document at once, through a session on the server (Coauthor,
// and Morph.Wopi's CoauthorSession). The editor edits as it does alone — every edit still file → parse →
// layout — and Coauthor commits each file ShowAsync shows. What comes back from the session is shown
// through ShowAsync too, as an adoption: no step of undo, and not committed again.
//
// Others' edits are never shown in the middle of typing: the paragraph being typed stays as it is until it
// is handed in, or has been left alone a while, when it is handed in, the version shown, and the paragraph
// opened again at the caret, found by its id. Opening a paragraph asks the session for it first, and one
// someone else has open is theirs until they leave it. Undo takes back the reader's own edit, from the file
// as others have edited it since (DocumentMerge).
//
// MorphWopi starts it (Coauthor); the viewer alone never coauthors.
public partial class MorphViewer : ICoauthorEditor
{
    // How long a paragraph is left alone before the others' edits that came meanwhile are shown.
    const double pauseMilliseconds = 2000;

    Coauthor? coauthor;
    bool adopting;
    (string Paragraph, int Offset, int? Page)? resumeAt;
    CoauthorState? presence;
    string? self;

    /// <summary>The session this viewer edits through; null editing alone.</summary>
    internal Coauthor? Coauthoring => coauthor;

    /// <summary>Edits through a coauthoring session from now on: the document open is the session's.</summary>
    internal void Coauthor(Coauthor session)
    {
        coauthor = session;
        session.Attach(this);
    }

    // Edits made while coauthoring give their new paragraphs ids, which the session merges by.
    IDisposable? Stamping()
    {
        if (coauthor == null)
        {
            return null;
        }

        return ParagraphIds.Stamping();
    }

    // After every file shown: the others drawn where they are on the new pages, and an edit committed.
    async Task CoauthoredAsync()
    {
        if (coauthor == null)
        {
            return;
        }

        await PushPresenceAsync();
        if (!adopting)
        {
            coauthor.Changed();
        }
    }

    // The file an undo or redo step shows: the one it keeps, or, where others' edits came after the step's
    // own file was shown, its change taken back from the file as it is now.
    byte[] Stepped(HistoryStep step, byte[] current)
    {
        if (coauthor == null ||
            step.Shown is not { } shown ||
            ReferenceEquals(shown, current))
        {
            return step.Bytes;
        }

        var outcome = DocumentMerge.Merge(shown, step.Bytes, current);
        if (outcome.Refused)
        {
            throw new InvalidOperationException("That edit cannot be undone: someone else has changed what it changed.");
        }

        return outcome.Document!;
    }

    byte[]? ICoauthorEditor.Current => sourceBytes;

    Task<bool> ICoauthorEditor.AdoptAsync(byte[] expected, byte[] next) =>
        OnRenderer(() => AdoptAsync(expected, next));

    Task ICoauthorEditor.AcceptedAsync(byte[] document) =>
        InvokeAsync(() => AcceptedAsync(document));

    Task<bool> ICoauthorEditor.ReadyAsync() =>
        OnRenderer(ReadyAsync);

    void ICoauthorEditor.Note(string message) =>
        _ = InvokeAsync(
            () =>
            {
                editNote = message;
                StateHasChanged();
            });

    Task ICoauthorEditor.PresenceAsync(CoauthorState state, string connection) =>
        InvokeAsync(
            () =>
            {
                presence = state;
                self = connection;
                return PushPresenceAsync();
            });

    async Task<T> OnRenderer<T>(Func<Task<T>> work)
    {
        T result = default!;
        await InvokeAsync(async () => result = await work());
        return result;
    }

    // Shows a version of the session's in place of the file expected, as long as that is still the file.
    // A paragraph open is handed in first, which may change the file, and opened again after.
    async Task<bool> AdoptAsync(byte[] expected, byte[] next)
    {
        await SettledAsync();
        if (!ReferenceEquals(sourceBytes, expected))
        {
            return false;
        }

        if (session != null)
        {
            await HandInToResumeAsync();
            await SettledAsync();
            if (!ReferenceEquals(sourceBytes, expected))
            {
                return false;
            }
        }

        adopting = true;
        bool shown;
        try
        {
            shown = await ShowAsync(() => next, relayout: true, () => activeKey, scroll: false);
        }
        finally
        {
            adopting = false;
        }

        if (!shown)
        {
            return false;
        }

        // Places taken in the file before are in other ordinals now.
        handedIn = null;
        beforeEdit = null;
        placingAfter = false;
        await ResumeWhereAsync();
        return true;
    }

    // With no paragraph open, ready. With one, ready only once it has been left alone a while: then it is
    // handed in, to be opened again where its caret was.
    async Task<bool> ReadyAsync()
    {
        if (session == null ||
            handle is not { } viewer)
        {
            return true;
        }

        double idle;
        try
        {
            idle = await viewer.EditIdleAsync();
        }
        catch (JSDisconnectedException)
        {
            return false;
        }

        if (idle < pauseMilliseconds)
        {
            return false;
        }

        await HandInToResumeAsync();
        return true;
    }

    // Hands in the paragraph open, keeping where its caret was by the paragraph's id, which a version
    // shown after leaves where it was while the ordinals move.
    async Task HandInToResumeAsync()
    {
        var id = session?.Paragraph.Id;
        if (await TargetAsync() is {Typing: true} target &&
            id != null)
        {
            resumeAt = (id, target.Offset, target.Page);
        }

        beforeEdit = null;
    }

    async Task ResumeWhereAsync()
    {
        if (resumeAt is not { } resume ||
            session != null)
        {
            return;
        }

        resumeAt = null;
        if (OrdinalOf(resume.Paragraph) is { } ordinal)
        {
            await ResumeAsync(ordinal, Math.Min(resume.Offset, outline.Paragraphs[ordinal].Length), page: resume.Page);
        }
    }

    // The file is on the server: nothing in it is unsaved here.
    async Task AcceptedAsync(byte[] document)
    {
        savedBytes = document;
        if (ReferenceEquals(sourceBytes, document))
        {
            await DocumentSavedAsync();
        }

        await ResumeWhereAsync();
        StateHasChanged();
    }

    // The paragraph the reader opens, asked of the session first: refused where someone else has it open.
    // Where it had changed since this file, the change is shown first and the paragraph found again.
    async Task<(EditParagraph Paragraph, EditBlock Block)?> EnterAsync(EditParagraph paragraph, EditBlock block)
    {
        if (coauthor == null ||
            paragraph.Id is not { } id)
        {
            return (paragraph, block);
        }

        var opened = documentId;
        CoauthorEnter reply;
        try
        {
            reply = await coauthor.EnterAsync(id);
        }
        catch (HttpRequestException)
        {
            editNote = "The connection is lost: that paragraph opens once it is back.";
            return null;
        }

        if (reply.Result == "refused")
        {
            editNote = $"{reply.By ?? "Someone else"} is editing this paragraph.";
            return null;
        }

        if (reply.Result != "granted")
        {
            editNote = "That paragraph has just been changed: click it again.";
            return null;
        }

        if (documentId == opened)
        {
            return (paragraph, block);
        }

        if (OrdinalOf(id) is not { } ordinal ||
            Reopened(ordinal, block.Page) is not { } found)
        {
            return null;
        }

        return (outline.Paragraphs[ordinal], found);
    }

    // No paragraph open any more: the session lets the others have it.
    void ExitIfClosed()
    {
        if (coauthor != null &&
            session == null)
        {
            _ = coauthor.ExitAsync();
        }
    }

    int? OrdinalOf(string id)
    {
        foreach (var paragraph in outline.Paragraphs)
        {
            if (string.Equals(paragraph.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return paragraph.Ordinal;
            }
        }

        return null;
    }

    // The others' bars, where the paragraphs they have open are on the pages now.
    async Task PushPresenceAsync()
    {
        if (coauthor == null ||
            handle is not { } viewer ||
            presence == null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var user in presence.Users)
            {
                if (user.Connection == self ||
                    user.Paragraph is not { } id ||
                    OrdinalOf(id) is not { } ordinal)
                {
                    continue;
                }

                foreach (var block in editMap.Blocks(ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("page", block.Page);
                    writer.WriteNumber("x", block.Left);
                    writer.WriteNumber("y", block.Top);
                    writer.WriteNumber("height", block.Height);
                    writer.WriteString("name", user.Name);
                    writer.WriteNumber("color", user.Color);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }

        try
        {
            await viewer.SetPresenceAsync(documentId, Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
        }
    }
}
