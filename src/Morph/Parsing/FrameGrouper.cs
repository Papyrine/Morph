/// <summary>
/// Post-processes a parsed element tree, pulling <c>w:framePr</c>-positioned paragraphs out of
/// normal flow into <see cref="PositionedFrameElement"/>s (Word's text-frame feature).
///
/// Framed paragraphs are collected document-wide and grouped by frame value: every paragraph
/// sharing the same <see cref="ParagraphFrame"/> forms one frame, even when the paragraphs are
/// scattered across separate table cells (the common template layout — each icon/label pair lives
/// in its own cell of a full-width layout table). They are lifted to the top level because a
/// frame's anchors (page / margin) resolve against the page, not the cell, mirroring how floating
/// tables are lifted. Lifted frames are appended after the in-flow content so they paint on top.
///
/// Within a group, Word collapses the authored paragraphs into the visual lines a frame shows:
/// empty paragraphs are dropped, and an icon-only paragraph (runs are only inline images, no
/// visible text) is merged onto the following text paragraph so the icon hangs beside its label.
/// </summary>
static class FrameGrouper
{
    // A page/margin-anchored frame with a small explicit y (under half an inch) is Word's trailing
    // "footer info block" (e.g. a right-aligned Location/Date/Time stack) that floats just above the
    // bottom margin. Larger y values mean an intentional upper-page placement (a centred sub-title),
    // which renders fine in normal flow — lifting those risks disturbing layout, so we leave them.
    // Keep in sync with PageRendererBase.frameBottomAnchorYThresholdPoints.
    const double bottomAnchorYThresholdPoints = 36;

    // Fallback icon→label gap when the style carries no hanging indent (0.3" ≈ Word's default).
    const double defaultIconGapPoints = 21.6;

    /// <summary>
    /// Whether a framed paragraph should be pulled out of flow into a floating frame. We only lift
    /// the bottom-anchored footer-block pattern: text-anchored frames (and upper-page placements)
    /// flow acceptably inline and are left alone to avoid regressing established layouts.
    /// </summary>
    static bool ShouldLift(ParagraphFrame frame)
    {
        if (frame.VerticalAnchor is not (VerticalAnchor.Page or VerticalAnchor.Margin))
        {
            return false;
        }

        return frame.VerticalAlignment == FrameVerticalAlignment.Bottom ||
               (frame.VerticalAlignment is FrameVerticalAlignment.None or FrameVerticalAlignment.Inline &&
                frame.YPoints < bottomAnchorYThresholdPoints);
    }

    public static List<DocumentElement> Group(List<DocumentElement> elements)
    {
        // Collect every framed paragraph, in document order, keyed by its frame. Preserve first-seen
        // frame order so the lifted frames stay deterministic.
        var groups = new List<(ParagraphFrame Frame, List<ParagraphElement> Paragraphs)>();
        var anyFramed = CollectFramed(elements, groups);
        if (!anyFramed)
        {
            return elements;
        }

        var stripped = Strip(elements);

        foreach (var (frame, paragraphs) in groups)
        {
            var content = MergeFramedParagraphs(paragraphs);
            if (content.Count > 0)
            {
                stripped.Add(BuildFrameElement(frame, content));
            }
        }

        return stripped;
    }

