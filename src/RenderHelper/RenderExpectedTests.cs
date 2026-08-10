using Word = Microsoft.Office.Interop.Word;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

[TestFixture]
[Explicit]
[Apartment(ApartmentState.STA)]
public class RenderExpectedTests
{
    // The scenario corpus is split by input format under Tests\Inputs\ — word\ (input.docx),
    // excel\ (input.xlsx) and powerpoint\ (input.pptx) — because the themed category names collide
    // across formats. One root and one office application per format; the XPS → PNG stage below is
    // shared, since it only ever sees an XPS file and does not care which application wrote it.
    string inputsPath = Path.Combine(ProjectFiles.SolutionDirectory, @"Tests\Inputs\word");
    string powerPointInputsPath = Path.Combine(ProjectFiles.SolutionDirectory, @"Tests\Inputs\powerpoint");
    const int dpi = 150;

    // Slides rendered per deck. MUST match ScenarioInputs.Pages(ScenarioFormat.PowerPoint) on the
    // Tests side: the scenario tests record a per-page metric only when the reference page count
    // equals the rendered page count, so a mismatch here silently drops the whole comparison.
    const int powerPointMaxPages = 2;

    // Slides render lower than documents: their fidelity is pictures and large-format layout rather
    // than typography, and 96 resolves a 16:9 canvas to 1280x720 at 40% of the pixels. MUST match
    // ScenarioInputs.Dpi(ScenarioFormat.PowerPoint) on the Tests side — a mismatch does not fail,
    // it silently suppresses SSIM and skews the error metric, because the images differ in size.
    const int powerPointDpi = 96;

    [Test]
    public void GenerateExpectedImages()
    {
        Word.Application? wordApp = null;

        try
        {
            wordApp = new()
            {
                Visible = false,
                DisplayAlerts = Word.WdAlertLevel.wdAlertsNone
            };

            var directories = Directory.GetDirectories(inputsPath, "*", SearchOption.AllDirectories)
                .Where(_ => Directory.GetFiles(_, "*.docx").Any())
                .ToList();

            foreach (var directory in directories)
            {
                var docxFiles = Directory.GetFiles(directory, "*.docx");
                if (docxFiles.Length == 0)
                {
                    continue;
                }

                var docxPath = docxFiles.First();
                Console.WriteLine($"Processing: {docxPath}");

                try
                {
                    // Delete existing expected_*.png files
                    var existingExpected = Directory.GetFiles(directory, "expected_*.png");
                    foreach (var file in existingExpected)
                    {
                        File.Delete(file);
                        Console.WriteLine($"  Deleted: {Path.GetFileName(file)}");
                    }

                    // Convert docx to XPS
                    var xpsPath = Path.Combine(directory, "temp_output.xps");
                    ConvertDocxToXps(wordApp, docxPath, xpsPath);

                    // Convert XPS pages to PNG
                    var pageCount = ConvertXpsToPng(xpsPath, directory);
                    Console.WriteLine($"  Generated {pageCount} pages");

                    // Clean up XPS file
                    if (File.Exists(xpsPath))
                    {
                        File.Delete(xpsPath);
                    }
                }
                catch (Exception ex)
                {
                    throw new(directory, ex);
                }
            }
        }
        finally
        {
            if (wordApp != null)
            {
                wordApp.Quit(false);
                Marshal.ReleaseComObject(wordApp);
            }
        }
    }

    static void ConvertDocxToXps(Word.Application wordApp, string docxPath, string xpsPath)
    {
        Word.Document? doc = null;

        try
        {
            doc = wordApp.Documents.Open(
                docxPath,
                ReadOnly: true,
                Visible: false,
                AddToRecentFiles: false
            );

            // Delete existing XPS if it exists
            if (File.Exists(xpsPath))
            {
                File.Delete(xpsPath);
            }

            doc.SaveAs2(
                xpsPath,
                Word.WdSaveFormat.wdFormatXPS
            );
        }
        finally
        {
            if (doc != null)
            {
                doc.Close(false);
                Marshal.ReleaseComObject(doc);
            }
        }
    }

