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
                .Where(d => Directory.GetFiles(d, "*.docx").Any())
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

                // Encode as PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                // Save to file
                var outputPath = Path.Combine(outputDirectory, $"expected_{pageCount:D4}.png");
                using var stream = new FileStream(outputPath, FileMode.Create);
                encoder.Save(stream);
            }
        }

        return pageCount;
    }

    [Test]
    [Explicit]
    public void GenerateSingleExpectedImage()
    {
        // Test with a single file for quick iteration
        var testDir = Path.Combine(inputsPath, "agendas-minutes", "01");
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

            var xpsPath = Path.Combine(testDir, "temp_output.xps");
            ConvertDocxToXps(wordApp, docxPath, xpsPath);

            var pageCount = ConvertXpsToPng(xpsPath, testDir);
            Console.WriteLine($"Generated {pageCount} pages");

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
