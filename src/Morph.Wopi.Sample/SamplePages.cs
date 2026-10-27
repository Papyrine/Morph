/// <summary>
/// The host's pages: the files of the folder, and the page that opens one in Morph.
/// </summary>
static class SamplePages
{
    static string Page(string title, string body) =>
        $$"""
          <!doctype html>
          <html lang="en">
          <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
              body { font-family: system-ui, sans-serif; margin: 0; }
              main { max-width: 48rem; margin: 2rem auto; padding: 0 1rem; }
              td { padding: 0.25rem 1rem 0.25rem 0; }
              code { overflow-wrap: anywhere; }
            </style>
          </head>
          <body>{{body}}</body>
          </html>
          """;

    /// <summary>
    /// The files of the storage, each with View, and Edit for a Word document: what Morph's discovery offers.
    /// </summary>
    public static string Files(FileSystemStorage storage)
    {
        var rows = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(storage.Root).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(path);
            var id = Uri.EscapeDataString(storage.GetFileId(name));
            var edit = "";
            if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                edit = $"""<a href="/edit/{id}">Edit</a>""";
            }

            rows.Append(
                $"""
                 <tr>
                   <td>{WebUtility.HtmlEncode(name)}</td>
                   <td>{edit}</td>
                   <td><a href="/view/{id}">View</a></td>
                 </tr>
                 """);
        }

        return Page(
            "Files",
            $"""
             <main>
               <h1>Files</h1>
               <p>The files of <code>{WebUtility.HtmlEncode(storage.Root)}</code>, opened in Morph. Add files to the folder to see them here.</p>
               <table>{rows}</table>
             </main>
             """);
    }

    // begin-snippet: WopiLaunch
    /// <summary>
    /// Opens a file in Morph: the user's access to it, the URL of Morph's action from its discovery, and a
    /// page that posts the access token to it in a frame.
    /// </summary>
    public static async Task<IResult> Open(string fileId, string action, HttpRequest request, OfficeLauncher launcher)
    {
        // an application decides the access from its own users and permissions
        var canWrite = action == "edit";
        var access = new OfficeAccess(fileId, "demo")
        {
            UserName = "Demo User",
            CanWrite = canWrite
        };
        try
        {
            var launch = await launcher.Launch(access, action, request);
            return Results.Content(Editor(launch), "text/html");
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            // Morph is not reachable, or does not offer the action for this type of file
            return Results.Content(
                Message(exception.Message),
                "text/html",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// The host page: a form posts the token to Morph's action URL, into a frame over the whole page.
    /// </summary>
    static string Editor(OfficeLaunch launch) =>
        Page(
            "Morph",
            $"""
             <form id="wopi" target="editor" method="post" action="{WebUtility.HtmlEncode(launch.Url)}">
               <input type="hidden" name="access_token" value="{WebUtility.HtmlEncode(launch.AccessToken)}">
               <input type="hidden" name="access_token_ttl" value="{launch.AccessTokenTtl}">
             </form>
             <iframe name="editor" title="Morph" allowfullscreen
                     style="position: fixed; inset: 0; width: 100%; height: 100%; border: none"></iframe>
             <script>document.getElementById('wopi').submit();</script>
             """);
    // end-snippet

    static string Message(string message) =>
        Page(
            "Cannot open the file",
            $"""
             <main>
               <h1>Cannot open the file</h1>
               <p>{WebUtility.HtmlEncode(message)}</p>
               <p><a href="/">Back to the files</a></p>
             </main>
             """);
}