    /// <summary>
    /// Rasterises an XPS to <c>expected_NNNN.png</c>. Shared by every input format — it only sees an
    /// XPS file, so nothing here knows which office application produced it.
    /// <paramref name="maxPages"/> caps the output (0 = no cap) for corpora whose scenarios are
    /// deliberately only partly baselined.
    /// </summary>
    static int ConvertXpsToPng(string xpsPath, string outputDirectory, int maxPages = 0)
    {
        using var xpsDoc = new XpsDocument(xpsPath, FileAccess.Read);
        var fixedDocSeq = xpsDoc.GetFixedDocumentSequence();

        if (fixedDocSeq == null)
        {
            return 0;
        }

        var pageCount = 0;

        foreach (var docRef in fixedDocSeq.References)
        {
            var fixedDoc = docRef.GetDocument(false);
            if (fixedDoc == null)
            {
                continue;
            }

            foreach (var pageRef in fixedDoc.Pages)
            {
                if (maxPages > 0 && pageCount >= maxPages)
                {
                    return pageCount;
                }

                pageCount++;
                var page = pageRef.GetPageRoot(false);
                if (page == null)
                {
                    continue;
                }

                // Calculate pixel dimensions based on DPI
                var scale = dpi / 96.0;
                var widthPixels = (int)(page.Width * scale);
                var heightPixels = (int)(page.Height * scale);

                // Measure and arrange - required for visuals not in visual tree
                var pageSize = new System.Windows.Size(page.Width, page.Height);
                page.Measure(pageSize);
                page.Arrange(new(pageSize));
                page.UpdateLayout();

                // Create render target at target DPI
                var renderBitmap = new RenderTargetBitmap(
                    widthPixels,
                    heightPixels,
                    dpi,
                    dpi,
                    PixelFormats.Pbgra32
                );

                // Render the page directly
                renderBitmap.Render(page);

                SaveAt96Dpi(renderBitmap, Path.Combine(outputDirectory, $"expected_{pageCount:D4}.png"));
            }
        }

        return pageCount;
    }

