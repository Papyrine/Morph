using static EditFixtures;

// How quickly MorphEditor answers the reader, timed on the real WebAssembly runtime in a real browser.
// The runtime is single-threaded: everything .NET does — writing an edit into the file, laying the
// document out again, painting its pages — runs on the browser's one main thread, between the keys the
// reader presses. So what is timed is not only how long each step takes but how long the page cannot
// answer a key while it runs: a long task is one of more than 50 ms, and nothing is typed during one.
//
// Explicit, so it never runs in the suite: the figures are the machine's as much as the code's, and
// only runs on one machine, back to back, compare. Build Release (what is deployed) and run
//
//   dotnet src/Morph.Web.Tests/bin/Release/net10.0/Morph.Web.Tests.dll --treenode-filter "/*/*/SnapshotTests/Benchmark_Editor"
//
// then read the report it names (benchmarks/editor-*.md beside the test assembly, with the raw figures
// in a .json beside it). MORPH_BENCH_THROTTLE=4 slows the browser's CPU fourfold, as a slower machine
// would be; MORPH_BENCH_PROFILE=1 also records a CPU profile of each document's first round, to open in
// DevTools' Performance panel; MORPH_BENCH_PAGES=svg opens the editor with its pages drawn as SVG
// (MorphViewer.VectorPages).
//
// Each document opens in a page of its own, and then, three rounds over:
//   - a paragraph is clicked: the time until its editor is up;
//   - a sentence is typed into it, each key timed from the browser receiving it to the end of the frame
//     that shows it, and the editor's own handling of it (beforeinput) timed apart;
//   - a second paragraph is clicked, which hands the first in — the file is written and laid out again
//     — before the second opens: the time until it is up;
//   - twenty characters are typed into the second at once, while the pages the first edit changed are
//     still being painted;
//   - the second is handed in with Ctrl+Enter: the time until its page is drawn again, and until the
//     main thread has been free of long tasks for a second.
public partial class SnapshotTests
{
    const string benchmarkText = "The quick brown fox jumps over the lazy dog. ";

    // Installed before the app loads. Long tasks and long animation frames come from the browser's own
    // observers; a key's latency is measured here, because the browser reports only those over 16 ms.
    const string instrument =
        """
        (() => {
            const benchmark = window.morphBenchmark = { keys: [], tasks: [], frames: [], lastTaskEnd: 0 };
            let pending = null;
            // The key's handlers run, then the next frame's callbacks, then its style, layout and paint,
            // and only then a message posted from those callbacks.
            addEventListener('keydown', event => {
                if (event.key.length !== 1) {
                    pending = null;
                    return;
                }

                const key = { start: event.timeStamp, delay: performance.now() - event.timeStamp, script: 0, latency: 0 };
                pending = key;
                requestAnimationFrame(() => {
                    const channel = new MessageChannel();
                    channel.port1.onmessage = () => {
                        key.latency = performance.now() - key.start;
                        benchmark.keys.push(key);
                    };
                    channel.port2.postMessage(null);
                });
            }, true);
            // The editor takes every key at beforeinput, on its box: between the window's capture and its
            // bubble is the editor's handling, model to DOM to ribbon.
            addEventListener('beforeinput', () => {
                if (pending) {
                    pending.from = performance.now();
                }
            }, true);
            addEventListener('beforeinput', () => {
                if (pending?.from) {
                    pending.script = performance.now() - pending.from;
                    delete pending.from;
                }
            });
            const observe = (type, take) => {
                try {
                    new PerformanceObserver(list => list.getEntries().forEach(take)).observe({ type, buffered: true });
                } catch {
                    // A kind of entry this browser does not report.
                }
            };
            observe('longtask', entry => {
                benchmark.tasks.push({ start: entry.startTime, duration: entry.duration });
                benchmark.lastTaskEnd = Math.max(benchmark.lastTaskEnd, entry.startTime + entry.duration);
            });
            observe('long-animation-frame', entry => benchmark.frames.push({
                start: entry.startTime,
                duration: entry.duration,
                layout: entry.styleAndLayoutStart > 0 ? entry.startTime + entry.duration - entry.styleAndLayoutStart : 0,
                scripts: entry.scripts.map(_ => ({
                    invoker: _.invoker,
                    source: _.sourceURL,
                    name: _.sourceFunctionName,
                    duration: _.duration
                }))
            }));
        })();
        """;

