using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

/// <summary>
/// Extension methods for OpenXML types to reduce code duplication in parsers.
/// </summary>
static class OpenXmlExtensions
{
    /// <summary>
    /// Materialises the builder's content with leading and trailing whitespace removed,
    /// trimming in place so only the final string is allocated (unlike
    /// <c>builder.ToString().Trim()</c>, which also allocates the untrimmed intermediate).
    /// </summary>
    public static string TrimmedToString(this StringBuilder builder)
    {
        while (builder.Length > 0 && char.IsWhiteSpace(builder[^1]))
        {
            builder.Length--;
        }

        var start = 0;
        while (start < builder.Length && char.IsWhiteSpace(builder[start]))
        {
            start++;
        }

        return builder.ToString(start, builder.Length - start);
    }

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
        double? hPctOffset = null;

        var posH = FindPositionElement(anchor, "positionH");
        if (posH != null)
        {
            hAnchor = ReadHorizontalAnchor(posH);
            var (emuOffset, pctOffset) = ReadPositionOffset(posH, "pctPosHOffset");
            if (emuOffset.HasValue)
            {
                hPosPoints += emuOffset.Value.EmuToPoints();
            }
            hPctOffset = pctOffset;
        }

        var vPosPoints = offsetY;
        var vAnchor = VerticalAnchor.Paragraph;
        double? vPctOffset = null;

        var posV = FindPositionElement(anchor, "positionV");
        if (posV != null)
        {
            vAnchor = ReadVerticalAnchor(posV);
            var (emuOffset, pctOffset) = ReadPositionOffset(posV, "pctPosVOffset");
            if (emuOffset.HasValue)
            {
                vPosPoints += emuOffset.Value.EmuToPoints();
            }
            vPctOffset = pctOffset;
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
            HeightRelativeFrom = heightRel,
            HorizontalPositionPercent = hPctOffset,
            VerticalPositionPercent = vPctOffset
        };
    }

    /// <summary>
    /// Finds the <c>wp:positionH</c> or <c>wp:positionV</c> child of an anchor, descending into
    /// <c>mc:AlternateContent</c> when present. Prefers the <c>mc:Choice</c> branch when its
    /// <c>Requires</c> namespace is recognised (currently <c>wp14</c>) so that
    /// <c>wp14:pctPosHOffset</c> / <c>wp14:pctPosVOffset</c> values are picked up; otherwise
    /// falls back to <c>mc:Fallback</c>.
    /// </summary>
    static OpenXmlElement? FindPositionElement(DW.Anchor anchor, string localName)
    {
        foreach (var child in anchor.ChildElements)
        {
            if (child.LocalName == localName)
            {
                return child;
            }
        }

        foreach (var child in anchor.ChildElements)
        {
            if (child.LocalName != "AlternateContent")
            {
                continue;
            }

            OpenXmlElement? choice = null;
            OpenXmlElement? fallback = null;
            foreach (var branch in child.ChildElements)
            {
                if (branch.LocalName == "Choice" && IsChoiceUnderstood(branch))
                {
                    choice = branch;
                }
                else if (branch.LocalName == "Fallback")
                {
                    fallback = branch;
                }
            }

            var selected = choice ?? fallback;
            if (selected == null)
            {
                continue;
            }

            foreach (var inner in selected.ChildElements)
            {
                if (inner.LocalName == localName)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    static bool IsChoiceUnderstood(OpenXmlElement choice)
    {
        var requires = choice.AttributeValue("Requires");
        if (requires == null)
        {
            return true;
        }
        // Recognised extension namespaces — wp14 brings pctPosH/VOffset, w14/w15 add
        // typography features Morph already consumes elsewhere in the tree.
        foreach (var token in requires.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is not ("wp14" or "w14" or "w15" or "wps" or "wpg"))
            {
                return false;
            }
        }
        return true;
    }

    static HorizontalAnchor ReadHorizontalAnchor(OpenXmlElement positionH) =>
        positionH.AttributeValue("relativeFrom") switch
        {
            "page" => HorizontalAnchor.Page,
            "margin" or "leftMargin" or "rightMargin" or "insideMargin" or "outsideMargin" => HorizontalAnchor.Margin,
            "character" => HorizontalAnchor.Character,
            _ => HorizontalAnchor.Column
        };

    static VerticalAnchor ReadVerticalAnchor(OpenXmlElement positionV) =>
        positionV.AttributeValue("relativeFrom") switch
        {
            "page" => VerticalAnchor.Page,
            "margin" or "topMargin" or "bottomMargin" or "insideMargin" or "outsideMargin" => VerticalAnchor.Margin,
            "line" => VerticalAnchor.Line,
            _ => VerticalAnchor.Paragraph
        };

    /// <summary>
    /// Reads either a <c>wp:posOffset</c> (EMU) or a <c>wp14:pctPosHOffset</c> /
    /// <c>wp14:pctPosVOffset</c> (×1000 percent) from a <c>wp:positionH</c> / <c>wp:positionV</c>.
    /// The two are mutually exclusive in the schema; the percent overrides if both somehow appear.
    /// </summary>
    static (long? emuOffset, double? pctOffset) ReadPositionOffset(OpenXmlElement positionElement, string pctLocalName)
    {
        long? emuOffset = null;
        double? pctOffset = null;
        foreach (var child in positionElement.ChildElements)
        {
            if (child.LocalName == "posOffset")
            {
                if (long.TryParse(child.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu))
                {
                    emuOffset = emu;
                }
            }
            else if (child.LocalName == pctLocalName)
            {
                if (long.TryParse(child.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var thousandths))
                {
                    // Stored ×1000 of percent: 50000 = 50% = 0.5. Word emits 0 as a placeholder
                    // alongside an explicit posOffset — treat zero as "no percentage in effect".
                    if (thousandths != 0)
                    {
                        pctOffset = thousandths / 100_000.0;
                    }
                }
            }
        }
        return (emuOffset, pctOffset);
    }

    static SizeRelativeFrom ParseSizeRelativeFrom(OpenXmlElement sizeRel) =>
        sizeRel.AttributeValue("relativeFrom") == "page" ? SizeRelativeFrom.Page : SizeRelativeFrom.Margin;

    /// <summary>
    /// Returns the value of the attribute matching <paramref name="localName"/>, or null if absent.
    /// </summary>
    public static string? AttributeValue(this OpenXmlElement element, string localName)
    {
        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName == localName)
            {
                return attribute.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the value of the attribute matching <paramref name="localName"/> within an
    /// already-materialised attribute list (used when several lookups share the same list).
    /// </summary>
    public static string? AttributeValue(this IList<OpenXmlAttribute> attributes, string localName)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.LocalName == localName)
            {
                return attribute.Value;
            }
        }
        return null;
    }

    static double? ParsePercentChild(OpenXmlElement parent, string localName)
    {
        var pct = parent.ChildElements.FirstOrDefault(_ => _.LocalName == localName);
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