    /// <summary>
    /// Walks the tree (body elements and table cells), appending each framed paragraph to the group
    /// matching its frame value. Returns true when any framed paragraph was found.
    /// </summary>
    static bool CollectFramed(IReadOnlyList<DocumentElement> elements, List<(ParagraphFrame Frame, List<ParagraphElement> Paragraphs)> groups)
    {
        var found = false;
        foreach (var element in elements)
        {
            switch (element)
            {
                case ParagraphElement {Properties.Frame: { } frame} paragraph when ShouldLift(frame):
                    found = true;
                    GroupFor(groups, frame).Add(paragraph);
                    break;
                case TableElement table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            found |= CollectFramed(cell.Content, groups);
                        }
                    }

                    break;
            }
        }

        return found;
    }

    static List<ParagraphElement> GroupFor(List<(ParagraphFrame Frame, List<ParagraphElement> Paragraphs)> groups, ParagraphFrame frame)
    {
        foreach (var group in groups)
        {
            if (group.Frame == frame)
            {
                return group.Paragraphs;
            }
        }

        var paragraphs = new List<ParagraphElement>();
        groups.Add((frame, paragraphs));
        return paragraphs;
    }

    /// <summary>Rebuilds the tree with every framed paragraph removed (they become lifted frames).</summary>
    static List<DocumentElement> Strip(IReadOnlyList<DocumentElement> elements)
    {
        var result = new List<DocumentElement>(elements.Count);
        foreach (var element in elements)
        {
            switch (element)
            {
                case ParagraphElement {Properties.Frame: { } frame} when ShouldLift(frame):
                    // Dropped from flow; re-emitted as a lifted PositionedFrameElement.
                    break;
                case TableElement table:
                    result.Add(StripTable(table));
                    break;
                default:
                    result.Add(element);
                    break;
            }
        }

        return result;
    }

    static TableElement StripTable(TableElement table)
    {
        var rows = new List<TableRow>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var cells = new List<TableCell>(row.Cells.Count);
            foreach (var cell in row.Cells)
            {
                cells.Add(new()
                {
                    Content = Strip(cell.Content),
                    Properties = cell.Properties
                });
            }

            rows.Add(new()
            {
                Cells = cells,
                HeightPoints = row.HeightPoints,
                IsExactHeight = row.IsExactHeight,
                IsHeader = row.IsHeader
            });
        }

        return new()
        {
            Rows = rows,
            Properties = table.Properties
        };
    }

    /// <summary>
    /// Collapses a group of same-frame paragraphs into the paragraphs the frame actually renders:
    /// empty paragraphs are dropped and each icon-only paragraph is folded onto the text paragraph
    /// that follows it (its image runs prepended).
    /// </summary>
    static List<ParagraphElement> MergeFramedParagraphs(List<ParagraphElement> paragraphs)
    {
        var merged = new List<ParagraphElement>(paragraphs.Count);
        var pendingIconRuns = new List<Run>();

        foreach (var paragraph in paragraphs)
        {
            if (IsEmpty(paragraph))
            {
                continue;
            }

            if (IsIconOnly(paragraph))
            {
                pendingIconRuns.AddRange(paragraph.Runs);
                continue;
            }

            if (pendingIconRuns.Count > 0)
            {
                merged.Add(FoldIconOntoLabel(pendingIconRuns, paragraph));
                pendingIconRuns.Clear();
                continue;
            }

            merged.Add(paragraph);
        }

        // A trailing icon-only paragraph with no text to attach to still renders its icon.
        if (pendingIconRuns.Count > 0)
        {
            merged.Add(new()
            {
                Runs = pendingIconRuns,
                Properties = paragraphs[0].Properties
            });
        }

        return merged;
    }

    /// <summary>
    /// Builds the merged line for an icon paragraph folded onto its following label paragraph,
    /// reproducing Word's ~0.3" icon→label gap (e.g. <c>🏠      Location:</c>). Word's gap comes
    /// from the style's hanging indent (the icon hangs, a tab advances the label to the indent
    /// stop). The frame is rendered out of flow at an arbitrary x where neither backend's indent /
    /// tab-stop machinery lines up cleanly, so the gap is reproduced directly: a Left tab stop is
    /// placed a fixed gap past the icon and a tab snaps the label there.
    /// </summary>
    static ParagraphElement FoldIconOntoLabel(List<Run> iconRuns, ParagraphElement label)
    {
        var runs = new List<Run>(iconRuns.Count + label.Runs.Count + 1);
        runs.AddRange(iconRuns);
        runs.Add(new() {Text = "\t", IsTab = true});
        runs.AddRange(label.Runs);

        // The label sits at the hanging-indent width from the line start (Word's gap), measured from
        // the paragraph's left edge (0 here — the direct ind override zeroed left). Add the matching
        // Left tab stop; the renderers snap the tab to it relative to the line start.
        var properties = label.Properties;
        var gap = properties.HangingIndentPoints > 0 ? properties.HangingIndentPoints : defaultIconGapPoints;
        var tabStops = new List<TabStop>(properties.TabStops) {new() {PositionPoints = gap}};
        tabStops.Sort((leftStop, rightStop) => leftStop.PositionPoints.CompareTo(rightStop.PositionPoints));

        return new()
        {
            Runs = runs,
            Properties = properties with
            {
                LeftIndentPoints = 0,
                HangingIndentPoints = 0,
                FirstLineIndentPoints = 0,
                TabStops = tabStops
            },
            IsAnchorOnlyMark = label.IsAnchorOnlyMark
        };
    }

    static PositionedFrameElement BuildFrameElement(ParagraphFrame frame, IReadOnlyList<DocumentElement> content) =>
        new()
        {
            Content = content,
            HorizontalAnchor = frame.HorizontalAnchor,
            VerticalAnchor = frame.VerticalAnchor,
            HorizontalAlignment = frame.HorizontalAlignment,
            VerticalAlignment = frame.VerticalAlignment,
            XPoints = frame.XPoints,
            YPoints = frame.YPoints,
            WidthPoints = frame.WidthPoints,
            HeightPoints = frame.HeightPoints
        };

    // A paragraph contributes nothing visual: no runs, or every run is whitespace text with no image.
    static bool IsEmpty(ParagraphElement paragraph)
    {
        foreach (var run in paragraph.Runs)
        {
            if (run.InlineImageData != null ||
                run.InlineImageRasterFallbackData != null ||
                run.InlineShapeGroup != null ||
                !string.IsNullOrWhiteSpace(run.Text))
            {
                return false;
            }
        }

        return true;
    }

    // A paragraph whose only visible content is inline image(s) — no text glyphs. Such a paragraph
    // is Word's "icon" line that pairs onto the following label paragraph.
    static bool IsIconOnly(ParagraphElement paragraph)
    {
        var hasImage = false;
        foreach (var run in paragraph.Runs)
        {
            if (run.InlineImageData != null ||
                run.InlineImageRasterFallbackData != null ||
                run.InlineShapeGroup != null)
            {
                hasImage = true;
            }
            else if (!string.IsNullOrWhiteSpace(run.Text))
            {
                return false;
            }
        }

        return hasImage;
    }
}
