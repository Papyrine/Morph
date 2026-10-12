using System.Xml.Linq;

/// <summary>
/// Reads an SVG document into an <see cref="SvgDrawing"/>: every shape as figures of lines and
/// cubics in the <c>viewBox</c>'s coordinates, with its fill, stroke, opacity and clip resolved, so
/// that a backend with nothing but a vector path API can draw it. It exists because Office stores
/// its icons and illustrations as SVG with a PNG beside them, and two of the three backends have
/// no SVG library to hand the markup to.
///
/// It reads what those documents are made of (surveyed across the corpus, 108 documents):
/// <c>path</c>, <c>rect</c>, <c>circle</c>, <c>ellipse</c>, <c>line</c>, <c>polyline</c> and
/// <c>polygon</c> inside <c>g</c>; <c>fill</c>, <c>stroke</c> and their widths, joins, caps and
/// opacities, as attributes, in a <c>style</c> attribute or by class from a <c>style</c> element;
/// <c>transform</c>; <c>use</c>; <c>clip-path</c>; <c>display</c> and <c>visibility</c>.
///
/// What it does not draw it does not approximate. A gradient or pattern paint, a mask, a filter,
/// an embedded image, text, a dashed stroke, a stroke squashed by its transform, a group made
/// translucent over shapes that overlap — any of these in the part of the document that is
/// actually drawn, and <see cref="Read"/> returns no drawing and names it. The caller then paints
/// the PNG, as every backend did for every document before this reader. That is the whole
/// contract: a picture is drawn from its SVG entire, or from its PNG, never from part of its SVG.
/// </summary>
sealed class SvgReader
{
    sealed class UnsupportedException(string feature) : Exception(feature);

    // A fill or stroke as the cascade leaves it: a colour, nothing (a null colour), or a paint this
    // reader cannot draw. The last is kept rather than thrown, because a paint only matters on the
    // shapes that end up using it — a gradient set on a group whose shapes all name their own
    // colour never reaches the page.
    readonly record struct Paint(string? ColorHex, double Alpha, string? Unsupported);

    // The inherited properties, as they stand at one element.
    sealed record Style
    {
        public Paint Fill { get; init; } = new("000000", 1, null);

        public Paint Stroke { get; init; }

        public double FillOpacity { get; init; } = 1;

        public double StrokeOpacity { get; init; } = 1;

        public double StrokeWidth { get; init; } = 1;

        public SvgLineCap Cap { get; init; }

        public SvgLineJoin Join { get; init; }

        public double MiterLimit { get; init; } = 4;

        public bool Dashed { get; init; }

        public bool EvenOdd { get; init; }

        public bool ClipEvenOdd { get; init; }

        public string ColorHex { get; init; } = "000000";

        public double ColorAlpha { get; init; } = 1;

        public bool Visible { get; init; } = true;
    }

    static readonly XNamespace svg = "http://www.w3.org/2000/svg";
    static readonly XNamespace xlink = "http://www.w3.org/1999/xlink";

    // A document nested deeper than this, or with more segments, is not an icon; it is given up
    // before it can exhaust the stack or the heap. A use of a use of a use doubles its content at
    // each step, so the second limit is what stops a small file from becoming a large drawing.
    const int maxDepth = 64;
    const int maxSegments = 2_000_000;

    // The attributes that are properties: they take part in the cascade, below a style sheet.
    static readonly HashSet<string> presentationAttributes =
    [
        "fill", "stroke", "fill-rule", "clip-rule", "fill-opacity", "stroke-opacity", "opacity",
        "stroke-width", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray",
        "display", "visibility", "color", "clip-path", "mask", "filter",
        "marker", "marker-start", "marker-mid", "marker-end",
        "paint-order", "vector-effect", "mix-blend-mode", "transform-origin"
    ];

    // Geometry a style sheet can move in SVG 2. The reader takes geometry from attributes alone, so
    // a document that sets any of these as a property is one it would draw in the wrong place.
    static readonly string[] geometryProperties =
    [
        "transform", "transform-origin", "transform-box", "d", "x", "y", "cx", "cy", "r", "rx", "ry", "width", "height"
    ];

