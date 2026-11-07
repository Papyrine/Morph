namespace Morph;

/// <summary>The editor as coauthoring drives it, behind which MorphViewer stands.</summary>
interface ICoauthorEditor
{
    /// <summary>The document the editor shows, as its last edit or adoption left it; null before it has one.</summary>
    byte[]? Current { get; }

    /// <summary>
    /// Shows <paramref name="next"/>, a version from the session rather than an edit (no step of undo), in
    /// place of <paramref name="expected"/>. False, and nothing shown, where the editor's document is no
    /// longer <paramref name="expected"/>: an edit came between.
    /// </summary>
    Task<bool> AdoptAsync(byte[] expected, byte[] next);

    /// <summary>The document is on the server: nothing in it is unsaved here.</summary>
    Task AcceptedAsync(byte[] document);

    /// <summary>
    /// Whether the editor can take a version from the session now: with no paragraph open, or once one
    /// left idle has been handed in. Typing is not interrupted.
    /// </summary>
    Task<bool> ReadyAsync();

    /// <summary>Tells the reader something about their edits: one refused, the connection lost.</summary>
    void Note(string message);

    /// <summary>Who is in the session and where, <paramref name="self"/> being this page's connection.</summary>
    Task PresenceAsync(CoauthorState state, string self);
}

/// <summary>
/// A page's side of a coauthoring session. The editor edits its own document as it always does; each edit
/// it makes is committed as a patch against the version it was made to, and the session's answer is shown:
/// the edit as made, or merged with others' edits that came between, or refused and taken back. Versions
/// others made are fetched and shown when the editor is ready for them: never in the middle of typing.
///
/// <c>based</c> is the version the editor's document descends from, by its number and document: an edit
/// is the change from it, and it moves only when the editor shows a version of the session's. One thing is
/// done at a time (<c>busy</c>), so there is never more than one commit in flight.
/// </summary>
sealed class Coauthor(CoauthorClient client, TimeProvider time) : IAsyncDisposable
{
    static readonly TimeSpan longestWait = TimeSpan.FromSeconds(30);

    // How long to wait before asking an editor in the middle of typing again.
    static readonly TimeSpan readyAgain = TimeSpan.FromSeconds(1);

    readonly SemaphoreSlim busy = new(1, 1);
    readonly CancelSource lifetime = new();
    ICoauthorEditor? editor;
    (long Version, byte[] Document) based;
    long latest;
    TimeSpan wait = TimeSpan.FromSeconds(1);
    ITimer? again;
    bool offline;

    // A version of the session's to show in place of the editor's document as it was (Expected): the
    // answer to a commit, waiting for the editor to be ready for it. Show is what the editor then shows,
    // and Based the version that leaves it descending from.
    (byte[] Expected, byte[] Show, (long Version, byte[] Document) Based)? pending;

    /// <summary>The page's place in the session; null before it joined, or where the server has no sessions.</summary>
    public CoauthorJoined? Joined { get; private set; }

    /// <summary>The session's state as it last came.</summary>
    public CoauthorState? State { get; private set; }

    /// <summary>Raised as the session's state comes: who is in, and whether the document is saved.</summary>
    public event Action? StateChanged;

    /// <summary>The version the editor's document descends from.</summary>
    public long Version => based.Version;

    /// <summary>The URL the page's beacon posts to as it goes away.</summary>
    public string LeaveUrl => client.LeaveUrl;

    /// <summary>Joins the file's session and fetches its document; null where the server has no sessions.</summary>
    public async Task<byte[]?> StartAsync(Cancel cancel = default)
    {
        Joined = await client.JoinAsync(cancel);
        if (Joined == null)
        {
            return null;
        }

        var (version, document) = await client.DocumentAsync(cancel);
        based = (version, document);
        latest = version;
        return document;
    }

    /// <summary>Hands coauthoring the editor showing the document, and starts listening to the session.</summary>
    public void Attach(ICoauthorEditor shown)
    {
        editor = shown;
        _ = ListenAsync(lifetime.Token);
    }

