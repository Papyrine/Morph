# Morph.Wopi.Sample

A WOPI host built on [OfficeHost](https://www.nuget.org/packages/OfficeHost/) that opens its files in Morph's
editor, which the same application serves through [Morph.Wopi](/src/Morph.Wopi/README.md). It plays both roles
of WOPI, on two ports, so that one run starts both:

* the host, at http://localhost:5000: the files of a folder, behind OfficeHost's endpoints, with a page that
  lists them and a page that opens one in a frame;
* the client, at http://localhost:5001: Morph's discovery, the editor's page, and the proxy the editor calls
  the host through. The editor itself is [Morph.Wopi.Sample.Client](/src/Morph.Wopi.Sample.Client), a
  WebAssembly app whose root component is `MorphWopi`.

Deployed, they would be two applications, usually on two sites. The host requires every request to carry a
proof (`RequireProof`), which Morph's discovery keys validate.


## Run it

```pwsh
dotnet run --project src/Morph.Wopi.Sample
```

Then open http://localhost:5000. The folder `files` starts with Morph's sample document, workbook and deck;
add files to it to open them too. Edit opens a Word document in the editor, locked while it is open, and Save
(Ctrl+S) puts it back in the folder; View opens any of them read only. Open the same document to edit in a
second tab to see it open read only, locked by the first.


## The host

<!-- snippet: WopiHost -->
<a id='snippet-WopiHost'></a>
```cs
// The host: the files of a folder, behind OfficeHost's endpoints
var storage = new FileSystemStorage(folder);
services.AddSingleton(storage);
services.AddSingleton<IOfficeStorage>(storage);
services.AddOfficeHost(
    options =>
    {
        // SigningKey, BaseUrl, DiscoveryUrls (Morph's discovery) and RequireProof come from appsettings.json
        builder.Configuration.GetSection("OfficeHost").Bind(options);
        options.HostEditUrl = (file, _) => $"{hostUrl}/edit/{Uri.EscapeDataString(file.Id)}";
        options.HostViewUrl = (file, _) => $"{hostUrl}/view/{Uri.EscapeDataString(file.Id)}";
        options.OnCheckFileInfo = context =>
        {
            // the breadcrumb over the editor, and where Close goes
            context.Properties["BreadcrumbBrandName"] = "Morph WOPI sample";
            context.Properties["BreadcrumbBrandUrl"] = hostUrl;
            context.Properties["BreadcrumbFolderName"] = "Files";
            context.Properties["BreadcrumbFolderUrl"] = hostUrl;
            context.Properties["CloseUrl"] = hostUrl;
            return Task.CompletedTask;
        };
    });
```
<sup><a href='/src/Morph.Wopi.Sample/Program.cs#L14-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-WopiHost' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The page that opens a file asks OfficeHost's `OfficeLauncher` for Morph's action URL and a token, and posts the
token to it in a frame:

<!-- snippet: WopiLaunch -->
<a id='snippet-WopiLaunch'></a>
```cs
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
```
<sup><a href='/src/Morph.Wopi.Sample/SamplePages.cs#L62-L110' title='Snippet source file'>snippet source</a> | <a href='#snippet-WopiLaunch' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## The client

<!-- snippet: WopiClient -->
<a id='snippet-WopiClient'></a>
```cs
// The client: Morph's discovery, the page the host opens a file in, and the way to the host
services.AddMorphWopi(options => builder.Configuration.GetSection("MorphWopi").Bind(options));
```
<sup><a href='/src/Morph.Wopi.Sample/Program.cs#L39-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-WopiClient' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The URLs, the signing key and the trusted hosts are in `appsettings.json`: `OfficeHost:DiscoveryUrls` is Morph's
discovery, `OfficeHost:BaseUrl` the URL Morph reaches the host at, and `MorphWopi:TrustedHosts` the hosts Morph
opens files for.
