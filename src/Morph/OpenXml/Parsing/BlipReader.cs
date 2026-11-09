using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Reads what a DrawingML <c>a:blip</c> and its <c>a:blipFill</c> say about a picture beyond the
/// image itself: opacity, the colour effect (greyscale, duotone, …), the crop, the tile and the
/// clip. Pure DrawingML, so Word, PowerPoint and the shape parsers share it.
/// </summary>
static class BlipReader
{
    /// <summary>
    /// Reads <c>a:alphaModFix</c> — the blip's constant transparency — as a multiplier on the
    /// picture's alpha, 1 when absent. Word writes <c>@amt</c> as a percentage in thousandths
    /// (<c>50000</c> = 50%), and values above 100% are legal (they amplify a source that is
    /// already translucent), so only the negative case is rejected.
    /// </summary>
    /// <remarks>
    /// Scanned independently of <see cref="ReadBlipColorEffect"/> because the two are separate
    /// children of the same blip: a picture may declare a duotone AND a transparency, and the
    /// effect scan returns on the first colour transform it finds.
    /// </remarks>
    public static double ReadBlipOpacity(OpenXmlElement? blip)
    {
        if (blip == null)
        {
            return 1;
        }

        foreach (var child in blip.Elements())
        {
            if (child.LocalName == "alphaModFix" &&
                int.TryParse(child.AttributeValue("amt"), out var amount) &&
                amount >= 0)
            {
                return amount / 100000d;
            }
        }

        return 1;
    }

    public static (BlipColorEffect Effect, string? DuotoneColorHex, string? DuotoneLightColorHex) ReadBlipColorEffect(OpenXmlElement? blip, ThemeColors? themeColors)
    {
        if (blip == null)
        {
            return (BlipColorEffect.None, null, null);
        }

        // a:blip can carry one or more colour-transform children. Pick the most visible one.
        foreach (var child in blip.Elements())
        {
            switch (child.LocalName)
            {
                case "grayscl":
                    return (BlipColorEffect.Grayscale, null, null);
                case "duotone":
                    var (darkHex, lightHex) = ResolveDuotoneColors(child, themeColors);
                    return (BlipColorEffect.Duotone, darkHex, lightHex);
                case "lum":
                    // a:lum bright="N" — N>0 means washout (lighten), N<0 means darken.
                    var brightAttribute = child.AttributeValue("bright");
                    if (int.TryParse(brightAttribute, out var bright) &&
                        bright > 0)
                    {
                        return (BlipColorEffect.Washout, null, null);
                    }

                    break;
            }
        }

        return (BlipColorEffect.None, null, null);
    }

    // a:duotone carries exactly two colour children: luminance maps onto a ramp from the FIRST
    // (shadow end) to the SECOND (highlight end). Word's Recolor gallery emits (darkColor,
    // white); letters/02's frame instead pairs prstClr black with a tinted accent. Each child
    // therefore consumes its position even when its colour can't be resolved; renderers default
    // an unresolved dark end to black and light end to white.
    static (string? Dark, string? Light) ResolveDuotoneColors(OpenXmlElement duotone, ThemeColors? themeColors)
    {
        string? dark = null, light = null;
        var position = 0;
        foreach (var colorChild in duotone.Elements())
        {
            string? resolved = null;
            switch (colorChild)
            {
                case A.RgbColorModelHex rgb when rgb.Val?.HasValue == true:
                    resolved = ShapeParser.ApplyLiteralColorTransforms(rgb.Val.Value!, rgb);
                    break;
                case A.SchemeColor scheme when scheme.Val?.HasValue == true &&
                                               themeColors != null:
                    var schemeValue = ((IEnumValue) scheme.Val.Value).Value;
                    resolved = themeColors.ResolveColor(schemeValue, ShapeParser.ExtractColorTransforms(scheme));
                    break;
                case A.PresetColor preset when preset.Val?.HasValue == true:
                    // The two presets the corpus uses; anything else keeps the slot's default.
                    if (preset.Val.Value == A.PresetColorValues.Black)
                    {
                        resolved = "000000";
                    }
                    else if (preset.Val.Value == A.PresetColorValues.White)
                    {
                        resolved = "FFFFFF";
                    }

                    break;
                default:
                    continue;
            }

            if (position == 0)
            {
                dark = resolved;
            }
            else if (position == 1)
            {
                light = resolved;
            }

            position++;
        }

        return (dark, light);
    }

    /// <summary>
    /// The part of a blip fill's picture that shows in its box. Two rectangles decide it, both in
    /// 1000ths of a percent per edge: <c>a:srcRect</c> picks the part of the picture that is used
    /// (positive trims, negative pads), and <c>a:stretch/a:fillRect</c> says where that part is
    /// stretched to, as insets from the box's own edges (positive inside it, negative beyond it —
    /// which is how Word stores a picture zoomed inside a shape: newsletters/03's banner photos
    /// reach a third of the box's width past its left edge and half past its right, and the box
    /// shows the middle of them). The two fold into one crop of the picture.
    /// </summary>
    public static ImageCrop? ReadCrop(OpenXmlElement blipFill)
    {
        var source = ReadEdges(blipFill.Elements().FirstOrDefault(_ => _.LocalName == "srcRect"));
        var fill = ReadEdges(blipFill.Elements().FirstOrDefault(_ => _.LocalName == "stretch")?.Elements().FirstOrDefault(_ => _.LocalName == "fillRect"));

        var (left, right) = Fold(source.Left, source.Right, fill.Left, fill.Right);
        var (top, bottom) = Fold(source.Top, source.Bottom, fill.Top, fill.Bottom);
        var crop = new ImageCrop
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };

