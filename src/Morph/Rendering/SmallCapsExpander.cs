/// <summary>
/// Expands runs flagged with <see cref="RunProperties.SmallCaps"/> into per-segment
/// sub-runs so the existing layout pipeline (which assumes one font per run) can
/// render them correctly. Lowercase segments are uppercased and rendered at a
/// reduced font size; everything else passes through unchanged.
/// </summary>
static class SmallCapsExpander
{
    // Word sets a small-caps run's lowercase letters as capitals at 80% of the run's size, rounded to
    // the nearest half point: read off Word's XPS (_probe_smallcaps, 2026-11-28, Calibri), the small
    // capitals' em is 57.65 under 72pt, 9.6 under 12pt, and 9, 8.4, 7.8 and 7.2 under 11, 10.5, 9.5 and
    // 8.5pt — 9, 8.5, 7.5 and 7pt drawn on Word's whole 120-dpi pixels, where 80% exactly would draw
    // 8.4 and 6.6 for the first and last.
    const double smallCapsScale = 0.8;

    /// <summary>
    /// Returns the input list unchanged when no run uses small caps; otherwise
    /// returns a new list with case-boundary splits applied to small-caps runs.
    /// </summary>
    public static IReadOnlyList<Run> Expand(IReadOnlyList<Run> runs)
    {
        var hasSmallCaps = false;
        for (var i = 0; i < runs.Count; i++)
        {
            if (runs[i].Properties.SmallCaps)
            {
                hasSmallCaps = true;
                break;
            }
        }

        if (!hasSmallCaps)
        {
            return runs;
        }

        var expanded = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            if (!run.Properties.SmallCaps || string.IsNullOrEmpty(run.Text))
            {
                expanded.Add(run);
                continue;
            }

            // Inline images / tabs aren't text — preserve as-is even if SmallCaps is set.
            if (run.IsTab || run.InlineImageData is {Length: > 0})
            {
                expanded.Add(run);
                continue;
            }

            ExpandRun(run, expanded);
        }
        return expanded;
    }

    static void ExpandRun(Run run, List<Run> output)
    {
        var props = run.Properties;
        var smallProps = props with
        {
            FontSizePoints = Math.Round(props.FontSizePoints * smallCapsScale * 2, MidpointRounding.AwayFromZero) / 2,
            SmallCaps = false
        };
        var fullProps = props with
        {
            SmallCaps = false
        };

        var text = run.Text;
        var start = 0;
        var currentIsLower = char.IsLower(text[0]);

        for (var i = 1; i < text.Length; i++)
        {
            var isLower = char.IsLower(text[i]);
            if (isLower == currentIsLower)
            {
                continue;
            }

            output.Add(BuildSegment(text[start..i], currentIsLower, smallProps, fullProps));
            start = i;
            currentIsLower = isLower;
        }

        output.Add(BuildSegment(text[start..], currentIsLower, smallProps, fullProps));
    }

    static Run BuildSegment(string segment, bool isLower, RunProperties smallProps, RunProperties fullProps) =>
        new()
        {
            Text = isLower ? segment.ToUpperInvariant() : segment,
            Properties = isLower ? smallProps : fullProps
        };
}
