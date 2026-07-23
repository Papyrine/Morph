using System.Text.RegularExpressions;

/// <summary>
/// The Summary block at the bottom of <c>docs/word-features.md</c> — the per-category table, its
/// <c>**Total**</c> row and the mermaid coverage pie — is a hand-maintained tally of the
/// <c>#### Feature `STATUS`</c> headings above it. Nothing recomputes it, so it silently drifts:
/// twice now it has disagreed with the document it summarises. Once a category row was left at
/// 22/2/1/1 when the section held three PARTIALs, understating the total by one; once a merge
/// added a Tables feature on one side while the other side rewrote the summary rows, and git
/// resolved both cleanly into numbers that matched neither.
///
/// Neither failure is visible to a reader or to any other test — the document stays well-formed
/// and the wrong figure is the one people quote. This recomputes the tally from the headings and
/// fails with the exact deltas, so the counts stop depending on whoever edits the file next
/// remembering to recount by hand.
///
/// Categories are matched on their leading number, not their label: the section heading carries a
/// qualifier the table row omits (<c>## 1. Text Formatting (Run Properties)</c> versus
/// <c>| 1. Text Formatting |</c>), and the number is the stable key across both.
/// </summary>
public class FeatureMatrixSummaryTests
{
    static readonly string matrixPath = Path.GetFullPath(
        Path.Combine(ProjectFiles.ProjectDirectory, "..", "..", "docs", "word-features.md"));

    // "## 4. Tables" — opens a category. A non-numbered "## Summary" closes the current one so the
    // legend and prose sections cannot contribute features.
    static readonly Regex sectionHeading = new(@"^## (\d+)\. ", RegexOptions.Compiled);
    static readonly Regex otherHeading = new(@"^## (?!\d+\. )", RegexOptions.Compiled);

    // "#### Font Width Scale `DONE`" — a feature and its status. The legend at the top of the file
    // lists the same words in a table, not a heading, so anchoring to #### excludes it.
    static readonly Regex featureHeading = new(@"^#### .*`(DONE|PARTIAL|TODO|WONTFIX)`\s*$", RegexOptions.Compiled);

    // "| 4. Tables | 28 | 0 | 0 | 0 | 28 |"
    static readonly Regex categoryRow = new(
        @"^\| (\d+)\. [^|]*\| *(\d+) *\| *(\d+) *\| *(\d+) *\| *(\d+) *\| *(\d+) *\|\s*$",
        RegexOptions.Compiled);

    // "| **Total** | **162** | **4** | **5** | **1** | **172** |"
    static readonly Regex totalRow = new(
        @"^\| \*\*Total\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \|\s*$",
        RegexOptions.Compiled);

    //     "Done" : 162
    static readonly Regex pieSlice = new(@"^\s*""(Done|Partial|Todo|Wontfix)"" : (\d+)\s*$", RegexOptions.Compiled);

    static readonly string[] statuses = ["DONE", "PARTIAL", "TODO", "WONTFIX"];

    // Pie labels are title-case; heading markers are upper-case. Same order as `statuses`.
    static readonly string[] pieLabels = ["Done", "Partial", "Todo", "Wontfix"];

