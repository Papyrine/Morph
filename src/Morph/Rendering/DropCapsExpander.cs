/// <summary>
/// Inserts a scaled-up "drop cap" sub-run at the start of paragraphs that request one.
/// The dropped letter is rendered at roughly <c>DropCapLines × line-height</c> points so it
/// visually spans the requested number of lines.
/// </summary>
/// <remarks>
/// Word's drop cap also wraps the following body text into the column to the right of
/// the cap for those N lines — the existing line layout doesn't support arbitrary
/// content-region cutouts, so we approximate by rendering the cap on its own line with
/// the rest of the paragraph starting underneath. This matches Word visually for short
/// paragraphs but shifts long paragraphs down by the cap's height.
/// </remarks>
static class DropCapsExpander
{
    public static IReadOnlyList<Run> Expand(IReadOnlyList<Run> runs, ParagraphProperties props)
    {
        if (props.DropCap == DropCapPosition.None ||
            props.DropCapLines <= 1 ||
            runs.Count == 0)
        {
            return runs;
        }

        // Find the first run that has visible text content.
        var firstIndex = -1;
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run is {IsTab: false, InlineImageData: null} && !string.IsNullOrEmpty(run.Text))
            {
                firstIndex = i;
                break;
            }
        }

        if (firstIndex < 0)
        {
            return runs;
        }

        var firstRun = runs[firstIndex];
        var first = firstRun.Text[0];
        if (firstRun.Text.Length < 1)
        {
            return runs;
        }

        // Cap font size to DropCapLines × current size; matches Word's "drop cap, lines: N" preset
        // closely enough for the common 2–4 line range.
        var capFontSize = firstRun.Properties.FontSizePoints * props.DropCapLines;
        var capProps = firstRun.Properties with {FontSizePoints = capFontSize};

        var expanded = new List<Run>(runs.Count + 2);
        for (var i = 0; i < firstIndex; i++)
        {
            expanded.Add(runs[i]);
        }

        expanded.Add(new()
        {
            Text = first.ToString(),
            Properties = capProps
        });

        // Force a line break after the cap so the rest of the paragraph starts on a new line.
        // Without this the body text would sit beside the cap on the cap's giant line, with no
        // wrap-around — visually worse than letting the cap stand alone.
        expanded.Add(new() {Text = "\n", Properties = firstRun.Properties});

        // Remaining text of the first run (everything after the cap character).
        if (firstRun.Text.Length > 1)
        {
            expanded.Add(new()
            {
                Text = firstRun.Text[1..],
                Properties = firstRun.Properties
            });
        }

        for (var i = firstIndex + 1; i < runs.Count; i++)
        {
            expanded.Add(runs[i]);
        }

        return expanded;
    }
}
