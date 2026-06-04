namespace Morph;

/// <summary>
/// Model-traversal helpers shared by the text exporters (<see cref="HtmlExporter"/> and
/// <see cref="MarkdownExporter"/>). These map the <see cref="ParsedDocument"/> tree onto the
/// semantic concepts that HTML/Markdown need — headings, ordered/unordered lists, list nesting —
/// independent of any rendering backend.
/// </summary>
static class DocumentExportHelpers
{
    /// <summary>
    /// Returns the heading level (1-6) for a paragraph styled as <c>HeadingN</c>, or null when the
    /// paragraph is not a heading. Levels beyond 6 are clamped to 6 (HTML has no <c>h7</c>).
    /// </summary>
    public static int? TryGetHeadingLevel(ParagraphProperties properties)
    {
        if (properties.StyleId is not {Length: > 0} styleId)
        {
            return null;
        }

        var compact = styleId.Replace(" ", "");
        if (compact.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(compact.AsSpan("Heading".Length), out var level) &&
            level >= 1)
        {
            return Math.Min(level, 6);
        }

        return null;
    }

    /// <summary>
    /// Decides whether a list paragraph belongs to an ordered (numbered) or unordered (bulleted)
    /// list. The model only retains the rendered marker text (e.g. "1.", "a)", "•"), so the kind is
    /// inferred: a marker containing a digit, or a letter followed by a "." / ")" separator, is
    /// ordered; bare glyphs ("•", "-", "o") are bullets.
    /// </summary>
    public static bool IsOrderedList(NumberingInfo numbering)
    {
        var text = numbering.Text;
        var hasDigit = false;
        var hasLetter = false;
        var hasSeparator = false;
        foreach (var character in text)
        {
            if (char.IsDigit(character))
            {
                hasDigit = true;
            }
            else if (char.IsLetter(character))
            {
                hasLetter = true;
            }
            else if (character is '.' or ')')
            {
                hasSeparator = true;
            }
        }

        return hasDigit || (hasLetter && hasSeparator);
    }

    /// <summary>
    /// Groups a flat run of consecutive list paragraphs into a nesting forest keyed off each
    /// paragraph's left indent. Deeper indents become children of the nearest shallower item.
    /// </summary>
    public static List<ListNode> BuildListForest(IReadOnlyList<ParagraphElement> items)
    {
        const double tolerance = 1;
        var roots = new List<ListNode>();
        var ancestors = new Stack<ListNode>();
        foreach (var paragraph in items)
        {
            var numbering = paragraph.Properties.Numbering!;
            var node = new ListNode
            {
                Paragraph = paragraph,
                Indent = numbering.IndentPoints,
                Ordered = IsOrderedList(numbering)
            };

            while (ancestors.Count > 0 &&
                   ancestors.Peek().Indent >= node.Indent - tolerance)
            {
                ancestors.Pop();
            }

            if (ancestors.Count > 0)
            {
                ancestors.Peek().Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }

            ancestors.Push(node);
        }

        return roots;
    }

    /// <summary>
    /// Whether a paragraph carries no rendered content (no runs, or only whitespace and no inline
    /// image). Empty paragraphs are dropped by the exporters to avoid empty blocks.
    /// </summary>
    public static bool IsBlank(ParagraphElement paragraph)
    {
        foreach (var run in paragraph.Runs)
        {
            if (run.Properties.Hidden)
            {
                continue;
            }

            if (run.InlineImageData != null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(run.Text))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Merges adjacent runs that share identical export-relevant formatting (and the same
    /// hyperlink target) into a single run with concatenated text. Word fragments runs at proofing,
    /// revision, and rsid boundaries, producing sequences like <c>"Sep","tember"</c> or three
    /// consecutive identically-bold runs. Left un-merged these bloat the HTML with a redundant tag
    /// pair per fragment and — worse — corrupt Markdown emphasis, where <c>*a*</c> immediately
    /// followed by <c>*b*</c> yields <c>*a**b*</c> (a stray <c>**</c> the parser reads as a bold
    /// toggle) instead of <c>*ab*</c>. Tab, inline-image, and inline-shape runs are never merged —
    /// they are atomic and carry payload beyond <see cref="Run.Text"/>.
    /// </summary>
    public static List<Run> CoalesceRuns(IReadOnlyList<Run> runs)
    {
        var merged = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            if (merged.Count > 0 && CanMerge(merged[^1], run))
            {
                var previous = merged[^1];
                merged[^1] = new()
                {
                    Text = previous.Text + run.Text,
                    Properties = previous.Properties,
                    HyperlinkUrl = previous.HyperlinkUrl
                };
            }
            else
            {
                merged.Add(run);
            }
        }

        return merged;
    }

    static bool CanMerge(Run left, Run right)
    {
        if (left.IsTab || right.IsTab ||
            left.InlineImageData != null || right.InlineImageData != null ||
            left.InlineShapeGroup != null || right.InlineShapeGroup != null)
        {
            return false;
        }

        return left.HyperlinkUrl == right.HyperlinkUrl &&
               SameFormatting(left.Properties, right.Properties);
    }

    // Compares only the run properties the text exporters actually emit. Two runs differing solely
    // in an unexported field (character spacing, kerning, rsid-style metadata) render identically,
    // so merging them is safe and keeps the merged run's properties for the survivor.
    static bool SameFormatting(RunProperties left, RunProperties right) =>
        left.Bold == right.Bold &&
        left.Italic == right.Italic &&
        left.Underline == right.Underline &&
        left.Strikethrough == right.Strikethrough &&
        left.AllCaps == right.AllCaps &&
        left.SmallCaps == right.SmallCaps &&
        left.Hidden == right.Hidden &&
        left.VerticalAlignment == right.VerticalAlignment &&
        left.ColorHex == right.ColorHex &&
        left.BackgroundColorHex == right.BackgroundColorHex &&
        left.FontFamily == right.FontFamily &&
        Math.Abs(left.FontSizePoints - right.FontSizePoints) < 0.01;

    /// <summary>
    /// Normalizes a model colour (a 6-digit hex string without "#", or "auto") to a CSS colour, or
    /// null when the value is absent / automatic.
    /// </summary>
    public static string? NormalizeColor(string? hex)
    {
        if (hex is not {Length: 6})
        {
            return null;
        }

        foreach (var character in hex)
        {
            if (!Uri.IsHexDigit(character))
            {
                return null;
            }
        }

        return $"#{hex.ToLowerInvariant()}";
    }
}

/// <summary>A node in a list-nesting forest produced by <see cref="DocumentExportHelpers.BuildListForest"/>.</summary>
sealed class ListNode
{
    public required ParagraphElement Paragraph { get; init; }
    public double Indent { get; init; }
    public bool Ordered { get; init; }
    public List<ListNode> Children { get; } = [];
}
