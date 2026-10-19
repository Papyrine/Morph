using System.Net.Http.Headers;
using System.Text.Json;

namespace Morph;

/// <summary>
/// The file a WOPI host opened Morph for, as Morph.Wopi writes it into the page: the file's WOPISrc, the
/// access token, when the token expires, the action (view or edit) and where the proxy to the host is.
/// </summary>
sealed record WopiLaunchInfo(string Source, string Token, long Ttl, string Action, string Proxy)
{
    public static WopiLaunchInfo? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var source = root.GetProperty("src").GetString();
            var token = root.GetProperty("token").GetString();
            var proxy = root.GetProperty("proxy").GetString();
            if (source is null ||
                token is null ||
                proxy is null)
            {
                return null;
            }

            return new(source, token, root.GetProperty("ttl").GetInt64(), root.GetProperty("action").GetString() ?? "view", proxy);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// What the editor reads of CheckFileInfo ([MS-WOPI] 3.3.5.1.1.2): the file's name, what the user may do
/// with it, and the host's links.
/// </summary>
sealed record WopiFileInfo(
    string BaseFileName,
    string? UserFriendlyName,
    bool UserCanWrite,
    bool ReadOnly,
    bool SupportsLocks,
    bool SupportsUpdate,
    bool DisablePrint,
    bool DisableExport,
    string? CloseUrl,
    string? BrandName,
    string? BrandUrl,
    string? FolderName,
    string? FolderUrl)
{
    public static WopiFileInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(
            Text(root, "BaseFileName") ?? "Document",
            Text(root, "UserFriendlyName"),
            Flag(root, "UserCanWrite"),
            Flag(root, "ReadOnly"),
            Flag(root, "SupportsLocks"),
            Flag(root, "SupportsUpdate"),
            Flag(root, "DisablePrint") || Flag(root, "HidePrintOption"),
            Flag(root, "DisableExport") || Flag(root, "HideExportOption"),
            Text(root, "CloseUrl"),
            Text(root, "BreadcrumbBrandName"),
            Text(root, "BreadcrumbBrandUrl"),
            Text(root, "BreadcrumbFolderName"),
            Text(root, "BreadcrumbFolderUrl"));
    }

    static string? Text(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.GetString() is {Length: > 0} text)
        {
            return text;
        }

        return null;
    }

    static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;

    /// <summary>Whether the user may edit and save the file over itself, under a lock.</summary>
    public bool Editable =>
        UserCanWrite &&
        !ReadOnly &&
        SupportsUpdate &&
        SupportsLocks;
}

/// <summary>
/// A lock operation's answer: its status, and the lock the file has when it is not the one asked for.
/// </summary>
readonly record struct WopiReply(int Status, string? Lock, string? Reason)
{
    public bool Succeeded => Status == 200;

    /// <summary>A 409 with no lock: the file is not locked at all, as after a lock expired.</summary>
    public bool Unlocked =>
        Status == 409 &&
        string.IsNullOrEmpty(Lock);
}

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
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
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
