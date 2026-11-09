/// <summary>
/// The endpoints of coauthoring, under <c>/morph/wopi/session</c>. A page joins with the file's WOPISrc and
/// its token, as it calls the proxy, and is given a connection id; everything after names the connection in
/// the path. The page reads the session's state as Server-Sent Events, and fetches what changed.
/// </summary>
static class CoauthorEndpoints
{
    public const string Path = "/morph/wopi/session";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The reply to a join: the page's connection, who it is, and whether its edits are taken.</summary>
    // ReSharper disable NotAccessedPositionalProperty.Local
    sealed record JoinReply(string Connection, string Name, int Color, bool CanWrite, long Version);
    // ReSharper restore NotAccessedPositionalProperty.Local

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost($"{Path}/join", JoinAsync).DisableAntiforgery();
        endpoints.MapGet($"{Path}/{{connection}}/document", DocumentAsync);
        endpoints.MapPost($"{Path}/{{connection}}/commit", CommitAsync).DisableAntiforgery();
        endpoints.MapGet($"{Path}/{{connection}}/patch", PatchAsync);
        endpoints.MapPost($"{Path}/{{connection}}/enter", EnterAsync).DisableAntiforgery();
        endpoints.MapPost($"{Path}/{{connection}}/exit", ExitAsync).DisableAntiforgery();
        endpoints.MapPost($"{Path}/{{connection}}/save", SaveAsync).DisableAntiforgery();
        endpoints.MapPost($"{Path}/{{connection}}/leave", LeaveAsync).DisableAntiforgery();
        endpoints.MapGet($"{Path}/{{connection}}/events", Events);
    }

    // The host says who the token is for and what it may do; the page joins the file's session as that user.
    static async Task<IResult> JoinAsync(HttpContext context, IOptions<MorphWopiOptions> options, WopiRequests requests, CoauthorSessions sessions)
    {
        context.Response.Headers.CacheControl = "no-store";
        var request = context.Request;
        var file = WopiHosts.Trusted(request.Headers[WopiProxy.SourceHeader], options.Value);
        string? token = request.Headers[WopiProxy.TokenHeader];
        if (file == null ||
            string.IsNullOrEmpty(token))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var cancel = context.RequestAborted;
        try
        {
            CoauthorUser user;
            HostFile info;
            using (var answer = await requests.SendAsync(file, token, "CheckFileInfo", cancel: cancel))
            {
                if (!answer.IsSuccessStatusCode)
                {
                    return Results.StatusCode((int) answer.StatusCode);
                }

                var json = await answer.Content.ReadAsStringAsync(cancel);
                user = CoauthorUser.Parse(json);
                info = HostFile.Parse(json);
            }

            var (_, connection) = await sessions.JoinAsync(file, token, user, info, cancel);
            return Results.Json(new JoinReply(connection.Id, connection.UserName, connection.Color, connection.CanWrite, connection.Version), Json);
        }
        catch (HttpRequestException exception)
        {
            return Results.StatusCode((int?) exception.StatusCode ?? StatusCodes.Status502BadGateway);
        }
    }

    static async Task<IResult> DocumentAsync(string connection, CoauthorSessions sessions, HttpContext context)
    {
        if (!sessions.TryFind(connection, out var session, out _))
        {
            return Results.NotFound();
        }

        var (version, document) = await session.DocumentAsync(context.RequestAborted);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Morph-Version"] = version.ToString(CultureInfo.InvariantCulture);
        return Results.Bytes(document, "application/octet-stream");
    }

    // The body is the patch, against the version in the query.
    static async Task<IResult> CommitAsync(string connection, long @base, CoauthorSessions sessions, IOptions<MorphWopiOptions> options, HttpContext context)
    {
        if (!sessions.TryFind(connection, out var session, out var found))
        {
            return Results.NotFound();
        }

        if (context.Request.ContentLength > options.Value.MaxFileSize)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        try
        {
            return Results.Json(await session.CommitAsync(found, @base, buffer.ToArray(), context.RequestAborted), Json);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest();
        }
    }

    static async Task<IResult> PatchAsync(string connection, long from, CoauthorSessions sessions, HttpContext context)
    {
        if (!sessions.TryFind(connection, out var session, out var found))
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(await session.PatchAsync(found, from, context.RequestAborted), Json);
    }

    static async Task<IResult> EnterAsync(string connection, string paragraph, long version, CoauthorSessions sessions, HttpContext context)
    {
        if (!sessions.TryFind(connection, out var session, out var found))
        {
            return Results.NotFound();
        }

        return Results.Json(await session.EnterAsync(found, paragraph, version, context.RequestAborted), Json);
    }

    static async Task<IResult> ExitAsync(string connection, CoauthorSessions sessions, HttpContext context)
    {
        if (!sessions.TryFind(connection, out var session, out var found))
        {
            return Results.NotFound();
        }

        await session.ExitAsync(found, context.RequestAborted);
        return Results.NoContent();
    }

    static async Task<IResult> SaveAsync(string connection, CoauthorSessions sessions, HttpContext context)
    {
        if (!sessions.TryFind(connection, out var session, out _))
        {
            return Results.NotFound();
        }

        var problem = await session.SaveAsync(context.RequestAborted);
        return Results.Json(new {Saved = problem == null, Problem = problem}, Json);
    }

    // Also the page's beacon as it goes away, which carries no headers: the connection is in the path.
    static async Task<IResult> LeaveAsync(string connection, CoauthorSessions sessions)
    {
        if (sessions.TryFind(connection, out var session, out var found))
        {
            await sessions.LeaveAsync(session, found);
        }

        return Results.NoContent();
    }

    static IResult Events(string connection, CoauthorSessions sessions, HttpContext context)
    {
        if (!sessions.TryFind(connection, out var session, out var found))
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.ServerSentEvents(session.EventsAsync(found, context.RequestAborted));
    }
}
