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
    /// Converts half-points (used by w:sz, w:kern, w:position) to points.
    /// </summary>
    public static double HalfPointsToPoints(this double halfPoints) => halfPoints / 2.0;

    /// <summary>
    /// Converts half-points (used by w:sz, w:kern, w:position) to points.
    /// </summary>
    public static double HalfPointsToPoints(this int halfPoints) => halfPoints / 2.0;

    /// <summary>
    /// Converts half-points (used by w:sz, w:kern, w:position) to points.
    /// </summary>
    public static double HalfPointsToPoints(this uint halfPoints) => halfPoints / 2.0;

    /// <summary>
    /// Returns true when an OnOff-style element is present and not explicitly set to false.
    /// OOXML semantics: a bare element (no w:val) is "on", w:val="false"/"0" is "off".
    /// </summary>
    public static bool IsOn(this OnOffType? element) =>
        element != null && element.Val?.Value != false;

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
            if (posOffset?.Text != null &&
                long.TryParse(posOffset.Text, out var hOffsetEmu))
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
            if (posOffset?.Text != null &&
                long.TryParse(posOffset.Text, out var vOffsetEmu))
            {
                vPosPoints += vOffsetEmu.EmuToPoints();
            }
        }

        // wp14:sizeRelH / wp14:sizeRelV percentage sizing (Word 2010+).
        double? widthPct = null;
        var widthRel = SizeRelativeFrom.Margin;
        double? heightPct = null;
        var heightRel = SizeRelativeFrom.Margin;
        foreach (var child in anchor.ChildElements)
        {
            if (child.LocalName == "sizeRelH")
            {
                widthRel = ParseSizeRelativeFrom(child);
                widthPct = ParsePercentChild(child, "pctWidth");
            }
            else if (child.LocalName == "sizeRelV")
            {
                heightRel = ParseSizeRelativeFrom(child);
                heightPct = ParsePercentChild(child, "pctHeight");
            }
        }

        return new()
        {
            HorizontalPositionPoints = hPosPoints,
            VerticalPositionPoints = vPosPoints,
            HorizontalAnchor = hAnchor,
            VerticalAnchor = vAnchor,
            BehindText = anchor.BehindDoc?.Value == true,
            WidthPercent = widthPct,
            WidthRelativeFrom = widthRel,
            HeightPercent = heightPct,
            HeightRelativeFrom = heightRel
        };
    }

    static SizeRelativeFrom ParseSizeRelativeFrom(OpenXmlElement sizeRel)
    {
        var attr = sizeRel.GetAttributes().FirstOrDefault(a => a.LocalName == "relativeFrom");
        return attr.Value == "page" ? SizeRelativeFrom.Page : SizeRelativeFrom.Margin;
    }

    static double? ParsePercentChild(OpenXmlElement parent, string localName)
    {
        var pct = parent.ChildElements.FirstOrDefault(c => c.LocalName == localName);
        if (pct?.InnerText is not { } text || string.IsNullOrEmpty(text))
        {
            return null;
        }

        // Stored ×1000 of percent: 50000 = 50% = 0.5. Zero placeholder means "no
        // percentage sizing" — Word writes <pctWidth>0</pctWidth> even when not used.
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var thousandths) || thousandths <= 0)
        {
            return null;
        }

        return thousandths / 100_000.0;
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
    public double? WidthPercent { get; init; }
    public SizeRelativeFrom WidthRelativeFrom { get; init; }
    public double? HeightPercent { get; init; }
    public SizeRelativeFrom HeightRelativeFrom { get; init; }
}