    /// <summary>The editor's document changed by an edit: committed, with what came since merged in.</summary>
    public void Changed() =>
        Kick();

    void Kick() =>
        _ = SyncAsync();

    /// <summary>Commits what the editor has not committed, and shows what the session has that it does not.</summary>
    public async Task SyncAsync()
    {
        try
        {
            await busy.WaitAsync(lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await StepAsync();
            if (offline)
            {
                offline = false;
                editor?.Note("Connected again: your edits are saved.");
            }

            wait = TimeSpan.FromSeconds(1);
        }
        catch (HttpRequestException)
        {
            if (!offline)
            {
                offline = true;
                editor?.Note("The connection is lost: your edits are kept here, and sent when it is back.");
            }

            Again(wait);
            wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, longestWait.Ticks));
        }
        catch (OperationCanceledException)
        {
            // Gone.
        }
        catch (Exception exception)
        {
            // Said rather than lost in a task nobody awaits: the edits made here are still here.
            editor?.Note($"Your edits could not be shared: {exception.Message}");
            Again(wait);
        }
        finally
        {
            busy.Release();
        }
    }

    async Task StepAsync()
    {
        while (editor?.Current is { } current)
        {
            // An answer to show first: until it is shown, what the editor has is not what the session
            // thinks it has, and nothing more is committed.
            if (pending is { } answer)
            {
                if (!await editor.ReadyAsync())
                {
                    Again(readyAgain);
                    return;
                }

                if (!await ShowAsync(answer))
                {
                    Again(readyAgain);
                    return;
                }

                continue;
            }

            if (!ReferenceEquals(current, based.Document))
            {
                if (Joined is not {CanWrite: true})
                {
                    return;
                }

                await CommitAsync(current);
                continue;
            }

            if (latest <= based.Version)
            {
                return;
            }

            if (!await editor.ReadyAsync())
            {
                Again(readyAgain);
                return;
            }

            // Handing in made an edit: committed first, and the answer brings the rest.
            if (!ReferenceEquals(editor.Current, based.Document))
            {
                continue;
            }

            await PullAsync();
        }
    }

    async Task CommitAsync(byte[] local)
    {
        var diff = DocumentPatch.Diff(based.Document, local);
        if (diff.IsEmpty)
        {
            // The same document, read again: nothing to send.
            based = (based.Version, local);
            await editor!.AcceptedAsync(local);
            return;
        }

        var reply = await client.CommitAsync(based.Version, diff.ToBytes(), lifetime.Token);
        latest = Math.Max(latest, reply.Version);
        switch (reply.Result)
        {
            case "accepted":
                based = (reply.Version, local);
                await editor!.AcceptedAsync(local);
                return;
            case "rebased":
            {
                var merged = DocumentPatch.FromBytes(Convert.FromBase64String(reply.Patch!)).Apply(local);
                pending = (local, merged, (reply.Version, merged));
                return;
            }
            case "refused":
                editor!.Note(Refusal(reply));
                pending = (local, based.Document, based);
                return;
            default:
                await RedoAsync(local);
                return;
        }
    }

    // The version the edit was made to is no longer kept: the edit is made again to the latest.
    async Task RedoAsync(byte[] local)
    {
        var latestDocument = await FetchAsync();
        var outcome = DocumentMerge.Merge(based.Document, local, latestDocument.Document);
        if (outcome.Refused)
        {
            editor!.Note(Refusal(outcome.Reason));
        }

        pending = (local, outcome.Document ?? latestDocument.Document, latestDocument);
    }

    // Shows an answer where the editor still shows what it answers. Where the editor moved on meanwhile,
    // its edits since are made to the answer too, and committed after. False where the editor could
    // neither show it nor had moved on: tried again later.
    async Task<bool> ShowAsync((byte[] Expected, byte[] Show, (long Version, byte[] Document) Based) answer)
    {
        var current = editor!.Current!;
        var shown = answer.Show;
        if (!ReferenceEquals(current, answer.Expected))
        {
            var outcome = DocumentMerge.Merge(answer.Expected, current, answer.Show);
            if (outcome.Refused)
            {
                editor.Note(Refusal(outcome.Reason));
            }
            else
            {
                shown = outcome.Document!;
            }
        }

        if (!await editor.AdoptAsync(current, shown))
        {
            // An edit came between: the answer is made to it on the next pass.
            pending = (current, shown, answer.Based);
            return !ReferenceEquals(editor.Current, current);
        }

        pending = null;
        based = answer.Based;
        if (ReferenceEquals(shown, based.Document))
        {
            await editor.AcceptedAsync(shown);
        }

        return true;
    }

    // The latest version, from the version the editor's document descends from.
    async Task PullAsync()
    {
        var expected = based.Document;
        var fetched = await FetchAsync();
        if (await editor!.AdoptAsync(expected, fetched.Document))
        {
            based = fetched;
            await editor.AcceptedAsync(fetched.Document);
        }
    }

    async Task<(long Version, byte[] Document)> FetchAsync()
    {
        var patch = await client.PatchAsync(based.Version, lifetime.Token);
        latest = Math.Max(latest, patch.Version);
        if (patch.Document is { } whole)
        {
            return (patch.Version, Convert.FromBase64String(whole));
        }

        return (patch.Version, DocumentPatch.FromBytes(Convert.FromBase64String(patch.Patch!)).Apply(based.Document));
    }

    /// <summary>
    /// Asks for the paragraph the reader opens. Where it changed after the editor's version, the changes are
    /// shown first and it is asked for again: the editor then finds the paragraph by its id.
    /// </summary>
    public async Task<CoauthorEnter> EnterAsync(string paragraph)
    {
        var reply = await client.EnterAsync(paragraph, based.Version, lifetime.Token);
        if (reply.Result != "stale")
        {
            return reply;
        }

        latest = Math.Max(latest, reply.Version);
        await SyncAsync();
        return await client.EnterAsync(paragraph, based.Version, lifetime.Token);
    }

    /// <summary>The reader is done with the paragraph they had open.</summary>
    public async Task ExitAsync()
    {
        try
        {
            await client.ExitAsync(lifetime.Token);
        }
        catch (HttpRequestException)
        {
            // The session frees it when the page has gone.
        }
    }

    /// <summary>Saves the document now, once the edits made here are in it; null if saved, else why not.</summary>
    public async Task<string?> SaveAsync()
    {
        await SyncAsync();
        if (offline)
        {
            return "The connection is lost: your edits are kept here, and saved when it is back.";
        }

        var saved = await client.SaveAsync(lifetime.Token);
        return saved.Problem;
    }

    async Task ListenAsync(Cancel cancel)
    {
        var retry = TimeSpan.FromSeconds(1);
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await foreach (var state in client.EventsAsync(cancel))
                {
                    retry = TimeSpan.FromSeconds(1);
                    State = state;
                    StateChanged?.Invoke();
                    await editor!.PresenceAsync(state, Joined!.Connection);
                    if (state.Version > latest)
                    {
                        latest = state.Version;
                        Kick();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                // Read again after a while.
            }

            try
            {
                await Task.Delay(retry, time, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, longestWait.Ticks));
            Kick();
        }
    }

    void Again(TimeSpan after)
    {
        again?.Dispose();
        again = time.CreateTimer(_ => Kick(), null, after, Timeout.InfiniteTimeSpan);
    }

    static string Refusal(CoauthorCommit reply)
    {
        if (reply.By is { } by)
        {
            return $"{by} is editing that paragraph, so that edit was not made.";
        }

        return Refusal(reply.Reason);
    }

    static string Refusal(string? reason) =>
        $"That edit could not be merged with someone else's, and was not made. {reason}".TrimEnd();

    /// <summary>Stops listening, and leaves the session.</summary>
    public async ValueTask DisposeAsync()
    {
        again?.Dispose();
        await lifetime.CancelAsync();
        if (Joined != null)
        {
            try
            {
                await client.LeaveAsync();
            }
            catch (HttpRequestException)
            {
                // The session lets the page go once its events have stopped a while.
            }
        }

        lifetime.Dispose();
    }
}