    [Test]
    public async Task SummaryTallyMatchesTheFeatureHeadings()
    {
        var lines = await File.ReadAllLinesAsync(matrixPath);

        var counted = CountFeatureHeadings(lines);
        var declared = ParseCategoryRows(lines);
        var declaredTotal = ParseTotalRow(lines);
        var pie = ParsePie(lines);

        // Every assertion below compares two numbers, so a parse that found nothing would pass
        // vacuously. Fail loudly instead: a zero here means the document's shape moved, not that
        // the tally is right.
        await Assert.That(counted).IsNotEmpty()
            .Because($"no '#### Feature `STATUS`' headings were found in {matrixPath} — the parser is out of step with the document.");
        await Assert.That(declared).IsNotEmpty()
            .Because("no '| N. Category | ... |' rows were found in the Summary table.");
        await Assert.That(declaredTotal).IsNotNull()
            .Because("the '| **Total** | ... |' row was not found in the Summary table.");
        await Assert.That(pie).IsNotEmpty()
            .Because("no slices were found in the mermaid coverage pie.");

        var total = declaredTotal!.Value;
        var problems = new List<string>();

        foreach (var category in counted.Keys.Union(declared.Keys).Order())
        {
            if (!declared.TryGetValue(category, out var row))
            {
                problems.Add($"category {category} has feature headings but no row in the Summary table");
                continue;
            }

            if (!counted.TryGetValue(category, out var actual))
            {
                problems.Add($"category {category} has a Summary row but no feature headings");
                continue;
            }

            for (var index = 0; index < statuses.Length; index++)
            {
                if (row.Counts[index] != actual[statuses[index]])
                {
                    problems.Add(
                        $"category {category} {statuses[index]}: table says {row.Counts[index]}, headings have {actual[statuses[index]]}");
                }
            }

            // The row's own sixth column is a third copy of the same tally; a row can be internally
            // consistent yet still disagree with the headings (that is how the 22/2/1/1 slip
            // survived), so check both directions.
            var rowSum = row.Counts.Sum();
            if (row.Total != rowSum)
            {
                problems.Add($"category {category} total column says {row.Total}, its own four columns sum to {rowSum}");
            }
        }

        for (var index = 0; index < statuses.Length; index++)
        {
            var status = statuses[index];
            var expected = counted.Values.Sum(_ => _[status]);

            if (total.Counts[index] != expected)
            {
                problems.Add($"**Total** {status}: row says {total.Counts[index]}, headings have {expected}");
            }

            if (pie.TryGetValue(pieLabels[index], out var slice) && slice != expected)
            {
                problems.Add($"coverage pie \"{pieLabels[index]}\": says {slice}, headings have {expected}");
            }
            else if (!pie.ContainsKey(pieLabels[index]))
            {
                problems.Add($"coverage pie has no \"{pieLabels[index]}\" slice");
            }
        }

        var overall = counted.Values.Sum(_ => _.Values.Sum());
        if (total.Total != overall)
        {
            problems.Add($"**Total** overall says {total.Total}, headings have {overall}");
        }

        await Assert.That(problems).IsEmpty()
            .Because(
                "the Summary block in docs/word-features.md no longer matches the feature headings " +
                "it tallies. Recount from the '#### Feature `STATUS`' headings and update the " +
                "category table, the **Total** row and the mermaid pie together." +
                Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    static Dictionary<int, Dictionary<string, int>> CountFeatureHeadings(string[] lines)
    {
        var counted = new Dictionary<int, Dictionary<string, int>>();
        int? section = null;

        foreach (var line in lines)
        {
            var heading = sectionHeading.Match(line);
            if (heading.Success)
            {
                section = int.Parse(heading.Groups[1].Value);
                counted[section.Value] = statuses.ToDictionary(_ => _, _ => 0);
                continue;
            }

            if (otherHeading.IsMatch(line))
            {
                section = null;
                continue;
            }

            var feature = featureHeading.Match(line);
            if (feature.Success && section is {} current)
            {
                counted[current][feature.Groups[1].Value]++;
            }
        }

        return counted;
    }

    static Dictionary<int, (int[] Counts, int Total)> ParseCategoryRows(string[] lines)
    {
        var declared = new Dictionary<int, (int[], int)>();

        foreach (var line in lines)
        {
            var match = categoryRow.Match(line);
            if (match.Success)
            {
                var counts = Enumerable.Range(2, 4).Select(_ => int.Parse(match.Groups[_].Value)).ToArray();
                declared[int.Parse(match.Groups[1].Value)] = (counts, int.Parse(match.Groups[6].Value));
            }
        }

        return declared;
    }

    static (int[] Counts, int Total)? ParseTotalRow(string[] lines)
    {
        foreach (var line in lines)
        {
            var match = totalRow.Match(line);
            if (match.Success)
            {
                var counts = Enumerable.Range(1, 4).Select(_ => int.Parse(match.Groups[_].Value)).ToArray();
                return (counts, int.Parse(match.Groups[5].Value));
            }
        }

        return null;
    }

    static Dictionary<string, int> ParsePie(string[] lines)
    {
        var pie = new Dictionary<string, int>();

        foreach (var line in lines)
        {
            var match = pieSlice.Match(line);
            if (match.Success)
            {
                pie[match.Groups[1].Value] = int.Parse(match.Groups[2].Value);
            }
        }

        return pie;
    }
}