    /// <summary>
    /// Writes a PNG whose pHYs chunk declares 96 DPI, whatever density the source carries.
    ///
    /// Pixel detail stays at the <see cref="dpi"/> it was rendered at; only the declared density
    /// changes. Morph's own result PNGs write no pHYs at all, which browsers read as 96, so a
    /// reference tagged at 150 would render two-thirds the width of the result columns beside it in
    /// compare-all-images.md — browsers take intrinsic size from pHYs.
    /// </summary>
    static void SaveAt96Dpi(BitmapSource source, string targetPath)
    {
        var stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[source.PixelHeight * stride];
        source.CopyPixels(pixels, stride, 0);

        // The palette is carried across rather than dropped: PowerPoint exports flat-colour slides as
        // indexed PNGs, and BitmapSource.Create rejects an indexed format with a null palette.
        // Preserving it also keeps those files at their much smaller palette-encoded size.
        var retagged = BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            96,
            96,
            source.Format,
            source.Palette,
            pixels,
            stride
        );

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(retagged));

        using var stream = new FileStream(targetPath, FileMode.Create);
        encoder.Save(stream);
    }

    /// <summary>
    /// Renders a deck straight to one PNG per slide, writing <c>expected_NNNN.png</c> into
    /// <paramref name="outputDirectory"/> and returning how many were kept.
    ///
    /// Slides skip the XPS stage the DOCX path needs. A deck has fixed geometry — one slide is one
    /// page, with no pagination to discover — so <c>Presentation.Export</c> rasterises directly at a
    /// chosen pixel size. It is also the only export entry point whose parameters are all plainly
    /// typed: <c>ExportAsFixedFormat</c> and <c>SaveAs</c> both take <c>MsoTriState</c>, which lives
    /// in the Office PIA (<c>office.dll</c>) — a GAC-only assembly with no NuGet package, so
    /// referencing it would break <c>dotnet build</c> on any machine without Office installed.
    ///
    /// <c>Presentations.Open</c> is still late-bound for that same reason, passing MsoTriState's raw
    /// values (msoTrue = -1, msoFalse = 0). <c>WithWindow: msoFalse</c> is what keeps PowerPoint off
    /// the screen — unlike Word it rejects <c>Application.Visible = false</c> — and it needs an
    /// interactive desktop session either way, so this cannot run under a service account.
    /// </summary>
    static int ExportSlidesToPng(PowerPoint.Application app, string pptxPath, string outputDirectory, int maxPages)
    {
        dynamic? presentation = null;

        // Both paths are canonicalised because ProjectFiles.SolutionDirectory yields forward slashes
        // while Path.Combine appends backslashes, and PowerPoint's COM export rejects the resulting
        // mixed-separator path with the unhelpful "PowerPoint can't save ^0 to ^1". .NET itself is
        // perfectly happy with it, so the paths look fine right up until PowerPoint sees them.
        pptxPath = Path.GetFullPath(pptxPath);
        var stagingDirectory = Path.GetFullPath(Path.Combine(outputDirectory, "temp_slides"));

        try
        {
            dynamic presentations = app.Presentations;
            // ReadOnly is msoFalse: PowerPoint refuses Presentation.Export from a read-only
            // presentation ("PowerPoint can't save ^0 to ^1"). Nothing here ever saves the file, and
            // it is closed without saving below.
            presentation = presentations.Open(pptxPath, 0, 0, 0);

            // PageSetup carries the slide box in points.
            var scale = powerPointDpi / 72.0;
            var widthPixels = (int) Math.Round((double) presentation.PageSetup.SlideWidth * scale);
            var heightPixels = (int) Math.Round((double) presentation.PageSetup.SlideHeight * scale);

            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }

            Directory.CreateDirectory(stagingDirectory);
            presentation.Export(stagingDirectory, "PNG", widthPixels, heightPixels);

            // Export names its output Slide1.PNG … Slide10.PNG, which sorts lexically into the wrong
            // order, so the trailing number is what orders them.
            var exported = Directory.GetFiles(stagingDirectory, "*.PNG")
                .OrderBy(SlideNumber)
                .ToList();

            var kept = 0;
            foreach (var file in exported)
            {
                if (maxPages > 0 && kept >= maxPages)
                {
                    break;
                }

                kept++;

                // OnLoad so the staging file's handle is released before the directory is deleted.
                var decoder = new PngBitmapDecoder(
                    new Uri(file),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad
                );
                SaveAt96Dpi(decoder.Frames[0], Path.Combine(outputDirectory, $"expected_{kept:D4}.png"));
            }

            return kept;
        }
        finally
        {
            if (presentation != null)
            {
                presentation.Close();
                Marshal.ReleaseComObject(presentation);
            }

            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
    }

    static int SlideNumber(string path)
    {
        var digits = new string(Path.GetFileNameWithoutExtension(path).Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? int.Parse(digits) : 0;
    }

    static IEnumerable<string> GetPowerPointScenarioNames()
    {
        var inputsDir = Path.Combine(ProjectFiles.SolutionDirectory, @"Tests\Inputs\powerpoint");
        if (!Directory.Exists(inputsDir))
        {
            yield break;
        }

        foreach (var pptxPath in Directory.GetFiles(inputsDir, "input.pptx", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(pptxPath)!;
            yield return dir.Substring(inputsDir.Length + 1);
        }
    }

    [Test]
    [TestCaseSource(nameof(GetPowerPointScenarioNames))]
    public void GenerateExpectedPowerPointImage(string scenarioName)
    {
        var testDir = Path.Combine(powerPointInputsPath, scenarioName);
        var pptxPath = Path.Combine(testDir, "input.pptx");

        if (!File.Exists(pptxPath))
        {
            Assert.Fail($"Test file not found: {pptxPath}");
            return;
        }

        foreach (var file in Directory.GetFiles(testDir, "expected_*.png"))
        {
            File.Delete(file);
        }

        var pageCount = ExportSlidesToPng(PowerPointApp(), pptxPath, testDir, powerPointMaxPages);
        Console.WriteLine($"Generated {pageCount} pages for {scenarioName}");
    }

    static PowerPoint.Application? powerPointApp;

    /// <summary>
    /// The one PowerPoint instance the whole fixture shares.
    ///
    /// PowerPoint's COM server is single-instance: <c>new Application()</c> attaches to a running
    /// copy rather than starting another. Creating and quitting one per test therefore races —
    /// quitting at the end of one case tears down the object the next is attaching to, which surfaces
    /// as <c>RPC_E_DISCONNECTED</c> on a scattered handful of scenarios. Word tolerates the per-test
    /// pattern; PowerPoint does not.
    /// </summary>
    static PowerPoint.Application PowerPointApp() =>
        powerPointApp ??= new()
        {
            DisplayAlerts = PowerPoint.PpAlertLevel.ppAlertsNone
        };

    [OneTimeTearDown]
    public void QuitPowerPoint()
    {
        if (powerPointApp == null)
        {
            return;
        }

        // PowerPoint leaks its process aggressively when Quit or the release is skipped.
        powerPointApp.Quit();
        Marshal.ReleaseComObject(powerPointApp);
        powerPointApp = null;
    }

    static IEnumerable<string> GetScenarioNames()
    {
        var inputsDir = Path.Combine(ProjectFiles.SolutionDirectory, @"Tests\Inputs\word");
        foreach (var docxPath in Directory.GetFiles(inputsDir, "input.docx", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(docxPath)!;
            // Return the path relative to the format root as the scenario name (e.g. "numbered_list"
            // or "agendas-minutes\01"), matching ScenarioInputs.ScenarioName on the Tests side.
            yield return dir.Substring(inputsDir.Length + 1);
        }
    }

    [Test]
    [TestCaseSource(nameof(GetScenarioNames))]
    public void GenerateExpectedImage(string scenarioName)
    {
        var testDir = Path.Combine(inputsPath, scenarioName);
        var docxPath = Path.Combine(testDir, "input.docx");

        if (!File.Exists(docxPath))
        {
            Assert.Fail($"Test file not found: {docxPath}");
            return;
        }

        Word.Application? wordApp = null;
        try
        {
            wordApp = new()
            {
                Visible = false,
                DisplayAlerts = Word.WdAlertLevel.wdAlertsNone
            };

            // Delete existing expected_*.png files
            foreach (var file in Directory.GetFiles(testDir, "expected_*.png"))
            {
                File.Delete(file);
            }

            var xpsPath = Path.Combine(testDir, "temp_output.xps");
            ConvertDocxToXps(wordApp, docxPath, xpsPath);

            var pageCount = ConvertXpsToPng(xpsPath, testDir);
            Console.WriteLine($"Generated {pageCount} pages for {scenarioName}");

            // MORPH_KEEP_XPS: preserve the intermediate XPS (exact glyph/border geometry) next to the PNGs
            // for measurement studies (e.g. the table row-height model). Off by default.
            if (File.Exists(xpsPath))
            {
                if (Environment.GetEnvironmentVariable("MORPH_KEEP_XPS") != null)
                {
                    File.Copy(xpsPath, Path.Combine(testDir, "word_output.xps"), overwrite: true);
                }

                File.Delete(xpsPath);
            }
        }
        finally
        {
            if (wordApp != null)
            {
                wordApp.Quit(false);
                Marshal.ReleaseComObject(wordApp);
            }
        }
    }
}
