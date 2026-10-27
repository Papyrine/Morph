using OoxmlEmbeddedObject = DocumentFormat.OpenXml.Wordprocessing.EmbeddedObject;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

/// <summary>
/// The preview picture an embedded OLE object stores, which is how Word draws one: the server that
/// made it — LibreOffice's or MS Graph's chart, an embedded workbook — is not here to draw it again.
/// The object is a <c>w:object</c> (or a <c>w:pict</c> holding an <c>o:OLEObject</c>) whose VML
/// <c>v:shape</c> carries the picture as <c>v:imagedata</c>; the shape's CSS style says how big it is
/// and, under <c>position:absolute</c>, where it floats. A metafile preview (EMF, WMF) is played into
/// a <see cref="MetafileDrawing"/>, which layout composes into its box; any other picture is placed as
/// one. An object with no preview, or one that cannot be read, is not drawn.
/// </summary>
static class OlePreviewParser
{
    const string vml = "urn:schemas-microsoft-com:vml";
    const string relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// The OLE objects in a run, in document order. Inside an <c>mc:AlternateContent</c> only the
    /// branch the parser draws counts: the first that holds a drawing or an object, and an object
    /// only when that branch holds no <c>w:drawing</c>, which the drawing path draws instead.
    /// </summary>
    public static IEnumerable<OpenXmlElement> Objects(OoxmlRun run) =>
        run.Descendants()
            .Where(IsObject)
            .Where(Chosen);

    static bool IsObject(OpenXmlElement element) =>
        element is OoxmlEmbeddedObject ||
        (element is Picture && element.Descendants().Any(_ => _.LocalName == "OLEObject"));

