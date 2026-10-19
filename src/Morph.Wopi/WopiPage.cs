/// <summary>
/// The action URL of the discovery: the host page POSTs the access token to it, in a frame ([MS-WOPI]
/// 3.1.5.1.1.2.3), and it answers with the editor's page, the file to open written into it for
/// <c>MorphWopi</c> to read. The token stays out of the URL, as WOPI asks.
/// </summary>
static class WopiPage
{
    public const string ElementId = "morph-wopi";

    public static async Task<IResult> Handle(HttpContext context, string action, IOptions<MorphWopiOptions> options, IWebHostEnvironment environment)
    {
        var settings = options.Value;
        var request = context.Request;
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.ContentSecurityPolicy = WopiHosts.FrameAncestors(settings);

        var source = WopiHosts.Trusted(request.Query["WOPISrc"], settings);
        if (source == null)
        {
            return Message(
                StatusCodes.Status403Forbidden,
                "Morph does not open files for this host: its WOPISrc is not on one of the trusted hosts.");
        }

        string? token = request.Query["access_token"];
        string? ttl = request.Query["access_token_ttl"];
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(context.RequestAborted);
            token = form["access_token"].FirstOrDefault() ?? token;
            ttl = form["access_token_ttl"].FirstOrDefault() ?? ttl;
        }

        if (string.IsNullOrEmpty(token))
        {
            return Message(
                StatusCodes.Status401Unauthorized,
                "No access token came with the file. Open it again from where it is kept.");
        }

        var page = environment.WebRootFileProvider.GetFileInfo(settings.Page);
        if (!page.Exists)
        {
            return Message(
                StatusCodes.Status500InternalServerError,
                $"The editor's page {settings.Page} is not in the web root.");
        }

        string html;
        await using (var stream = page.CreateReadStream())
        using (var reader = new StreamReader(stream))
        {
            html = await reader.ReadToEndAsync(context.RequestAborted);
        }

        long.TryParse(ttl, out var expires);
        var launch = JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["src"] = source.AbsoluteUri,
                ["token"] = token,
                ["ttl"] = expires,
                ["action"] = action,
                ["proxy"] = $"{request.PathBase}{MorphWopiExtensions.ProxyPath}"
            });
        // The serializer escapes <, > and &, so the JSON cannot end the element early.
        var script = $"""<script type="application/json" id="{ElementId}">{launch}</script>""";
        return Results.Content(Inject(html, script), "text/html", Encoding.UTF8);
    }

    public static string Inject(string html, string script)
    {
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (head < 0)
        {
            return script + html;
        }

        return html.Insert(head, script);
    }

    static IResult Message(int status, string message) =>
        Results.Content(
            $"""
             <!doctype html>
             <html lang="en">
             <head><meta charset="utf-8"><title>Morph</title></head>
             <body style="font-family: system-ui, sans-serif; margin: 2rem">
             <p>{WebUtility.HtmlEncode(message)}</p>
             </body>
             </html>
             """,
            "text/html",
            Encoding.UTF8,
            status);
}
