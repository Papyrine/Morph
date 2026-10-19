using System.Diagnostics;
using System.IO.Compression;

// The WOPI sample as it is published, run as a process: its host page opens Report.docx in Morph's editor,
// in a frame on another origin, where it is typed into and saved back to the host's folder. Every request
// to the host carries a proof, which the sample's host requires.
[NotInParallel]
public class WopiSampleTests
{
    static Process? sample;
    static string folder = "";
    static int hostPort;
    static IPlaywright? playwright;
    static IBrowser? browser;

    static string HostUrl => $"http://localhost:{hostPort}";

    [Before(Class)]
    public static async Task StartSample()
    {
        var published = Path.Combine(Path.GetDirectoryName(typeof(WopiSampleTests).Assembly.Location)!, "..", "wopi-sample-publish");
        folder = Path.Combine(Path.GetTempPath(), $"morph-wopi-sample-{Guid.NewGuid():N}");
        hostPort = FreePort();
        var morphPort = FreePort();
        var morphUrl = $"http://localhost:{morphPort}";
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = published,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in (string[])
                 [
                     Path.Combine(published, "Morph.Wopi.Sample.dll"),
                     $"--Urls={HostUrl};{morphUrl}",
                     $"--Sample:Files={folder}",
                     $"--Sample:HostUrl={HostUrl}",
                     $"--Wopi:BaseUrl={HostUrl}",
                     $"--Wopi:DiscoveryUrls:0={morphUrl}/hosting/discovery",
                     $"--MorphWopi:BaseUrl={morphUrl}",
                     $"--MorphWopi:TrustedHosts:0={HostUrl}"
                 ])
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        sample = Process.Start(start)!;
        sample.OutputDataReceived += (_, _) => { };
        sample.ErrorDataReceived += (_, _) => { };
        sample.BeginOutputReadLine();
        sample.BeginErrorReadLine();

        using var http = new HttpClient();
        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                if ((await http.GetAsync(HostUrl)).IsSuccessStatusCode)
                {
                    break;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(500);
        }

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync();
    }

    [After(Class)]
    public static async Task StopSample()
    {
        if (browser != null)
        {
            await browser.CloseAsync();
        }

        playwright?.Dispose();
        if (sample != null)
        {
            sample.Kill(true);
            await sample.WaitForExitAsync();
            sample.Dispose();
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint) listener.LocalEndpoint).Port;
    }

    static string Body(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }

    static async Task<IFrame> OpenAsync(IPage page)
    {
        await page.GotoAsync(HostUrl);
        await page.ClickAsync("a:text-is('Edit')");
        var element = await page.WaitForSelectorAsync("iframe[name=editor]");
        var frame = (await element!.ContentFrameAsync())!;
        await frame.WaitForSelectorAsync(
            ".viewer-page[data-page-number='1'][data-rendered-dpi] .text-layer[data-text-state=ready]",
            new()
            {
                Timeout = 90000
            });
        return frame;
    }

    [Test]
    public async Task AFile_IsEditedAndSavedToTheHost()
    {
        var page = await browser!.NewPageAsync();
        var frame = await OpenAsync(page);
        await Assert.That(await frame.InnerTextAsync(".wopi-crumbs")).Contains("Report.docx");

        await frame.Locator(".text-layer span", new() { HasTextString = "MINUTES" }).First.ClickAsync();
        await frame.WaitForSelectorAsync(
            ".edit-box[contenteditable=true]",
            new()
            {
                Timeout = 60000
            });
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" over WOPI");
        await page.Keyboard.PressAsync("Control+s");
        await frame.WaitForSelectorAsync(
            ".wopi-status:text-is('Saved')",
            new()
            {
                Timeout = 60000
            });

        await Assert.That(Body(Path.Combine(folder, "Report.docx"))).Contains("over WOPI");
        await Assert.That(await frame.InnerTextAsync(".editor-name")).DoesNotContain("edited");

        // Close goes back to the host, and the file is unlocked: it opens to edit again.
        await frame.ClickAsync(".wopi-close");
        await page.WaitForURLAsync($"{HostUrl}/");
        var again = await OpenAsync(page);
        await Assert.That(await again.InnerTextAsync(".wopi-status")).DoesNotContain("Read-only");
    }

    // A second editor finds the file locked, and opens it read only.
    [Test]
    public async Task ALockedFile_OpensReadOnly()
    {
        var first = await browser!.NewPageAsync();
        var editing = await OpenAsync(first);
        var second = await browser.NewPageAsync();

        var frame = await OpenAsync(second);

        await Assert.That(await frame.InnerTextAsync(".wopi-status")).StartsWith("Read-only");
        // Closed as a user closes it, which unlocks the file for the other tests.
        await editing.ClickAsync(".wopi-close");
        await first.WaitForURLAsync($"{HostUrl}/");
        await first.CloseAsync();
        await second.CloseAsync();
    }
}
