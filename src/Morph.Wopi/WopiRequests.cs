/// <summary>
/// Sends a WOPI operation to a host, signed with the proof keys ([MS-WOPI] 2.2.1, 3.3.5): the proxy sends
/// the editor's on its behalf, and a coauthoring session its own (the lock it holds, its saves).
/// </summary>
sealed class WopiRequests(IHttpClientFactory clients, WopiProofs proofs, TimeProvider time)
{
    public const string ClientName = "Morph.Wopi";

    sealed record Operation(HttpMethod Method, bool Contents, string? Override);

    static Dictionary<string, Operation> operations = new(StringComparer.Ordinal)
    {
        ["CheckFileInfo"] = new(HttpMethod.Get, false, null),
        ["GetFile"] = new(HttpMethod.Get, true, null),
        ["PutFile"] = new(HttpMethod.Post, true, "PUT"),
        ["Lock"] = new(HttpMethod.Post, false, "LOCK"),
        ["GetLock"] = new(HttpMethod.Post, false, "GET_LOCK"),
        ["RefreshLock"] = new(HttpMethod.Post, false, "REFRESH_LOCK"),
        ["Unlock"] = new(HttpMethod.Post, false, "UNLOCK")
    };

    public static bool Knows(string operation) =>
        operations.ContainsKey(operation);

    /// <summary>
    /// Sends <paramref name="operation"/> for the file at <paramref name="file"/> with the token, the lock and,
    /// for PutFile, the content; <paramref name="headers"/> go with it, as X-WOPI-Editors does with a save.
    /// The answer is the host's, its body not yet read.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        Uri file,
        string token,
        string operation,
        string? lockId = null,
        HttpContent? content = null,
        IReadOnlyDictionary<string, string>? headers = null,
        Cancel cancel = default)
    {
        var each = operations[operation];
        var url = WopiProxy.Url(file, token, each.Contents);
        using var message = new HttpRequestMessage(each.Method, url);
        if (each.Override is { } value)
        {
            message.Headers.Add("X-WOPI-Override", value);
        }

        if (!string.IsNullOrEmpty(lockId))
        {
            message.Headers.Add("X-WOPI-Lock", lockId);
        }

        foreach (var (name, header) in headers ?? new Dictionary<string, string>())
        {
            message.Headers.Add(name, header);
        }

        var ticks = time.GetUtcNow().UtcTicks;
        var (proof, proofOld) = proofs.Sign(token, url.AbsoluteUri, ticks);
        message.Headers.Add("X-WOPI-Proof", proof);
        message.Headers.Add("X-WOPI-ProofOld", proofOld);
        message.Headers.Add("X-WOPI-TimeStamp", ticks.ToString(CultureInfo.InvariantCulture));
        if (content != null)
        {
            message.Content = content;
        }
        else if (each.Method == HttpMethod.Post)
        {
            message.Content = new ByteArrayContent([]);
        }

        var client = clients.CreateClient(ClientName);
        return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancel);
    }
}
