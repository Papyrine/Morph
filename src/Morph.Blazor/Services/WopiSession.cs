namespace Morph;

/// <summary>
/// The WOPI operations the editor asks of the host, through Morph.Wopi's proxy (an operation by name, the
/// WOPISrc and the token as headers), which signs them and sends them on.
/// </summary>
sealed class WopiSession(HttpClient http, WopiLaunchInfo launch)
{
    public WopiLaunchInfo Launch => launch;

    public async Task<WopiFileInfo> CheckFileInfoAsync(Cancel cancel = default)
    {
        using var response = await SendAsync("CheckFileInfo", null, null, cancel);
        Ensure(response);
        return WopiFileInfo.Parse(await response.Content.ReadAsStringAsync(cancel));
    }

    public async Task<byte[]> GetFileAsync(Cancel cancel = default)
    {
        using var response = await SendAsync("GetFile", null, null, cancel);
        Ensure(response);
        return await response.Content.ReadAsByteArrayAsync(cancel);
    }

    public Task<WopiReply> LockAsync(string id, Cancel cancel = default) =>
        ReplyAsync("Lock", id, null, cancel);

    public Task<WopiReply> RefreshLockAsync(string id, Cancel cancel = default) =>
        ReplyAsync("RefreshLock", id, null, cancel);

    public Task<WopiReply> UnlockAsync(string id, Cancel cancel = default) =>
        ReplyAsync("Unlock", id, null, cancel);

    public Task<WopiReply> PutFileAsync(byte[] bytes, string id, Cancel cancel = default) =>
        ReplyAsync("PutFile", id, bytes, cancel);

    /// <summary>The proxy's URL for an operation, and the form a beacon posts it.</summary>
    public string Url(string operation) =>
        $"{launch.Proxy}/{operation}";

    public Dictionary<string, string> Fields(string lockId) =>
        new()
        {
            ["src"] = launch.Source,
            ["token"] = launch.Token,
            ["lock"] = lockId
        };

    async Task<WopiReply> ReplyAsync(string operation, string id, byte[]? content, Cancel cancel)
    {
        using var response = await SendAsync(operation, id, content, cancel);
        return new((int) response.StatusCode, Header(response, "X-WOPI-Lock"), Header(response, "X-WOPI-LockFailureReason"));
    }

    async Task<HttpResponseMessage> SendAsync(string operation, string? lockId, byte[]? content, Cancel cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(operation));
        request.Headers.Add("X-Morph-Wopi-Src", launch.Source);
        request.Headers.Add("X-Morph-Wopi-Token", launch.Token);
        if (lockId is not null)
        {
            request.Headers.Add("X-WOPI-Lock", lockId);
        }

        var body = new ByteArrayContent(content ?? []);
        body.Headers.ContentType = new("application/octet-stream");
        request.Content = body;
        return await http.SendAsync(request, cancel);
    }

    static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    static void Ensure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(Problem((int) response.StatusCode), null, response.StatusCode);
    }

    /// <summary>What a failed operation's status means to the reader.</summary>
    public static string Problem(int status) =>
        status switch
        {
            401 => "The access to the file has expired. Open it again from where it is kept.",
            403 => "Morph does not open files for this host.",
            404 => "The file is not there any more.",
            409 => "The file is locked by someone else.",
            413 => "The file is too large for the host to save.",
            502 or 503 or 504 => "The host of the file could not be reached.",
            _ => $"The host of the file answered {status}."
        };
}
