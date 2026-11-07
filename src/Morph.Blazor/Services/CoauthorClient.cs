using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

namespace Morph;

/// <summary>A page's place in a coauthoring session, as joining gave it.</summary>
sealed record CoauthorJoined(string Connection, string Name, int Color, bool CanWrite, long Version);

/// <summary>What a commit came to: accepted, rebased (with the patch from what was sent to the merged version), refused or stale.</summary>
sealed record CoauthorCommit(string Result, long Version, string? Patch, string? Reason, string? Paragraph, string? By);

/// <summary>Whether the paragraph asked for was given, is someone else's, or changed since the version asked with.</summary>
sealed record CoauthorEnter(string Result, long Version, string? By);

/// <summary>What changed from a version to the latest: a patch, or the whole document.</summary>
sealed record CoauthorPatch(long Version, string? Patch, string? Document);

/// <summary>Who is in the session and where, the latest version, and whether the document is saved.</summary>
sealed record CoauthorState(long Version, IReadOnlyList<CoauthorPresence> Users, bool Editable, string? Problem, string? Saved);

/// <summary>A page in the session: its connection, its user's name, its colour and the paragraph it has open.</summary>
sealed record CoauthorPresence(string Connection, string Name, int Color, string? Paragraph, bool CanWrite);

/// <summary>Whether a save went through, and why not.</summary>
sealed record CoauthorSaved(bool Saved, string? Problem);

/// <summary>
/// A page's calls to its coauthoring session in Morph.Wopi (<c>/morph/wopi/session</c>, beside the proxy):
/// it joins with the file's WOPISrc and token, as it calls the proxy, and names its connection after.
/// </summary>
sealed class CoauthorClient(HttpClient http, WopiLaunchInfo launch)
{
    static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    string? connection;

    // The session's endpoints sit beside the proxy, under the application's path base.
    string Base =>
        launch.Proxy[..launch.Proxy.LastIndexOf('/')] + "/session";

    string Url(string action) =>
        $"{Base}/{connection}/{action}";

    /// <summary>The URL a beacon posts to as the page goes away, which names the connection and needs no headers.</summary>
    public string LeaveUrl => Url("leave");

    /// <summary>
    /// Joins the file's session; null where the server has no sessions (coauthoring off, or an older
    /// Morph.Wopi), and the page edits the file on its own.
    /// </summary>
    public async Task<CoauthorJoined?> JoinAsync(Cancel cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/join");
        request.Headers.Add("X-Morph-Wopi-Src", launch.Source);
        request.Headers.Add("X-Morph-Wopi-Token", launch.Token);
        request.Content = new ByteArrayContent([]);
        using var response = await http.SendAsync(request, cancel);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            return null;
        }

        Ensure(response);
        var joined = await Read<CoauthorJoined>(response, cancel);
        connection = joined.Connection;
        return joined;
    }

    /// <summary>The document as the session has it, and its version.</summary>
    public async Task<(long Version, byte[] Document)> DocumentAsync(Cancel cancel = default)
    {
        using var response = await http.GetAsync(Url("document"), cancel);
        Ensure(response);
        var version = long.Parse(response.Headers.GetValues("X-Morph-Version").First(), CultureInfo.InvariantCulture);
        return (version, await response.Content.ReadAsByteArrayAsync(cancel));
    }

    /// <summary>Commits an edit: the patch from <paramref name="baseVersion"/>'s document to the page's.</summary>
    public async Task<CoauthorCommit> CommitAsync(long baseVersion, byte[] patch, Cancel cancel = default)
    {
        var content = new ByteArrayContent(patch);
        content.Headers.ContentType = new("application/octet-stream");
        using var response = await http.PostAsync($"{Url("commit")}?base={baseVersion.ToString(CultureInfo.InvariantCulture)}", content, cancel);
        Ensure(response);
        return await Read<CoauthorCommit>(response, cancel);
    }

    public async Task<CoauthorPatch> PatchAsync(long from, Cancel cancel = default)
    {
        using var response = await http.GetAsync($"{Url("patch")}?from={from.ToString(CultureInfo.InvariantCulture)}", cancel);
        Ensure(response);
        return await Read<CoauthorPatch>(response, cancel);
    }

    /// <summary>Asks for the paragraph the page opens, saying the version the page has.</summary>
    public async Task<CoauthorEnter> EnterAsync(string paragraph, long version, Cancel cancel = default)
    {
        var url = $"{Url("enter")}?paragraph={Uri.EscapeDataString(paragraph)}&version={version.ToString(CultureInfo.InvariantCulture)}";
        using var response = await http.PostAsync(url, new ByteArrayContent([]), cancel);
        Ensure(response);
        return await Read<CoauthorEnter>(response, cancel);
    }

    public async Task ExitAsync(Cancel cancel = default)
    {
        using var response = await http.PostAsync(Url("exit"), new ByteArrayContent([]), cancel);
        Ensure(response);
    }

    public async Task<CoauthorSaved> SaveAsync(Cancel cancel = default)
    {
        using var response = await http.PostAsync(Url("save"), new ByteArrayContent([]), cancel);
        Ensure(response);
        return await Read<CoauthorSaved>(response, cancel);
    }

    public async Task LeaveAsync(Cancel cancel = default)
    {
        using var response = await http.PostAsync(LeaveUrl, new ByteArrayContent([]), cancel);
    }

    /// <summary>The session's state as it changes, read off its Server-Sent Events until cancelled or closed.</summary>
    public async IAsyncEnumerable<CoauthorState> EventsAsync([EnumeratorCancellation] Cancel cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("events"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        Ensure(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        await foreach (var item in SseParser.Create(stream).EnumerateAsync(cancel))
        {
            if (item.EventType != "state")
            {
                continue;
            }

            if (JsonSerializer.Deserialize<CoauthorState>(item.Data, json) is { } state)
            {
                yield return state;
            }
        }
    }

    static async Task<T> Read<T>(HttpResponseMessage response, Cancel cancel) =>
        await response.Content.ReadFromJsonAsync<T>(json, cancel) ??
        throw new HttpRequestException("The session's answer could not be read.");

    static void Ensure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(WopiSession.Problem((int) response.StatusCode), null, response.StatusCode);
    }
}
