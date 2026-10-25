/// <summary>
/// Draws a document's comments onto its laid-out pages as Word prints them with All Markup
/// (<see cref="ExportOptions.ShowComments"/>): each page is laid out as it would be without them and
/// then shrunk, with a grey pane beside it holding a balloon for each comment the page anchors.
///
/// <para>A step after layout, because nothing the page lays out moves: Word paginates a commented
/// document exactly as it does a clean one (a 26-paragraph probe broke after the same line both ways)
/// and prints the page and its pane shrunk together to the paper's width.</para>
///
/// <para>Every number here was read off Word's XPS for the <c>_probe_comment_*</c> fixtures (Word 365,
/// 2026-10-25), which varied the right margin (1in, 3in), the left margin (1in, 3in), the page width
/// (8.5in, 6in) and height (11in, 7in), and stacked four comments over two pages, one of them wrapping
/// over five lines and one of two paragraphs. The measurements are in <c>docs/word-features.md</c>,
/// Comments.</para>
/// </summary>
static class CommentMarkup
{
    // The pane starts this far right of the right margin and is this wide, at every page width and
    // margin probed (259.2-259.4 read; 3.6in).
    const float paneGap = 7.3f;
    const float paneWidth = 259.2f;

    // A balloon stands in from the pane's left and right edges, and its text from the balloon's. The
    // text's right inset is bracketed by the wraps of the probe's five-line comment, whose measure
    // took a line of 224.02pt and refused one of 225.53pt.
    const float balloonLeftInset = 23.4f;
    const float balloonRightInset = 3f;
    const float textLeftInset = 4.8f;
    const float textRightInset = 3.2f;
    const float cornerRadius = 3f;

    // A balloon's first baseline sits this far below its top and its bottom this far below its last
    // baseline; a balloon that would overlap the one above it is pushed down to this gap under it.
    const float firstBaseline = 12f;
    const float bottomPadding = 3.6f;
    const float stackGap = 2.4f;

    // The connector's diagonal leaves the line this far short of the pane and meets the balloon this
    // far below its top.
    const float connectorBend = 3f;
    const float connectorDrop = 6f;

    const float strokeWidth = 0.6f;
    const float textSize = 9f;
    const string paneColor = "F2F2F2";
    const string fillColor = "F8DCDD";
    const string strokeColor = "D13438";

    public static LaidOutDocument Apply(LaidOutDocument laidOut, ParsedDocument document, CanonicalParagraphMeasurer measurer)
    {
        var comments = document.Comments
            .Where(_ => _.Number != null)
            .ToDictionary(_ => _.Id, StringComparer.Ordinal);
        if (comments.Count == 0)
        {
            return laidOut;
        }

        return new([.. laidOut.Pages.Select(_ => Page(_, comments, measurer))]);
    }

    static LaidOutPage Page(LaidOutPage page, Dictionary<string, Comment> comments, CanonicalParagraphMeasurer measurer)
    {
        var settings = page.Settings;
        var pageWidth = (float) settings.WidthPoints;
        var pageHeight = (float) settings.HeightPoints;
        var paneLeft = pageWidth - (float) settings.MarginRight + paneGap;
        var scale = pageWidth / (paneLeft + paneWidth);

        var ranges = new List<Range>();
        Collect(page.Items, ranges);

        var items = new List<PlacedItem>
        {
            new PlacedShading(paneLeft, 0, paneWidth, pageHeight, paneColor)
        };
        foreach (var range in ranges)
        {
            items.Add(new PlacedShading(range.Left, range.Top, range.Right - range.Left, range.Bottom - range.Top, fillColor));
        }

        items.AddRange(page.Items);
        foreach (var range in ranges)
        {
            items.Add(Bracket(range.Left, range.Top, range.Bottom, 1));
            items.Add(Bracket(range.Right, range.Top, range.Bottom, -1));
        }

        // A balloon for each comment, in the order their ranges start down the page, at the top of the
        // line its range starts on or under the balloon before it.
        var balloonLeft = paneLeft + balloonLeftInset;
        var balloonWidth = paneWidth - balloonLeftInset - balloonRightInset;
        var below = 0f;
        foreach (var anchor in ranges
                     .GroupBy(_ => _.CommentId)
                     .Select(_ => _.First())
                     .OrderBy(_ => _.Top))
        {
            if (!comments.TryGetValue(anchor.CommentId, out var comment))
            {
                continue;
            }

            var top = Math.Max(anchor.Top, below);
            var lines = Text(comment, balloonLeft + textLeftInset, top, balloonWidth - textLeftInset - textRightInset, measurer);
            var bottom = lines[^1].Baseline + bottomPadding;
            items.Add(Shape(RoundedRectangle(balloonLeft, top, balloonWidth, bottom - top), fillColor, null));
            var bend = (paneLeft - connectorBend, anchor.Bottom - strokeWidth);
            items.Add(Shape([[(anchor.Right, anchor.Bottom - strokeWidth), bend], [bend, (balloonLeft, top + connectorDrop)]], null, [1, 1]));
            items.AddRange(lines);
            below = bottom + stackGap;
        }

        var height = pageHeight * scale;
        return page with
        {
            Items = [new PlacedScaledGroup(0, (pageHeight - height) / 2, pageWidth, height, items, scale)]
        };
    }

