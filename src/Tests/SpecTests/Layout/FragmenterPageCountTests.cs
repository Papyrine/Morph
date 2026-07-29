/// <summary>
/// The crown validation for step 3 (<c>docs/layout-engine-proposal.md</c>): does the
/// <see cref="Fragmenter"/> paginate real corpus documents to the same page count as Word
/// (<c>expected_*.png</c>)? The block-flow, table and column slices handle multi-column paragraph flow
/// (including column breaks), inline images, non-wrapping body floats (which take no flow space), and
/// tables whose cells hold text paragraphs or nested tables, so this compares on that subset — excluding
/// wrapping floats, inline shape groups, mid-document section breaks, and floating tables (all later
/// slices). Where it matches, the canonical measurer plus the height-model rules reproduce Word's
/// pagination from one backend-independent pass.
/// </summary>
public class FragmenterPageCountTests
{
    static readonly string inputsDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
    static readonly Fragmenter Fragmenter = new(LayoutTestFonts.Measurer);

    [Test]
    public async Task Fragmenter_page_count_matches_Word_on_block_table_and_column_documents()
    {
        var compared = 0;
        var matched = 0;
        var mismatches = new List<string>();

        foreach (var input in Directory.GetFiles(inputsDirectory, "input.docx", SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(input)!;
            var wordPages = Directory.GetFiles(directory, "expected_*.png").Length;
            if (wordPages == 0)
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

            var pages = Fragmenter.Layout(document.Elements, document.PageSettings).Pages.Count;
            compared++;
            if (pages == wordPages)
            {
                matched++;
            }
            else if (mismatches.Count < 20)
            {
                var scenario = $"{Path.GetFileName(Path.GetDirectoryName(directory))}/{Path.GetFileName(directory)}";
                mismatches.Add($"{scenario}: fragmenter={pages} word={wordPages}");
            }
        }

        var rate = compared == 0 ? 0 : (double) matched / compared;
        Console.WriteLine($"Fragmenter vs Word page count: {matched}/{compared} block/simple-table documents match ({rate:P1}).");
        foreach (var line in mismatches)
        {
            Console.WriteLine("  DIFF " + line);
        }

        await Assert.That(compared).IsGreaterThan(20);
        // The block-flow slice matched every pure-block document (96/96); successively adding plain text
        // tables, multi-column flow, the w:contextualSpacing collapse, inline images, nested tables, and now
        // non-wrapping body floats (which take no flow space, so they leave pagination untouched) widens the
        // set to 239 and holds 238 (99.6%). All four corpus column documents match. The one miss, resumes/13,
        // is a sub-line knife-edge Word's own backends straddle (6 vs 5). The threshold is calibrated just
        // under the measured rate; a regression that drops another document out of agreement fails here.
        await Assert.That(rate > 0.99).IsTrue();
    }

    // Documents whose top-level content is paragraphs (with inline images, but no inline shape groups),
    // plain page and column breaks, non-wrapping body floats (no flow effect), and non-floating tables whose
    // cells hold such paragraphs or nested tables — the shape the block-flow, table and column slices cover,
    // at the document's single (equal-width) column geometry. Everything else (wrapping floats, inline shape
    // groups, mid-document section breaks, floating tables) is a later slice.
    static bool IsBlockTableOrColumnFlow(ParsedDocument document)
    {
        foreach (var element in document.Elements)
        {
            switch (element)
            {
                case ParagraphElement paragraph:
                    if (ParagraphHasInlineArt(paragraph))
                    {
                        return false;
                    }

                    break;
                case PageBreakElement:
                case ColumnBreakElement:
                    break;
                case TableElement table when IsSimpleTable(table):
                    break;
                case FloatingImageElement image when image.WrapType == WrapType.None:
                    break;
                case FloatingShapeElement:
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
                    switch (element)
                    {
                        case ParagraphElement paragraph when !ParagraphHasInlineArt(paragraph):
                            break;
                        case TableElement nested when IsSimpleTable(nested):
                            break;
                        default:
                            return false;
                    }
                }
            }
        }

        return true;
    }

    // Inline images stay in the set — the measurer sizes each line to its tallest inline image, so they
    // paginate. Only an inline shape group (grouped drawing / WordArt) is still out of scope.
    static bool ParagraphHasInlineArt(ParagraphElement paragraph) =>
        paragraph.Runs.Any(_ => _.InlineShapeGroup != null);
}
