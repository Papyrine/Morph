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
        catch (Exception exception)
            when (exception is
                      JsonException or
                      KeyNotFoundException or
                      InvalidOperationException or
                      FormatException)
        {
            return null;
        }
    }
}