using System.Net.ServerSentEvents;
using System.Threading.Channels;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

/// <summary>What a commit came to: kept as sent, kept merged with others' edits, refused, or sent against a version no longer kept.</summary>
sealed record CommitReply(string Result, long Version, string? Patch = null, string? Reason = null, string? Paragraph = null, string? By = null);

/// <summary>Whether a paragraph was given to the page that asked, or is someone else's, or changed since that page's version.</summary>
sealed record EnterReply(string Result, long Version, string? By = null);

/// <summary>The changes from a version to the latest: a patch, or the whole document where that version is no longer kept.</summary>
sealed record PatchReply(long Version, string? Patch = null, string? Document = null);

/// <summary>Who is in a session and where, as each page's event stream sends it.</summary>
sealed record SessionState(long Version, IReadOnlyList<SessionPresence> Users, bool Editable, string? Problem, string? Saved);

sealed record SessionPresence(string Connection, string Name, int Color, string? Paragraph, bool CanWrite);

/// <summary>
/// One file edited by several people at once. The session holds the document and the file's one WOPI lock,
/// so the pages in it never lock or save the file themselves. Each page commits its edits as patches against
/// a version it has; an edit against an older version is merged with what came since
/// (<see cref="DocumentMerge"/>), and refused whole where it cannot be. A paragraph a page has open is that
/// page's: an edit by anyone else that changes it is refused, and nobody else may open it. The document is
/// saved to the host a while after the first change not yet saved, and when the last page leaves.
///
/// Everything a session does runs one thing at a time, behind its gate.
/// </summary>
sealed class CoauthorSession(Uri file, WopiRequests requests, TimeProvider time, MorphWopiOptions options, ILogger logger, Action<CoauthorSession> closed)
{
    // A lock lasts 30 minutes unless it is refreshed ([MS-WOPI] 3.3.5.1.3).
    static readonly TimeSpan refreshInterval = TimeSpan.FromMinutes(10);

    // How long a page may be gone before it has left, and the session after its last page left: a reload.
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    // How often an event stream says it is still there, which keeps proxies from closing it.
    static readonly TimeSpan heartbeat = TimeSpan.FromSeconds(20);

    // The versions an edit may be made against: the most recent, within a budget.
    const int keptVersions = 64;
    const long keptBytes = 64L * 1024 * 1024;

    readonly SemaphoreSlim gate = new(1, 1);
    readonly List<(long Version, byte[] Document)> history = [];
    readonly Dictionary<string, long> lastChanged = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, CoauthorConnection> connections = new(StringComparer.Ordinal);
    readonly HashSet<string> editors = new(StringComparer.Ordinal);
    byte[]? document;
    long version;
    bool editable;
    string? lockId;
    string? problem;
    string? saved;
    bool dirty;
    string? lastToken;
    ITimer? autosave;
    ITimer? refresh;
    ITimer? closing;

    public Uri File => file;

    public bool IsClosed { get; private set; }

