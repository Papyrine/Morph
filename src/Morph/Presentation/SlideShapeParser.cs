using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

/// <summary>
/// Walks a slide's <c>p:spTree</c> and emits one absolutely-positioned float per shape.
///
/// A slide is a fixed canvas of absolutely-positioned DrawingML, which is exactly what the shared
/// layout engine already models as a body float — so nothing here is slide-specific below the model:
/// <c>p:sp</c> becomes a <see cref="FloatingTextBoxElement"/> or <see cref="FloatingShapeElement"/>,
/// <c>p:pic</c> a <see cref="FloatingImageElement"/>, and <c>p:grpSp</c> recurses under a composed
/// <see cref="SlideTransform"/>.
///
/// <b>Anchoring is load-bearing and non-obvious.</b> Floats use <see cref="HorizontalAnchor.Page"/>
/// but <see cref="VerticalAnchor.Paragraph"/>, not <c>VerticalAnchor.Page</c>. A page-anchored float
/// is treated by the fragmenter as having an absolute Y, so it parks in its pending queue and binds
/// to a page only when a visible line or row lands (<c>Fragmenter.AddBodyFloat</c> /
/// <c>ResolvePendingFloats</c>). A slide has no flow content at all, so every deck's shapes would
/// collapse onto the last page. A paragraph anchor binds immediately to the page being built, and
/// because slide <see cref="PageSettings"/> carry zero margins the flow cursor sits at 0 and never
/// moves — so <c>y + offset</c> is exactly the absolute slide coordinate.
/// </summary>
sealed class SlideShapeParser(
    ThemeColors? themeColors,
    ThemeFonts? themeFonts,
    string defaultFont,
    Func<OpenXmlPart, byte[]> partBytes,
    double slideWidthPoints,
    double slideHeightPoints,
    TableStylesPart? tableStylesPart)
{
    const double emusPerPoint = 914400.0 / 72.0;

    readonly DrawingTextParser textParser = new(themeColors, themeFonts, defaultFont);
    readonly SlideTableParser tableParser = new(themeColors, new(themeColors, themeFonts, defaultFont));

    /// <summary>
    /// Emits the slide's shapes in <c>spTree</c> order. Paint order is document order, which is also
    /// PowerPoint's z-order, so <see cref="FloatingShapeElement.RelativeHeight"/> is the running
    /// ordinal rather than anything read from the file.
    /// </summary>
    public List<DocumentElement> ParseSlide(SlidePart slidePart, SlidePlaceholders placeholders, OpenXmlElement? defaultTextStyle)
    {
        var elements = new List<DocumentElement>();
        var tree = slidePart.Slide?.CommonSlideData?.ShapeTree;
        if (tree == null)
        {
            return elements;
        }

        var layoutPart = slidePart.SlideLayoutPart;
        var masterPart = layoutPart?.SlideMasterPart;
        var master = masterPart?.SlideMaster;
        var ordinal = 0u;

        // Bottom to top: background, then the master's decoration, then the layout's, then the
        // slide's own shapes. Emission order is paint order.
        if (ResolveBackground(slidePart, layoutPart, masterPart, ordinal) is { } background)
        {
            elements.Add(background);
            ordinal++;
        }

        // A layout may suppress the master's decoration (p:sldLayout/@showMasterSp), and a slide may
        // suppress whatever it inherits (p:sld/@showMasterShapes); both default to true.
        if (slidePart.Slide?.ShowMasterShapes?.Value != false &&
            layoutPart?.SlideLayout?.ShowMasterShapes?.Value != false)
        {
            WalkDecoration(master?.CommonSlideData?.ShapeTree, masterPart, placeholders, defaultTextStyle, master, elements, ref ordinal);
        }

        WalkDecoration(layoutPart?.SlideLayout?.CommonSlideData?.ShapeTree, layoutPart, placeholders, defaultTextStyle, master, elements, ref ordinal);

        Walk(tree, SlideTransform.Identity, slidePart, placeholders, defaultTextStyle, master, elements, ref ordinal);
        return elements;
    }

    /// <summary>
    /// Emits a layout's or master's own decoration — rules, frames, banners, background art.
    ///
    /// Placeholders are skipped deliberately. A layout or master placeholder is a template slot, and
    /// its text is prompt text PowerPoint never renders; the slide's own placeholder shape is what
    /// carries real content, inheriting geometry and style from these through
    /// <see cref="SlidePlaceholders"/>. Emitting them here would draw every deck's prompt text
    /// twice over.
    /// </summary>
    void WalkDecoration(
        OpenXmlElement? tree,
        OpenXmlPart? hostPart,
        SlidePlaceholders placeholders,
        OpenXmlElement? defaultTextStyle,
        P.SlideMaster? master,
        List<DocumentElement> elements,
        ref uint ordinal)
    {
        if (tree == null)
        {
            return;
        }

        foreach (var child in tree.ChildElements)
        {
            if (child is P.Shape shape && SlidePlaceholders.Placeholder(shape) != null)
            {
                continue;
            }

            if (child is P.Picture or P.Shape or P.GroupShape or P.ConnectionShape)
            {
                WalkOne(child, hostPart, placeholders, defaultTextStyle, master, elements, ref ordinal);
            }
        }
    }

    /// <summary>
    /// The full-bleed shape standing in for the slide's background fill, or null when nothing in the
    /// chain declares one. <c>p:bg</c> resolves slide → layout → master, first declaration winning.
    /// </summary>
    DocumentElement? ResolveBackground(SlidePart slidePart, SlideLayoutPart? layoutPart, SlideMasterPart? masterPart, uint ordinal)
    {
        var background =
            slidePart.Slide?.CommonSlideData?.Background ??
            (OpenXmlElement?) layoutPart?.SlideLayout?.CommonSlideData?.Background ??
            masterPart?.SlideMaster?.CommonSlideData?.Background;
        if (background == null)
        {
            return null;
        }

        // Either a direct fill (p:bgPr) or a reference into the theme's background fill styles
        // (p:bgRef), whose own child is the colour to use. Only the colour of a bgRef is honoured —
        // the indexed fill style it points at is approximated as a plain solid.
        var properties = background.GetFirstChild<P.BackgroundProperties>();
        var reference = background.GetFirstChild<P.BackgroundStyleReference>();

        if (properties?.GetFirstChild<A.GradientFill>() is { } gradient)
        {
            return BackgroundShape(ShapeParser.ExtractGradientFill(gradient, themeColors), null, 1, ordinal);
        }

        var solid = properties?.GetFirstChild<A.SolidFill>();
        var colorSource = (OpenXmlElement?) solid ?? reference;
        if (colorSource == null)
        {
            return null;
        }

        var color = ShapeParser.ExtractSolidFillColor(colorSource, themeColors);
        if (color == null)
        {
            return null;
        }

        return BackgroundShape(null, color, solid != null ? ShapeParser.ExtractSolidFillAlpha(solid) : 1, ordinal);
    }

    FloatingShapeElement BackgroundShape(GradientFill? gradient, string? color, double alpha, uint ordinal) =>
        new()
        {
            WidthPoints = slideWidthPoints,
            HeightPoints = slideHeightPoints,
            HorizontalPositionPoints = 0,
            VerticalPositionPoints = 0,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Paragraph,
            FillColorHex = color,
            FillAlpha = alpha,
            Gradient = gradient,
            RelativeHeight = ordinal
        };

    void Walk(
        OpenXmlElement tree,
        SlideTransform transform,
        OpenXmlPart? hostPart,
        SlidePlaceholders placeholders,
        OpenXmlElement? defaultTextStyle,
        P.SlideMaster? master,
        List<DocumentElement> elements,
        ref uint ordinal)
    {
        foreach (var child in tree.ChildElements)
        {
            WalkOne(child, hostPart, placeholders, defaultTextStyle, master, elements, ref ordinal, transform);
        }
    }

    void WalkOne(
        OpenXmlElement child,
        OpenXmlPart? hostPart,
        SlidePlaceholders placeholders,
        OpenXmlElement? defaultTextStyle,
        P.SlideMaster? master,
        List<DocumentElement> elements,
        ref uint ordinal,
        SlideTransform transform = default)
    {
        if (transform == default)
        {
            transform = SlideTransform.Identity;
        }

        {
            switch (child)
            {
                case P.Shape shape:
                    if (ParseShape(shape, transform, placeholders, defaultTextStyle, master, ordinal) is { } parsed)
                    {
                        elements.Add(parsed);
                        ordinal++;
                    }

                    break;

                case P.Picture picture:
                    if (ParsePicture(picture, transform, hostPart, placeholders, ordinal) is { } image)
                    {
                        elements.Add(image);
                        ordinal++;
                    }

                    break;

                // A connection shape is a line: no fill, an outline, and geometry that is just the
                // diagonal of its box. Templates use them for rules and frames — the four coral
                // frame lines on the memo layout are cxnSp, not sp.
                case P.ConnectionShape connector:
                    if (ParseConnector(connector, transform, ordinal) is { } line)
                    {
                        elements.Add(line);
                        ordinal++;
                    }

                    break;

                case P.GraphicFrame frame:
                    foreach (var element in ParseGraphicFrame(frame, transform, hostPart, ordinal))
                    {
                        elements.Add(element);
                        ordinal++;
                    }

                    break;

                case P.GroupShape group:
                    Walk(
                        group,
                        transform.Compose(group.GroupShapeProperties?.TransformGroup),
                        hostPart,
                        placeholders,
                        defaultTextStyle,
                        master,
                        elements,
                        ref ordinal);
                    break;
            }
        }
    }

    DocumentElement? ParseShape(
        P.Shape shape,
        SlideTransform transform,
        SlidePlaceholders placeholders,
        OpenXmlElement? defaultTextStyle,
        P.SlideMaster? master,
        uint ordinal)
    {
        var geometry = ResolveGeometry(placeholders.ResolveTransform(shape), transform);
        if (geometry is not { } box)
        {
            return null;
        }

        var shapeProperties = shape.ShapeProperties;
        var fill = ResolveFill(shapeProperties, shape.ShapeStyle);
        var outline = ResolveOutline(shapeProperties, shape.ShapeStyle);

        if (HasVisibleText(shape.TextBody))
        {
            var chain = BuildChain(shape, placeholders, defaultTextStyle, master);
            var scale = FontScale(shape.TextBody);
            return new FloatingTextBoxElement
            {
                Content = textParser.Parse(shape.TextBody!, chain, scale),
                WidthPoints = box.Width,
                HeightPoints = box.Height,
                HorizontalPositionPoints = box.X,
                VerticalPositionPoints = box.Y,
                HorizontalAnchor = HorizontalAnchor.Page,
                VerticalAnchor = VerticalAnchor.Paragraph,
                BackgroundColorHex = fill.ColorHex,
                LineColorHex = outline.ColorHex,
                LineWidthPoints = outline.WidthPoints ?? 0,
                Subpaths = ResolveSubpaths(shapeProperties, box.Width, box.Height),
                RotationDegrees = box.RotationDegrees,
                RelativeHeight = ordinal
            };
        }

        // A shape with neither fill, outline nor text paints nothing — placeholders left empty by the
        // author are the bulk of these, and PowerPoint does not render their prompt text either.
        if (fill.ColorHex == null && fill.Gradient == null && outline.ColorHex == null)
        {
            return null;
        }

        return new FloatingShapeElement
        {
            WidthPoints = box.Width,
            HeightPoints = box.Height,
            HorizontalPositionPoints = box.X,
            VerticalPositionPoints = box.Y,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Paragraph,
            FillColorHex = fill.ColorHex,
            FillAlpha = fill.Alpha,
            Gradient = fill.Gradient,
            LineColorHex = outline.ColorHex,
            LineWidthPoints = outline.WidthPoints,
            Preset = ResolvePreset(shapeProperties),
            Subpaths = ResolveSubpaths(shapeProperties, box.Width, box.Height),
            RotationDegrees = box.RotationDegrees,
            FlipHorizontal = box.FlipHorizontal,
            FlipVertical = box.FlipVertical,
            RelativeHeight = ordinal
        };
    }

    /// <summary>
    /// Resolves a relationship on the part that owns it. <c>GetPartById</c> THROWS
    /// <see cref="ArgumentOutOfRangeException"/> for an unknown id rather than returning null, and an
    /// id that is perfectly valid on a slide means nothing on its layout or master — so a walk that
    /// spans all three has to treat a miss as "absent" instead of letting it abort the parse.
    /// </summary>
    static bool TryGetPart<T>(OpenXmlPart? host, string relationshipId, out T? part)
        where T : OpenXmlPart
    {
        part = null;
        if (host == null)
        {
            return false;
        }

        try
        {
            if (host.GetPartById(relationshipId) is T typed)
            {
                part = typed;
                return true;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return false;
    }

    DocumentElement? ParseConnector(P.ConnectionShape connector, SlideTransform transform, uint ordinal)
    {
        var shapeProperties = connector.ShapeProperties;
        var geometry = ResolveGeometry(shapeProperties?.Transform2D, transform);
        if (geometry is not { } box)
        {
            return null;
        }

        var outline = ResolveOutline(shapeProperties, connector.ShapeStyle);
        if (outline.ColorHex == null)
        {
            return null;
        }

        // A straight connector runs corner to corner of its box; the flips choose WHICH diagonal.
        // The contour is emitted already mirrored rather than leaving the flip flags set, so the
        // result does not depend on how a painter chooses to apply flips to a subpath.
        var startX = box.FlipHorizontal ? 1d : 0d;
        var startY = box.FlipVertical ? 1d : 0d;

        return new FloatingShapeElement
        {
            WidthPoints = box.Width,
            HeightPoints = box.Height,
            HorizontalPositionPoints = box.X,
            VerticalPositionPoints = box.Y,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Paragraph,
            LineColorHex = outline.ColorHex,
            LineWidthPoints = outline.WidthPoints,
            Subpaths = [[(startX, startY), (1 - startX, 1 - startY)]],
            RotationDegrees = box.RotationDegrees,
            RelativeHeight = ordinal
        };
    }

    DocumentElement? ParsePicture(P.Picture picture, SlideTransform transform, OpenXmlPart? hostPart, SlidePlaceholders placeholders, uint ordinal)
    {
        var placeholder = picture.NonVisualPictureProperties?
            .ApplicationNonVisualDrawingProperties?
            .GetFirstChild<P.PlaceholderShape>();
        var geometry = ResolveGeometry(
            placeholders.ResolveTransform(picture.ShapeProperties?.Transform2D, placeholder),
            transform);
        if (geometry is not { } box)
        {
            return null;
        }

        if (picture.BlipFill == null || hostPart == null)
        {
            return null;
        }

        var (data, contentType) = ShapeParser.ExtractBlipFillImage(picture.BlipFill, hostPart, partBytes);
        if (data is not { Length: > 0 })
        {
            return null;
        }

        return new FloatingImageElement
        {
            ImageData = data,
            ContentType = contentType,
            WidthPoints = box.Width,
            HeightPoints = box.Height,
            HorizontalPositionPoints = box.X,
            VerticalPositionPoints = box.Y,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Paragraph,
            WrapType = WrapType.None,
            RotationDegrees = box.RotationDegrees,
            FlipHorizontal = box.FlipHorizontal,
            FlipVertical = box.FlipVertical,
            Crop = ReadCrop(picture.BlipFill),
            Description = picture.NonVisualPictureProperties?
                .NonVisualDrawingProperties?.Description?.Value,
            RelativeHeight = ordinal
        };
    }

    /// <summary>
    /// The DrawingML namespace SmartArt's pre-laid-out fallback lives in. Its shapes are read through
    /// <see cref="OpenXmlElement"/> rather than generated types because the payload is entirely
    /// standard DrawingML — <c>a:xfrm</c>, <c>a:prstGeom</c>, <c>a:solidFill</c> — wrapped in
    /// <c>dsp:</c> elements.
    /// </summary>
    const string diagramDrawingNamespace = "http://schemas.microsoft.com/office/drawing/2008/diagram";

    /// <summary>
    /// A <c>p:graphicFrame</c> holds a table, a chart or SmartArt, distinguished by the
    /// <c>a:graphicData/@uri</c>.
    ///
    /// SmartArt is the interesting case, and it renders properly here: every deck ships a
    /// <c>diagramDrawing</c> part holding the diagram ALREADY laid out as ordinary absolutely
    /// positioned DrawingML shapes, so no diagram engine is needed. (This differs from the DOCX
    /// corpus, where no such fallback ships and SmartArt only reserves space — see
    /// docs/word-features.md.)
    /// </summary>
    IEnumerable<DocumentElement> ParseGraphicFrame(P.GraphicFrame frame, SlideTransform transform, OpenXmlPart? hostPart, uint ordinal)
    {
        var graphicData = frame.Graphic?.GraphicData;
        var uri = graphicData?.Uri?.Value;

        if (uri == "http://schemas.openxmlformats.org/drawingml/2006/table")
        {
            if (ParseTable(frame, graphicData!, transform, ordinal) is { } table)
            {
                yield return table;
            }

            yield break;
        }

        if (uri != "http://schemas.openxmlformats.org/drawingml/2006/diagram")
        {
            // A chart frame reserves nothing today, matching the DOCX policy of leaving the slot
            // blank rather than drawing a wrong chart.
            yield break;
        }

        var drawing = ResolveDiagramDrawing(graphicData!, hostPart);
        if (drawing == null)
        {
            yield break;
        }

        // dsp shapes are authored relative to the frame's origin, so the frame contributes a pure
        // translation on top of whatever group transform is already in force.
        var offsetX = frame.Transform?.Offset?.X?.Value ?? 0;
        var offsetY = frame.Transform?.Offset?.Y?.Value ?? 0;
        var (frameX, frameY, _, _) = transform.Apply(offsetX, offsetY, 0, 0);
        var frameTransform = transform with { OffsetX = frameX, OffsetY = frameY };

        var index = ordinal;
        foreach (var shape in drawing.Descendants()
                     .Where(_ => _ is { LocalName: "sp", NamespaceUri: diagramDrawingNamespace }))
        {
            if (ParseDiagramShape(shape, frameTransform, index) is { } parsed)
            {
                yield return parsed;
                index++;
            }
        }
    }

    /// <summary>
    /// A table frame, wrapped in a text box rather than emitted as a floating table.
    ///
    /// The wrapper is what gives a slide table absolute placement. The fragmenter's floating-table
    /// path offers two anchors and neither fits: a text anchor advances the flow cursor, which would
    /// shift every shape emitted after it, while a page anchor defers the rows to the pending-float
    /// queue that a slide never drains. A text box places its content at absolute coordinates,
    /// touches no flow state, and lays out a nested table through the ordinary cell-content path.
    /// </summary>
    DocumentElement? ParseTable(P.GraphicFrame frame, A.GraphicData graphicData, SlideTransform transform, uint ordinal)
    {
        var table = graphicData.GetFirstChild<A.Table>();
        if (table == null)
        {
            return null;
        }

        var geometry = ResolveGeometry(frame.Transform, transform);
        if (geometry is not { } box)
        {
            return null;
        }

        var parsed = tableParser.Parse(table, tableStylesPart);
        if (parsed == null)
        {
            return null;
        }

        return new FloatingTextBoxElement
        {
            Content = [parsed],
            WidthPoints = box.Width,
            HeightPoints = box.Height,
            HorizontalPositionPoints = box.X,
            VerticalPositionPoints = box.Y,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Paragraph,
            RelativeHeight = ordinal
        };
    }

    /// <summary>
    /// Walks graphicFrame → <c>dgm:relIds/@r:dm</c> → the diagram data part → its
    /// <c>dsp:dataModelExt/@relId</c> → the drawing part. The drawing relationship hangs off the
    /// SLIDE, not off the data part, which is why the data part has to be read to find it.
    /// </summary>
    static OpenXmlElement? ResolveDiagramDrawing(A.GraphicData graphicData, OpenXmlPart? hostPart)
    {
        var relationshipIds = graphicData.ChildElements.FirstOrDefault(_ => _.LocalName == "relIds");
        var dataId = relationshipIds?.GetAttributes()
            .FirstOrDefault(_ => _.LocalName == "dm").Value;
        if (dataId == null || hostPart == null || !TryGetPart(hostPart, dataId, out DiagramDataPart? dataPart))
        {
            return null;
        }

        var drawingId = dataPart!.DataModelRoot?.Descendants()
            .FirstOrDefault(_ => _ is { LocalName: "dataModelExt", NamespaceUri: diagramDrawingNamespace })?
            .GetAttributes()
            .FirstOrDefault(_ => _.LocalName == "relId").Value;
        if (drawingId == null || !TryGetPart(hostPart, drawingId, out DiagramPersistLayoutPart? drawingPart))
        {
            return null;
        }

        return drawingPart!.RootElement;
    }

    DocumentElement? ParseDiagramShape(OpenXmlElement shape, SlideTransform transform, uint ordinal)
    {
        var shapeProperties = shape.ChildElements.FirstOrDefault(_ => _.LocalName == "spPr");
        var geometry = ResolveGeometry(shapeProperties?.GetFirstChild<A.Transform2D>(), transform);
        if (geometry is not { } box)
        {
            return null;
        }

        var textBody = shape.ChildElements.FirstOrDefault(_ => _.LocalName == "txBody");
        var style = shape.ChildElements.FirstOrDefault(_ => _.LocalName == "style");
        var fill = ResolveFill(shapeProperties, style);
        var outline = ResolveOutline(shapeProperties, style);

        if (HasVisibleText(textBody))
        {
            // A diagram node's text carries its own a:pPr/a:rPr in full, so the cascade is empty.
            return new FloatingTextBoxElement
            {
                Content = textParser.Parse(textBody!, new([])),
                WidthPoints = box.Width,
                HeightPoints = box.Height,
                HorizontalPositionPoints = box.X,
                VerticalPositionPoints = box.Y,
                HorizontalAnchor = HorizontalAnchor.Page,
                VerticalAnchor = VerticalAnchor.Paragraph,
                BackgroundColorHex = fill.ColorHex,
                LineColorHex = outline.ColorHex,
                LineWidthPoints = outline.WidthPoints ?? 0,
                Subpaths = ResolveSubpaths(shapeProperties, box.Width, box.Height),
                RotationDegrees = box.RotationDegrees,
                RelativeHeight = ordinal
            };
        }

        if (fill.ColorHex == null && fill.Gradient == null && outline.ColorHex == null)
        {
            return null;
        }

        return new FloatingShapeElement
        {
            WidthPoints = box.Width,
            HeightPoints = box.Height,
            HorizontalPositionPoints = box.X,
            VerticalPositionPoints = box.Y,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Paragraph,
            FillColorHex = fill.ColorHex,
            FillAlpha = fill.Alpha,
            Gradient = fill.Gradient,
            LineColorHex = outline.ColorHex,
            LineWidthPoints = outline.WidthPoints,
            Preset = ResolvePreset(shapeProperties),
            Subpaths = ResolveSubpaths(shapeProperties, box.Width, box.Height),
            RotationDegrees = box.RotationDegrees,
            FlipHorizontal = box.FlipHorizontal,
            FlipVertical = box.FlipVertical,
            RelativeHeight = ordinal
        };
    }

    /// <summary>
    /// Maps a shape's <c>a:xfrm</c> through the accumulated group transform into slide points.
    /// Null when nothing in the placeholder chain declared geometry, or the shape is degenerate.
    /// </summary>
    static ResolvedBox? ResolveGeometry(OpenXmlElement? shapeTransform, SlideTransform transform)
    {
        if (shapeTransform == null)
        {
            return null;
        }

        // Read generically rather than off A.Transform2D: a shape's a:xfrm, a graphic frame's
        // p:xfrm and a diagram shape's dsp a:xfrm are three different generated types wrapping the
        // identical a:off / a:ext / rot / flip payload.
        var offset = shapeTransform.GetFirstChild<A.Offset>();
        var extents = shapeTransform.GetFirstChild<A.Extents>();
        var attributes = shapeTransform.GetAttributes();

        var offsetX = offset?.X?.Value ?? 0;
        var offsetY = offset?.Y?.Value ?? 0;
        var extentX = extents?.Cx?.Value ?? 0;
        var extentY = extents?.Cy?.Value ?? 0;

        // Zero on ONE axis is legitimate and common: a horizontal rule is a connector with cy="0",
        // a vertical one has cx="0". Rejecting those (as an area test would) drops every divider a
        // template draws. Only a negative extent, or a shape with no extent at all, is degenerate.
        if (extentX < 0 || extentY < 0 || (extentX == 0 && extentY == 0))
        {
            return null;
        }

        var (x, y, width, height) = transform.Apply(offsetX, offsetY, extentX, extentY);

        return new(
            x / emusPerPoint,
            y / emusPerPoint,
            width / emusPerPoint,
            height / emusPerPoint,
            // rot is 60000ths of a degree, clockwise.
            (long.TryParse(Attribute(attributes, "rot"), out var rotation) ? rotation : 0) / 60000.0,
            Attribute(attributes, "flipH") is "1" or "true",
            Attribute(attributes, "flipV") is "1" or "true");
    }

    static string? Attribute(IEnumerable<OpenXmlAttribute> attributes, string name)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.LocalName == name)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    readonly record struct ResolvedBox(
        double X,
        double Y,
        double Width,
        double Height,
        double RotationDegrees,
        bool FlipHorizontal,
        bool FlipVertical);

    /// <summary>
    /// The shape's fill: a direct <c>a:solidFill</c>/<c>a:gradFill</c> first, then the
    /// <c>p:style/a:fillRef</c> theme reference. The reference matters — template shapes routinely
    /// carry no direct fill at all and take their entire colour from it.
    /// </summary>
    (string? ColorHex, double Alpha, GradientFill? Gradient) ResolveFill(OpenXmlElement? properties, OpenXmlElement? style)
    {
        if (properties?.GetFirstChild<A.NoFill>() != null)
        {
            return (null, 1, null);
        }

        if (properties?.GetFirstChild<A.SolidFill>() is { } solid)
        {
            return (ShapeParser.ExtractSolidFillColor(solid, themeColors), ShapeParser.ExtractSolidFillAlpha(solid), null);
        }

        if (properties?.GetFirstChild<A.GradientFill>() is { } gradient)
        {
            return (null, 1, ShapeParser.ExtractGradientFill(gradient, themeColors));
        }

        if (style?.GetFirstChild<A.FillReference>() is { } fillReference)
        {
            return (ShapeParser.ExtractSolidFillColor(fillReference, themeColors), 1, null);
        }

        return (null, 1, null);
    }

    (string? ColorHex, double? WidthPoints) ResolveOutline(OpenXmlElement? properties, OpenXmlElement? style)
    {
        var outline = properties?.GetFirstChild<A.Outline>();
        if (outline?.GetFirstChild<A.NoFill>() != null)
        {
            return (null, null);
        }

        var width = outline?.Width?.Value is { } emu ? emu / emusPerPoint : (double?) null;

        if (outline?.GetFirstChild<A.SolidFill>() is { } solid)
        {
            return (ShapeParser.ExtractSolidFillColor(solid, themeColors), width ?? 1);
        }

        if (style?.GetFirstChild<A.LineReference>() is { } lineReference &&
            ShapeParser.ExtractSolidFillColor(lineReference, themeColors) is { } referenced)
        {
            return (referenced, width ?? 1);
        }

        return (null, null);
    }

    static PresetShape ResolvePreset(OpenXmlElement? properties) =>
        properties?.GetFirstChild<A.PresetGeometry>()?.Preset?.Value == A.ShapeTypeValues.Ellipse
            ? PresetShape.Ellipse
            : PresetShape.Rect;

    static IReadOnlyList<IReadOnlyList<(double X, double Y)>>? ResolveSubpaths(OpenXmlElement? properties, double width, double height)
    {
        if (properties == null)
        {
            return null;
        }

        // A custom geometry wins; otherwise a preset richer than rect/ellipse is built into contours.
        return ShapeParser.ExtractSubpaths(properties) ??
               PresetShapeGeometry.TryBuild(properties.GetFirstChild<A.PresetGeometry>(), width, height);
    }

    static ImageCrop? ReadCrop(OpenXmlElement blipFill)
    {
        var rectangle = blipFill.GetFirstChild<A.SourceRectangle>();
        if (rectangle == null)
        {
            return null;
        }

        // a:srcRect edges are thousandths of a percent of the source image.
        const double scale = 100000.0;
        var crop = new ImageCrop
        {
            Left = (rectangle.Left?.Value ?? 0) / scale,
            Top = (rectangle.Top?.Value ?? 0) / scale,
            Right = (rectangle.Right?.Value ?? 0) / scale,
            Bottom = (rectangle.Bottom?.Value ?? 0) / scale
        };

        return crop.IsCropped ? crop : null;
    }

    /// <summary>
    /// The cascade a shape's text resolves through. Ordered most specific first; the master's
    /// <c>p:txStyles</c> entry is chosen by the placeholder's kind, matching PowerPoint.
    /// </summary>
    static TextStyleChain BuildChain(
        P.Shape shape,
        SlidePlaceholders placeholders,
        OpenXmlElement? defaultTextStyle,
        P.SlideMaster? master)
    {
        var placeholder = SlidePlaceholders.Placeholder(shape);

        // Layout THEN master placeholder list styles, then the master's txStyles list for this
        // placeholder kind, then the presentation default. Taking only the nearest match would drop
        // the master's contribution whenever a layout declares the placeholder at all — which every
        // layout does, usually with an empty a:lstStyle.
        var sources = new List<OpenXmlElement?> { shape.TextBody?.ListStyle };
        sources.AddRange(placeholders.Matches(placeholder).Select(OpenXmlElement? (_) => _.TextBody?.ListStyle));
        sources.Add(MasterStyle(master, placeholder));
        sources.Add(defaultTextStyle);

        return new(sources);
    }

    // The master's title / body / other list for the placeholder's kind, the same three-way split
    // PowerPoint applies. A shape that is not a placeholder takes the "other" list.
    static OpenXmlElement? MasterStyle(P.SlideMaster? master, P.PlaceholderShape? placeholder)
    {
        var styles = master?.TextStyles;
        if (styles == null)
        {
            return null;
        }

        return SlidePlaceholders.TypeOf(placeholder) switch
        {
            "title" => styles.TitleStyle,
            "body" or "subTitle" or "obj" => styles.BodyStyle,
            _ => styles.OtherStyle
        };
    }

    /// <summary>
    /// PowerPoint bakes the shrink it applied to overflowing text into
    /// <c>a:normAutofit/@fontScale</c> (thousandths of a percent). Honouring it is what keeps a
    /// dense content placeholder inside its box — 31 of the 40 corpus decks carry one.
    /// </summary>
    static double FontScale(OpenXmlElement? textBody)
    {
        var scale = textBody?.GetFirstChild<A.BodyProperties>()?
            .GetFirstChild<A.NormalAutoFit>()?.FontScale?.Value;
        return scale is > 0 ? scale.Value / 100000.0 : 1;
    }

    static bool HasVisibleText(OpenXmlElement? textBody) =>
        textBody != null &&
        textBody.Descendants<A.Text>().Any(_ => !string.IsNullOrWhiteSpace(_.Text));
}
