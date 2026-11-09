/// <summary>
/// Reads a WordprocessingML border edge (<c>w:top</c>, <c>w:bdr</c>, …) and a shading fill into
/// the model, resolving theme colours through the document's <see cref="ThemeColors"/>.
/// </summary>
static class BorderParser
{
    public static BorderEdge ParseBorderEdge(BorderType? border, ThemeColors? themeColors)
    {
        if (border == null)
        {
            return BorderEdge.None;
        }

        // none and nil both draw nothing; none still holds its w:space open (BorderEdge.HoldsSpace)
        if (border.Val?.Value == BorderValues.None)
        {
            return BorderEdge.None with
            {
                HoldsSpace = true
            };
        }

        if (border.Val?.Value == BorderValues.Nil)
        {
            return BorderEdge.None;
        }

        // If no Val specified, treat as no border
        if (!border.Val?.HasValue ?? true)
        {
            return BorderEdge.None;
        }

        var width = 0.5;
        if (border.Size?.HasValue == true)
        {
            // Size is in eighths of a point
            width = border.Size.Value / 8.0;
        }

        var color = "000000";
        if (border.Color?.HasValue == true &&
            border.Color.Value != "auto")
        {
            color = border.Color.Value;
        }
        else if (border.ThemeColor?.HasValue == true &&
                 themeColors != null)
        {
            var themeColorValue = ((IEnumValue) border.ThemeColor.Value).Value;
            color = themeColors.ResolveColor(themeColorValue);
        }

        return new()
        {
            IsVisible = true,
            WidthPoints = width,
            ColorHex = color,
            Style = MapBorderStyle(border.Val?.Value),
            SpacePoints = border.Space?.Value ?? 0,
            Shadow = border.Shadow?.Value == true,
            Frame = border.Frame?.Value == true
        };
    }

    // Every ST_Border line style is carried through distinctly: besides the stroke, the paragraph
    // border grouping compares border records for equality (see BorderLineStyle), so two styles
    // folded together would merge into one box. Anything genuinely unknown falls back to a plain line.
    static BorderLineStyle MapBorderStyle(BorderValues? val)
    {
        if (val == null) return BorderLineStyle.Single;
        if (val == BorderValues.Nil || val == BorderValues.None) return BorderLineStyle.None;
        if (val == BorderValues.Single) return BorderLineStyle.Single;
        if (val == BorderValues.Thick) return BorderLineStyle.Thick;
        if (val == BorderValues.Double) return BorderLineStyle.Double;
        if (val == BorderValues.Dotted) return BorderLineStyle.Dotted;
        if (val == BorderValues.Dashed) return BorderLineStyle.Dashed;
        if (val == BorderValues.DotDash) return BorderLineStyle.DotDash;
        if (val == BorderValues.DotDotDash) return BorderLineStyle.DotDotDash;
        if (val == BorderValues.Triple) return BorderLineStyle.Triple;
        if (val == BorderValues.ThinThickSmallGap) return BorderLineStyle.ThinThickSmallGap;
        if (val == BorderValues.ThickThinSmallGap) return BorderLineStyle.ThickThinSmallGap;
        if (val == BorderValues.ThinThickThinSmallGap) return BorderLineStyle.ThinThickThinSmallGap;
        if (val == BorderValues.ThinThickMediumGap) return BorderLineStyle.ThinThickMediumGap;
        if (val == BorderValues.ThickThinMediumGap) return BorderLineStyle.ThickThinMediumGap;
        if (val == BorderValues.ThinThickThinMediumGap) return BorderLineStyle.ThinThickThinMediumGap;
        if (val == BorderValues.ThinThickLargeGap) return BorderLineStyle.ThinThickLargeGap;
        if (val == BorderValues.ThickThinLargeGap) return BorderLineStyle.ThickThinLargeGap;
        if (val == BorderValues.ThinThickThinLargeGap) return BorderLineStyle.ThinThickThinLargeGap;
        if (val == BorderValues.Wave) return BorderLineStyle.Wave;
        if (val == BorderValues.DoubleWave) return BorderLineStyle.DoubleWave;
        if (val == BorderValues.DashSmallGap) return BorderLineStyle.DashSmallGap;
        if (val == BorderValues.DashDotStroked) return BorderLineStyle.DashDotStroked;
        if (val == BorderValues.ThreeDEmboss) return BorderLineStyle.ThreeDEmboss;
        if (val == BorderValues.ThreeDEngrave) return BorderLineStyle.ThreeDEngrave;
        if (val == BorderValues.Outset) return BorderLineStyle.Outset;
        if (val == BorderValues.Inset) return BorderLineStyle.Inset;
        return BorderLineStyle.Single;
    }

    public static string? ReadShadingFill(Shading? shading)
    {
        if (shading?.Fill?.HasValue != true)
        {
            return null;
        }

        var fill = shading.Fill.Value;
        if (fill is null or "auto")
        {
            return null;
        }

        return fill;
    }
}