    static bool Chosen(OpenXmlElement element)
    {
        foreach (var branch in element.Ancestors().TakeWhile(_ => _ is not OoxmlRun))
        {
            if (branch.LocalName is not ("Choice" or "Fallback") ||
                branch.Parent is not { LocalName: "AlternateContent" } alternate)
            {
                continue;
            }

            var drawn = alternate.ChildElements.FirstOrDefault(_ => _.Descendants().Any(descendant => descendant is Drawing || IsObject(descendant)));
            if (!ReferenceEquals(drawn, branch) ||
                branch.Descendants<Drawing>().Any())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The object's preview, sized and placed: by the VML shape's style <c>width</c> and
    /// <c>height</c>, else by the object's <c>w:dxaOrig</c> and <c>w:dyaOrig</c>. Null when the object
    /// has no picture that can be drawn, or no size.
    /// </summary>
    public static OlePreview? Read(OpenXmlElement container, OpenXmlPart hostPart, Func<OpenXmlPart, byte[]> partBytes, PageSettings? page)
    {
        var shapeId = container.Descendants().FirstOrDefault(_ => _.LocalName == "OLEObject")?.AttributeValue("ShapeID");
        var shapes = container.Descendants()
            .Where(_ => _ is {LocalName: "shape", NamespaceUri: vml})
            .ToList();
        var shape = shapes.FirstOrDefault(_ => shapeId != null && _.AttributeValue("id") == shapeId && HasPicture(_)) ??
                    shapes.FirstOrDefault(HasPicture);
        var imageData = shape?.Descendants().FirstOrDefault(_ => _ is {LocalName: "imagedata", NamespaceUri: vml});
        var id = imageData?.GetAttributes().FirstOrDefault(_ => _ is {LocalName: "id", NamespaceUri: relationships} or {LocalName: "relid"}).Value;
        if (shape == null ||
            string.IsNullOrEmpty(id) ||
            !hostPart.TryGetPartById(id, out var part))
        {
            return null;
        }

        var data = partBytes(part);
        if (data.Length == 0)
        {
            return null;
        }

        var contentType = part.ContentType;
        MetafileDrawing? metafile = null;
        if (IsMetafile(contentType))
        {
            metafile = MetafileReader.Read(data);
            if (metafile == null)
            {
                return null;
            }

            // A metafile draws only its marks, so the shape's own fill shows through it: LibreOffice's
            // chart previews have no background, and stand on the white of the filled shape.
            if (Fill(shape) is { } fill)
            {
                metafile = new([new MetafilePath([[(0, 0), (1, 0), (1, 1), (0, 1)]], true, fill, null, 0), .. metafile.Items]);
            }
        }

        var style = Style(shape.AttributeValue("style"));
        var width = Length(style, "width") ?? Twips(container.AttributeValue("dxaOrig"));
        var height = Length(style, "height") ?? Twips(container.AttributeValue("dyaOrig"));
        if (width is not > 0 || height is not > 0)
        {
            return null;
        }

        var description = imageData!.AttributeValue("title") is {Length: > 0} title && !string.IsNullOrWhiteSpace(title)
            ? title.Trim()
            : shape.AttributeValue("alt") is {Length: > 0} alt && !string.IsNullOrWhiteSpace(alt)
                ? alt.Trim()
                : null;
        var floating = style.GetValueOrDefault("position") == "absolute"
            ? Float(shape, style, width.Value, height.Value, page)
            : null;
        return new(data, contentType, metafile, width.Value, height.Value, description, floating);
    }

    static bool HasPicture(OpenXmlElement shape) =>
        shape.Descendants().Any(_ => _ is {LocalName: "imagedata", NamespaceUri: vml});

    // The colour a VML shape is filled with, or null when it is not filled: its own filled and
    // fillcolor, else its v:shapetype's (the picture frame Word writes for an object, _x0000_t75, says
    // filled="f"), else VML's defaults of filled and white. A v:fill with on="f" turns it off; a
    // gradient or picture fill is taken as its colour.
    static string? Fill(OpenXmlElement shape)
    {
        var typeId = shape.AttributeValue("type")?.TrimStart('#');
        var shapeType = typeId == null
            ? null
            : shape.Ancestors().Last().Descendants().FirstOrDefault(_ => _ is {LocalName: "shapetype", NamespaceUri: vml} && _.AttributeValue("id") == typeId);
        var filled = shape.AttributeValue("filled") ?? shapeType?.AttributeValue("filled");
        if (filled is "f" or "false" ||
            shape.Elements().FirstOrDefault(_ => _ is {LocalName: "fill", NamespaceUri: vml})?.AttributeValue("on") is "f" or "false")
        {
            return null;
        }

        // A colour may carry the palette index Office wrote it from ("#ffc000 [3207]").
        var color = shape.AttributeValue("fillcolor") ?? shapeType?.AttributeValue("fillcolor") ?? "white";
        return HtmlParser.NormalizeColor(color.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
    }

    static bool IsMetafile(string contentType) =>
        contentType is "image/x-emf" or "image/emf" or "image/x-wmf" or "image/wmf";

    // Where a shape with position:absolute floats: its left and top (with their margins) from what
    // mso-position-horizontal-relative and -vertical-relative name, the column and the paragraph when
    // they name nothing, as Word reads them; an mso-position-horizontal or -vertical alignment folded
    // into the offset against the page or its margins, as a DrawingML wp:align is.
    static OleFloat Float(OpenXmlElement shape, Dictionary<string, string> style, double width, double height, PageSettings? page)
    {
        var horizontalRelative = style.GetValueOrDefault("mso-position-horizontal-relative");
        var verticalRelative = style.GetValueOrDefault("mso-position-vertical-relative");
        var horizontalAnchor = horizontalRelative switch
        {
            "page" => HorizontalAnchor.Page,
            "margin" => HorizontalAnchor.Margin,
            "char" => HorizontalAnchor.Character,
            _ => HorizontalAnchor.Column
        };
        var verticalAnchor = verticalRelative switch
        {
            "page" => VerticalAnchor.Page,
            "margin" => VerticalAnchor.Margin,
            "line" => VerticalAnchor.Line,
            _ => VerticalAnchor.Paragraph
        };

        var x = (Length(style, "left") ?? 0) + (Length(style, "margin-left") ?? 0);
        var y = (Length(style, "top") ?? 0) + (Length(style, "margin-top") ?? 0);
        if (page != null &&
            style.GetValueOrDefault("mso-position-horizontal") is { } alignH and not "absolute")
        {
            double? referenceWidth = horizontalAnchor switch
            {
                HorizontalAnchor.Page => page.WidthPoints,
                HorizontalAnchor.Margin or HorizontalAnchor.Column => page.WidthPoints - page.MarginLeft - page.MarginRight,
                _ => null
            };
            if (referenceWidth is { } reference)
            {
                x = alignH switch
                {
                    "center" => (reference - width) / 2,
                    "right" or "outside" => reference - width,
                    _ => 0
                };
            }
        }

        if (page != null &&
            style.GetValueOrDefault("mso-position-vertical") is { } alignV and not "absolute")
        {
            double? referenceHeight = verticalAnchor switch
            {
                VerticalAnchor.Page => page.HeightPoints,
                VerticalAnchor.Margin => page.HeightPoints - page.MarginTop - page.MarginBottom,
                _ => null
            };
            if (referenceHeight is { } reference)
            {
                y = alignV switch
                {
                    "center" => (reference - height) / 2,
                    "bottom" or "outside" => reference - height,
                    _ => 0
                };
            }
        }

        // A negative z-index puts the shape behind the text (Word writes behind-text shapes from
        // -251658240 up); its size orders floats as wp:anchor's relativeHeight does.
        long.TryParse(style.GetValueOrDefault("z-index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var zIndex);

        // No w10:wrap is text that does not wrap: the shape is in front of the text or behind it.
        var wrap = shape.Elements().FirstOrDefault(_ => _.LocalName == "wrap");
        var wrapType = wrap?.AttributeValue("type") switch
        {
            "square" => WrapType.Square,
            "tight" => WrapType.Tight,
            "through" => WrapType.Through,
            "topAndBottom" => WrapType.TopAndBottom,
            _ => WrapType.None
        };
        var wrapSide = wrap?.AttributeValue("side") switch
        {
            "left" => WrapTextSide.Left,
            "right" => WrapTextSide.Right,
            "largest" => WrapTextSide.Largest,
            _ => WrapTextSide.BothSides
        };

        return new(
            x,
            y,
            horizontalAnchor,
            verticalAnchor,
            zIndex < 0,
            (uint) Math.Min(Math.Abs(zIndex), uint.MaxValue),
            wrapType,
            wrapSide,
            Length(style, "mso-wrap-distance-left") ?? 0,
            Length(style, "mso-wrap-distance-top") ?? 0,
            Length(style, "mso-wrap-distance-right") ?? 0,
            Length(style, "mso-wrap-distance-bottom") ?? 0);
    }

    // A VML style attribute ("position:absolute;width:322.8pt;…") as its properties, keyed in lower case.
    static Dictionary<string, string> Style(string? style)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in (style ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon > 0)
            {
                properties[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
            }
        }

        return properties;
    }

    // A CSS length in points: pt, in, cm, mm, pc or px, a bare number being pixels.
    static double? Length(Dictionary<string, string> style, string name)
    {
        if (!style.TryGetValue(name, out var value))
        {
            return null;
        }

        var (number, scale) = value switch
        {
            _ when value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) => (value[..^2], 1d),
            _ when value.EndsWith("in", StringComparison.OrdinalIgnoreCase) => (value[..^2], 72d),
            _ when value.EndsWith("cm", StringComparison.OrdinalIgnoreCase) => (value[..^2], 72 / 2.54),
            _ when value.EndsWith("mm", StringComparison.OrdinalIgnoreCase) => (value[..^2], 72 / 25.4),
            _ when value.EndsWith("pc", StringComparison.OrdinalIgnoreCase) => (value[..^2], 12d),
            _ when value.EndsWith("px", StringComparison.OrdinalIgnoreCase) => (value[..^2], 0.75),
            _ => (value, 0.75)
        };

        if (!double.TryParse(number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        return parsed * scale;
    }

    static double? Twips(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips))
        {
            return null;
        }

        return twips / 20;
    }
}

/// <summary>
/// An OLE object's preview picture (<see cref="OlePreviewParser"/>): its bytes and type, the drawing
/// a metafile plays into, its size in points, its alt text, and where it floats — null for an object
/// that stands in the line.
/// </summary>
sealed record OlePreview(
    byte[] Data,
    string ContentType,
    MetafileDrawing? Metafile,
    double WidthPoints,
    double HeightPoints,
    string? Description,
    OleFloat? Float);

/// <summary>Where a floating OLE object's preview stands and how the text goes round it, as VML says.</summary>
sealed record OleFloat(
    double HorizontalPositionPoints,
    double VerticalPositionPoints,
    HorizontalAnchor HorizontalAnchor,
    VerticalAnchor VerticalAnchor,
    bool BehindText,
    uint RelativeHeight,
    WrapType WrapType,
    WrapTextSide WrapTextSide,
    double WrapDistanceLeftPoints,
    double WrapDistanceTopPoints,
    double WrapDistanceRightPoints,
    double WrapDistanceBottomPoints);