        if (crop.IsCropped)
        {
            return crop;
        }

        return null;

        // Along one axis: the used part of the picture spans the fill rectangle, and the box sees
        // the stretch of it between the box's own edges.
        static (double Near, double Far) Fold(double sourceNear, double sourceFar, double fillNear, double fillFar)
        {
            var fillSpan = 1 - fillNear - fillFar;
            if (fillSpan <= 0 ||
                (fillNear == 0 &&
                 fillFar == 0))
            {
                return (sourceNear, sourceFar);
            }

            var sourceSpan = 1 - sourceNear - sourceFar;
            return (sourceNear - fillNear / fillSpan * sourceSpan, sourceFar - fillFar / fillSpan * sourceSpan);
        }

        static (double Left, double Top, double Right, double Bottom) ReadEdges(OpenXmlElement? element) =>
            element == null
                ? default
                : (ReadFraction(element, "l"), ReadFraction(element, "t"), ReadFraction(element, "r"), ReadFraction(element, "b"));

        static double ReadFraction(OpenXmlElement element, string attributeName)
        {
            if (element.AttributeValue(attributeName) is { } value &&
                long.TryParse(value, out var thousandthsOfPercent))
            {
                // Negative values are padding (the image shrinks inside its frame) and are as
                // common as positive crops in Word's icon templates; the -10 floor only guards
                // against degenerate markup.
                return Math.Clamp(thousandthsOfPercent / 100000.0, -10, 1);
            }

            return 0;
        }
    }

    /// <summary>
    /// A blip fill's <c>a:tile</c>: the picture repeats across its shape at its own size rather than
    /// stretching over it. The tile is the picture's pixels at the resolution the fill names
    /// (<c>a:blipFill/@dpi</c>) or, when that is absent or zero, the resolution the picture's own file
    /// declares, scaled by the tile's <c>sx</c>/<c>sy</c>. Null for a stretched fill, a tile with no
    /// extent, and a picture whose header gives no size (a vector format).
    /// </summary>
    public static ImageTile? ReadTile(OpenXmlElement blipFill, byte[]? imageData)
    {
        var tile = blipFill.Elements().FirstOrDefault(_ => _.LocalName == "tile");
        if (tile == null ||
            imageData == null ||
            !ImageHeader.TryRead(imageData, out var pixelWidth, out var pixelHeight, out var dpiX, out var dpiY))
        {
            return null;
        }

        if (blipFill.AttributeValue("dpi") is { } declared &&
            double.TryParse(declared, NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) &&
            dpi > 0)
        {
            dpiX = dpi;
            dpiY = dpi;
        }

        var width = pixelWidth * 72.0 / dpiX * Scale("sx");
        var height = pixelHeight * 72.0 / dpiY * Scale("sy");
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var alignment = tile.AttributeValue("algn") switch
        {
            "t" => ImageTileAlignment.Top,
            "tr" => ImageTileAlignment.TopRight,
            "l" => ImageTileAlignment.Left,
            "ctr" => ImageTileAlignment.Center,
            "r" => ImageTileAlignment.Right,
            "bl" => ImageTileAlignment.BottomLeft,
            "b" => ImageTileAlignment.Bottom,
            "br" => ImageTileAlignment.BottomRight,
            _ => ImageTileAlignment.TopLeft
        };
        return new(width, height, Offset("tx"), Offset("ty"), alignment);

        // sx and sy are 1000ths of a percent; absent, the tile is the picture's own size.
        double Scale(string attributeName)
        {
            if (tile.AttributeValue(attributeName) is { } value &&
                long.TryParse(value, out var thousandthsOfPercent))
            {
                return thousandthsOfPercent / 100000.0;
            }

            return 1;
        }

        double Offset(string attributeName)
        {
            if (tile.AttributeValue(attributeName) is { } value &&
                long.TryParse(value, out var emu))
            {
                return emu / OoxmlUnits.EmusPerPoint;
            }

            return 0;
        }
    }

    // pic:spPr geometry = Word's picture-style crop shape, floating or inline alike: an ellipse makes
    // the round photo (brochures/03 has one of each), a custGeom an arbitrary crop. Rect (the
    // default) is no crop. A slide picture passes its size, so a curved crop is flattened to it
    // (ShapeParser.ExtractSubpaths).
    public static (bool ClipToEllipse, IReadOnlyList<IReadOnlyList<(double X, double Y)>>? ClipSubpaths) ReadPictureClip(OpenXmlElement? spPr, (double Width, double Height)? sizePoints = null)
    {
        if (spPr == null)
        {
            return (false, null);
        }

        var clipToEllipse = spPr.Elements().FirstOrDefault(_ => _.LocalName == "prstGeom")
            ?.GetAttributes().AttributeValue("prst") == "ellipse";
        return (clipToEllipse, ShapeParser.ExtractSubpaths(spPr, sizePoints: sizePoints));
    }
}
