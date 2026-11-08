using System.IO.Compression;

// The WOPI sample as it is published, run as a process: its host page opens Report.docx in Morph's editor,
// in a frame on another origin, where it is typed into and saved back to the host's folder. Every request
// to the host carries a proof, which the sample's host requires.
[NotInParallel]
public class WopiSampleTests
{
    static Process? sample;
    static string folder = "";

    // What the sample wrote, for a test that fails to say what the server did.
    static ConcurrentQueue<string> output = new();
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
                     $"--OfficeHost:BaseUrl={HostUrl}",
                     $"--OfficeHost:DiscoveryUrls:0={morphUrl}/hosting/discovery",
                     $"--MorphWopi:BaseUrl={morphUrl}",
                     $"--MorphWopi:TrustedHosts:0={HostUrl}"
                 ])
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        sample = Process.Start(start)!;
        // What the sample wrote, told when it does not come up: without it, a sample that failed to start
        // left the tests navigating to a closed port, and the reason was lost.
        output = new();
        sample.OutputDataReceived += (_, line) => output.Enqueue($"out {line.Data}");
        sample.ErrorDataReceived += (_, line) => output.Enqueue($"err {line.Data}");
        sample.BeginOutputReadLine();
        sample.BeginErrorReadLine();

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var ready = false;
        for (var attempt = 0; attempt < 120 && !ready && !sample.HasExited; attempt++)
        {
            try
            {
                ready = (await http.GetAsync(HostUrl)).IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            if (!ready)
            {
                await Task.Delay(500);
            }
        }

        if (!ready)
        {
            var state = sample.HasExited ? $"exited with {sample.ExitCode}" : "is running but did not answer";
            throw new($"The WOPI sample at {HostUrl} {state}.{Environment.NewLine}{string.Join(Environment.NewLine, output)}");
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

    // Waits until the host's copy of the file holds the text: a save lands when the host has written it,
    // and a file being written may not read as a package yet.
    static async Task UntilSavedAsync(string path, string text)
    {
        var body = "";
        for (var tries = 0; tries < 240; tries++)
        {
            try
            {
                body = Body(path);
                if (body.Contains(text))
                {
                    return;
                }
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"{path} never held \"{text}\"; its body read:{Environment.NewLine}{body}");
    }

    // What each page did — its frames' console, its dialogs and where it went — told when a wait for it fails.
    static readonly ConditionalWeakTable<IPage, ConcurrentQueue<string>> logs = [];

    static async Task<IPage> NewPageAsync()
    {
        var page = await browser!.NewPageAsync();
        var log = logs.GetOrCreateValue(page);
        page.Console += (_, message) => log.Enqueue($"console {message.Type} [{message.Location}] {message.Text}");
        page.PageError += (_, error) => log.Enqueue($"error {error}");
        page.FrameNavigated += (_, frame) => log.Enqueue($"navigated {(frame.ParentFrame == null ? "top" : frame.Name)} {frame.Url}");
        page.RequestFailed += (_, request) => log.Enqueue($"request failed {request.Url} {request.Failure}");
        // Handled, a dialog is no longer dismissed for the test; it is dismissed here as it was.
        page.Dialog += async (_, dialog) =>
        {
            log.Enqueue($"dialog {dialog.Type} {dialog.Message}");
            await dialog.DismissAsync();
        };
        return page;
    }

    // Close goes back to the host's page; when it does not, the page's log says what happened instead.
    // Reaching it is the top frame's navigation committing, heard from the page as it happens, with the
    // handler in place before the click. Not WaitForURLAsync: the click already waits for the navigation it
    // starts to commit, so the URL always matches by then, and a URL that already matches makes Playwright
    // .NET wait for the load event whatever WaitUntil says (it maps Commit to Load there). That wait checks
    // whether load has fired and only then subscribes to it, on another thread than the one load arrives on,
    // and the host's page is small enough that its load lands in between: the wait then ran out with the page
    // loaded (runs 366, 369, 388, 434, 447 and 449; 4 Closes in 40 on a desktop). Both are Playwright's:
    // microsoft/playwright-dotnet#3365 (Commit, fixed after 1.64.1) and #3364 (the lost load, open).
    static async Task CloseAsync(IPage page, IFrame frame)
    {
        var host = $"{HostUrl}/";
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Navigated(object? _, IFrame navigated)
        {
            if (navigated == page.MainFrame &&
                navigated.Url == host)
            {
                arrived.TrySetResult();
            }
        }

        page.FrameNavigated += Navigated;
        try
        {
            await frame.ClickAsync(".wopi-close");
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException exception)
        {
            var log = logs.TryGetValue(page, out var entries) ? string.Join(Environment.NewLine, entries) : "(none)";
            throw new TimeoutException($"Close did not reach {host}; the page is at {page.Url}.{Environment.NewLine}{log}", exception);
        }
        finally
        {
            page.FrameNavigated -= Navigated;
        }
    }

    // Opens Report.docx to edit from the host's page, as the user given (?user=) or the sample's own.
    static async Task<IFrame> OpenAsync(IPage page, string? user = null)
    {
        await page.GotoAsync(HostUrl);
        if (user == null)
        {
            await page.ClickAsync("a:text-is('Edit')");
        }
        else
        {
            var edit = await page.GetAttributeAsync("a:text-is('Edit')", "href");
            await page.GotoAsync($"{HostUrl}{edit}?user={user}");
        }

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
        var page = await NewPageAsync();
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
        // The file itself, then the status: run after TwoEditors_EditTogether, this page joins the session
        // that test saved, whose status read "Saved" before anything was typed here, so the status alone
        // does not say that this save has landed.
        var path = Path.Combine(folder, "Report.docx");
        await UntilSavedAsync(path, "over WOPI");
        await frame.WaitForSelectorAsync(
            ".wopi-status:text-is('Saved')",
            new()
            {
                Timeout = 60000
            });
        await Assert.That(await frame.InnerTextAsync(".editor-name")).DoesNotContain("edited");

        // Typed into again, the bar stops saying "Saved" at the first key, though what is typed reaches
        // the file only when the paragraph is handed in (MorphWopi.ShownStatus) — within 1.5s, before the
        // hand-in a coauthoring page makes after 2s idle could clear it for another reason.
        await page.Keyboard.PressAsync("Escape");
        await frame.Locator(".text-layer span", new() { HasTextString = "MINUTES" }).First.ClickAsync();
        await frame.WaitForSelectorAsync(
            ".edit-box[contenteditable=true]",
            new()
            {
                Timeout = 60000
            });
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" again");
        await frame.WaitForFunctionAsync(
            "() => document.querySelector('.wopi-status')?.textContent !== 'Saved'",
            null,
            new()
            {
                Timeout = 1500
            });
        await page.Keyboard.PressAsync("Control+s");
        await UntilSavedAsync(path, "again");
        await frame.WaitForSelectorAsync(
            ".wopi-status:text-is('Saved')",
            new()
            {
                Timeout = 60000
            });

        // Close goes back to the host, and the file is unlocked: it opens to edit again.
        await CloseAsync(page, frame);
        var again = await OpenAsync(page);
        await Assert.That(await again.InnerTextAsync(".wopi-status")).DoesNotContain("Read-only");
    }

    static async Task<ILocator> OpenParagraphAsync(IPage page, IFrame frame, string text)
    {
        var line = frame.Locator(".text-layer span", new() { HasTextString = text }).First;
        await line.ClickAsync();
        return line;
    }

    // Waits until the text of the pages' text layers, which are built again after each layout, says so: the
    // whole of it, since what is typed into a line may be drawn in more than one span.
    static async Task UntilAsync(IFrame frame, Func<string, bool> condition)
    {
        var text = "";
        for (var tries = 0; tries < 240; tries++)
        {
            text = await frame.EvaluateAsync<string>("() => [...document.querySelectorAll('.text-layer')].map(_ => _.textContent).join(' ')");
            if (condition(text))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"The pages never read as expected; they read:{Environment.NewLine}{text}");
    }

    // Waits for an element; when it never comes, what the page did meanwhile says why.
    static async Task WaitForAsync(IFrame frame, string selector)
    {
        try
        {
            await frame.WaitForSelectorAsync(
                selector,
                new()
                {
                    Timeout = 60000
                });
        }
        catch (TimeoutException exception)
        {
            var log = logs.TryGetValue(frame.Page, out var entries) ? string.Join(Environment.NewLine, entries) : "(none)";
            var status = await frame.InnerTextAsync(".wopi-bar");
            var note = await frame.EvaluateAsync<string>("() => document.querySelector('.editor-status-note')?.textContent ?? ''");
            throw new TimeoutException($"{selector} never came. Bar: {status} Note: {note}{Environment.NewLine}{log}", exception);
        }
    }

    // Two people edit the file at once, through the session Morph.Wopi holds: each sees who the other is
    // and where, cannot open the paragraph the other has open, and sees the other's text once it is handed
    // in; undo takes back only the reader's own edit, and the file saved holds both people's.
    [Test]
    public async Task TwoEditors_EditTogether()
    {
        var first = await NewPageAsync();
        var demo = await OpenAsync(first);
        var second = await NewPageAsync();
        var bob = await OpenAsync(second, "bob");

        await Assert.That(await bob.InnerTextAsync(".wopi-status")).DoesNotContain("Read-only");
        await WaitForAsync(demo, ".wopi-user:text-is('Bob User')");
        await WaitForAsync(bob, ".wopi-user:text-is('Demo User')");

        // The demo user opens MINUTES: Bob sees where, and cannot open it too.
        await OpenParagraphAsync(first, demo, "MINUTES");
        await WaitForAsync(demo, ".edit-box[contenteditable=true]");
        await first.Keyboard.PressAsync("End");
        await first.Keyboard.TypeAsync(" by Demo");
        await WaitForAsync(bob, ".viewer-presence[data-name='Demo User']");
        await OpenParagraphAsync(second, bob, "MINUTES");
        await WaitForAsync(bob, ".editor-status-note:text-is('Demo User is editing this paragraph.')");

        // Handed in as the demo user leaves it with Escape, the text reaches Bob. (A click on blank paper
        // would open the nearest paragraph, as Word's does, and keep it from Bob.)
        await first.Keyboard.PressAsync("Escape");
        await UntilAsync(bob, _ => _.Contains("by Demo", StringComparison.OrdinalIgnoreCase));

        // Bob types elsewhere; the demo user sees it once Bob hands it in.
        var other = await bob.Locator(".text-layer span").Nth(10).InnerTextAsync();
        await OpenParagraphAsync(second, bob, other.Trim());
        await WaitForAsync(bob, ".edit-box[contenteditable=true]");
        await second.Keyboard.PressAsync("End");
        await second.Keyboard.TypeAsync(" by Bob");
        await second.Keyboard.PressAsync("Escape");
        await UntilAsync(demo, _ => _.Contains("by Bob", StringComparison.OrdinalIgnoreCase));

        // Bob's undo takes back his own edit, and leaves the demo user's, on both pages.
        await second.Keyboard.PressAsync("Control+z");
        // As drawn, which may be in capitals.
        await UntilAsync(bob, _ => _.Contains("by Demo", StringComparison.OrdinalIgnoreCase) && !_.Contains("by Bob", StringComparison.OrdinalIgnoreCase));
        await UntilAsync(demo, _ => _.Contains("by Demo", StringComparison.OrdinalIgnoreCase) && !_.Contains("by Bob", StringComparison.OrdinalIgnoreCase));

        // Saved by the session, with what both left.
        await first.Keyboard.PressAsync("Control+s");
        await WaitForAsync(demo, ".wopi-status:text-is('Saved')");
        var saved = Body(Path.Combine(folder, "Report.docx"));
        await Assert.That(saved).Contains("by Demo");
        await Assert.That(saved).DoesNotContain("by Bob");

        // Closed as users close it, which leaves the session and unlocks the file for the other tests.
        await CloseAsync(second, bob);
        await CloseAsync(first, demo);
        await first.CloseAsync();
        await second.CloseAsync();
    }
}