    /// <summary>
    /// Adds a page with the token the host accepted, for the user it is. The first page opens the document
    /// from the host; the first that may write takes the file's lock, without which nobody's edits are taken.
    /// </summary>
    public async Task<CoauthorConnection> JoinAsync(string token, CoauthorUser user, Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            if (IsClosed)
            {
                throw new ObjectDisposedException(nameof(CoauthorSession));
            }

            if (document == null)
            {
                await OpenAsync(token, cancel);
            }

            var canWrite = user.CanWrite && editable;
            if (canWrite &&
                lockId == null)
            {
                await LockAsync(token, cancel);
            }

            var color = Enumerable.Range(0, int.MaxValue).First(_ => connections.Values.All(connection => connection.Color != _));
            var connection = new CoauthorConnection(NewId(), user.Id, user.Name, token, canWrite, color)
            {
                Version = version
            };
            connections[connection.Id] = connection;
            closing?.Dispose();
            closing = null;
            logger.LogInformation("{User} joined {File} at version {Version}, {Access}", user.Name, file, version, canWrite ? "editing" : "read only");
            Broadcast();
            return connection;
        }
        finally
        {
            gate.Release();
        }
    }

    async Task OpenAsync(string token, Cancel cancel)
    {
        using var answer = await requests.SendAsync(file, token, "GetFile", cancel: cancel);
        if (!answer.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The host answered {(int) answer.StatusCode} to GetFile.", null, answer.StatusCode);
        }

        var bytes = await answer.Content.ReadAsByteArrayAsync(cancel);
        editable = Editable(bytes);
        if (editable)
        {
            bytes = ParagraphIds.StampAll(bytes);
        }
        else
        {
            problem = "This document cannot be edited here.";
        }

        document = bytes;
        version = 1;
        history.Add((version, bytes));
    }

    static bool Editable(byte[] bytes)
    {
        try
        {
            return DocumentOutline.Read(bytes).AllowsEditing;
        }
        catch (Exception exception) when (exception is InvalidDataException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or IOException)
        {
            return false;
        }
    }

    async Task LockAsync(string token, Cancel cancel)
    {
        var id = Guid.NewGuid().ToString("N");
        using var answer = await requests.SendAsync(file, token, "Lock", id, cancel: cancel);
        if (!answer.IsSuccessStatusCode)
        {
            problem = Reason(answer, "The file is locked by someone else.");
            return;
        }

        lockId = id;
        problem = null;
        refresh = time.CreateTimer(_ => Later(RefreshAsync), null, refreshInterval, refreshInterval);
    }

    /// <summary>The document as it is now, and its version.</summary>
    public async Task<(long Version, byte[] Document)> DocumentAsync(Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            return (version, document!);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Takes a page's edit, a patch against <paramref name="baseVersion"/>: kept as it is when that is the
    /// latest, merged with what came since when it is not.
    /// </summary>
    public async Task<CommitReply> CommitAsync(CoauthorConnection connection, long baseVersion, byte[] patch, Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            if (!connection.CanWrite ||
                lockId == null)
            {
                return new("refused", version, Reason: problem ?? "This document is read only.");
            }

            if (Kept(baseVersion) is not { } original)
            {
                return new("stale", version);
            }

            var mine = DocumentPatch.FromBytes(patch).Apply(original);
            var locked = connections.Values
                .Where(_ => _ != connection && _.Paragraph != null)
                .Select(_ => _.Paragraph!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var theirs = document!;
            if (baseVersion == version)
            {
                theirs = original;
            }

            var outcome = DocumentMerge.Merge(original, mine, theirs, locked);
            if (outcome.Refused)
            {
                var by = connections.Values.FirstOrDefault(_ => _ != connection && string.Equals(_.Paragraph, outcome.Paragraph, StringComparison.OrdinalIgnoreCase));
                logger.LogInformation("{User}'s edit to {File} against version {Base} was refused: {Reason}", connection.UserName, file, baseVersion, outcome.Reason);
                return new("refused", version, Reason: outcome.Reason, Paragraph: outcome.Paragraph, By: by?.UserName);
            }

            var previous = document!;
            Adopt(outcome.Document!);
            foreach (var changed in DocumentMerge.Changed(previous, document!))
            {
                lastChanged[changed] = version;
            }

            editors.Add(connection.UserId);
            lastToken = connection.Token;
            connection.Version = version;
            logger.LogInformation("{User}'s edit to {File} against version {Base} made version {Version}", connection.UserName, file, baseVersion, version);
            Changed();
            if (ReferenceEquals(outcome.Document, mine))
            {
                return new("accepted", version);
            }

            return new("rebased", version, Convert.ToBase64String(DocumentPatch.Diff(mine, document!).ToBytes()));
        }
        finally
        {
            gate.Release();
        }
    }

    void Adopt(byte[] next)
    {
        document = next;
        version++;
        history.Add((version, next));
        var bytes = history.Sum(_ => (long) _.Document.Length);
        while (history.Count > 1 &&
               (history.Count > keptVersions || bytes > keptBytes))
        {
            bytes -= history[0].Document.Length;
            history.RemoveAt(0);
        }
    }

    byte[]? Kept(long kept)
    {
        foreach (var (each, bytes) in history)
        {
            if (each == kept)
            {
                return bytes;
            }
        }

        return null;
    }

    /// <summary>What changed from <paramref name="from"/> to now, for a page that has <paramref name="from"/>.</summary>
    public async Task<PatchReply> PatchAsync(CoauthorConnection connection, long from, Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            connection.Version = Math.Max(connection.Version, from);
            if (Kept(from) is { } original)
            {
                return new(version, Patch: Convert.ToBase64String(DocumentPatch.Diff(original, document!).ToBytes()));
            }

            return new(version, Document: Convert.ToBase64String(document!));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Gives a page the paragraph it opens, unless someone else has it open, or it changed after the version
    /// the page has: that page then takes the changes first and asks again.
    /// </summary>
    public async Task<EnterReply> EnterAsync(CoauthorConnection connection, string paragraph, long applied, Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            connection.Version = Math.Max(connection.Version, applied);
            var holder = connections.Values.FirstOrDefault(_ => _ != connection && string.Equals(_.Paragraph, paragraph, StringComparison.OrdinalIgnoreCase));
            if (holder != null)
            {
                logger.LogInformation("{User} may not open paragraph {Paragraph} of {File}: {Holder} has it open", connection.UserName, paragraph, file, holder.UserName);
                return new("refused", version, holder.UserName);
            }

            if (lastChanged.GetValueOrDefault(paragraph) > applied)
            {
                return new("stale", version);
            }

            logger.LogInformation("{User} opened paragraph {Paragraph} of {File}", connection.UserName, paragraph, file);
            connection.Paragraph = paragraph;
            Broadcast();
            return new("granted", version);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The page is done with the paragraph it had open.</summary>
    public async Task ExitAsync(CoauthorConnection connection, Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            if (connection.Paragraph == null)
            {
                return;
            }

            connection.Paragraph = null;
            Broadcast();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Saves what is not yet saved, now; null if it was saved, else why not.</summary>
    public async Task<string?> SaveAsync(Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            return await PutAsync(exit: false, cancel);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The page has gone: its paragraph is free, and with no page left the session closes after a while.</summary>
    public async Task LeaveAsync(CoauthorConnection connection)
    {
        await gate.WaitAsync();
        try
        {
            Remove(connection);
        }
        finally
        {
            gate.Release();
        }
    }

    void Remove(CoauthorConnection connection)
    {
        if (!connections.Remove(connection.Id))
        {
            return;
        }

        connection.Leaving?.Dispose();
        connection.Events.Writer.TryComplete();
        logger.LogInformation("{User} left {File}", connection.UserName, file);
        Broadcast();
        if (connections.Count == 0 &&
            closing == null)
        {
            closing = time.CreateTimer(_ => Later(() => CloseAsync(force: false, CancellationToken.None)), null, Grace, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Saves what is not saved, lets the file's lock go, and closes: no page may join it after. Unless
    /// <paramref name="force"/>d, as when the application stops, it stays open for a page that came back.
    /// </summary>
    public async Task CloseAsync(bool force, Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        var closedNow = false;
        try
        {
            if (IsClosed ||
                !force && connections.Count > 0)
            {
                return;
            }

            IsClosed = true;
            closedNow = true;
            await PutAsync(exit: true, cancel);
            if (lockId != null &&
                Token() is { } token)
            {
                try
                {
                    using var _ = await requests.SendAsync(file, token, "Unlock", lockId, cancel: cancel);
                }
                catch (HttpRequestException)
                {
                    // The lock lapses on its own.
                }
            }

            lockId = null;
            autosave?.Dispose();
            refresh?.Dispose();
            closing?.Dispose();
            foreach (var connection in connections.Values)
            {
                connection.Events.Writer.TryComplete();
            }
        }
        finally
        {
            gate.Release();
            if (closedNow)
            {
                closed(this);
            }
        }
    }

    /// <summary>
    /// The session's state as it changes, for one page: what it is now, then each change, and a heartbeat
    /// between. A page whose last stream has been closed for a while has left.
    /// </summary>
    public async IAsyncEnumerable<SseItem<string>> EventsAsync(CoauthorConnection connection, [EnumeratorCancellation] Cancel cancel)
    {
        await Streaming(connection, 1);
        try
        {
            var reader = connection.Events.Reader;
            yield return new(await StateAsync(reader, cancel), "state");
            while (!cancel.IsCancellationRequested)
            {
                var waiting = reader.WaitToReadAsync(cancel).AsTask();
                var beat = Task.Delay(heartbeat, time, cancel);
                if (await Task.WhenAny(waiting, beat) == beat)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        break;
                    }

                    yield return new("", "ping");
                    continue;
                }

                if (!await waiting)
                {
                    break;
                }

                while (reader.TryRead(out var state))
                {
                    yield return new(state, "state");
                }
            }
        }
        finally
        {
            await Streaming(connection, -1);
        }
    }

    // The state now, which supersedes any still waiting to be sent.
    async Task<string> StateAsync(ChannelReader<string> reader, Cancel cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            while (reader.TryRead(out _))
            {
            }

            return State();
        }
        finally
        {
            gate.Release();
        }
    }

    async Task Streaming(CoauthorConnection connection, int change)
    {
        await gate.WaitAsync();
        try
        {
            connection.Streams += change;
            connection.Leaving?.Dispose();
            connection.Leaving = null;
            if (connection.Streams == 0 &&
                connections.ContainsKey(connection.Id))
            {
                connection.Leaving = time.CreateTimer(_ => Later(() => LeaveAsync(connection)), null, Grace, Timeout.InfiniteTimeSpan);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    // A change not yet saved: saved a while after the first, so a stream of edits still gets saved.
    void Changed()
    {
        dirty = true;
        saved = null;
        if (autosave == null)
        {
            autosave = time.CreateTimer(_ => Later(AutosaveAsync), null, options.AutosaveDelay, Timeout.InfiniteTimeSpan);
        }

        Broadcast();
    }

    async Task AutosaveAsync()
    {
        await gate.WaitAsync();
        try
        {
            autosave?.Dispose();
            autosave = null;
            await PutAsync(exit: false, CancellationToken.None);
        }
        finally
        {
            gate.Release();
        }
    }

    // Puts the document to the host under the lock, with whoever edited it since the last save, taking the
    // lock again if it lapsed. Null if saved, else why not.
    async Task<string?> PutAsync(bool exit, Cancel cancel)
    {
        if (!dirty ||
            lockId == null ||
            Token() is not { } token)
        {
            return null;
        }

        var headers = new Dictionary<string, string>
        {
            ["X-WOPI-Editors"] = string.Join(',', editors),
            [exit ? "X-COOL-WOPI-IsExitSave" : "X-COOL-WOPI-IsAutosave"] = "true"
        };
        try
        {
            var answer = await PutFileAsync(token, headers, cancel);
            if (answer.Status == 409 &&
                answer.Unlocked)
            {
                using var relocked = await requests.SendAsync(file, token, "Lock", lockId, cancel: cancel);
                if (relocked.IsSuccessStatusCode)
                {
                    answer = await PutFileAsync(token, headers, cancel);
                }
            }

            if (answer.Status is >= 200 and < 300)
            {
                dirty = false;
                editors.Clear();
                saved = "Saved";
                Broadcast();
                return null;
            }

            saved = $"Not saved: {answer.Reason}";
        }
        catch (HttpRequestException)
        {
            saved = "Not saved: the host of the file could not be reached.";
        }

        Broadcast();
        return saved;
    }

    async Task<(int Status, bool Unlocked, string Reason)> PutFileAsync(string token, Dictionary<string, string> headers, Cancel cancel)
    {
        var content = new ByteArrayContent(document!);
        content.Headers.ContentType = new("application/octet-stream");
        using var answer = await requests.SendAsync(file, token, "PutFile", lockId, content, headers, cancel);
        var status = (int) answer.StatusCode;
        string? current = null;
        if (answer.Headers.TryGetValues("X-WOPI-Lock", out var values))
        {
            current = values.FirstOrDefault();
        }

        // A conflict naming no lock: the file is not locked at all, as when the lock lapsed.
        var unlocked = status == 409 &&
                       string.IsNullOrEmpty(current);
        return (status, unlocked, Reason(answer, $"the host answered {status}."));
    }

    async Task RefreshAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (lockId == null ||
                Token() is not { } token)
            {
                return;
            }

            using var answer = await requests.SendAsync(file, token, "RefreshLock", lockId);
            if (answer.StatusCode == HttpStatusCode.Conflict)
            {
                // The lock lapsed, as while the server slept: take it again.
                using var _ = await requests.SendAsync(file, token, "Lock", lockId);
            }
        }
        catch (HttpRequestException)
        {
            // Tried again at the next refresh.
        }
        finally
        {
            gate.Release();
        }
    }

    // A token that may write: of a page still here, or of the last that committed.
    string? Token() =>
        connections.Values.LastOrDefault(_ => _.CanWrite)?.Token ?? lastToken;

    void Broadcast()
    {
        var state = State();
        foreach (var connection in connections.Values)
        {
            connection.Events.Writer.TryWrite(state);
        }
    }

    string State() =>
        JsonSerializer.Serialize(
            new SessionState(
                version,
                [.. connections.Values.Select(_ => new SessionPresence(_.Id, _.UserName, _.Color, _.Paragraph, _.CanWrite))],
                editable && lockId != null,
                problem,
                saved),
            CoauthorEndpoints.Json);

    static string Reason(HttpResponseMessage answer, string fallback)
    {
        if (answer.Headers.TryGetValues("X-WOPI-LockFailureReason", out var values) &&
            values.FirstOrDefault() is {Length: > 0} reason)
        {
            return reason;
        }

        return fallback;
    }

    // Runs a timer's work off the timer's thread.
    static void Later(Func<Task> work) =>
        _ = Task.Run(work);

    static string NewId() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');
}
