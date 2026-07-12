/// <summary>
/// Model-traversal helpers shared by the text exporters (<see cref="HtmlExporter"/> and
/// <see cref="MarkdownExporter"/>). These map the <see cref="ParsedDocument"/> tree onto the
/// semantic concepts that HTML/Markdown need — headings, ordered/unordered lists, list nesting —
/// independent of any rendering backend.
/// </summary>
static class DocumentExportHelpers
{
    /// <summary>
    /// Returns the heading level (1-6) to export a paragraph at, or null when it is not a heading.
    /// <c>HeadingN</c> maps to level N (clamped to 6 — HTML has no <c>h7</c>). Word's <c>Title</c>
    /// and <c>Subtitle</c> styles sit above the heading scale visually, so they map to levels 1
    /// and 2 — otherwise the document's own title exports as an ordinary paragraph while
    /// template-styled section labels below it become headings.
    /// </summary>
    public static int? TryGetHeadingLevel(ParagraphProperties properties)
    {
        if (properties.StyleId is not {Length: > 0} styleId)
        {
            return null;
        }

        var compact = styleId.Replace(" ", "");
        if (compact.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(compact, out var level) &&
            level >= 1)
        {
            return Math.Min(level, 6);
        }

        if (compact.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (compact.Equals("Subtitle", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return null;
    }

    /// <summary>
    /// Whether a paragraph uses Word's built-in <c>Quote</c> or <c>Intense Quote</c> style — the
    /// paragraphs the exporters gather into a block quote. Custom template quote styles
    /// (e.g. "Quotecentred") are deliberately not matched.
    /// </summary>
    public static bool IsQuote(ParagraphProperties properties)
    {
        if (properties.StyleId is not {Length: > 0} styleId)
        {
            return false;
        }

        var compact = styleId.Replace(" ", "");
        return compact.Equals("Quote", StringComparison.OrdinalIgnoreCase) ||
               compact.Equals("IntenseQuote", StringComparison.OrdinalIgnoreCase);
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
    /// Groups a flat run of consecutive list paragraphs into a nesting forest. Nesting depth
    /// compares the multilevel-list level (<c>w:ilvl</c>) first when both items carry one — the
    /// document's real list structure, robust against styles that flatten the visual indents
    /// (Word's ListParagraph style sets one indent for every level). Equal or absent levels fall
    /// back to the paragraph's resolved <see cref="ParagraphProperties.LeftIndentPoints"/> (the
    /// full direct &gt; style &gt; numbering cascade) — this nests per-level indents supplied by
    /// direct formatting, and separate one-level list styles (ListBullet / ListBullet2) whose
    /// nesting exists only visually.
    /// </summary>
    public static List<ListNode> BuildListForest(IReadOnlyList<ParagraphElement> items)
    {
        var roots = new List<ListNode>();
        var ancestors = new Stack<ListNode>();
        foreach (var paragraph in items)
        {
            var numbering = paragraph.Properties.Numbering!;
            var node = new ListNode
            {
                Paragraph = paragraph,
                Level = numbering.Level,
                Indent = paragraph.Properties.LeftIndentPoints,
                Ordered = IsOrderedList(numbering)
            };

            while (ancestors.TryPeek(out var top) &&
                   !IsShallower(top, node))
            {
                ancestors.Pop();
            }

            if (ancestors.TryPeek(out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }

            ancestors.Push(node);
        }

        return roots;
    }

    // Whether ancestor sits strictly shallower than node — a valid parent for it. Levels decide
    // when they differ; ties (and level-less items) fall through to the visual indent.
    static bool IsShallower(ListNode ancestor, ListNode node)
    {
        if (ancestor.Level is {} ancestorLevel &&
            node.Level is {} nodeLevel &&
            ancestorLevel != nodeLevel)
        {
            return ancestorLevel < nodeLevel;
        }

        const double tolerance = 1;
        return ancestor.Indent < node.Indent - tolerance;
    }

    /// <summary>
    /// Whether a paragraph carries no rendered content (no runs, or only whitespace and no inline
    /// image). Empty paragraphs are dropped by the exporters to avoid empty blocks.
    /// </summary>
    /// <param name="paragraph">The paragraph to test.</param>
    /// <param name="vectorShapesRender">
    /// Whether an inline shape group counts as content on its own. HTML draws one as an inline
    /// <c>&lt;svg&gt;</c>, so any group is content. Markdown has no vector primitives and emits only
    /// a group's pictures, so a group built purely of shapes — a divider rule, an arrow glyph —
    /// leaves its paragraph blank.
    /// </param>
    public static bool IsBlank(ParagraphElement paragraph, bool vectorShapesRender = true)
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

            if (run.InlineShapeGroup is {} shapeGroup &&
                (vectorShapesRender ||
                 shapeGroup.Shapes.Any(_ => _.ImageData != null)))
            {
                return false;
            }

            // A footnote / endnote reference is content even though its marker run has no text.
            if (run.FootnoteReferenceId != null ||
                run.EndnoteReferenceId != null)
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
    /// Drops hidden (<c>w:vanish</c>) runs, then merges adjacent runs that share identical
    /// export-relevant formatting (and the same hyperlink target) into a single run with
    /// concatenated text. Word fragments runs at proofing, revision, and rsid boundaries,
    /// producing sequences like <c>"Sep","tember"</c> or three consecutive identically-bold runs.
    /// Left un-merged these bloat the HTML with a redundant tag pair per fragment and — worse —
    /// corrupt Markdown emphasis, where <c>*a*</c> immediately followed by <c>*b*</c> yields
    /// <c>*a**b*</c> (a stray <c>**</c> the parser reads as a bold toggle) instead of <c>*ab*</c>.
    /// Filtering hidden runs here (rather than in the emit loops) also lets two visible runs
    /// separated only by hidden text merge, and keeps hidden text out of hyperlink groups.
    /// Tab, inline-image, and inline-shape runs are never merged — they are atomic and carry
    /// payload beyond <see cref="Run.Text"/>.
    /// </summary>
    public static List<Run> CoalesceRuns(IReadOnlyList<Run> runs)
    {
        var merged = new List<Run>(runs.Count);
        var index = 0;
        while (index < runs.Count)
        {
            var run = runs[index];
            index++;
            if (run.Properties.Hidden)
            {
                continue;
            }

            // Absorb the whole mergeable group in one forward scan, building the combined text
            // once — re-copying the accumulated prefix per fragment made a k-fragment group cost
            // O(k²) in text length. CanMerge only inspects flags/properties/link target, so
            // comparing against the group's first run is equivalent to comparing against the
            // accumulated run.
            StringBuilder? combined = null;
            while (index < runs.Count)
            {
                var next = runs[index];
                if (next.Properties.Hidden)
                {
                    index++;
                    continue;
                }

                if (!CanMerge(run, next))
                {
                    break;
                }

                combined ??= new(run.Text);
                combined.Append(next.Text);
                index++;
            }

            if (combined == null)
            {
                merged.Add(run);
            }
            else
            {
                merged.Add(
                    new()
                    {
                        Text = combined.ToString(),
                        Properties = run.Properties,
                        HyperlinkUrl = run.HyperlinkUrl
                    });
            }
        }

        return merged;
    }

    static bool CanMerge(Run left, Run right)
    {
        if (left.IsTab || right.IsTab ||
            left.InlineImageData != null || right.InlineImageData != null ||
            left.InlineShapeGroup != null || right.InlineShapeGroup != null ||
            left.FootnoteReferenceId != null || right.FootnoteReferenceId != null ||
            left.EndnoteReferenceId != null || right.EndnoteReferenceId != null)
        {
            return false;
        }

        return left.HyperlinkUrl == right.HyperlinkUrl &&
               SameFormatting(left.Properties, right.Properties);
    }

    // Compares only the run properties the text exporters actually emit (hidden runs are dropped
    // before merging, so Hidden needs no comparison). Two runs differing solely in an unexported
    // field (kerning, rsid-style metadata) render identically, so merging them is safe and keeps
    // the merged run's properties for the survivor.
    static bool SameFormatting(RunProperties left, RunProperties right) =>
        left.Bold == right.Bold &&
        left.Italic == right.Italic &&
        left.Underline == right.Underline &&
        left.Strikethrough == right.Strikethrough &&
        left.AllCaps == right.AllCaps &&
        left.SmallCaps == right.SmallCaps &&
        left.VerticalAlignment == right.VerticalAlignment &&
        left.ColorHex == right.ColorHex &&
        left.BackgroundColorHex == right.BackgroundColorHex &&
        left.FontFamily == right.FontFamily &&
        Math.Abs(left.FontSizePoints - right.FontSizePoints) < 0.01 &&
        Math.Abs(left.CharacterSpacingPoints - right.CharacterSpacingPoints) < 0.01;

    /// <summary>
    /// The ordinal an ordered list starts at, recovered from the first item's rendered marker
    /// text — "10." → 10, "(3)" → 3, "2.4." (multilevel) → 4. The model keeps only the rendered
    /// marker, so this is how a <c>w:startOverride</c> (or a list continuing an earlier one)
    /// survives into <c>&lt;ol start&gt;</c> / a Markdown ordinal. The last integer wins because
    /// multilevel markers put the item's own counter last. Null for non-numeric markers ("a)",
    /// "iv.") — those fall back to 1, matching the previous behaviour.
    /// </summary>
    public static int? ListStartNumber(NumberingInfo numbering)
    {
        var text = numbering.Text;
        int? result = null;
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                continue;
            }

            var start = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }

            // Cap the digit run so a pathological marker can't overflow; 6 digits is far beyond
            // any real list ordinal.
            if (index - start <= 6)
            {
                result = int.Parse(text.AsSpan(start, index - start), CultureInfo.InvariantCulture);
            }
        }

        return result;
    }

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