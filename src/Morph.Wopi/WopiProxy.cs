/// <summary>
/// The editor's way to the WOPI host. The editor runs in the browser, which neither may call another origin
/// nor holds the proof key, so it asks this endpoint for an operation by name; the request goes from here to
/// the file's WOPISrc, signed, and the answer comes back as the host gave it ([MS-WOPI] 3.3.5).
/// </summary>
static class WopiProxy
{
    public const string ClientName = "Morph.Wopi";
    public const string SourceHeader = "X-Morph-Wopi-Src";
    public const string TokenHeader = "X-Morph-Wopi-Token";

    record Operation(HttpMethod Method, bool Contents, string? Override);

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

    // What of the host's answer the editor reads.
    static string[] returned = ["X-WOPI-Lock", "X-WOPI-LockFailureReason", "X-WOPI-ItemVersion"];

    public static async Task Handle(HttpContext context, string operation, IOptions<MorphWopiOptions> options, IHttpClientFactory clients, WopiProofs proofs, TimeProvider time)
    {
        var settings = options.Value;
        var request = context.Request;
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        if (!operations.TryGetValue(operation, out var each))
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

        var url = Url(file, token, each.Contents);
        using var message = new HttpRequestMessage(each.Method, url);
        if (each.Override is { } value)
        {
            message.Headers.Add("X-WOPI-Override", value);
        }

        if (!string.IsNullOrEmpty(lockId))
        {
            message.Headers.Add("X-WOPI-Lock", lockId);
        }

        var ticks = time.GetUtcNow().UtcTicks;
        var (proof, proofOld) = proofs.Sign(token, url.AbsoluteUri, ticks);
        message.Headers.Add("X-WOPI-Proof", proof);
        message.Headers.Add("X-WOPI-ProofOld", proofOld);
        message.Headers.Add("X-WOPI-TimeStamp", ticks.ToString(CultureInfo.InvariantCulture));
        if (operation == "PutFile")
        {
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is {IsReadOnly: false} limit)
            {
                limit.MaxRequestBodySize = settings.MaxFileSize;
            }

            var content = new MemoryStream();
            await request.Body.CopyToAsync(content, context.RequestAborted);
            content.Position = 0;
            message.Content = new StreamContent(content);
            message.Content.Headers.ContentType = new("application/octet-stream");
        }
        else if (each.Method == HttpMethod.Post)
        {
            message.Content = new ByteArrayContent([]);
        }

        using var client = clients.CreateClient(ClientName);
        HttpResponseMessage answer;
        try
        {
            answer = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
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
