/// <summary>
/// The editor's way to the WOPI host. The editor runs in the browser, which neither may call another origin
/// nor holds the proof key, so it asks this endpoint for an operation by name; the request goes from here to
/// the file's WOPISrc, signed (<see cref="WopiRequests"/>), and the answer comes back as the host gave it
/// ([MS-WOPI] 3.3.5).
/// </summary>
static class WopiProxy
{
    public const string ClientName = WopiRequests.ClientName;
    public const string SourceHeader = "X-Morph-Wopi-Src";
    public const string TokenHeader = "X-Morph-Wopi-Token";

    // What of the host's answer the editor reads.
    static string[] returned = ["X-WOPI-Lock", "X-WOPI-LockFailureReason", "X-WOPI-ItemVersion"];

    public static async Task Handle(HttpContext context, string operation, IOptions<MorphWopiOptions> options, WopiRequests requests)
    {
        var settings = options.Value;
        var request = context.Request;
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        if (!WopiRequests.Knows(operation))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        string? source = request.Headers[SourceHeader];
        string? token = request.Headers[TokenHeader];
        string? lockId = request.Headers["X-WOPI-Lock"];
        // An unlock as the page goes away is a beacon, which sends a form and no headers.
        if (operation != "PutFile" &&
            request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(context.RequestAborted);
            source ??= form["src"].FirstOrDefault();
            token ??= form["token"].FirstOrDefault();
            lockId ??= form["lock"].FirstOrDefault();
        }

        var file = WopiHosts.Trusted(source, settings);
        if (file == null ||
            string.IsNullOrEmpty(token))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (operation == "PutFile" &&
            request.ContentLength > settings.MaxFileSize)
        {
            response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        HttpContent? content = null;
        if (operation == "PutFile")
        {
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is {IsReadOnly: false} limit)
            {
                limit.MaxRequestBodySize = settings.MaxFileSize;
            }

            var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, context.RequestAborted);
            buffer.Position = 0;
            content = new StreamContent(buffer);
            content.Headers.ContentType = new("application/octet-stream");
        }

        HttpResponseMessage answer;
        try
        {
            answer = await requests.SendAsync(file, token, operation, lockId, content, cancel: context.RequestAborted);
        }
        catch (HttpRequestException)
        {
            response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using (answer)
        {
            response.StatusCode = (int) answer.StatusCode;
            foreach (var name in returned)
            {
                if (answer.Headers.TryGetValues(name, out var values))
                {
                    response.Headers[name] = values.ToArray();
                }
            }

            if (answer.Content.Headers.ContentType is { } type)
            {
                response.ContentType = type.ToString();
            }

            await answer.Content.CopyToAsync(response.Body, context.RequestAborted);
        }
    }

    /// <summary>
    /// The file's URL, or its contents' (<c>/contents</c> after the path), with the token as access_token.
    /// </summary>
    public static Uri Url(Uri file, string token, bool contents)
    {
        var path = file.GetLeftPart(UriPartial.Path);
        if (contents)
        {
            path = path.TrimEnd('/') + "/contents";
        }

        var query = "?";
        if (file.Query.Length > 1)
        {
            query = file.Query + "&";
        }

        return new($"{path}{query}access_token={Uri.EscapeDataString(token)}");
    }
}
