/// <summary>
/// The crown validation for step 3 (<c>docs/layout-engine-proposal.md</c>): does the
/// <see cref="Fragmenter"/> paginate real corpus documents to the same page count as Word
/// (<c>expected_*.png</c>)? The block-flow and table slices handle single-column paragraph flow plus
/// tables whose cells hold plain text paragraphs, so this compares on that subset — excluding floats,
/// inline art, multi-column sections, mid-document section breaks, floating tables, and tables with
/// images or nested tables in a cell (all later slices). Where it matches, the canonical measurer plus
/// the height-model rules reproduce Word's pagination from one backend-independent pass.
/// </summary>
public class FragmenterPageCountTests
{
    static readonly string inputsDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
    static readonly Fragmenter Fragmenter = new(LayoutTestFonts.Measurer);

    [Test]
    public async Task Fragmenter_page_count_matches_Word_on_block_and_simple_table_documents()
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

            if (!IsBlockFlowOrSimpleTable(document))
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
        // The block-flow slice alone matched every pure-block document (96/96); adding simple text tables
        // widens the set to 153 and holds 150 (98.0%). The three misses are sub-line knife-edges where a
        // table tips onto an extra page or a trailing line spills — resumes/13 is one Word's own backends
        // straddle. The threshold is calibrated just under the measured rate; a regression that drops
        // another document out of agreement fails here.
        await Assert.That(rate > 0.97).IsTrue();
    }

    // Single-column documents whose top-level content is paragraphs (without inline images or shape
    // groups), plain page breaks, and non-floating tables whose cells hold only such paragraphs — the
    // shape the block-flow and table slices cover. Everything else (floats, inline art, columns, section
    // breaks, floating tables, images or nested tables in a cell) is a later slice.
    static bool IsBlockFlowOrSimpleTable(ParsedDocument document)
    {
        if (document.PageSettings.ColumnCount > 1)
        {
            return false;
        }

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
                    if (element is not ParagraphElement paragraph || ParagraphHasInlineArt(paragraph))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    static bool ParagraphHasInlineArt(ParagraphElement paragraph) =>
        paragraph.Runs.Any(_ => _.InlineImageData != null || _.InlineShapeGroup != null);
}
