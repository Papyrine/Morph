/// <summary>
/// The crown validation for step 3 (<c>docs/layout-engine.md</c>): does the
/// <see cref="Fragmenter"/> paginate real corpus documents to the same page count as Word
/// (<c>expected_*.png</c>)? The block-flow, table and column slices handle multi-column paragraph flow
/// (including column breaks), inline images, non-wrapping body floats (which take no flow space), section
/// breaks — NextPage/even/odd switching page size, margins or columns, and same-geometry Continuous — and
/// tables whose cells hold text paragraphs or nested tables, so this compares on that subset — excluding
/// wrapping floats, inline shape groups, a Continuous mid-page geometry switch, and floating tables (all
/// later slices). Where it matches, the canonical measurer plus the height-model rules reproduce Word's
/// pagination from one backend-independent pass.
/// </summary>
public class FragmenterPageCountTests
{
    static readonly string inputsDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word");
    static readonly Fragmenter fragmenter = new(LayoutTestFonts.Measurer);

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

            var pages = fragmenter.Layout(document.Elements, document.PageSettings).Pages.Count;
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
        // tables, multi-column flow, the w:contextualSpacing collapse, inline images, nested tables,
        // non-wrapping body floats (which take no flow space), flow-neutral section breaks, and per-section
        // geometry (NextPage/even/odd breaks that switch page size, margins or columns, plus even/odd parity
        // filler pages) widens the set to 276 and holds 274 (99.3%). All four corpus column documents match.
        // Two misses remain: resumes/13 (a sub-line knife-edge Word's own backends straddle, 6 vs 5) and
        // business-plans/15 (18 vs 19, a one-page knife-edge across a nineteen-page multi-geometry document).
        // The threshold is calibrated just under the measured rate; a regression that drops another document
        // out of agreement fails here.
        await Assert.That(rate > 0.99).IsTrue();
    }

    // Documents whose top-level content is paragraphs (with inline images, but no inline shape groups),
    // plain page and column breaks, supported section breaks (NextPage/even/odd at any geometry, same-geometry
    // Continuous), non-wrapping body floats (no flow effect), and non-floating tables whose cells hold such
    // paragraphs or nested tables — the shape the block-flow, table and column slices cover. Everything else
    // (wrapping floats, inline shape groups, a Continuous mid-page geometry switch, floating tables) is a
    // later slice.
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
                case FloatingImageElement {WrapType: WrapType.None}:
                    break;
                case FloatingShapeElement:
                    break;
                case SectionBreakElement sectionBreak when IsSupportedSection(sectionBreak, document.PageSettings):
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

    // A section break the Fragmenter paginates like Word. NextPage/EvenPage/OddPage start the new section on
    // a fresh page and adopt its geometry (page size, margins, columns), with an even/odd parity filler page
    // — so any geometry is supported. A Continuous break stays on the same page: a same-geometry one is a
    // no-op, and a column-count switch flows the new columns from the break point (a page size cannot change
    // on a continuous break); only a margin-only continuous change is not yet adopted. NextColumn is not yet
    // exercised, so those documents stay out.
    static bool IsSupportedSection(SectionBreakElement sectionBreak, PageSettings page)
    {
        switch (sectionBreak.BreakType)
        {
            case SectionBreakType.NextPage or SectionBreakType.EvenPage or SectionBreakType.OddPage:
                return true;
            case SectionBreakType.Continuous:
                return sectionBreak.NewSectionSettings is not { } settings
                       || IsSameGeometry(settings, page)
                       || Math.Max(1, settings.ColumnCount) != Math.Max(1, page.ColumnCount);
            default:
                return false;
        }
    }

    static bool IsSameGeometry(PageSettings settings, PageSettings page) =>
        Math.Max(1, settings.ColumnCount) == Math.Max(1, page.ColumnCount) &&
        Math.Abs(settings.WidthPoints - page.WidthPoints) <= 1 &&
        Math.Abs(settings.HeightPoints - page.HeightPoints) <= 1 &&
        Math.Abs(settings.MarginTop - page.MarginTop) <= 1 &&
        Math.Abs(settings.MarginBottom - page.MarginBottom) <= 1 &&
        Math.Abs(settings.MarginLeft - page.MarginLeft) <= 1 &&
        Math.Abs(settings.MarginRight - page.MarginRight) <= 1;
}
