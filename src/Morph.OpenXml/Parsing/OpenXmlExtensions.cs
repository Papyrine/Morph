using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

/// <summary>
/// Extension methods for OpenXML types to reduce code duplication in parsers.
/// </summary>
static class OpenXmlExtensions
{
    /// <summary>
    /// Conversion constant: EMUs per point.
    /// </summary>
    public const double EmusPerPoint = 914400.0 / 72.0;

    /// <summary>
    /// Converts EMUs to points.
    /// </summary>
    public static double EmuToPoints(this long emus) => emus / EmusPerPoint;

    /// <summary>
    /// Converts EMUs (as double) to points. Used when EMU values have been scaled.
    /// </summary>
    public static double EmuToPoints(this double emus) => emus / EmusPerPoint;

    /// <summary>
    /// Extracts dimensions from a Drawing element (works with both Inline and Anchor).
    /// </summary>
    /// <returns>Tuple of (widthPoints, heightPoints) or null if dimensions cannot be extracted.</returns>
    public static (double widthPoints, double heightPoints)? GetDimensions(this Drawing drawing)
    {
        var inline = drawing.GetFirstChild<DW.Inline>();
        var anchor = drawing.GetFirstChild<DW.Anchor>();
        var extent = inline?.Extent ?? anchor?.Extent;

        if (extent == null)
        {
            return null;
        }

        long widthEmu = extent.Cx ?? 0;
        long heightEmu = extent.Cy ?? 0;

        if (widthEmu == 0 || heightEmu == 0)
        {
            return null;
        }

        return (widthEmu.EmuToPoints(), heightEmu.EmuToPoints());
    }

    /// <summary>
    /// Extracts dimensions from an Extent element.
    /// </summary>
    public static (double widthPoints, double heightPoints)? GetDimensions(this DW.Extent? extent)
    {
        if (extent == null)
        {
            return null;
        }

        long widthEmu = extent.Cx ?? 0;
        long heightEmu = extent.Cy ?? 0;

        if (widthEmu == 0 || heightEmu == 0)
        {
            return null;
        }

        return (widthEmu.EmuToPoints(), heightEmu.EmuToPoints());
    }

    /// <summary>
    /// Parses positioning information from an Anchor element.
    /// </summary>
    /// <param name="anchor">The anchor element to parse.</param>
    /// <param name="offsetX">Optional X offset to add to the position (in points).</param>
    /// <param name="offsetY">Optional Y offset to add to the position (in points).</param>
    /// <returns>Positioning information including positions and anchor types.</returns>
    public static AnchorPositioning ParsePositioning(this DW.Anchor anchor, double offsetX = 0, double offsetY = 0)
    {
        var hPosPoints = offsetX;
        var hAnchor = HorizontalAnchor.Column;

        var posH = anchor.GetFirstChild<DW.HorizontalPosition>();
        if (posH != null)
        {
            if (posH.RelativeFrom?.HasValue == true)
            {
                var relFrom = posH.RelativeFrom.Value;
                if (relFrom == DW.HorizontalRelativePositionValues.Page)
                {
                    hAnchor = HorizontalAnchor.Page;
                }
                else if (relFrom == DW.HorizontalRelativePositionValues.Margin)
                {
                    hAnchor = HorizontalAnchor.Margin;
                }
                else if (relFrom == DW.HorizontalRelativePositionValues.Column)
                {
                    hAnchor = HorizontalAnchor.Column;
                }
            }

            var posOffset = posH.GetFirstChild<DW.PositionOffset>();
            if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var hOffsetEmu))
            {
                hPosPoints += hOffsetEmu.EmuToPoints();
            }
        }

        var vPosPoints = offsetY;
        var vAnchor = VerticalAnchor.Paragraph;

        var posV = anchor.GetFirstChild<DW.VerticalPosition>();
        if (posV != null)
        {
            if (posV.RelativeFrom?.HasValue == true)
            {
                var relFrom = posV.RelativeFrom.Value;
                if (relFrom == DW.VerticalRelativePositionValues.Page)
                {
                    vAnchor = VerticalAnchor.Page;
                }
                else if (relFrom == DW.VerticalRelativePositionValues.Margin)
                {
                    vAnchor = VerticalAnchor.Margin;
                }
                else if (relFrom == DW.VerticalRelativePositionValues.Paragraph)
                {
                    vAnchor = VerticalAnchor.Paragraph;
                }
            }

            var posOffset = posV.GetFirstChild<DW.PositionOffset>();
            if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var vOffsetEmu))
            {
                vPosPoints += vOffsetEmu.EmuToPoints();
            }
        }

        return new()
        {
            HorizontalPositionPoints = hPosPoints,
            VerticalPositionPoints = vPosPoints,
            HorizontalAnchor = hAnchor,
            VerticalAnchor = vAnchor,
            BehindText = anchor.BehindDoc?.Value == true
        };
    }
}

/// <summary>
/// Positioning information extracted from an anchor element.
/// </summary>
internal readonly struct AnchorPositioning
{
    public double HorizontalPositionPoints { get; init; }
    public double VerticalPositionPoints { get; init; }
    public HorizontalAnchor HorizontalAnchor { get; init; }
    public VerticalAnchor VerticalAnchor { get; init; }
    public bool BehindText { get; init; }
}
