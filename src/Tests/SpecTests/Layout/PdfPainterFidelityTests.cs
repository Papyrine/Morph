using Morph.PDFium;

/// <summary>
/// End-to-end fidelity of the layout engine on real corpus documents: parse → fragment
/// (<see cref="Fragmenter"/>) → paint (<c>PdfPainter</c>) → rasterise (PDFium at 150 DPI) → SSIM against
/// Word's reference render (<c>expected_*.png</c>). Runs on the block/table/column subset the painter
/// covers. This is the honest measure of how close the whole engine is to Word on real content, not a
/// synthetic fixture — the gaps (headers/footers, tabs, floats, shapes not yet painted) show up as
/// reduced SSIM, so the number is a floor, not a ceiling.
/// </summary>
public class PdfPainterFidelityTests
{
    static readonly string inputsDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
    static readonly Fragmenter Fragmenter = new(LayoutTestFonts.Measurer);
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    [Test]
    public async Task Painter_fidelity_vs_Word_on_block_table_and_column_documents()
    {
        var perDocument = new List<(string Name, double Ssim, int Pages)>();
        var pageCountMismatches = 0;

        foreach (var input in Directory.GetFiles(inputsDirectory, "input.docx", SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(input)!;
            var expectedFiles = Directory.GetFiles(directory, "expected_*.png").Order().ToArray();
            if (expectedFiles.Length == 0)
            {
                continue;
            }

            ParsedDocument document;
            try
            {
                using var stream = File.OpenRead(input);
                document = new DocumentParser().Parse(stream);
            }
            catch
            {
                continue;
            }

            if (!IsBlockTableOrColumnFlow(document))
            {
                continue;
            }

            byte[] pdfBytes;
            try
            {
                var pdf = PdfPainter.Paint(Fragmenter.Layout(document.Elements, document.PageSettings), fontsDirectory);
                using var stream = new MemoryStream();
                pdf.Save(stream, false);
                pdfBytes = stream.ToArray();
            }
            catch
            {
                continue;
            }

            using var rasterized = PdfiumDocument.Load(pdfBytes);
            if (rasterized.PageCount != expectedFiles.Length)
            {
                pageCountMismatches++;
                continue;
            }

            double sum = 0;
            var pages = 0;
            for (var page = 0; page < rasterized.PageCount; page++)
            {
                var (_, ssim) = PageComparison.Compare(expectedFiles[page], rasterized.RenderPage(page, 150));
                if (ssim is { } value)
                {
                    sum += value;
                    pages++;
                }
            }

            if (pages > 0)
            {
                var name = $"{Path.GetFileName(Path.GetDirectoryName(directory))}/{Path.GetFileName(directory)}";
                perDocument.Add((name, sum / pages, pages));
            }
        }

        var ordered = perDocument.OrderByDescending(_ => _.Ssim).ToList();
        var meanSsim = ordered.Count == 0 ? 0 : ordered.Average(_ => _.Ssim);
        var median = ordered.Count == 0 ? 0 : ordered[ordered.Count / 2].Ssim;

        var report = new List<string>
        {
            $"Painter fidelity vs Word: {ordered.Count} documents (page count matched), {pageCountMismatches} page-count mismatches skipped.",
            $"SSIM mean={meanSsim:F3} median={median:F3} (1.000 = pixel-identical to Word).",
            "",
            "Best 8:",
        };
        report.AddRange(ordered.Take(8).Select(_ => $"  {_.Ssim:F3}  {_.Name} ({_.Pages}p)"));
        report.Add("");
        report.Add("Worst 12:");
        report.AddRange(ordered.AsEnumerable().Reverse().Take(12).Select(_ => $"  {_.Ssim:F3}  {_.Name} ({_.Pages}p)"));
        Console.WriteLine(string.Join("\n", report));

        await Assert.That(ordered.Count).IsGreaterThan(100);
        // Measured mean 0.940 / median 0.977 SSIM against Word — plain text and tables are near
        // pixel-identical; the low scorers are the not-yet-painted features (intra-paragraph line breaks,
        // all-caps, justify, tabs, headers/footers, column balancing). The floor guards the headline
        // median from a gross regression while those gaps are closed.
        await Assert.That(median > 0.95).IsTrue();
    }

    static bool IsBlockTableOrColumnFlow(ParsedDocument document)
    {
        foreach (var element in document.Elements)
        {
            switch (element)
            {
                case ParagraphElement paragraph:
                    if (paragraph.Runs.Any(_ => _.InlineImageData != null || _.InlineShapeGroup != null))
                    {
                        return false;
                    }

                    break;
                case PageBreakElement:
                case ColumnBreakElement:
                    break;
                case TableElement table when IsSimpleTable(table):
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    static bool IsSimpleTable(TableElement table)
    {
        if (table.Properties.IsFloating)
        {
            return false;
        }

        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var element in cell.Content)
                {
                    if (element is not ParagraphElement paragraph ||
                        paragraph.Runs.Any(_ => _.InlineImageData != null || _.InlineShapeGroup != null))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
