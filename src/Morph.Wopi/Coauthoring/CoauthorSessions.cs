/// <summary>
/// The coauthoring sessions of this application, one per file being edited, and the pages in each. They are
/// held in memory: the pages editing a file have to reach the same instance of the application, and an
/// instance that cannot take a file's lock (another holds it) opens the file read only.
/// </summary>
sealed class CoauthorSessions(WopiRequests requests, TimeProvider time, IOptions<MorphWopiOptions> options, ILoggerFactory loggers)
{
    readonly ConcurrentDictionary<string, CoauthorSession> sessions = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, (CoauthorSession Session, CoauthorConnection Connection)> connections = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds a page to the session of its file, which it opens if there is none. <paramref name="info"/> is the
    /// file as the CheckFileInfo of the page's token found it.
    /// </summary>
    public async Task<(CoauthorSession Session, CoauthorConnection Connection)> JoinAsync(Uri file, string token, CoauthorUser user, HostFile info, Cancel cancel)
    {
        while (true)
        {
            var session = sessions.GetOrAdd(file.AbsoluteUri, _ => new(file, requests, time, options.Value, loggers.CreateLogger<CoauthorSession>(), Closed));
            if (session.IsClosed)
            {
                sessions.TryRemove(new(file.AbsoluteUri, session));
                continue;
            }

            try
            {
                var connection = await session.JoinAsync(token, user, info, cancel);
                connections[connection.Id] = (session, connection);
                return (session, connection);
            }
            catch (ObjectDisposedException)
            {
                // Closed as the page joined: a new session opens the file again.
                sessions.TryRemove(new(file.AbsoluteUri, session));
            }
        }
    }

    /// <summary>The session and the page a connection id names, if it is still in one.</summary>
    public bool TryFind(string id, out CoauthorSession session, out CoauthorConnection connection)
    {
        if (connections.TryGetValue(id, out var found) &&
            !found.Session.IsClosed)
        {
            (session, connection) = found;
            return true;
        }

        session = null!;
        connection = null!;
        return false;
    }

    public Task LeaveAsync(CoauthorSession session, CoauthorConnection connection)
    {
        connections.TryRemove(connection.Id, out _);
        return session.LeaveAsync(connection);
    }

    void Closed(CoauthorSession session)
    {
        sessions.TryRemove(new(session.File.AbsoluteUri, session));
        foreach (var (id, found) in connections)
        {
            if (found.Session == session)
            {
                connections.TryRemove(id, out _);
            }
        }
    }

    /// <summary>Saves and unlocks every file, as the application stops.</summary>
    public Task CloseAllAsync(Cancel cancel) =>
        Task.WhenAll(sessions.Values.Select(_ => _.CloseAsync(force: true, cancel)));
}

/// <summary>Saves and unlocks the files being edited when the application stops.</summary>
sealed class CoauthorShutdown(CoauthorSessions sessions) : IHostedService
{
    public Task StartAsync(Cancel cancel) =>
        Task.CompletedTask;

    public Task StopAsync(Cancel cancel) =>
        sessions.CloseAllAsync(cancel);
}