    // Where one comment's range runs along one line: its first run's left to its last's right.
    sealed record Range(string CommentId, float Left, float Right, float Top, float Bottom);

    static void Collect(IEnumerable<PlacedItem> items, List<Range> ranges)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case PlacedLine line:
                    Collect(line, ranges);
                    break;
                case PlacedTableRow row:
                    foreach (var cell in row.Cells)
                    {
                        Collect(cell.Content, ranges);
                    }

                    break;
                case PlacedClipGroup clip:
                    Collect(clip.Items, ranges);
                    break;
            }
        }
    }

    static void Collect(PlacedLine line, List<Range> ranges)
    {
        Range? open = null;
        foreach (var run in line.Runs)
        {
            var id = run.Properties.CommentId;
            if (open != null &&
                id == open.CommentId)
            {
                open = open with
                {
                    Right = run.X + run.Width
                };
                continue;
            }

            if (open != null)
            {
                ranges.Add(open);
                open = null;
            }

            if (id != null)
            {
                open = new(id, run.X, run.X + run.Width, line.Y, line.Y + line.Height);
            }
        }

        if (open != null)
        {
            ranges.Add(open);
        }
    }

    // The balloon's lines: "Commented [R1]: " in bold and then the comment's first paragraph, with each
    // paragraph after it a paragraph of its own, set in the comment's face at 9pt.
    static List<PlacedLine> Text(Comment comment, float left, float top, float width, CanonicalParagraphMeasurer measurer)
    {
        var properties = new RunProperties
        {
            FontFamily = comment.FontFamily ?? DefaultFontSettings.DefaultFont,
            FontSizePoints = textSize
        };
        var label = $"Commented [{comment.Initials}{comment.Number}]: ";
        var paragraphs = comment.Paragraphs.Count > 0 ? comment.Paragraphs : [comment.Text];

        var result = new List<PlacedLine>();
        var baseline = top + firstBaseline;
        for (var index = 0; index < paragraphs.Count; index++)
        {
            List<Run> runs = [new() {Text = paragraphs[index], Properties = properties}];
            if (index == 0)
            {
                runs.Insert(0, new() {Text = label, Properties = properties with {Bold = true}});
            }

            var paragraph = new ParagraphElement
            {
                Runs = runs,
                Properties = new()
                {
                    LineSpacingMultiplier = 1
                }
            };
            var lines = measurer.LayoutLineContents(paragraph, width);
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                if (result.Count > 0)
                {
                    baseline += line.Height;
                }

                var placed = line.Runs
                    .Select(_ => new PlacedRun(left + _.X, _.Width, _.Text, _.Properties))
                    .ToList();
                result.Add(new(left, baseline - line.Ascent, line.Width, line.Height, baseline, paragraph, lineIndex, placed, []));
            }
        }

        return result;
    }

    // The thin red bracket Word draws at each end of a commented span, opening toward the text
    // (direction 1 at its left, -1 at its right).
    static PlacedShape Bracket(float x, float top, float bottom, int direction)
    {
        var tick = 0.6f * direction;
        (double X, double Y) upper = (x, top + 2.8f);
        (double X, double Y) lower = (x, bottom - 0.8f);
        return Shape([[(x + tick, top + 1.6f), upper], [upper, lower], [lower, (x + tick, bottom + 0.4f)]], null, null);
    }

    static IReadOnlyList<IReadOnlyList<(double X, double Y)>> RoundedRectangle(float left, float top, float width, float height)
    {
        var points = new List<(double X, double Y)>();
        void Corner(double centerX, double centerY, double startDegrees)
        {
            for (var step = 0; step <= 6; step++)
            {
                var radians = (startDegrees + step * 15) * Math.PI / 180;
                points.Add((centerX + cornerRadius * Math.Cos(radians), centerY + cornerRadius * Math.Sin(radians)));
            }
        }

        Corner(left + width - cornerRadius, top + cornerRadius, 270);
        Corner(left + width - cornerRadius, top + height - cornerRadius, 0);
        Corner(left + cornerRadius, top + height - cornerRadius, 90);
        Corner(left + cornerRadius, top + cornerRadius, 180);
        return [points];
    }

    // A shape in the markup's red stroke, from contours in points on the page, boxed by them. A rule
    // has no height, so the box is never thinner than the stroke. A stroked contour is closed, so an
    // open line is drawn as its segments, two points each.
    static PlacedShape Shape(IReadOnlyList<IReadOnlyList<(double X, double Y)>> contours, string? fill, IReadOnlyList<double>? dash)
    {
        var all = contours.SelectMany(_ => _).ToList();
        var left = all.Min(_ => _.X);
        var top = all.Min(_ => _.Y);
        var width = Math.Max(all.Max(_ => _.X) - left, strokeWidth);
        var height = Math.Max(all.Max(_ => _.Y) - top, strokeWidth);
        return new(
            (float) left,
            (float) top,
            (float) width,
            (float) height,
            new()
            {
                WidthPoints = width,
                HeightPoints = height,
                FillColorHex = fill,
                LineColorHex = strokeColor,
                LineWidthPoints = strokeWidth,
                LineDashPattern = dash,
                Subpaths = [.. contours.Select(_ => (IReadOnlyList<(double X, double Y)>) [.. _.Select(point => ((point.X - left) / width, (point.Y - top) / height))])]
            });
    }
}