    // A paragraph of two hundred runs, a word each, bold and italic in turn: the editor draws a span
    // for each run every time a key is pressed.
    static byte[] Runs { get; } = Build(
        P("", R("Opening paragraph, plain.")) +
        P("", Enumerable.Range(0, 200).Select(_ => R($"word{_} ", _ % 2 == 0 ? "<w:b/>" : "<w:i/>")).ToArray()) +
        P("", R("Closing paragraph, plain.")));

    // The second paragraph of each is above the first: the editor open on the first grows down the page
    // as it is typed in, over what follows it, and a click there would land in the editor.
    static IEnumerable<BenchmarkDocument> BenchmarkDocuments()
    {
        yield return new("Three short paragraphs", Paragraphs, "Third and last", "First paragraph");
        yield return new("A paragraph of 200 runs", Runs, "word0 word1", "Opening paragraph");
        yield return new("The app's sample", Sample.DocxBytes, "The Board, new", "The minutes were");
        yield return new("60 plain paragraphs", Plain(60), "Paragraph 4 of", "Paragraph 2 of");
        yield return new("300 plain paragraphs", Plain(300), "Paragraph 4 of", "Paragraph 2 of");
        yield return new("Business plan", File.ReadAllBytes(ProjectFiles.corpus.business_plan_docx), "If capital is", "Creating an extensive");
    }

    // Paragraphs of plain text, each several lines long and numbered so that one can be found.
    static byte[] Plain(int count)
    {
        var filler = string.Concat(Enumerable.Repeat(benchmarkText, 11));
        return Build(string.Concat(Enumerable.Range(1, count).Select(_ => P("", R($"Paragraph {_} of the benchmark. {filler}")))));
    }

    [Test]
    [Explicit]
    public async Task Benchmark_Editor()
    {
        var throttle = double.TryParse(Environment.GetEnvironmentVariable("MORPH_BENCH_THROTTLE"), CultureInfo.InvariantCulture, out var rate) ? rate : 1;
        var profile = Environment.GetEnvironmentVariable("MORPH_BENCH_PROFILE") == "1";
        var directory = Path.Combine(AppContext.BaseDirectory, "benchmarks");
        Directory.CreateDirectory(directory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // A document that fails is reported as failed and the rest still run; the report is written
        // after each, so that a run stopped part way still says what it found.
        var path = Path.Combine(directory, $"editor-{stamp}.md");
        var results = new List<DocumentTimings>();
        foreach (var document in BenchmarkDocuments())
        {
            var profilePath = profile ? Path.Combine(directory, $"editor-{stamp}-{results.Count + 1}.cpuprofile") : null;
            try
            {
                results.Add(await BenchmarkAsync(document, throttle, profilePath));
            }
            catch (Exception exception)
            {
                results.Add(new(document.Name, 0, double.NaN, double.NaN, [], 0, 0, [], exception.Message.Split('\n')[0]));
            }

            Console.WriteLine($"{document.Name}: {results[^1].Error ?? "done"}");
            await File.WriteAllTextAsync(path, Report(results, throttle));
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(results, reportJson));
        }

        Console.WriteLine(Report(results, throttle));
        Console.WriteLine($"Written to {path}");
        await Assert.That(results.Where(_ => _.Error != null).Select(_ => $"{_.Name}: {_.Error}")).IsEmpty();
    }

