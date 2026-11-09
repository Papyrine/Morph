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
/// The file may change without the session: another client saves it while the session does not hold the
/// lock (before the session could take it, or after it lapsed), or the host's storage is written to. So a
/// lock the session takes, or takes again, is trusted only once the file is seen not to have changed since
/// the session read or saved it (<see cref="CatchUpAsync"/>): a change is taken into the document, merged
/// with the edits not yet saved, and nothing is saved over it. A save also carries the time the host gave
/// for the file the session has (X-COOL-WOPI-Timestamp), which a host for Collabora Online refuses when
/// the file changed since.
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
    // The file as the session last read or saved it: the document it was, the host's version of it, and when
    // the host says it was last changed. Either of the last two may be unknown.
    byte[]? hostDocument;
    string? hostVersion;
    string? hostModified;
    // Whether the file may have changed since: the lock was taken (again) and the file not yet compared.
    bool behind;
    string? lastToken;
    ITimer? autosave;
    ITimer? refresh;
    ITimer? closing;

    public Uri File => file;

    public bool IsClosed { get; private set; }

    /// <summary>
    /// Adds a page with the token the host accepted, for the user it is, and the file as that token's
    /// CheckFileInfo found it. The first page opens the document from the host; the first that may write
    /// takes the file's lock, without which nobody's edits are taken.
    /// </summary>
    public async Task<CoauthorConnection> JoinAsync(string token, CoauthorUser user, HostFile info, Cancel cancel)
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
                await OpenAsync(token, info, cancel);
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

    async Task OpenAsync(string token, HostFile info, Cancel cancel)
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
        // The version of what GetFile sent; CheckFileInfo's, from before it, where the host sends none.
        hostDocument = bytes;
        hostVersion = Header(answer, "X-WOPI-ItemVersion") ?? info.Version;
        hostModified = info.LastModifiedTime;
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
        // The file may have changed since the session read it: while another client held the lock, or
        // between the read and the lock.
        behind = true;
        await CatchUpAsync(token, cancel);
    }

    /// <summary>
    /// Compares the file the host has with the one the session last read or saved, once the session holds
    /// the lock: a file changed meanwhile is taken into the document, merged with the edits not yet saved,
    /// and the pages are told of the new version. Null once caught up, else why not: until then nothing is
    /// saved, and the next save tries again.
    /// </summary>
    async Task<string?> CatchUpAsync(string token, Cancel cancel)
    {
        try
        {
            if (await FileInfoAsync(token, cancel) is not { } info)
            {
                return "the host did not say what the file is now.";
            }

            hostModified = info.LastModifiedTime;
            // Unknown on either side: nothing to compare, as before the session took the lock.
            if (info.Version == null ||
                hostVersion == null ||
                info.Version == hostVersion)
            {
                hostVersion ??= info.Version;
                behind = false;
                return null;
            }

            byte[] theirs;
            string? theirVersion;
            using (var answer = await requests.SendAsync(file, token, "GetFile", cancel: cancel))
            {
                if (!answer.IsSuccessStatusCode)
                {
                    return $"the host answered {(int) answer.StatusCode} to GetFile.";
                }

                theirs = await answer.Content.ReadAsByteArrayAsync(cancel);
                theirVersion = Header(answer, "X-WOPI-ItemVersion") ?? info.Version;
            }

            if (!Editable(theirs))
            {
                logger.LogWarning("{File} was changed elsewhere into a document that cannot be edited here", file);
                return "the file was changed elsewhere into a document that cannot be edited here.";
            }

            theirs = ParagraphIds.StampAll(theirs);
            var next = theirs;
            // Edits not yet saved were made to the file as the session last had it: merged with the change.
            if (dirty)
            {
                var outcome = DocumentMerge.Merge(hostDocument!, document!, theirs);
                if (outcome.Refused)
                {
                    logger.LogWarning("{File} was changed elsewhere, and the edits not yet saved could not be merged with it: {Reason}", file, outcome.Reason);
                    return $"the file was changed elsewhere, and the edits made here could not be merged with it: {outcome.Reason}";
                }

                next = outcome.Document!;
            }

            var previous = document!;
            Adopt(next);
            foreach (var changed in DocumentMerge.Changed(previous, document!))
            {
                lastChanged[changed] = version;
            }

            hostDocument = theirs;
            hostVersion = theirVersion;
            behind = false;
            logger.LogInformation("{File} was changed elsewhere: taken in as version {Version}", file, version);
            Broadcast();
            return null;
        }
        catch (HttpRequestException)
        {
            return "the host of the file could not be reached.";
        }
    }

    async Task<HostFile?> FileInfoAsync(string token, Cancel cancel)
    {
        using var answer = await requests.SendAsync(file, token, "CheckFileInfo", cancel: cancel);
        if (!answer.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            return HostFile.Parse(await answer.Content.ReadAsStringAsync(cancel));
        }
        catch (JsonException)
        {
            return null;
        }
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
            closing = time.CreateTimer(_ => Later(() => CloseAsync(force: false, Cancel.None)), null, Grace, Timeout.InfiniteTimeSpan);
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
        autosave ??= time.CreateTimer(_ => Later(AutosaveAsync), null, options.AutosaveDelay, Timeout.InfiniteTimeSpan);

        Broadcast();
    }

    async Task AutosaveAsync()
    {
        await gate.WaitAsync();
        try
        {
            autosave?.Dispose();
            autosave = null;
            await PutAsync(exit: false, Cancel.None);
        }
        finally
        {
            gate.Release();
        }
    }

    // Puts the document to the host under the lock, with whoever edited it since the last save, taking the
    // lock again if it lapsed. Never over a file changed since the session read or saved it: that change is
    // taken in first. Null if saved, else why not.
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
            if (behind &&
                await CatchUpAsync(token, cancel) is { } stale)
            {
                return NotSaved(stale);
            }

            var answer = await PutFileAsync(token, headers, cancel);
            if (answer is {Status: 409, Changed: true})
            {
                // The host says the file changed since the session read or saved it.
                behind = true;
                if (await CatchUpAsync(token, cancel) is { } changed)
                {
                    return NotSaved(changed);
                }

                answer = await PutFileAsync(token, headers, cancel);
            }
            else if (answer is {Status: 409, Unlocked: true})
            {
                // The lock lapsed: taken again, the file may have changed while nobody held it.
                using var relocked = await requests.SendAsync(file, token, "Lock", lockId, cancel: cancel);
                if (relocked.IsSuccessStatusCode)
                {
                    behind = true;
                    if (await CatchUpAsync(token, cancel) is { } lapsed)
                    {
                        return NotSaved(lapsed);
                    }

                    answer = await PutFileAsync(token, headers, cancel);
                }
            }

            if (answer.Status is >= 200 and < 300)
            {
                dirty = false;
                editors.Clear();
                saved = "Saved";
                if (hostVersion == null)
                {
                    await SavedVersionAsync(token, cancel);
                }

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

    string NotSaved(string reason)
    {
        saved = $"Not saved: {reason}";
        Broadcast();
        return saved;
    }

    // A host that gave no version with the save: its CheckFileInfo's, what the next lock is compared with.
    async Task SavedVersionAsync(string token, Cancel cancel)
    {
        try
        {
            hostVersion = (await FileInfoAsync(token, cancel))?.Version;
        }
        catch (HttpRequestException)
        {
            // Unknown until the next save.
        }
    }

    async Task<(int Status, bool Unlocked, bool Changed, string Reason)> PutFileAsync(string token, Dictionary<string, string> headers, Cancel cancel)
    {
        var sent = document!;
        var content = new ByteArrayContent(sent);
        content.Headers.ContentType = new("application/octet-stream");
        var all = new Dictionary<string, string>(headers);
        if (hostModified != null)
        {
            all["X-COOL-WOPI-Timestamp"] = hostModified;
        }

        using var answer = await requests.SendAsync(file, token, "PutFile", lockId, content, all, cancel);
        var status = (int) answer.StatusCode;
        if (answer.IsSuccessStatusCode)
        {
            // What the host now has, for the next lock and save to be compared with.
            hostDocument = sent;
            hostVersion = Header(answer, "X-WOPI-ItemVersion");
            hostModified = await SavedTimeAsync(answer, cancel);
            return (status, false, false, "");
        }

        if (status != 409)
        {
            return (status, false, false, Reason(answer, $"the host answered {status}."));
        }

        // The file changed since the time the save carried: a host for Collabora Online answers so (1010),
        // naming no lock.
        if (await CoolStatusAsync(answer, cancel) == 1010)
        {
            return (status, false, true, "the file was changed elsewhere.");
        }

        // A conflict naming no lock: the file is not locked at all, as when the lock lapsed.
        var unlocked = string.IsNullOrEmpty(Header(answer, "X-WOPI-Lock"));
        return (status, unlocked, false, Reason(answer, $"the host answered {status}."));
    }

    // The LastModifiedTime a save answers with, which the next save carries.
    static async Task<string?> SavedTimeAsync(HttpResponseMessage answer, Cancel cancel)
    {
        try
        {
            return HostFile.Parse(await answer.Content.ReadAsStringAsync(cancel)).LastModifiedTime;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static async Task<int?> CoolStatusAsync(HttpResponseMessage answer, Cancel cancel)
    {
        try
        {
            using var json = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(cancel));
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("COOLStatusCode", out var code) &&
                code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    static string? Header(HttpResponseMessage answer, string name)
    {
        if (answer.Headers.TryGetValues(name, out var values) &&
            values.FirstOrDefault() is {Length: > 0} value)
        {
            return value;
        }

        return null;
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
                // The lock lapsed, as while the server slept: taken again, the file may have changed while
                // nobody held it.
                using var relocked = await requests.SendAsync(file, token, "Lock", lockId);
                if (relocked.IsSuccessStatusCode)
                {
                    behind = true;
                    if (await CatchUpAsync(token, Cancel.None) is { } stale &&
                        dirty)
                    {
                        NotSaved(stale);
                    }
                }
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
