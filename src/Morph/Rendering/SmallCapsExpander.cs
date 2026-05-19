/// <summary>
/// Expands runs flagged with <see cref="RunProperties.SmallCaps"/> into per-segment
/// sub-runs so the existing layout pipeline (which assumes one font per run) can
/// render them correctly. Lowercase segments are uppercased and rendered at a
/// reduced font size; everything else passes through unchanged.
/// </summary>
static class SmallCapsExpander
{
    // Word draws small-caps lowercase letters at roughly 80% of the run's font size.
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
            FontSizePoints = props.FontSizePoints * smallCapsScale,
            SmallCaps = false
        };
        var fullProps = props with {SmallCaps = false};

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
