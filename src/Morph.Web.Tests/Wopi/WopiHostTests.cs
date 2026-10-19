using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using WopiServer;

// Morph.Wopi against a real WOPI host: WopiServer's endpoints and Morph.Wopi's in one test server, the host
// requiring every request to carry a proof that Morph's discovery keys validate. The host is http://host,
// Morph http://morph; the test server answers both.
public class WopiHostTests
{
    sealed class Server(WebApplication app, FileSystemStorage storage, string folder) : IAsyncDisposable
    {
        public WebApplication App => app;
        public FileSystemStorage Storage => storage;
        public string Folder => folder;

        public HttpClient Client()
        {
            var client = app.GetTestServer().CreateClient();
            client.BaseAddress = new("http://morph/");
            return client;
        }

        public string Id => storage.GetFileId("Report.docx");

        public Task<WopiServer.WopiLaunch> LaunchAsync(string action = "edit", string file = "Report.docx") =>
            app.Services.GetRequiredService<WopiLauncher>()
                .Launch(
                    new(storage.GetFileId(file), "ann")
                    {
                        UserName = "Ann",
                        CanWrite = action == "edit"
                    },
                    action);

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            Directory.Delete(folder, true);
        }
    }

    static async Task<Server> StartAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"morph-wopi-{Guid.NewGuid():N}");
        var files = Path.Combine(folder, "files");
        var web = Path.Combine(folder, "web");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(web);
        File.Copy("sample.docx", Path.Combine(files, "Report.docx"));
        File.Copy("sample.xlsx", Path.Combine(files, "Workbook.xlsx"));
        await File.WriteAllTextAsync(Path.Combine(web, "index.html"), "<!doctype html><html><head><title>Editor</title></head><body><div id=\"app\"></div></body></html>");

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                WebRootPath = web,
                EnvironmentName = "Production"
            });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        TestServer? server = null;
        var services = builder.Services;
        var storage = new FileSystemStorage(files);
        services.AddSingleton<IWopiStorage>(storage);
        services.AddWopi(
            options =>
            {
                options.SigningKey = "the signing key of WopiHostTests, at least 32 characters";
                options.BaseUrl = "http://host";
                options.DiscoveryUrls.Add("http://morph/hosting/discovery");
                options.RequireProof = true;
            });
        services.AddMorphWopi(
            options =>
            {
                options.BaseUrl = "http://morph";
                options.TrustedHosts.Add("http://host");
            });
        // Both sides' requests go to the test server: the host's for the discovery, Morph's to the host.
        services.AddHttpClient(WopiOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => server!.CreateHandler());
        services.AddHttpClient(WopiProxy.ClientName).ConfigurePrimaryHttpMessageHandler(() => server!.CreateHandler());

        var app = builder.Build();
        app.MapWopi();
        app.MapMorphWopi();
        await app.StartAsync();
        server = app.GetTestServer();
        return new(app, storage, folder);
    }

    // What the action URL answers with: the editor's page, the launch written into it for MorphWopi.
    static async Task<(HttpResponseMessage Response, WopiLaunchInfo? Launch)> PostAsync(HttpClient client, WopiServer.WopiLaunch launch)
    {
        var response = await client.PostAsync(
            launch.Url,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["access_token"] = launch.AccessToken,
                    ["access_token_ttl"] = launch.AccessTokenTtl.ToString(CultureInfo.InvariantCulture)
                }));
        var html = await response.Content.ReadAsStringAsync();
        const string start = "<script type=\"application/json\" id=\"morph-wopi\">";
        var from = html.IndexOf(start, StringComparison.Ordinal);
        if (from < 0)
        {
            return (response, null);
        }

        from += start.Length;
        var json = html[from..html.IndexOf("</script>", from, StringComparison.Ordinal)];
        return (response, WopiLaunchInfo.Parse(json));
    }

    // The host reads Morph's discovery for the action URL, posts the token to it, and the page carries the
    // file to open: its WOPISrc, the token and the proxy. Only the host may frame it.
    [Test]
    public async Task TheActionUrl_AnswersWithThePage()
    {
        await using var server = await StartAsync();
        var launch = await server.LaunchAsync();
        await Assert.That(launch.Url).StartsWith("http://morph/morph/wopi/edit?WOPISrc=http%3A%2F%2Fhost%2Fwopi%2Ffiles%2F");

        var (response, opened) = await PostAsync(server.Client(), launch);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues("Content-Security-Policy").Single()).IsEqualTo("frame-ancestors 'self' http://host");
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(opened).IsNotNull();
        await Assert.That(opened!.Source).IsEqualTo($"http://host/wopi/files/{server.Id}");
        await Assert.That(opened.Token).IsEqualTo(launch.AccessToken);
        await Assert.That(opened.Action).IsEqualTo("edit");
        await Assert.That(opened.Proxy).IsEqualTo("/morph/wopi/proxy");
    }

    // Through the proxy, signed: the file's info, its content, the lock, a save over it, and the unlock.
    [Test]
    public async Task TheEditor_OpensLocksAndSavesTheFile()
    {
        await using var server = await StartAsync();
        var (_, opened) = await PostAsync(server.Client(), await server.LaunchAsync());
        var session = new WopiSession(server.Client(), opened!);

        var info = await session.CheckFileInfoAsync();
        await Assert.That(info.BaseFileName).IsEqualTo("Report.docx");
        await Assert.That(info.UserFriendlyName).IsEqualTo("Ann");
        await Assert.That(info.Editable).IsTrue();

        var path = Path.Combine(server.Storage.Root, "Report.docx");
        await Assert.That(await session.GetFileAsync()).IsEquivalentTo(await File.ReadAllBytesAsync(path));

        await Assert.That((await session.LockAsync("mine")).Succeeded).IsTrue();
        await Assert.That((await session.RefreshLockAsync("mine")).Succeeded).IsTrue();
        var saved = Encoding.UTF8.GetBytes("saved by Morph");
        await Assert.That((await session.PutFileAsync(saved, "mine")).Succeeded).IsTrue();
        await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(saved);

        await Assert.That((await session.UnlockAsync("mine")).Succeeded).IsTrue();
        // Unlocked, a save is refused, and says the file has no lock: the editor then locks it again.
        var refused = await session.PutFileAsync(saved, "mine");
        await Assert.That(refused.Unlocked).IsTrue();
    }

    // A second editor finds the file locked, and why.
    [Test]
    public async Task ALockedFile_TellsTheSecondEditor()
    {
        await using var server = await StartAsync();
        var (_, first) = await PostAsync(server.Client(), await server.LaunchAsync());
        var (_, second) = await PostAsync(server.Client(), await server.LaunchAsync());
        await new WopiSession(server.Client(), first!).LockAsync("first");

        var reply = await new WopiSession(server.Client(), second!).LockAsync("second");

        await Assert.That(reply.Status).IsEqualTo(409);
        await Assert.That(reply.Lock).IsEqualTo("first");
        await Assert.That(reply.Unlocked).IsFalse();
    }

    // The unlock as the page goes away is a beacon: a form, with no headers.
    [Test]
    public async Task TheBeacon_Unlocks()
    {
        await using var server = await StartAsync();
        var (_, opened) = await PostAsync(server.Client(), await server.LaunchAsync());
        var session = new WopiSession(server.Client(), opened!);
        await session.LockAsync("mine");

        var form = new MultipartFormDataContent();
        foreach (var (name, value) in session.Fields("mine"))
        {
            form.Add(new StringContent(value), name);
        }

        var response = await server.Client().PostAsync(session.Url("Unlock"), form);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await session.LockAsync("again")).Succeeded).IsTrue();
    }

    // The host requires proof, so the proxy's requests passing is the proof keys working; the same
    // request without one is refused.
    [Test]
    public async Task TheHost_RefusesARequestWithoutProof()
    {
        await using var server = await StartAsync();
        var launch = await server.LaunchAsync();

        var response = await server.Client().GetAsync($"http://host/wopi/files/{server.Id}?access_token={Uri.EscapeDataString(launch.AccessToken)}");

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    // Neither the page nor the proxy follows a WOPISrc off the trusted hosts.
    [Test]
    public async Task AnUntrustedHost_IsRefused()
    {
        await using var server = await StartAsync();
        var client = server.Client();

        var page = await client.PostAsync(
            "/morph/wopi/edit?WOPISrc=" + Uri.EscapeDataString("http://elsewhere/wopi/files/1"),
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["access_token"] = "token"
                }));
        var proxy = await new WopiSession(client, new("http://elsewhere/wopi/files/1", "token", 0, "edit", "/morph/wopi/proxy"))
            .LockAsync("mine");

        await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(proxy.Status).IsEqualTo(403);
    }

    // Morph edits a Word document, and only views a workbook.
    [Test]
    public async Task AWorkbook_OpensOnlyToView()
    {
        await using var server = await StartAsync();

        var view = await server.LaunchAsync("view", "Workbook.xlsx");

        await Assert.That(view.Url).StartsWith("http://morph/morph/wopi/view?WOPISrc=");
        await Assert.That(async () => await server.LaunchAsync("edit", "Workbook.xlsx")).Throws<InvalidOperationException>();
    }

    // The discovery's keys are the ones the requests are signed with, as a CSP blob too, which is what
    // a host without the modulus and exponent reads.
    [Test]
    public async Task TheDiscovery_PublishesTheProofKey()
    {
        using var key = RSA.Create(2048);
        using var proofs = new WopiProofs(Options.Create(new MorphWopiOptions { ProofKey = key }));

        var element = WopiDiscoveryXml.Build("https://morph.example", proofs).Root!.Element("proof-key")!;
        using var blob = new RSACryptoServiceProvider();
        blob.ImportCspBlob(Convert.FromBase64String((string) element.Attribute("value")!));
        var data = WopiProofs.Data("token", "https://host/wopi/files/1?access_token=token", 42);
        var (proof, _) = proofs.Sign("token", "https://host/wopi/files/1?access_token=token", 42);

        await Assert.That(blob.ExportParameters(false).Modulus).IsEquivalentTo(key.ExportParameters(false).Modulus!);
        await Assert.That(blob.VerifyData(data, Convert.FromBase64String(proof), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)).IsTrue();
        await Assert.That(WopiDiscoveryXml.Build("https://morph.example", proofs).Root!.Element("net-zone")!.Attribute("name")!.Value).IsEqualTo("external-https");
    }
}
