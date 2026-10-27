// One application in the two roles of WOPI, on two ports:
// - the WOPI host, at http://localhost:5000: a folder of files, which OfficeHost serves to WOPI clients;
// - the WOPI client, at http://localhost:5001: Morph's editor, which Morph.Wopi serves, and which the
//   host opens its files in.
// Deployed, they are two applications, usually on two sites; here one run starts both.
var builder = WebApplication.CreateBuilder(args);
var sample = builder.Configuration.GetSection("Sample");
var hostUrl = sample["HostUrl"]!.TrimEnd('/');
var folder = Path.Combine(builder.Environment.ContentRootPath, sample["Files"]!);
SampleFiles.Seed(folder);

var services = builder.Services;

// begin-snippet: WopiHost
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
// end-snippet

// begin-snippet: WopiClient
// The client: Morph's discovery, the page the host opens a file in, and the way to the host
services.AddMorphWopi(options => builder.Configuration.GetSection("MorphWopi").Bind(options));
// end-snippet

var app = builder.Build();
// the editor's WebAssembly app, Morph.Wopi.Sample.Client
app.MapStaticAssets();
// the host's WOPI endpoints, /wopi/files/{fileId}, which Morph calls
app.MapOfficeHost();
// Morph's: /hosting/discovery, /morph/wopi/view and /morph/wopi/edit, /morph/wopi/proxy/{operation}
app.MapMorphWopi();
// the host's pages: its files, and the page that opens one in Morph
app.MapGet("/", () => Results.Content(SamplePages.Files(storage), "text/html"));
app.MapGet(
    "/edit/{fileId}",
    (string fileId, HttpRequest request, OfficeLauncher launcher) =>
        SamplePages.Open(fileId, "edit", request, launcher));
app.MapGet(
    "/view/{fileId}",
    (string fileId, HttpRequest request, OfficeLauncher launcher) =>
        SamplePages.Open(fileId, "view", request, launcher));
app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation("Open {HostUrl} in a browser", hostUrl));
app.Run();