    static async Task<DocumentTimings> BenchmarkAsync(BenchmarkDocument document, double throttle, string? profile)
    {
        var page = await browser!.NewPageAsync();
        try
        {
            await page.AddInitScriptAsync(instrument);
            var devTools = await page.Context.NewCDPSessionAsync(page);
            if (throttle > 1)
            {
                await devTools.SendAsync(
                    "Emulation.setCPUThrottlingRate",
                    new()
                    {
                        ["rate"] = throttle
                    });
            }

            var pagesMode = Environment.GetEnvironmentVariable("MORPH_BENCH_PAGES");
            await page.GotoAsync(string.IsNullOrEmpty(pagesMode) ? $"http://localhost:{port}/edit" : $"http://localhost:{port}/edit?pages={pagesMode}");
            await page.WaitForSelectorAsync(
                ".viewer-open input[type=file]",
                new()
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = 120000
                });
            await QuietAsync(page);

            var opening = await NowAsync(page);
            var watch = Stopwatch.StartNew();
            await page.SetInputFilesAsync(
                ".viewer-open input[type=file]",
                new FilePayload
                {
                    Name = "benchmark.docx",
                    MimeType = ConversionService.Find(InputFormat.Docx).ContentType,
                    Buffer = document.Bytes
                });
            // The first page drawn, or the viewer saying why it never will be: the document could not be
            // opened, or a page could not be painted.
            await page.WaitForSelectorAsync(
                ".viewer-page[data-current][data-rendered-dpi] .text-layer[data-text-state=ready], .error, .viewer-page-message",
                new()
                {
                    Timeout = 600000
                });
            var opened = watch.Elapsed.TotalMilliseconds;
            if (await page.QuerySelectorAsync(".error, .viewer-page-message") is { } error)
            {
                throw new InvalidOperationException($"The editor could not show the document: {await error.InnerTextAsync()}");
            }
            await page.WaitForSelectorAsync(".viewer[data-edit=on]");
            var openIdle = Math.Max(0, await QuietAsync(page) - opening);
            var pages = await page.Locator(".viewer-page").CountAsync();

            var editing = await NowAsync(page);
            var rounds = new List<RoundTimings>();
            for (var round = 0; round < 3; round++)
            {
                var profiling = profile != null &&
                                round == 0;
                if (profiling)
                {
                    await devTools.SendAsync("Profiler.enable");
                    await devTools.SendAsync(
                        "Profiler.setSamplingInterval",
                        new()
                        {
                            ["interval"] = 200
                        });
                    await devTools.SendAsync("Profiler.start");
                }

                rounds.Add(await RoundAsync(page, document));
                if (profiling)
                {
                    var stopped = await devTools.SendAsync("Profiler.stop");
                    await File.WriteAllTextAsync(profile!, stopped!.Value.GetProperty("profile").GetRawText());
                }
            }

            var frames = (await ReadAsync<List<LongFrame>>(page, "morphBenchmark.frames"))
                .Where(_ => _.Start >= editing)
                .ToList();
            return new(
                document.Name,
                pages,
                opened,
                openIdle,
                rounds,
                frames.Sum(_ => _.Duration),
                frames.Sum(_ => _.Layout),
                Attribute(frames));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    static async Task<RoundTimings> RoundAsync(IPage page, BenchmarkDocument document)
    {
        await QuietAsync(page);

        // Into the first paragraph.
        var point = await PointAtAsync(page, document.First);
        var watch = Stopwatch.StartNew();
        await page.Mouse.ClickAsync(point[0], point[1]);
        var first = await page.WaitForSelectorAsync(
            editor,
            new()
            {
                Timeout = 120000
            });
        var openParagraph = watch.Elapsed.TotalMilliseconds;
        var session = await first!.GetAttributeAsync("data-edit-session");
        await page.Keyboard.PressAsync("Control+End");
        await QuietAsync(page);

        // Typing, with nothing else going on.
        var typingFrom = await NowAsync(page);
        var typing = await TypeTimedAsync(page, benchmarkText);
        var typingTo = await NowAsync(page);

        // On to the second paragraph, which hands the first in before it opens.
        point = await PointAtAsync(page, document.Second);
        watch.Restart();
        await page.Mouse.ClickAsync(point[0], point[1]);
        await page.WaitForSelectorAsync(
            $".edit-box[contenteditable=true]:not([data-edit-session='{session}'])",
            new()
            {
                Timeout = 120000
            });
        var nextParagraph = watch.Elapsed.TotalMilliseconds;

        // Typing at once, while the pages the first edit changed are painted again — at the paragraph's
        // end, so that the text it is found by stays whole.
        await page.Keyboard.PressAsync("Control+End");
        var hurried = await TypeTimedAsync(page, benchmarkText[..20]);
        await QuietAsync(page);

        // Handing the second in.
        var handingFrom = await NowAsync(page);
        watch.Restart();
        await HandInAsync(page);
        var handIn = watch.Elapsed.TotalMilliseconds;
        var idleAt = await QuietAsync(page);

        var tasks = await ReadAsync<List<LongTask>>(page, "morphBenchmark.tasks");
        var handInTasks = tasks
            .Where(_ => Overlaps(_, handingFrom, idleAt))
            .Select(_ => _ with { Start = _.Start - handingFrom })
            .ToList();
        return new(
            openParagraph,
            typing,
            Blocked(tasks, typingFrom, typingTo),
            nextParagraph,
            hurried,
            handIn,
            Math.Max(0, idleAt - handingFrom),
            Blocked(tasks, handingFrom, idleAt),
            handInTasks);
    }

    // Types text a key at a time, and reads back each key's timing once the frame showing it is done.
    static async Task<List<KeyTiming>> TypeTimedAsync(IPage page, string text)
    {
        var from = await page.EvaluateAsync<int>("() => morphBenchmark.keys.length");
        await page.Keyboard.TypeAsync(
            text,
            new()
            {
                Delay = 30
            });
        await page.WaitForFunctionAsync(
            "count => morphBenchmark.keys.length >= count",
            from + text.Length,
            new()
            {
                Timeout = 120000
            });
        return await ReadAsync<List<KeyTiming>>(page, $"morphBenchmark.keys.slice({from})");
    }

    // The middle of the first character of some text, on whichever page has it first, scrolled into
    // view — once that page's text layer has been built.
    static async Task<float[]> PointAtAsync(IPage page, string text)
    {
        try
        {
            return await FindPointAsync(page, text);
        }
        catch (TimeoutException exception)
        {
            var state = await page.EvaluateAsync<string>(
                """
                () => [...document.querySelectorAll('.viewer-page')].map(_ =>
                    `${_.dataset.pageNumber}: text ${_.dataset.textState} dpi ${_.dataset.renderedDpi} '${_.querySelector('.text-layer')?.textContent.slice(0, 200)}'`).join('\n')
                """);
            throw new TimeoutException($"No page has the text '{text}':\n{state}", exception);
        }
    }

    static async Task<float[]> FindPointAsync(IPage page, string text)
    {
        var found = await page.WaitForFunctionAsync(
            """
            text => {
                for (const element of document.querySelectorAll('.viewer-page[data-text-state=ready]')) {
                    const walker = document.createTreeWalker(element.querySelector('.text-layer'), NodeFilter.SHOW_TEXT);
                    const nodes = [];
                    let all = '';
                    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                        nodes.push([node, all.length]);
                        all += node.data;
                    }

                    const offset = all.indexOf(text);
                    if (offset < 0) {
                        continue;
                    }

                    for (const [node, start] of nodes) {
                        if (offset < start + node.data.length) {
                            node.parentElement.scrollIntoView({ block: 'center' });
                            const range = document.createRange();
                            range.setStart(node, offset - start);
                            range.setEnd(node, offset - start + 1);
                            const box = range.getBoundingClientRect();
                            const x = box.left + box.width / 2;
                            const y = box.top + box.height / 2;
                            return document.elementFromPoint(x, y)?.closest('.edit-box') ? 'covered' : [x, y];
                        }
                    }
                }

                return null;
            }
            """,
            text,
            new()
            {
                Timeout = 120000
            });
        var point = await found.JsonValueAsync<JsonElement>();
        if (point.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The text '{text}' is under the open editor, so a click there would not leave it.");
        }

        return point.Deserialize<float[]>()!;
    }

    // Waits until the main thread has run no long task for a second, and says when its last one ended,
    // on the page's clock. A long task is reported a little after it ends, so the quiet has to hold
    // over several looks rather than one.
    static async Task<double> QuietAsync(IPage page)
    {
        var watch = Stopwatch.StartNew();
        var quiet = 0;
        while (true)
        {
            var state = await page.EvaluateAsync<double[]>("() => [performance.now(), morphBenchmark.lastTaskEnd]");
            quiet = state[0] - state[1] >= 1000 ? quiet + 1 : 0;
            if (quiet >= 5)
            {
                return state[1];
            }

            if (watch.Elapsed > TimeSpan.FromMinutes(3))
            {
                throw new TimeoutException("The page never went a second without a long task.");
            }

            await Task.Delay(100);
        }
    }

    static Task<double> NowAsync(IPage page) =>
        page.EvaluateAsync<double>("() => performance.now()");

    static readonly JsonSerializerOptions pageJson = new(JsonSerializerDefaults.Web);

    static readonly JsonSerializerOptions reportJson = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    static async Task<T> ReadAsync<T>(IPage page, string expression)
    {
        var json = await page.EvaluateAsync<string>($"() => JSON.stringify({expression})");
        return JsonSerializer.Deserialize<T>(json, pageJson)!;
    }

    static bool Overlaps(LongTask task, double from, double to) =>
        task.Start < to &&
        task.Start + task.Duration > from;

    // How long the main thread was held by long tasks between two times.
    static double Blocked(List<LongTask> tasks, double from, double to) =>
        tasks.Sum(_ => Math.Max(0, Math.Min(_.Start + _.Duration, to) - Math.Max(_.Start, from)));

    // What ran in the long frames, by script, longest first.
    static List<ScriptTime> Attribute(List<LongFrame> frames) =>
        frames
            .SelectMany(_ => _.Scripts)
            .GroupBy(_ => $"{FileOf(_.Source)} {_.Name} ← {_.Invoker}".Trim())
            .Select(_ => new ScriptTime(_.Key, _.Count(), _.Sum(script => script.Duration)))
            .OrderByDescending(_ => _.Duration)
            .Take(8)
            .ToList();

    static string FileOf(string? url) =>
        url == null ? "" : url[(url.LastIndexOf('/') + 1)..];

    static string Report(List<DocumentTimings> results, double throttle)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var builder = new StringBuilder();
        builder.AppendLine("# Editor benchmark");
        builder.AppendLine();
        builder.AppendLine(
            $"Chromium {browser!.Version}, {Environment.ProcessorCount} logical cores, CPU throttled {throttle}x, " +
            $"{configuration} build, pages {Environment.GetEnvironmentVariable("MORPH_BENCH_PAGES") ?? "svg (the default)"}, {DateTime.Now:yyyy-MM-dd HH:mm}. Milliseconds; a step's time is the median of three rounds, " +
            "a key's the percentile over all of them. A key is timed from the browser receiving it to the end of the frame " +
            "that shows it; its handler is the editor's own beforeinput. A long task is one over 50 ms, during which the page answers nothing.");
        builder.AppendLine();
        builder.AppendLine("## Typing");
        builder.AppendLine();
        builder.AppendLine("| Document | Pages | Click → editor | Key p50 | Key p95 | Key max | Handler p50 | Handler max | Blocked while typing | Keys after moving on: p50 | p95 | max |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var result in results)
        {
            var keys = result.Rounds.SelectMany(_ => _.Typing).ToList();
            var latencies = keys.Select(_ => _.Latency).ToList();
            var handlers = keys.Select(_ => _.Script).ToList();
            var hurried = result.Rounds.SelectMany(_ => _.Hurried).Select(_ => _.Latency).ToList();
            builder.AppendLine(
                $"| {result.Name} | {result.Pages} | {Ms(Median(result.Rounds.Select(_ => _.OpenParagraph)))} | " +
                $"{Ms(Percentile(latencies, 0.5))} | {Ms(Percentile(latencies, 0.95))} | {Ms(Percentile(latencies, 1))} | " +
                $"{Ms(Percentile(handlers, 0.5))} | {Ms(Percentile(handlers, 1))} | {Ms(result.Rounds.Sum(_ => _.BlockedWhileTyping))} | " +
                $"{Ms(Percentile(hurried, 0.5))} | {Ms(Percentile(hurried, 0.95))} | {Ms(Percentile(hurried, 1))} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Edits");
        builder.AppendLine();
        builder.AppendLine("| Document | Open → first page | Open → idle | Click next paragraph → editor | Hand in → page redrawn | Hand in → idle | Long tasks after hand in | Longest task |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var result in results)
        {
            builder.AppendLine(
                $"| {result.Name} | {Ms(result.Open)} | {Ms(result.OpenIdle)} | {Ms(Median(result.Rounds.Select(_ => _.NextParagraph)))} | " +
                $"{Ms(Median(result.Rounds.Select(_ => _.HandIn)))} | {Ms(Median(result.Rounds.Select(_ => _.HandInIdle)))} | " +
                $"{Ms(Median(result.Rounds.Select(_ => _.HandInBlocked)))} | {Ms(Median(result.Rounds.Select(_ => _.HandInLongest)))} |");
        }

        // Each step .NET takes runs as a task of its own — the edit written into the file, the file laid
        // out again, each page painted — so the long tasks after a hand in, in order, are its steps.
        builder.AppendLine();
        builder.AppendLine("## Long tasks after the last hand in");
        builder.AppendLine();
        builder.AppendLine("Each as its start after Ctrl+Enter, then its length.");
        builder.AppendLine();
        foreach (var result in results.Where(_ => _.Rounds.Count > 0))
        {
            var tasks = result.Rounds[^1].HandInTasks.Select(_ => $"{Ms(_.Start)}+{Ms(_.Duration)}");
            builder.AppendLine($"- {result.Name}: {string.Join(", ", tasks)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Long animation frames while editing");
        foreach (var result in results)
        {
            builder.AppendLine();
            builder.AppendLine($"**{result.Name}**: {Ms(result.FrameTotal)} ms in long frames, {Ms(result.FrameLayout)} of it style, layout and paint.");
            builder.AppendLine();
            foreach (var script in result.Scripts)
            {
                builder.AppendLine($"- {Ms(script.Duration)} ms, {script.Count}×: `{script.Script}`");
            }
        }

        var failed = results.Where(_ => _.Error != null).ToList();
        if (failed.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failed");
            builder.AppendLine();
            foreach (var result in failed)
            {
                builder.AppendLine($"- {result.Name}: {result.Error}");
            }
        }

        return builder.ToString();
    }

    static double Median(IEnumerable<double> values) =>
        Percentile(values.ToList(), 0.5);

    // Nearest rank.
    static double Percentile(List<double> values, double fraction)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.Order().ToList();
        return sorted[Math.Clamp((int) Math.Ceiling(fraction * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    static string Ms(double value) =>
        double.IsNaN(value) ? "–" : value.ToString("0", CultureInfo.InvariantCulture);

    record BenchmarkDocument(string Name, byte[] Bytes, string First, string Second);

    // Every timing is written to the report's JSON, which ReSharper does not see as a read.
    // ReSharper disable NotAccessedPositionalProperty.Local
    record KeyTiming(double Start, double Delay, double Script, double Latency);
    // ReSharper restore NotAccessedPositionalProperty.Local

    record LongTask(double Start, double Duration);

    record LongFrame(double Start, double Duration, double Layout, List<FrameScript> Scripts);

    record FrameScript(string? Invoker, string? Source, string? Name, double Duration);

    record ScriptTime(string Script, int Count, double Duration);

    record RoundTimings(
        double OpenParagraph,
        List<KeyTiming> Typing,
        double BlockedWhileTyping,
        double NextParagraph,
        List<KeyTiming> Hurried,
        double HandIn,
        double HandInIdle,
        double HandInBlocked,
        List<LongTask> HandInTasks)
    {
        public double HandInLongest => HandInTasks.Select(_ => _.Duration).DefaultIfEmpty(0).Max();
    }

    record DocumentTimings(
        string Name,
        int Pages,
        double Open,
        double OpenIdle,
        List<RoundTimings> Rounds,
        double FrameTotal,
        double FrameLayout,
        List<ScriptTime> Scripts,
        string? Error = null);
}