    // Elements that hold things to be referred to, or say something about the document, and draw
    // nothing where they stand.
    static readonly HashSet<string> silentElements =
    [
        "defs", "style", "clipPath", "mask", "filter", "linearGradient", "radialGradient", "pattern",
        "symbol", "marker", "title", "desc", "metadata", "script", "view", "cursor",
        "font", "font-face", "color-profile",
        "animate", "animateTransform", "animateMotion", "animateColor", "set"
    ];

    readonly Dictionary<string, XElement> elementsById = new(StringComparer.Ordinal);
    readonly HashSet<XElement> beingUsed = [];
    readonly SvgStyleSheet styleSheet;
    int segmentCount;

    SvgReader(XElement root)
    {
        // The first element to claim an id keeps it, as in a browser.
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Attribute("id") is { } id)
            {
                elementsById.TryAdd(id.Value, element);
            }
        }

        var sheets = new List<string>();
        foreach (var style in root.Descendants(svg + "style"))
        {
            if (style.Attribute("type") is { } type && type.Value.Trim() != "text/css")
            {
                throw new UnsupportedException($"a style sheet of type '{type.Value}'");
            }

            sheets.Add(style.Value);
        }

        if (!SvgStyleSheet.TryParse(sheets, out var parsed, out var unsupported))
        {
            throw new UnsupportedException(unsupported);
        }

        styleSheet = parsed;
    }

    /// <summary>
    /// Reads a document. The result holds the drawing, or what in the document the reader does not
    /// draw; it never throws for the content of the bytes.
    /// </summary>
    public static SvgReadResult Read(byte[] data)
    {
        XElement? root;
        try
        {
            // A DOCTYPE is skipped rather than refused, since drawing tools write one; nothing is
            // fetched for it and its entities are not expanded, so a document that leans on them
            // fails to parse and is given up below.
            using var stream = new MemoryStream(data);
            using var reader = XmlReader.Create(stream, new() {DtdProcessing = DtdProcessing.Ignore, XmlResolver = null});
            root = XDocument.Load(reader).Root;
        }
        catch (XmlException)
        {
            return new(null, "markup that is not well-formed XML");
        }

        if (root == null || root.Name != svg + "svg")
        {
            return new(null, "a root that is not an svg element");
        }

        try
        {
            return new(new SvgReader(root).ReadDocument(root), null);
        }
        catch (UnsupportedException exception)
        {
            return new(null, exception.Message);
        }
    }

    SvgDrawing ReadDocument(XElement root)
    {
        var (x, y, width, height) = ReadFrame(root);
        if (root.Attribute("transform") != null)
        {
            throw new UnsupportedException("a transform on the root element");
        }

        var paths = new List<SvgPath>();
        Render(root, SvgMatrix.Identity, new(), null, 0, paths, isRoot: true);
        return new(x, y, width, height, paths);
    }

    // The rectangle of the document that is stretched over the picture — which is not simply the
    // viewBox. Office takes an SVG to be an image a whole number of pixels each way: its width and
    // height (the viewBox's, when it declares none) rounded to the nearest pixel, a half going up.
    // The viewBox is then fitted into that image as preserveAspectRatio says, which by default is
    // scaled alike both ways until one side fits, and centred. So a document 44.02 by 25.36 is an
    // image 44 by 25, with its drawing scaled by 25/25.36 and a sliver of margin left and right.
    //
    // Two things in the corpus say so (2026-10-12). The frame Office gives such a picture has the
    // proportions of the rounded size: that document's is 548640 by 311728 EMU, 44:25 exactly, and
    // of the 49 uncropped pictures of fractional-size documents 20 are framed in the rounded
    // proportions to within 0.01% — the PowerPoint ones, which carry no raster at all, among them.
    // And the raster Office saves beside the SVG is drawn this way: over the 41 such documents that
    // have one, the mean difference from it per channel (of 255) is 0.31 fitted and centred, 0.73
    // with the viewBox stretched over the image, and 1.43 with the drawing unscaled from the top
    // left. Fitted is the closest for 34 of them and within 0.02 of the closest for the other seven.
    static (double X, double Y, double Width, double Height) ReadFrame(XElement root)
    {
        var width = ReadSize(root, "width");
        var height = ReadSize(root, "height");
        double x = 0, y = 0, boxWidth, boxHeight;
        if (root.Attribute("viewBox") is { } viewBox)
        {
            if (SvgNumbers.ReadList(viewBox.Value) is not {Count: 4} box || box[2] <= 0 || box[3] <= 0)
            {
                throw new UnsupportedException("a viewBox that does not parse");
            }

            (x, y, boxWidth, boxHeight) = (box[0], box[1], box[2], box[3]);

            // A size given one way only takes the other from the viewBox's proportions.
            width ??= height * boxWidth / boxHeight ?? boxWidth;
            height ??= width * boxHeight / boxWidth;
        }
        else if (width > 0 && height > 0)
        {
            // With no viewBox the document's units are the image's pixels.
            (boxWidth, boxHeight) = (Pixels(width.Value), Pixels(height.Value));
        }
        else
        {
            throw new UnsupportedException("neither a viewBox nor a size");
        }

        if (width is not > 0 || height is not > 0)
        {
            throw new UnsupportedException("a size that is not above zero");
        }

        var (alignX, alignY, slice) = ReadAspect(root.Attribute("preserveAspectRatio")?.Value);
        if (alignX is not { } offsetX || alignY is not { } offsetY)
        {
            // preserveAspectRatio="none": the viewBox itself is stretched over the image.
            return (x, y, boxWidth, boxHeight);
        }

        var scaleX = Pixels(width.Value) / boxWidth;
        var scaleY = Pixels(height.Value) / boxHeight;
        var scale = slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        var frameWidth = Pixels(width.Value) / scale;
        var frameHeight = Pixels(height.Value) / scale;
        return (x - (frameWidth - boxWidth) * offsetX, y - (frameHeight - boxHeight) * offsetY, frameWidth, frameHeight);
    }

    // A length in whole pixels as Office rounds it: 38.5 is 39, where Math.Round's default would
    // make it 38 and the frame the wrong proportions (menus/05 has exactly that icon, framed 39:22).
    static double Pixels(double length) =>
        Math.Max(1, Math.Round(length, MidpointRounding.AwayFromZero));

    // preserveAspectRatio as where the viewBox sits in the image along each axis — 0 at the near
    // edge, a half centred, 1 at the far edge — and whether it is scaled to cover the image (slice)
    // rather than to fit inside it. Both offsets are null for none.
    static (double? AlignX, double? AlignY, bool Slice) ReadAspect(string? value)
    {
        var words = (value ?? "").Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries).ToList();
        words.Remove("defer");
        var align = words.Count > 0 ? words[0] : "xMidYMid";
        var slice = words.Count > 1 && words[1] == "slice";
        if (align == "none")
        {
            return (null, null, false);
        }

        if (align.Length != 8 || words.Count > 2 || (words.Count == 2 && words[1] is not ("meet" or "slice")))
        {
            throw new UnsupportedException($"the preserveAspectRatio '{value}'");
        }

        return (Offset(align[..4], 'x'), Offset(align[4..], 'Y'), slice);

        double Offset(string part, char axis) =>
            part[0] == axis
                ? part[1..] switch
                {
                    "Min" => 0,
                    "Mid" => 0.5,
                    "Max" => 1,
                    _ => throw new UnsupportedException($"the preserveAspectRatio '{value}'")
                }
                : throw new UnsupportedException($"the preserveAspectRatio '{value}'");
    }

    // A root width or height in real units. A percentage is of whatever holds the picture, which
    // says nothing about the document, so it reads as absent.
    static double? ReadSize(XElement root, string name)
    {
        if (root.Attribute(name) is not { } attribute || attribute.Value.TrimEnd().EndsWith('%'))
        {
            return null;
        }

        return ParseLength(attribute.Value);
    }

    void Render(XElement element, SvgMatrix matrix, Style inherited, SvgClip? clip, int depth, List<SvgPath> output, bool isRoot = false)
    {
        // Anything from another vocabulary — an editor's own metadata, say — is not part of the picture.
        if (element.Name.Namespace != svg)
        {
            return;
        }

        var name = element.Name.LocalName;
        if (silentElements.Contains(name))
        {
            return;
        }

        if (depth > maxDepth)
        {
            throw new UnsupportedException($"nesting more than {maxDepth} deep");
        }

        var declared = Declare(element);
        if (declared.TryGetValue("display", out var display) && display == "none")
        {
            return;
        }

        RejectEffects(declared);
        var style = Inherit(inherited, declared);

        if (!isRoot && element.Attribute("transform") is { } transform)
        {
            if (!SvgMatrix.TryParse(transform.Value, out var local))
            {
                throw new UnsupportedException($"the transform '{transform.Value}'");
            }

            matrix = matrix.Multiply(local);
        }

        if (declared.TryGetValue("clip-path", out var clipPath) && clipPath != "none")
        {
            clip = ReadClip(clipPath, matrix, clip);

            // A clip with nothing in it leaves nothing of what it is set on.
            if (clip.Figures.Count == 0)
            {
                return;
            }
        }

        // A translucent container's shapes are gathered apart, so that Fade can judge them together.
        var opacity = ReadOpacity(declared, "opacity");
        var group = opacity < 1 ? new List<SvgPath>() : output;
        switch (name)
        {
            case "svg" when isRoot:
            case "g":
            case "a":
                foreach (var child in element.Elements())
                {
                    Render(child, matrix, style, clip, depth + 1, group);
                }

                break;
            case "use":
            {
                var target = Resolve(element);
                if (target.Name == svg + "svg" || target.Name == svg + "symbol")
                {
                    // Either would bring its own viewport, which is what the use's width and height
                    // are for; on anything else they mean nothing and are ignored.
                    throw new UnsupportedException("a use of a symbol or a nested svg");
                }

                if (!beingUsed.Add(target))
                {
                    throw new UnsupportedException("a use that leads back to itself");
                }

                var placed = matrix.Multiply(SvgMatrix.Translation(ReadLength(element, "x"), ReadLength(element, "y")));
                Render(target, placed, style, clip, depth + 1, group);
                beingUsed.Remove(target);
                break;
            }
            case "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon":
                Emit(ReadShape(element), matrix, style, opacity, clip, output, fillable: name != "line");
                return;
            case "svg":
                throw new UnsupportedException("a nested svg element");
            default:
                // text, image, foreignObject, switch and anything newer than this reader.
                throw new UnsupportedException($"the {name} element");
        }

        if (opacity < 1)
        {
            Fade(group, opacity, output);
        }
    }

    // The properties an element declares, strongest last applied: its presentation attributes, then
    // the style sheet's matching rules, then its style attribute.
    Dictionary<string, string> Declare(XElement element)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.Namespace == XNamespace.None && presentationAttributes.Contains(attribute.Name.LocalName))
            {
                properties[attribute.Name.LocalName] = attribute.Value.Trim();
            }
        }

        var classes = element.Attribute("class")?.Value.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        styleSheet.Apply(element.Name.LocalName, element.Attribute("id")?.Value, classes, properties);
        if (element.Attribute("style") is { } inline && !SvgStyleSheet.TryParseDeclarations(inline.Value, properties))
        {
            throw new UnsupportedException("!important in a style attribute");
        }

        foreach (var property in geometryProperties)
        {
            if (properties.ContainsKey(property))
            {
                throw new UnsupportedException($"{property} set as a style property");
            }
        }

        return properties;
    }

    // Properties that are not inherited and that this reader has no way to honour. Each is fine at
    // its initial value, which is how a document that merely spells out its defaults stays readable.
    static void RejectEffects(Dictionary<string, string> declared)
    {
        Reject("mask", "none", "a mask");
        Reject("filter", "none", "a filter");
        Reject("marker", "none", "markers");
        Reject("marker-start", "none", "markers");
        Reject("marker-mid", "none", "markers");
        Reject("marker-end", "none", "markers");
        Reject("paint-order", "normal", "a paint-order");
        Reject("vector-effect", "none", "a vector-effect");
        Reject("mix-blend-mode", "normal", "a blend mode");

        void Reject(string property, string initial, string feature)
        {
            if (declared.TryGetValue(property, out var value) && value != initial && value.Length > 0)
            {
                throw new UnsupportedException(feature);
            }
        }
    }

    Style Inherit(Style style, Dictionary<string, string> declared)
    {
        if (declared.Count == 0)
        {
            return style;
        }

        // The colour first: a fill or stroke of currentColor on this same element means this one.
        if (Declared("color") is { } color)
        {
            if (!SvgColor.TryParse(color, out var colorHex, out var colorAlpha))
            {
                throw new UnsupportedException($"the colour '{color}'");
            }

            style = style with {ColorHex = colorHex, ColorAlpha = colorAlpha};
        }

        if (Declared("fill") is { } fill)
        {
            style = style with {Fill = ReadPaint(fill, "fill", style)};
        }

        if (Declared("stroke") is { } stroke)
        {
            style = style with {Stroke = ReadPaint(stroke, "stroke", style)};
        }

        if (Declared("fill-opacity") != null)
        {
            style = style with {FillOpacity = ReadOpacity(declared, "fill-opacity")};
        }

        if (Declared("stroke-opacity") != null)
        {
            style = style with {StrokeOpacity = ReadOpacity(declared, "stroke-opacity")};
        }

        if (Declared("stroke-width") is { } strokeWidth)
        {
            style = style with {StrokeWidth = ParseLength(strokeWidth)};
        }

        if (Declared("stroke-miterlimit") is { } miterLimit)
        {
            if (!double.TryParse(miterLimit, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) || limit < 1)
            {
                throw new UnsupportedException($"the miter limit '{miterLimit}'");
            }

            style = style with {MiterLimit = limit};
        }

        if (Declared("stroke-linecap") is { } cap)
        {
            style = style with
            {
                Cap = cap switch
                {
                    "butt" => SvgLineCap.Butt,
                    "round" => SvgLineCap.Round,
                    "square" => SvgLineCap.Square,
                    _ => throw new UnsupportedException($"the line cap '{cap}'")
                }
            };
        }

        if (Declared("stroke-linejoin") is { } join)
        {
            style = style with
            {
                Join = join switch
                {
                    "miter" => SvgLineJoin.Miter,
                    "round" => SvgLineJoin.Round,
                    "bevel" => SvgLineJoin.Bevel,
                    _ => throw new UnsupportedException($"the line join '{join}'")
                }
            };
        }

        if (Declared("stroke-dasharray") is { } dashes)
        {
            // Office writes an empty dash array on solid strokes; that, none, and a list of zeros
            // are all a solid line. Anything else is a dash pattern, units or not.
            style = style with {Dashed = dashes is not ("none" or "") && (SvgNumbers.ReadList(dashes) is not { } lengths || lengths.Exists(_ => _ != 0))};
        }

        if (Declared("fill-rule") is { } fillRule)
        {
            style = style with {EvenOdd = ReadRule(fillRule)};
        }

        if (Declared("clip-rule") is { } clipRule)
        {
            style = style with {ClipEvenOdd = ReadRule(clipRule)};
        }

        if (Declared("visibility") is { } visibility)
        {
            style = style with {Visible = visibility == "visible"};
        }

        return style;

        // A property's declared value, with inherit read as nothing declared: every property here
        // inherits anyway.
        string? Declared(string property) =>
            declared.TryGetValue(property, out var value) && value != "inherit" ? value : null;
    }

    static bool ReadRule(string value) =>
        value switch
        {
            "nonzero" => false,
            "evenodd" => true,
            _ => throw new UnsupportedException($"the fill rule '{value}'")
        };

    Paint ReadPaint(string value, string property, Style style)
    {
        if (value == "none")
        {
            return default;
        }

        if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            return new(style.ColorHex, style.ColorAlpha, null);
        }

        if (value.StartsWith("url(", StringComparison.Ordinal))
        {
            var server = ReadReference(value) is { } id && elementsById.TryGetValue(id, out var element) ? element.Name.LocalName : null;
            return new(
                null,
                0,
                server switch
                {
                    "linearGradient" or "radialGradient" => $"a gradient {property}",
                    "pattern" => $"a pattern {property}",
                    _ => $"the {property} '{value}'"
                });
        }

        if (SvgColor.TryParse(value, out var hex, out var alpha))
        {
            return new(hex, alpha, null);
        }

        return new(null, 0, $"the {property} '{value}'");
    }

    // An opacity as a number or a percentage, held to 0-1; fully opaque when not declared.
    static double ReadOpacity(Dictionary<string, string> declared, string property)
    {
        if (!declared.TryGetValue(property, out var value))
        {
            return 1;
        }

        var percent = value.EndsWith('%');
        if (!double.TryParse(percent ? value.AsSpan(0, value.Length - 1) : value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) || !double.IsFinite(opacity))
        {
            throw new UnsupportedException($"the {property} '{value}'");
        }

        return Math.Clamp(percent ? opacity / 100 : opacity, 0, 1);
    }

    // A group's opacity is the opacity of the group drawn as one picture: where two of its shapes
    // overlap, the one beneath does not show through the one above. A flat list has no way to say
    // that, but it does not need one when the shapes keep clear of each other — then fading each
    // on its own is the same picture. A group that overlaps is given up. One shape with both a fill
    // and a stroke overlaps itself, along the inner half of the stroke.
    static void Fade(List<SvgPath> group, double opacity, List<SvgPath> output)
    {
        if (opacity <= 0 || group.Count == 0)
        {
            return;
        }

        if (group.Exists(_ => _.FillColorHex != null && _.Stroke != null) || Overlaps(group))
        {
            throw new UnsupportedException("opacity on a group of shapes that overlap");
        }

        foreach (var path in group)
        {
            output.Add(path with
            {
                FillAlpha = path.FillAlpha * opacity,
                Stroke = path.Stroke == null ? null : path.Stroke with {Alpha = path.Stroke.Alpha * opacity}
            });
        }
    }

    // Whether any two paths' bounding boxes meet. Boxes are taken over control points as well as
    // end points and grown by the stroke, so they err on the side of meeting — which errs on the
    // side of the PNG. Sorted by left edge, so only boxes that share a span across are compared.
    static bool Overlaps(List<SvgPath> paths)
    {
        if (paths.Count < 2)
        {
            return false;
        }

        var boxes = paths.Select(Bounds).OrderBy(_ => _.Left).ToArray();
        for (var index = 0; index < boxes.Length; index++)
        {
            for (var later = index + 1; later < boxes.Length && boxes[later].Left < boxes[index].Right; later++)
            {
                if (boxes[later].Top < boxes[index].Bottom && boxes[index].Top < boxes[later].Bottom)
                {
                    return true;
                }
            }
        }

        return false;
    }

    static (double Left, double Top, double Right, double Bottom) Bounds(SvgPath path)
    {
        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        foreach (var figure in path.Figures)
        {
            Include(figure.StartX, figure.StartY);
            foreach (var segment in figure.Segments)
            {
                Include(segment.X, segment.Y);
                if (segment.IsCurve)
                {
                    Include(segment.Control1X, segment.Control1Y);
                    Include(segment.Control2X, segment.Control2Y);
                }
            }
        }

        // A mitred corner reaches further than half the width, by up to the miter limit.
        var reach = path.Stroke is { } stroke ? stroke.Width / 2 * Math.Max(1, stroke.MiterLimit) : 0;
        return (left - reach, top - reach, right + reach, bottom + reach);

        void Include(double x, double y)
        {
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
    }

    static void Emit(List<SvgFigure> figures, SvgMatrix matrix, Style style, double opacity, SvgClip? clip, List<SvgPath> output, bool fillable)
    {
        if (figures.Count == 0 || !style.Visible)
        {
            return;
        }

        var fills = fillable && Paints(style.Fill, style.FillOpacity * opacity);
        var strokes = style.StrokeWidth > 0 && Paints(style.Stroke, style.StrokeOpacity * opacity);
        if (!fills && !strokes)
        {
            return;
        }

        if (strokes)
        {
            if (style.Dashed)
            {
                throw new UnsupportedException("a dashed stroke");
            }

            // Squashed or skewed, a stroke is no longer one width all the way round.
            if (!matrix.IsUniform)
            {
                throw new UnsupportedException("a stroke under a transform that squashes or skews it");
            }
        }

        // The element's opacity is of its fill and stroke drawn together — see Fade.
        if (opacity < 1 && fills && strokes)
        {
            throw new UnsupportedException("opacity on a shape that is both filled and stroked");
        }

        output.Add(new(
            Transform(figures, matrix),
            fills ? style.Fill.ColorHex : null,
            style.Fill.Alpha * style.FillOpacity * opacity,
            style.EvenOdd,
            strokes ? new SvgStroke(style.Stroke.ColorHex!, style.Stroke.Alpha * style.StrokeOpacity * opacity, style.StrokeWidth * matrix.Scale, style.Cap, style.Join, style.MiterLimit) : null,
            clip));
    }

    // Whether a paint puts anything on the page — and the one place a paint the reader cannot draw
    // stops the reading, since here it is certain to be used.
    static bool Paints(Paint paint, double opacity)
    {
        if (paint.Unsupported != null)
        {
            throw new UnsupportedException(paint.Unsupported);
        }

        return paint.ColorHex != null && paint.Alpha * opacity > 0;
    }

    // The clip a clip-path names, in the space of the element it is set on. One shape is one region;
    // several would be their union, which a single path under a single rule cannot always state
    // (two overlapping shapes wound opposite ways cancel under nonzero), so more than one is given up.
    SvgClip ReadClip(string value, SvgMatrix matrix, SvgClip? parent)
    {
        if (ReadReference(value) is not { } id || !elementsById.TryGetValue(id, out var clipPath) || clipPath.Name != svg + "clipPath")
        {
            throw new UnsupportedException($"the clip-path '{value}'");
        }

        if (clipPath.Attribute("clipPathUnits")?.Value == "objectBoundingBox")
        {
            throw new UnsupportedException("a clipPath in bounding-box units");
        }

        var declared = Declare(clipPath);
        if (declared.ContainsKey("clip-path"))
        {
            throw new UnsupportedException("a clipPath that is itself clipped");
        }

        matrix = Transformed(clipPath, matrix);
        var style = Inherit(new(), declared);
        XElement? shape = null;
        foreach (var child in clipPath.Elements())
        {
            if (child.Name.Namespace != svg || silentElements.Contains(child.Name.LocalName))
            {
                continue;
            }

            var childDeclared = Declare(child);
            if (childDeclared.TryGetValue("display", out var display) && display == "none")
            {
                continue;
            }

            if (shape != null)
            {
                throw new UnsupportedException("a clipPath of more than one shape");
            }

            if (childDeclared.ContainsKey("clip-path"))
            {
                throw new UnsupportedException("a clipPath whose shape is itself clipped");
            }

            shape = child;
            style = Inherit(style, childDeclared);
            matrix = Transformed(child, matrix);
            if (child.Name == svg + "use")
            {
                // A use inside a clipPath may only bring in a shape, and that shape's own transform
                // and clip-rule apply as they would where it stands.
                shape = Resolve(child);
                matrix = matrix.Multiply(SvgMatrix.Translation(ReadLength(child, "x"), ReadLength(child, "y")));
                style = Inherit(style, Declare(shape));
                matrix = Transformed(shape, matrix);
            }
        }

        if (shape == null || !style.Visible)
        {
            return new([], false, parent);
        }

        if (shape.Name.Namespace != svg || shape.Name.LocalName is not ("path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon"))
        {
            throw new UnsupportedException($"a clipPath holding the {shape.Name.LocalName} element");
        }

        return new(Transform(ReadShape(shape), matrix), style.ClipEvenOdd, parent);
    }

    static SvgMatrix Transformed(XElement element, SvgMatrix matrix)
    {
        if (element.Attribute("transform") is not { } transform)
        {
            return matrix;
        }

        if (!SvgMatrix.TryParse(transform.Value, out var local))
        {
            throw new UnsupportedException($"the transform '{transform.Value}'");
        }

        return matrix.Multiply(local);
    }

    // The element a use points at, by href or the older xlink:href.
    XElement Resolve(XElement use)
    {
        var reference = (use.Attribute("href") ?? use.Attribute(xlink + "href"))?.Value.Trim();
        if (reference is not {Length: > 1} || reference[0] != '#' || !elementsById.TryGetValue(reference[1..], out var target))
        {
            throw new UnsupportedException($"a use of '{reference}'");
        }

        return target;
    }

    // The id in url(#id), with or without quotes. Null for a reference out of the document.
    static string? ReadReference(string value)
    {
        var close = value.IndexOf(')');
        if (!value.StartsWith("url(", StringComparison.Ordinal) || close < 0)
        {
            return null;
        }

        var target = value[4..close].Trim().Trim('"', '\'');
        return target.StartsWith('#') ? target[1..] : null;
    }

    // An element's figures, in its own coordinates.
    List<SvgFigure> ReadShape(XElement element)
    {
        var builder = new SvgFigureBuilder();
        switch (element.Name.LocalName)
        {
            case "path":
            {
                var data = element.Attribute("d")?.Value;
                if (data != null && data.Trim() != "none" && !SvgPathData.TryParse(data, builder))
                {
                    throw new UnsupportedException("path data that does not parse");
                }

                break;
            }
            case "rect":
            {
                var width = ReadLength(element, "width");
                var height = ReadLength(element, "height");
                if (width <= 0 || height <= 0)
                {
                    break;
                }

                // A corner radius given on one axis only is used on both, and neither reaches past
                // the middle of its side.
                var radiusX = element.Attribute("rx") != null ? ReadLength(element, "rx") : ReadLength(element, "ry");
                var radiusY = element.Attribute("ry") != null ? ReadLength(element, "ry") : radiusX;
                builder.Rectangle(ReadLength(element, "x"), ReadLength(element, "y"), width, height, Math.Min(Math.Max(0, radiusX), width / 2), Math.Min(Math.Max(0, radiusY), height / 2));
                break;
            }
            case "circle":
            {
                var radius = ReadLength(element, "r");
                if (radius > 0)
                {
                    builder.Ellipse(ReadLength(element, "cx"), ReadLength(element, "cy"), radius, radius);
                }

                break;
            }
            case "ellipse":
            {
                var radiusX = ReadLength(element, "rx");
                var radiusY = ReadLength(element, "ry");
                if (radiusX > 0 && radiusY > 0)
                {
                    builder.Ellipse(ReadLength(element, "cx"), ReadLength(element, "cy"), radiusX, radiusY);
                }

                break;
            }
            case "line":
                builder.MoveTo(ReadLength(element, "x1"), ReadLength(element, "y1"));
                builder.LineTo(ReadLength(element, "x2"), ReadLength(element, "y2"));
                break;
            default:
            {
                // polyline and polygon: pairs of numbers, the polygon closed back to its first.
                if (SvgNumbers.ReadList(element.Attribute("points")?.Value ?? "") is not { } points)
                {
                    throw new UnsupportedException("a points list that does not parse");
                }

                for (var index = 0; index + 1 < points.Count; index += 2)
                {
                    if (index == 0)
                    {
                        builder.MoveTo(points[0], points[1]);
                    }
                    else
                    {
                        builder.LineTo(points[index], points[index + 1]);
                    }
                }

                if (element.Name.LocalName == "polygon")
                {
                    builder.Close();
                }

                break;
            }
        }

        segmentCount += builder.SegmentCount;
        if (segmentCount > maxSegments)
        {
            throw new UnsupportedException($"more than {maxSegments} segments");
        }

        return builder.Build();
    }

    static List<SvgFigure> Transform(List<SvgFigure> figures, SvgMatrix matrix)
    {
        if (matrix == SvgMatrix.Identity)
        {
            return figures;
        }

        var transformed = new List<SvgFigure>(figures.Count);
        foreach (var figure in figures)
        {
            var segments = new SvgSegment[figure.Segments.Count];
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = figure.Segments[index];
                var (x, y) = matrix.Apply(segment.X, segment.Y);
                if (segment.IsCurve)
                {
                    var (control1X, control1Y) = matrix.Apply(segment.Control1X, segment.Control1Y);
                    var (control2X, control2Y) = matrix.Apply(segment.Control2X, segment.Control2Y);
                    segments[index] = SvgSegment.Curve(control1X, control1Y, control2X, control2Y, x, y);
                }
                else
                {
                    segments[index] = SvgSegment.Line(x, y);
                }
            }

            var (startX, startY) = matrix.Apply(figure.StartX, figure.StartY);
            transformed.Add(new(startX, startY, segments, figure.Closed));
        }

        return transformed;
    }

    static double ReadLength(XElement element, string name) =>
        element.Attribute(name) is { } attribute ? ParseLength(attribute.Value) : 0;

    // A length in user units: a bare number, or one in an absolute unit at CSS's 96 to the inch.
    // A percentage or an em depends on a viewport or a font the reader does not model.
    static double ParseLength(string text)
    {
        var span = text.AsSpan().Trim();
        var position = 0;
        if (!SvgNumbers.TryRead(span, ref position, out var value))
        {
            throw new UnsupportedException($"the length '{text}'");
        }

        return span[position..].Trim() switch
        {
            "" or "px" => value,
            "pt" => value * 96 / 72,
            "pc" => value * 16,
            "in" => value * 96,
            "cm" => value * 96 / 2.54,
            "mm" => value * 96 / 25.4,
            _ => throw new UnsupportedException($"the length '{text}'")
        };
    }
}
