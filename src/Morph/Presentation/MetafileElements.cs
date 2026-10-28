/// <summary>
/// A <see cref="MetafileDrawing"/> put into a box on a slide as the floats every painter already draws:
/// each path a <see cref="FloatingShapeElement"/> whose subpaths are its figures, each text a
/// <see cref="FloatingTextBoxElement"/> holding one paragraph, in the metafile's own order.
///
/// A metafile places text by a reference point and an alignment, where a text box has a top-left
/// corner, so each text is given a box wide enough never to wrap, aligned within it as the text is to
/// its point, and stood so its first line's baseline (or top, or bottom) falls on the point: the
/// baseline taken at nine tenths of the size below the top and the line at 1.15 of it, about where a
/// sans face puts them. A turned text (the escapement of an axis title) turns its box about its
/// centre, which is placed so the point stays where the metafile put it.
/// </summary>
static class MetafileElements
{
    const double ascent = 0.9;
    const double lineHeight = 1.15;

    public static IEnumerable<DocumentElement> For(MetafileDrawing drawing, double left, double top, double width, double height, uint ordinal)
    {
        (double X, double Y) Page((double X, double Y) unit) =>
            (left + unit.X * width, top + unit.Y * height);

        foreach (var item in drawing.Items)
        {
            switch (item)
            {
                case MetafilePath path when Shape(path, Page, height, ordinal) is { } shape:
                    yield return shape;
                    break;
                case MetafileText text:
                    yield return TextBox(text, Page((text.X, text.Y)), text.Size * height, ordinal);
                    break;
            }
        }
    }

    static FloatingShapeElement? Shape(MetafilePath path, Func<(double X, double Y), (double X, double Y)> page, double height, uint ordinal)
    {
        // A closed figure that is only stroked has its last edge drawn back to its first point.
        var figures = path.Figures
            .Select(figure => figure.Select(page).ToList())
            .Where(_ => _.Count >= 2)
            .Select(figure =>
            {
                if (path is
                    {
                        Closed: true,
                        FillHex: null
                    })
                {
                    return [.. figure, figure[0]];
                }

                return figure;
            })
            .ToList();
        if (figures.Count == 0)
        {
            return null;
        }

        // A straight rule has no breadth one way; give its box a sliver so its points can be placed in it.
        var minX = figures.Min(_ => _.Min(point => point.X));
        var minY = figures.Min(_ => _.Min(point => point.Y));
        var boxWidth = Math.Max(figures.Max(_ => _.Max(point => point.X)) - minX, 0.01);
        var boxHeight = Math.Max(figures.Max(_ => _.Max(point => point.Y)) - minY, 0.01);
        return new()
        {
            WidthPoints = boxWidth,
            HeightPoints = boxHeight,
            HorizontalPositionPoints = minX,
            VerticalPositionPoints = minY,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Paragraph,
            FillColorHex = path.FillHex,
            LineColorHex = path.LineHex,
            LineWidthPoints = path.LineHex == null ? null : Math.Max(0.25, path.LineWidth * height),
            Subpaths = [.. figures.Select(figure => (IReadOnlyList<(double X, double Y)>) [.. figure.Select(point => ((point.X - minX) / boxWidth, (point.Y - minY) / boxHeight))])],
            RelativeHeight = ordinal
        };
    }

    static FloatingTextBoxElement TextBox(MetafileText text, (double X, double Y) point, double size, uint ordinal)
    {
        size = Math.Max(size, 1);
        var boxWidth = Math.Max(text.Text.Length * size * 1.2, size * 2);
        var boxHeight = size * lineHeight;

        // The box's centre, in the text's own frame from the point: along the line, then down from it.
        var along = text.Horizontal switch
        {
            MetafileTextAlign.Centre => 0,
            MetafileTextAlign.Right => -boxWidth / 2,
            _ => boxWidth / 2
        };
        var down = text.Vertical switch
        {
            MetafileTextBaseline.Baseline => boxHeight / 2 - size * ascent,
            MetafileTextBaseline.Bottom => -boxHeight / 2,
            _ => boxHeight / 2
        };

        // Anticlockwise on the page, whose y runs down: the line runs (cos, -sin), and down from it (sin, cos).
        var radians = text.Degrees * Math.PI / 180;
        var centreX = point.X + along * Math.Cos(radians) + down * Math.Sin(radians);
        var centreY = point.Y - along * Math.Sin(radians) + down * Math.Cos(radians);
        var alignment = text.Horizontal switch
        {
            MetafileTextAlign.Centre => TextAlignment.Center,
            MetafileTextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        return new()
        {
            Content =
            [
                new ParagraphElement
                {
                    Runs =
                    [
                        new()
                        {
                            Text = text.Text,
                            Properties = new()
                            {
                                FontFamily = text.FontFamily,
                                FontSizePoints = size,
                                ColorHex = text.ColorHex,
                                Bold = text.Bold,
                                Italic = text.Italic,
                                Underline = text.Underline
                            }
                        }
                    ],
                    Properties = new()
                    {
                        Alignment = alignment,
                        LineSpacingMultiplier = 1
                    }
                }
            ],
            WidthPoints = boxWidth,
            HeightPoints = boxHeight,
            HorizontalPositionPoints = centreX - boxWidth / 2,
            VerticalPositionPoints = centreY - boxHeight / 2,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Paragraph,
            RotationDegrees = -text.Degrees,
            RelativeHeight = ordinal
        };
    }
}
