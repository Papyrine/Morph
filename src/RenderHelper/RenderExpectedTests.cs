using Word = Microsoft.Office.Interop.Word;

[TestFixture]
[Explicit]
[Apartment(ApartmentState.STA)]
public class RenderExpectedTests
{
    string inputsPath = Path.Combine(ProjectFiles.SolutionDirectory, @"Tests\Inputs");
    const int dpi = 150;

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

    static int ConvertXpsToPng(string xpsPath, string outputDirectory)
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

                // Re-tag the bitmap with 96 DPI metadata so the saved PNG's pHYs
                // chunk matches the result PNGs produced by Morph (Skia/ImageSharp
                // write 96 DPI / no DPI). Without this, GitHub renders the
                // expected column smaller than the result columns in compare-all-images.md
                // because browsers compute CSS intrinsic size from pHYs DPI.
                // Why: render quality stays at 150 DPI (more pixel detail), but the
                // file's declared density is 96 DPI to align with the comparison set.
                var stride = (renderBitmap.PixelWidth * renderBitmap.Format.BitsPerPixel + 7) / 8;
                var pixels = new byte[renderBitmap.PixelHeight * stride];
                renderBitmap.CopyPixels(pixels, stride, 0);
                var bitmap96Dpi = BitmapSource.Create(
                    renderBitmap.PixelWidth,
                    renderBitmap.PixelHeight,
                    96,
                    96,
                    renderBitmap.Format,
                    null,
                    pixels,
                    stride
                );

                // Encode as PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap96Dpi));

                // Save to file
                var outputPath = Path.Combine(outputDirectory, $"expected_{pageCount:D4}.png");
                using var stream = new FileStream(outputPath, FileMode.Create);
                encoder.Save(stream);
            }
        }

        return pageCount;
    }

    static IEnumerable<string> GetScenarioNames()
    {
        var inputsDir = Path.Combine(ProjectFiles.SolutionDirectory, @"Tests\Inputs");
        foreach (var docxPath in Directory.GetFiles(inputsDir, "input.docx", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(docxPath)!;
            // Return relative path from Inputs/ as the scenario name (e.g. "numbered_list" or "agendas-minutes\01")
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

            if (File.Exists(xpsPath))
            {
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
