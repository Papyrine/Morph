using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

/// <summary>
/// A drawing: inline and anchored pictures, charts and groups, and how they wrap.
/// </summary>
sealed partial class DocumentParser
{
    /// <summary>
    /// Tries to parse an inline image from a drawing element and returns it as a Run.
    /// Returns null if the drawing is not a simple inline image (e.g., if it's anchored or a group).
    /// </summary>
    Run? TryParseInlineImageRun(Drawing drawing, MainDocumentPart mainPart, RunProperties runProps)
    {
        var hostPart = ResolveHostPart(drawing, mainPart);
        // Use XML-based approach for better namespace handling
        var hasAnchor = drawing.Descendants().Any(_ => _.LocalName == "anchor");
        var hasInline = drawing.Descendants().Any(_ => _.LocalName == "inline");

        // Only handle simple inline images, not anchored images
        if (hasAnchor || !hasInline)
        {
            return null;
        }

        // Inline WordprocessingGroup — parse the contained shapes into an inline shape-group
        // run so the renderer can draw the icon inline (down-arrows on heading rows, accent
        // arrow accents on cover pages, etc.).
        var wpgGroup = drawing.Descendants<WPG.WordprocessingGroup>().FirstOrDefault();
        if (wpgGroup != null)
        {
            return ParseInlineShapeGroupRun(drawing, wpgGroup, hostPart, runProps);
        }

        // An inline chart (a:graphicData holding a c:chart reference to a chart part).
        if (drawing
                .Descendants()
                .FirstOrDefault(_ => _ is
                {
                    LocalName: "chart",
                    Parent.LocalName: "graphicData"
                }) is { } chartReference)
        {
            return ParseInlineChartRun(drawing, chartReference, hostPart, runProps);
        }

        // Standalone inline connector — a single <wps:wsp> with prstGeom prst="line" sitting
        // directly under <a:graphicData> (no wpg:wgp wrapper, no <pic> child). Used by some
        // templates for section divider rules between sub-headings.
        var standaloneWsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
        if (standaloneWsp != null &&
            ShapeParser.IsLineShape(standaloneWsp.GetFirstChild<WPS.ShapeProperties>()))
        {
            return ParseInlineSingleLineRun(drawing, standaloneWsp, runProps);
        }

        // Standalone inline picture- or fill-shape — a single <wps:wsp> sitting directly
        // under <a:graphicData> with no wpg:wgp wrapper, carrying either an <a:blipFill>
        // (Word's "fill a shape with a picture" — cover-letters/09's circular profile photo)
        // or an ordinary solid fill (cover-letters/10's custGeom logo).
        // The gate admits a style-referenced fill too (wps:style/a:fillRef), not just a fill named
        // directly in spPr — see ParseInlineSingleShapeRun — and an unfilled shape whose outline is
        // its whole mark (brochures/04's stroked roof chevrons), so long as it is not a text box.
        if (standaloneWsp?.GetFirstChild<WPS.ShapeProperties>() is { } standaloneProps &&
            (standaloneProps.GetFirstChild<A.BlipFill>() != null ||
             standaloneProps.GetFirstChild<A.SolidFill>() != null ||
             standaloneWsp.GetFirstChild<WPS.ShapeStyle>()?.FillReference != null ||
             (standaloneWsp.GetFirstChild<WPS.TextBoxInfo2>() == null &&
              ReadGroupStroke(standaloneProps, standaloneWsp.GetFirstChild<WPS.ShapeStyle>()?.LineReference).WidthEmu > 0)))
        {
            return ParseInlineSingleShapeRun(drawing, standaloneWsp, hostPart, runProps);
        }

        // Find the pic element
        var pic = drawing.Descendants().FirstOrDefault(_ => _.LocalName == "pic");
        if (pic == null)
        {
            return null;
        }

        // Get the picture's shape properties for size (same approach as ParseDrawingElements)
        var spPr = pic.Elements().FirstOrDefault(_ => _.LocalName == "spPr");

        var xfrm = spPr?.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");
        if (xfrm == null)
        {
            return null;
        }

        // Get image extent from pic's spPr (more reliable than inline.Extent for some documents)
        long picWidth = 0, picHeight = 0;
        var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");
        if (ext != null)
        {
            var attributes = ext.GetAttributes();
            long.TryParse(attributes.AttributeValue("cx"), out picWidth);
            long.TryParse(attributes.AttributeValue("cy"), out picHeight);
        }

        if (picWidth == 0 || picHeight == 0)
        {
            return null;
        }

        // Rotation (a:xfrm/@rot, in 60,000ths of a degree, clockwise)
        var rotationDegrees = ReadRotationDegrees(xfrm);

        // But where the drawing's own wp:extent disagrees with the picture's a:ext, Word draws and
        // lays the picture out at the drawing's: newsletters/02's photo declares 320.65 x 186.94pt
        // there and 324.5 x 189.2 in its a:ext, and Word's XPS draws it 320.58 x 186.9 (2026-10-31).
        // Eight corpus pictures disagree, every one by 0.3-1.3%, the drawing's the smaller.
        if (rotationDegrees == 0 &&
            drawing.Descendants<DW.Extent>().FirstOrDefault() is {Cx.Value: > 0, Cy.Value: > 0} drawingExtent)
        {
            picWidth = drawingExtent.Cx.Value;
            picHeight = drawingExtent.Cy.Value;
        }

        var widthPoints = picWidth / OoxmlUnits.EmusPerPoint;
        var heightPoints = picHeight / OoxmlUnits.EmusPerPoint;
        var (flipHorizontal, flipVertical) = ReadFlips(xfrm);

        // Find the blip (image reference)
        var blipFill = pic.Elements().FirstOrDefault(_ => _.LocalName == "blipFill");
        if (blipFill == null)
        {
            return null;
        }

        var crop = BlipReader.ReadCrop(blipFill);
        var (clipToEllipse, clipSubpaths) = BlipReader.ReadPictureClip(spPr);

        // a:grayscl / a:duotone on the pic's blip (brochures/03's grey circle photo is an inline pic).
        var inlineBlip = blipFill.Descendants().FirstOrDefault(_ => _.LocalName == "blip");
        var (inlineColorEffect, inlineDuotoneColorHex, inlineDuotoneLightColorHex) = BlipReader.ReadBlipColorEffect(
            inlineBlip,
            currentThemeColors);
        var inlineOpacity = BlipReader.ReadBlipOpacity(inlineBlip);

        if (ReadBlipImage(blipFill, hostPart) is not { } image)
        {
            return null;
        }

        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageData = image.Data,
            InlineImageWidthPoints = widthPoints,
            InlineImageHeightPoints = heightPoints,
            InlineImageContentType = image.ContentType,
            InlineImageDescription = ReadImageDescription(pic, drawing),
            InlineImageRasterFallbackData = image.RasterFallbackData,
            InlineImageRasterFallbackContentType = image.RasterFallbackContentType,
            InlineImageRotationDegrees = rotationDegrees,
            InlineImageFlipHorizontal = flipHorizontal,
            InlineImageFlipVertical = flipVertical,
            InlineImageCrop = crop,
            InlineImageColorEffect = inlineColorEffect,
            InlineImageDuotoneColorHex = inlineDuotoneColorHex,
            InlineImageDuotoneLightColorHex = inlineDuotoneLightColorHex,
            InlineImageOpacity = inlineOpacity,
            InlineImageOutline = ReadPictureOutline(spPr),
            InlineImageEffectExtent = ReadEffectExtent(drawing),
            InlineImageClipToEllipse = clipToEllipse,
            InlineImageClipSubpaths = clipSubpaths,
            Source = AnchorSource(drawing)
        };
    }

    // wp:effectExtent — the room Word reserves around an inline picture's extent for its outline and
    // effects (ImageEffectExtent). Absent or all-zero reads as null.
    static ImageEffectExtent? ReadEffectExtent(Drawing drawing)
    {
        var effectExtent = drawing.Descendants<DW.EffectExtent>().FirstOrDefault();
        if (effectExtent == null)
        {
            return null;
        }

        var left = (effectExtent.LeftEdge?.Value ?? 0) / OoxmlUnits.EmusPerPoint;
        var top = (effectExtent.TopEdge?.Value ?? 0) / OoxmlUnits.EmusPerPoint;
        var right = (effectExtent.RightEdge?.Value ?? 0) / OoxmlUnits.EmusPerPoint;
        var bottom = (effectExtent.BottomEdge?.Value ?? 0) / OoxmlUnits.EmusPerPoint;
        if (left == 0 &&
            top == 0 &&
            right == 0 &&
            bottom == 0)
        {
            return null;
        }

        return new(left, top, right, bottom);
    }

    // A picture's own a:ln: the solid line Word's picture styles stroke on the picture edge (see
    // ImageOutline). Null without an a:ln, with a:noFill, or without a width — a theme line
    // reference never applies to a picture, so nothing is inferred.
    ImageOutline? ReadPictureOutline(OpenXmlElement? shapeProperties)
    {
        var outline = shapeProperties?.GetFirstChild<A.Outline>();
        if (outline == null || outline.GetFirstChild<A.NoFill>() != null || outline.GetFirstChild<A.SolidFill>() is not { } solidFill || outline.Width?.Value is not { } widthEmu || widthEmu <= 0)
        {
            return null;
        }

        var colorHex = ExtractFirstFillColor(solidFill);
        if (colorHex == null)
        {
            return null;
        }

        // a:ln/@algn is not read: Word strokes a picture's line outside its extent for ctr and in alike.
        return new(widthEmu / OoxmlUnits.EmusPerPoint, colorHex, ShapeParser.ExtractSolidFillAlpha(solidFill));
    }

    // The a:blip extension that carries the vector original of an Office icon. When present the
    // SVG is the primary image and the a:blip raster becomes a fallback for backends that can't
    // rasterize SVG; without it the raster is the primary image.
    const string svgBlipExtensionUri = "{96DAC541-7B7A-43D3-8B79-37D633B846F1}";

    /// <summary>
    /// Resolves the image bytes a <c>blipFill</c> points at. Null when the blip is missing or
    /// resolves to an empty part.
    /// </summary>
    (byte[] Data, string? ContentType, byte[]? RasterFallbackData, string? RasterFallbackContentType)?
        ReadBlipImage(OpenXmlElement blipFill, OpenXmlPart hostPart)
    {
        var blip = blipFill.Descendants().FirstOrDefault(_ => _.LocalName == "blip");
        if (blip?.AttributeValue("embed") is not { } embed)
        {
            return null;
        }

        byte[]? imageData = null;
        string? contentType = null;
        byte[]? rasterFallbackData = null;
        string? rasterFallbackContentType = null;

        var extLst = blip.Elements().FirstOrDefault(_ => _.LocalName == "extLst");
        if (extLst != null)
        {
            foreach (var extEl in extLst.Elements().Where(_ => _.LocalName == "ext"))
            {
                if (extEl.AttributeValue("uri") != svgBlipExtensionUri)
                {
                    continue;
                }

                var svgBlip = extEl.Descendants().FirstOrDefault(_ => _.LocalName == "svgBlip");
                if (svgBlip?.AttributeValue("embed") is not { } svgEmbed)
                {
                    continue;
                }

                if (hostPart.PartOrNull(svgEmbed) is not { } svgSource)
                {
                    continue;
                }

                imageData = GetPartBytes(svgSource);
                contentType = "image/svg+xml";
            }
        }

        if (hostPart.PartOrNull(embed) is ImagePart imagePart)
        {
            var rasterBytes = GetPartBytes(imagePart);
            if (rasterBytes.Length > 0)
            {
                if (imageData == null)
                {
                    imageData = rasterBytes;
                    contentType = imagePart.ContentType;
                }
                else
                {
                    rasterFallbackData = rasterBytes;
                    rasterFallbackContentType = imagePart.ContentType;
                }
            }
        }

        if (imageData is not {Length: > 0})
        {
            return null;
        }

        return (imageData, contentType, rasterFallbackData, rasterFallbackContentType);
    }

    Run? ParseInlineShapeGroupRun(Drawing drawing, WPG.WordprocessingGroup wpgGroup, OpenXmlPart hostPart, RunProperties runProps)
    {
        // Inline drawings carry their displayed size on <wp:extent>; the wpg's own xfrm gives
        // the child coordinate space we treat as 0..ChildExtX × 0..ChildExtY.
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        if (extent?.Cx == null || extent.Cy == null)
        {
            return null;
        }

        // wp:effectExtent reserves layout space for shape effects (shadows, outlines that
        // visually extend past the bounding box). Word lays inline drawings out at
        // (extent + effectExtent) so adjacent icons get a visible gap; honouring it here
        // keeps the connector-line groups (e.g. arrow icons) from packing tight.
        var effectExtent = drawing.Descendants<DW.EffectExtent>().FirstOrDefault();
        var effectL = effectExtent?.LeftEdge?.Value ?? 0;
        var effectT = effectExtent?.TopEdge?.Value ?? 0;
        var effectR = effectExtent?.RightEdge?.Value ?? 0;
        var effectB = effectExtent?.BottomEdge?.Value ?? 0;

        var outerCx = extent.Cx.Value + effectL + effectR;
        var outerCy = extent.Cy.Value + effectT + effectB;
        var widthPoints = outerCx / OoxmlUnits.EmusPerPoint;
        var heightPoints = outerCy / OoxmlUnits.EmusPerPoint;
        if (widthPoints <= 0 || heightPoints <= 0)
        {
            return null;
        }

        var grpSpPr = wpgGroup.GetFirstChild<WPG.GroupShapeProperties>();
        var grpXfrm = grpSpPr?.GetFirstChild<A.TransformGroup>();
        var chExt = grpXfrm?.ChildExtents;
        var childExtentX = (double) (chExt?.Cx ?? extent.Cx.Value);
        var childExtentY = (double) (chExt?.Cy ?? extent.Cy.Value);
        var chOffX = (double) (grpXfrm?.ChildOffset?.X ?? 0);
        var chOffY = (double) (grpXfrm?.ChildOffset?.Y ?? 0);
        if (childExtentX <= 0 || childExtentY <= 0)
        {
            return null;
        }

        var rotationDegrees = grpXfrm?.Rotation?.Value is { } rot ? rot / 60000.0 : 0;

        // Shift each child's position by the effect-padding offset so it lines up with the inner
        // (extent) box inside the outer (extent + effectExtent) frame. The renderer scales the
        // entire outer rectangle into the inline fragment, so without the offset every shape
        // would land in the top-left padding region.
        var paddingX = effectL * (childExtentX / extent.Cx.Value);
        var paddingY = effectT * (childExtentY / extent.Cy.Value);

        var shapes = new List<GroupShape>();
        foreach (var child in GroupDrawables(wpgGroup))
        {
            if (child is WPS.WordprocessingShape wsp)
            {
                var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
                var shapeXfrm = shapeProps?.GetFirstChild<A.Transform2D>();
                if (shapeProps == null || shapeXfrm?.Offset == null || shapeXfrm.Extents == null)
                {
                    continue;
                }

                // a:grpFill defers the fill to the ancestor group's a:solidFill — Word's brochure
                // templates paint a whole cluster (brochures/06's accent stripes, its balloon
                // silhouette) by filling the wpg:wgp once and letting each rect inherit it. Mirrors
                // the floating path's resolution (ParseSolidFillShape).
                var explicitNoFill = shapeProps.GetFirstChild<A.NoFill>() != null;
                var fill = explicitNoFill ? null : shapeProps.GetFirstChild<A.SolidFill>();
                if (fill == null &&
                    !explicitNoFill &&
                    shapeProps.GetFirstChild<A.GroupFill>() != null)
                {
                    fill = ResolveGroupFill(wsp);
                }

                var stroke = ReadGroupStroke(shapeProps, wsp.GetFirstChild<WPS.ShapeStyle>()?.LineReference);

                // A wps:wsp can be picture-filled the same way a pic:pic is (Word's "fill a
                // shape with a picture" — newsletters/03 stores its inset photos as blip-filled
                // rects inside inline groups).
                var wspBlipFill = shapeProps.GetFirstChild<A.BlipFill>();
                var blipImage = wspBlipFill != null ? ReadBlipImage(wspBlipFill, hostPart) : null;

                var geometry = MapGroupGeometry(shapeProps);
                var member = PlaceGroupMember(
                    wsp,
                    shapeXfrm,
                    geometry,
                    ShapeParser.ExtractSubpaths(shapeProps)
                    ?? PresetShapeGeometry.TryBuild(
                        shapeProps.GetFirstChild<A.PresetGeometry>(),
                        shapeXfrm.Extents.Cx ?? 0,
                        shapeXfrm.Extents.Cy ?? 0),
                    turnable: blipImage == null);

                shapes.Add(
                    new()
                    {
                        X = member.X - chOffX + paddingX,
                        Y = member.Y - chOffY + paddingY,
                        Width = member.Width,
                        Height = member.Height,
                        ColorHex = stroke.ColorHex,
                        LineWidthEmu = stroke.WidthEmu,
                        LineAlpha = stroke.Alpha,
                        LineDashPattern = stroke.DashPattern,
                        FlipVertical = member.FlipVertical,
                        FlipHorizontal = member.FlipHorizontal,
                        Geometry = geometry,
                        Subpaths = member.Subpaths,
                        FillColorHex = fill != null ? ExtractFirstFillColor(fill) : null,
                        FillAlpha = fill != null ? ShapeParser.ExtractSolidFillAlpha(fill) : 1,
                        Shadow = ReadOuterShadow(shapeProps),
                        ImageData = blipImage?.Data,
                        ImageContentType = blipImage?.ContentType,
                        ImageRasterFallbackData = blipImage?.RasterFallbackData,
                        ImageCrop = wspBlipFill != null ? BlipReader.ReadCrop(wspBlipFill) : null
                    });
            }
            else if (child is PIC.Picture picture)
            {
                var picProps = picture.ShapeProperties;
                var picXfrm = picProps?.Transform2D;
                if (picProps == null ||
                    picXfrm?.Offset == null ||
                    picXfrm.Extents == null ||
                    picture.BlipFill == null ||
                    ReadBlipImage(picture.BlipFill, hostPart) is not { } image)
                {
                    continue;
                }

                // pic:spPr carries the shape the picture is cropped to (Word's circular "picture
                // style") and the ring drawn around it, the same way a wps:wsp carries its own.
                // A picture has no wps:style, so its outline width has to be explicit.
                var picStroke = ReadGroupStroke(picProps, lineReference: null);
                var member = PlaceGroupMember(picture, picXfrm, GroupShapeGeometry.Rectangle, subpaths: null, turnable: false);

                shapes.Add(
                    new()
                    {
                        X = member.X - chOffX + paddingX,
                        Y = member.Y - chOffY + paddingY,
                        Width = member.Width,
                        Height = member.Height,
                        ColorHex = picStroke.ColorHex,
                        LineWidthEmu = picStroke.WidthEmu,
                        LineAlpha = picStroke.Alpha,
                        ImageDescription = ReadImageDescription(picture, drawing),
                        Geometry = MapGroupGeometry(picProps),
                        Shadow = ReadOuterShadow(picProps),
                        ImageData = image.Data,
                        ImageContentType = image.ContentType,
                        ImageRasterFallbackData = image.RasterFallbackData,
                        ImageCrop = BlipReader.ReadCrop(picture.BlipFill)
                    });
            }
        }

        if (shapes.Count == 0)
        {
            return null;
        }

        // Stretch the child coord space by the same outer/inner ratio so shape coordinates
        // (now padded above) map correctly into the rendered outer rectangle.
        var outerChildExtentX = childExtentX * outerCx / extent.Cx.Value;
        var outerChildExtentY = childExtentY * outerCy / extent.Cy.Value;

        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageWidthPoints = widthPoints,
            InlineImageHeightPoints = heightPoints,
            InlineShapeGroup = new()
            {
                ChildExtentX = outerChildExtentX,
                ChildExtentY = outerChildExtentY,
                RotationDegrees = rotationDegrees,

                // In compatibility mode 12, and in a document that declares no mode, Word draws the
                // group its left and top effect edges further in AGAIN, past the inner box; in modes
                // 14 and 15 it draws it at the inner box. Word-probed (_probe_arrowmode): one arrow
                // group (effect edges 13.5pt left, 12pt top) alone on a line at the 72pt margin has
                // its ink from x=72.0 in modes 15 and 14 and from x=85.4 in mode 12, and from y=94.6
                // against y=106.6; after two letters of text the same 13.4pt and 12.0pt. On
                // inline_shape_arrows, which declares no mode, a 6pt-stroked arrow with edges of 5pt
                // sits 5.3pt further in both ways. The frame the line reserves does not change with
                // it: a second arrow starts 75.4pt after the first in every mode, the extent and its
                // two side edges, so in mode 12 the ink runs past its frame on the right and at the
                // foot. business-plans/12 holds the same arrows in mode 15, at the inner box.
                DrawOffsetXPoints = compatibilityMode <= 12 ? effectL / OoxmlUnits.EmusPerPoint : 0,
                DrawOffsetYPoints = compatibilityMode <= 12 ? effectT / OoxmlUnits.EmusPerPoint : 0,
                Shapes = shapes
            }
        };
    }

    /// <summary>
    /// An inline chart: its box on the line, and the chart read as the model a sheet's charts use
    /// (<see cref="ChartReader"/>), which layout composes into the box with its title, legend, axes and
    /// data labels (<c>ChartComposer</c>) for every chart kind the reader knows. A pie or doughnut also
    /// keeps its slices as an inline group (<see cref="PieSliceRun"/>), which is how the HTML exporter
    /// draws it. A chart the reader cannot read keeps just those slices when it has any.
    /// </summary>
    Run? ParseInlineChartRun(Drawing drawing, OpenXmlElement chartReference, OpenXmlPart hostPart, RunProperties runProps)
    {
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        var relationshipId = chartReference.GetAttributes().FirstOrDefault(_ => _.LocalName == "id").Value;
        if (extent?.Cx == null || extent.Cy == null || extent.Cx.Value <= 0 || extent.Cy.Value <= 0 || string.IsNullOrEmpty(relationshipId))
        {
            return null;
        }

        OpenXmlPartRootElement? chartSpace;
        try
        {
            chartSpace = hostPart.GetPartById(relationshipId).RootElement;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var slices = PieSliceRun(chartSpace, extent.Cx.Value, extent.Cy.Value, runProps);
        if (new ChartReader(currentThemeColors, currentThemeFonts, culture).Read(chartSpace) is not { } model)
        {
            return slices;
        }

        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageWidthPoints = extent.Cx.Value / OoxmlUnits.EmusPerPoint,
            InlineImageHeightPoints = extent.Cy.Value / OoxmlUnits.EmusPerPoint,
            InlineShapeGroup = slices?.InlineShapeGroup,
            InlineChart = model
        };
    }

    /// <summary>
    /// An anchored chart (<c>c:chart</c> under a <c>wp:anchor</c>): its box at the anchor's position, with the
    /// chart read as the model a sheet's charts use, so it is composed and placed as they are. Null when the
    /// drawing holds no chart, or one the reader cannot read.
    /// </summary>
    FloatingChartElement? AnchoredChart(Drawing drawing, DW.Anchor anchor, OpenXmlPart hostPart)
    {
        if (drawing
                .Descendants()
                .FirstOrDefault(_ => _ is
                {
                    LocalName: "chart",
                    Parent.LocalName: "graphicData"
                }) is not { } chartReference ||
            anchor.Extent is not
            {
                Cx:
                {
                    Value: > 0
                } width,
                Cy:
                {
                    Value: > 0
                } height
            } ||
            chartReference.GetAttributes().FirstOrDefault(_ => _.LocalName == "id").Value is not {Length: > 0} relationshipId ||
            hostPart.PartOrNull(relationshipId)?.RootElement is not { } chartSpace ||
            new ChartReader(currentThemeColors, currentThemeFonts, culture).Read(chartSpace) is not { } model)
        {
            return null;
        }

        var positioning = anchor.ParsePositioning(0, 0, anchorAlignmentPage);
        return new()
        {
            Chart = model,
            WidthPoints = width.Value / OoxmlUnits.EmusPerPoint,
            HeightPoints = height.Value / OoxmlUnits.EmusPerPoint,
            HorizontalPositionPoints = positioning.HorizontalPositionPoints,
            VerticalPositionPoints = positioning.VerticalPositionPoints,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            RelativeHeight = positioning.RelativeHeight
        };
    }

    static double ReadRotationDegrees(OpenXmlElement xfrm)
    {
        if (xfrm.AttributeValue("rot") is { } rotValue &&
            long.TryParse(rotValue, out var rot60000ths))
        {
            return rot60000ths / 60000.0;
        }

        return 0;
    }

    /// <summary>Reads the mirror flags from an <c>a:xfrm</c> (<c>@flipH</c>/<c>@flipV</c>).</summary>
    static (bool FlipHorizontal, bool FlipVertical) ReadFlips(OpenXmlElement xfrm) =>
        (xfrm.AttributeValue("flipH") is "1" or "true",
            xfrm.AttributeValue("flipV") is "1" or "true");

    // Alt text for a picture: the picture's own pic:cNvPr wins (most specific — e.g. per-image
    // inside a group), falling back to the drawing-level wp:docPr. @descr is Word's "Description"
    // alt-text field; @title ("Title") is the secondary fallback. The auto-generated @name
    // ("Picture 1") is never used — it is a shape name, not alt text.
    static string? ReadImageDescription(OpenXmlElement pic, Drawing drawing)
    {
        return Describe(pic.Descendants().FirstOrDefault(_ => _.LocalName == "cNvPr"))
               ?? Describe(drawing.Descendants().FirstOrDefault(_ => _.LocalName == "docPr"));

        static string? Describe(OpenXmlElement? properties)
        {
            if (properties == null)
            {
                return null;
            }

            if (properties.AttributeValue("descr").TryTrim(out var descr))
            {
                return descr;
            }

            if (properties.AttributeValue("title").TryTrim(out var title))
            {
                return title;
            }

            return null;
        }
    }

    /// <summary>
    /// Parses all images from a drawing element, including multiple images in groups.
    /// </summary>
    List<DocumentElement> ParseDrawingElements(Drawing drawing, MainDocumentPart mainPart, Dictionary<DocumentElement, OpenXmlElement>? childSources = null)
    {
        var hostPart = ResolveHostPart(drawing, mainPart);
        var result = new List<DocumentElement>();

        var anchor = drawing.GetFirstChild<DW.Anchor>();

        // An anchored chart floats where its anchor puts it, composed at layout like a sheet's chart
        // (Fragmenter.PlaceChart); an inline one is a run on its line (ParseInlineChartRun).
        if (anchor != null &&
            AnchoredChart(drawing, anchor, hostPart) is { } floatingChart)
        {
            result.Add(floatingChart);
            return result;
        }

        // Get the group transform if present (for coordinate system)
        long groupOffsetX = 0, groupOffsetY = 0;
        double groupScaleX = 1.0, groupScaleY = 1.0;

        var grpSpPr = drawing.Descendants().FirstOrDefault(_ => _.LocalName == "grpSpPr");
        if (grpSpPr != null)
        {
            var xfrm = grpSpPr.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");
            if (xfrm != null)
            {
                // Get child extents for scaling
                var chOff = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chOff");
                var chExt = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chExt");
                var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");

                if (chOff != null)
                {
                    var attributes = chOff.GetAttributes();
                    long.TryParse(attributes.AttributeValue("x"), out groupOffsetX);
                    long.TryParse(attributes.AttributeValue("y"), out groupOffsetY);
                }

                if (chExt != null &&
                    ext != null)
                {
                    var chExtAttributes = chExt.GetAttributes();
                    var extAttributes = ext.GetAttributes();
                    var chCx = chExtAttributes.AttributeValue("cx");
                    var chCy = chExtAttributes.AttributeValue("cy");
                    var extCx = extAttributes.AttributeValue("cx");
                    var extCy = extAttributes.AttributeValue("cy");

                    if (chCx != null &&
                        extCx != null &&
                        long.TryParse(chCx, out var childWidth) &&
                        long.TryParse(extCx, out var actualWidth) &&
                        childWidth > 0)
                    {
                        groupScaleX = (double) actualWidth / childWidth;
                    }

                    if (chCy != null &&
                        extCy != null &&
                        long.TryParse(chCy, out var childHeight) &&
                        long.TryParse(extCy, out var actualHeight) &&
                        childHeight > 0)
                    {
                        groupScaleY = (double) actualHeight / childHeight;
                    }
                }
            }
        }

        // Find ALL pic elements (including in groups)
        var pics = drawing.Descendants().Where(_ => _.LocalName == "pic").ToList();

        foreach (var pic in pics)
        {
            // Get the picture's shape properties for position/size
            var spPr = pic.Elements().FirstOrDefault(_ => _.LocalName == "spPr");
            if (spPr == null)
            {
                continue;
            }

            var xfrm = spPr.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");
            if (xfrm == null)
            {
                continue;
            }

            long picOffsetX = 0, picOffsetY = 0;
            var off = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "off");
            if (off != null)
            {
                var attributes = off.GetAttributes();
                long.TryParse(attributes.AttributeValue("x"), out picOffsetX);
                long.TryParse(attributes.AttributeValue("y"), out picOffsetY);
            }

            long picWidth = 0, picHeight = 0;
            var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");
            if (ext != null)
            {
                var attributes = ext.GetAttributes();
                long.TryParse(attributes.AttributeValue("cx"), out picWidth);
                long.TryParse(attributes.AttributeValue("cy"), out picHeight);
            }

            if (picWidth == 0 || picHeight == 0)
            {
                continue;
            }

            // The accumulated transform from all ancestor grpSp groups.
            var accumTransform = AccumulatedTransform.Of(pic, groupOffsetX, groupOffsetY, groupScaleX, groupScaleY, composeFlips: true);

            var (finalX, finalY, finalWidth, finalHeight, groupRotationDegrees, groupFlipVertical) = accumTransform.MapRectangle(picOffsetX, picOffsetY, picWidth, picHeight);

            var widthPoints = finalWidth / OoxmlUnits.EmusPerPoint;
            var heightPoints = finalHeight / OoxmlUnits.EmusPerPoint;
            var offsetXPoints = finalX / OoxmlUnits.EmusPerPoint;
            var offsetYPoints = finalY / OoxmlUnits.EmusPerPoint;

            // Rotation (a:xfrm/@rot, in 60,000ths of a degree, clockwise) composed with any
            // rotation the ancestor groups apply. A mirroring ancestor reverses the sense of
            // the pic's own rotation (F·R(θ) = R(−θ)·F) and XORs into its vertical flip —
            // MapRectangle's canonical det<0 decomposition is rotation + flipV.
            var ownRotationDegrees = ReadRotationDegrees(xfrm);
            var rotationDegrees = groupRotationDegrees + (groupFlipVertical ? -ownRotationDegrees : ownRotationDegrees);
            var (flipHorizontal, flipVertical) = ReadFlips(xfrm);
            flipVertical ^= groupFlipVertical;

            var (clipToEllipse, clipSubpaths) = BlipReader.ReadPictureClip(spPr);

            // Find the blip (image reference)
            var blipFill = pic.Elements().FirstOrDefault(_ => _.LocalName == "blipFill");
            if (blipFill == null)
            {
                continue;
            }

            var crop = BlipReader.ReadCrop(blipFill);

            var blip = blipFill.Descendants().FirstOrDefault(_ => _.LocalName == "blip");
            if (blip == null)
            {
                continue;
            }

            if (blip.AttributeValue("embed") is not { } embed)
            {
                continue;
            }

            // Try to get SVG first, then fall back to regular image
            byte[]? imageData = null;
            string? contentType = null;
            byte[]? rasterFallbackData = null;
            string? rasterFallbackContentType = null;

            var extLst = blip.Elements().FirstOrDefault(_ => _.LocalName == "extLst");
            if (extLst != null)
            {
                foreach (var extEl in extLst.Elements().Where(_ => _.LocalName == "ext"))
                {
                    if (extEl.AttributeValue("uri") == "{96DAC541-7B7A-43D3-8B79-37D633B846F1}")
                    {
                        var svgBlip = extEl.Descendants().FirstOrDefault(_ => _.LocalName == "svgBlip");
                        if (svgBlip != null)
                        {
                            if (svgBlip.AttributeValue("embed") is { } svgEmbed &&
                                hostPart.PartOrNull(svgEmbed) is { } svgPart)
                            {
                                imageData = GetPartBytes(svgPart);
                                contentType = "image/svg+xml";
                            }
                        }
                    }
                }
            }

            // Read the raster blob the blip points to. When SVG is set, this becomes the
            // raster fallback for backends that can't render SVG; otherwise it's the
            // primary imageData.
            if (hostPart.PartOrNull(embed) is ImagePart rasterPart)
            {
                var rasterBytes = GetPartBytes(rasterPart);
                if (rasterBytes.Length > 0)
                {
                    if (imageData == null)
                    {
                        imageData = rasterBytes;
                        contentType = rasterPart.ContentType;
                    }
                    else
                    {
                        rasterFallbackData = rasterBytes;
                        rasterFallbackContentType = rasterPart.ContentType;
                    }
                }
            }

            if (imageData == null || imageData.Length == 0)
            {
                continue;
            }

            var (colorEffect, duotoneColorHex, duotoneLightColorHex) = BlipReader.ReadBlipColorEffect(blip, currentThemeColors);
            var opacity = BlipReader.ReadBlipOpacity(blip);
            var description = ReadImageDescription(pic, drawing);

            if (anchor == null)
            {
                var imageElement = new ImageElement
                {
                    ImageData = imageData,
                    WidthPoints = widthPoints,
                    HeightPoints = heightPoints,
                    ContentType = contentType,
                    Description = description,
                    RotationDegrees = rotationDegrees,
                    FlipHorizontal = flipHorizontal,
                    FlipVertical = flipVertical,
                    Crop = crop,
                    ColorEffect = colorEffect,
                    DuotoneColorHex = duotoneColorHex,
                    DuotoneLightColorHex = duotoneLightColorHex,
                    Opacity = opacity,
                    RasterFallbackData = rasterFallbackData,
                    RasterFallbackContentType = rasterFallbackContentType
                };
                result.Add(imageElement);
                childSources?[imageElement] = pic;
            }
            else
            {
                var floatingImage = ParseAnchoredImageWithOffset(anchor, imageData, widthPoints, heightPoints, contentType, offsetXPoints, offsetYPoints, rotationDegrees, flipHorizontal, flipVertical, crop, rasterFallbackData, rasterFallbackContentType, description, colorEffect, duotoneColorHex, duotoneLightColorHex, clipToEllipse, clipSubpaths, opacity, ReadPictureOutline(spPr));
                result.Add(floatingImage);
                childSources?[floatingImage] = pic;
            }
        }

        return result;
    }

    // Where an anchored drawing is, under captureSources: its run's ordinal, and its place in the run
    // counted as SourceRuns counts — what the children ahead of it take. Read only; nothing is written.
    // An inline picture's drawing is found the same way.
    RunSource? AnchorSource(OpenXmlElement anchor)
    {
        if (runOrdinals == null ||
            anchor.Ancestors<OoxmlRun>().FirstOrDefault() is not { } run ||
            !runOrdinals.TryGetValue(run, out var ordinal))
        {
            return null;
        }

        var position = 0;
        foreach (var child in run.ChildElements)
        {
            if (ReferenceEquals(child, anchor) ||
                child.Descendants().Any(_ => ReferenceEquals(_, anchor)))
            {
                return new RunSource(ordinal, position, true);
            }

            position += SourceRuns.Length(child);
        }

        return null;
    }

    /// <summary>
    /// Parses an anchored image with additional X/Y offset within a group.
    /// </summary>
    FloatingImageElement ParseAnchoredImageWithOffset(DW.Anchor anchor, byte[] imageData, double widthPoints, double heightPoints, string? contentType, double offsetXPoints, double offsetYPoints, double rotationDegrees = 0, bool flipHorizontal = false, bool flipVertical = false, ImageCrop? crop = null, byte[]? rasterFallbackData = null, string? rasterFallbackContentType = null, string? description = null, BlipColorEffect colorEffect = BlipColorEffect.None, string? duotoneColorHex = null, string? duotoneLightColorHex = null, bool clipToEllipse = false, IReadOnlyList<IReadOnlyList<(double X, double Y)>>? clipSubpaths = null, double opacity = 1, ImageOutline? outline = null)
    {
        var positioning = anchor.ParsePositioning(offsetXPoints, offsetYPoints, anchorAlignmentPage);
        var wrap = ParseWrap(anchor);

        return new()
        {
            Source = AnchorSource(anchor),
            Outline = outline,
            ImageData = imageData,
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            ContentType = contentType,
            Description = description,
            ColorEffect = colorEffect,
            DuotoneColorHex = duotoneColorHex,
            DuotoneLightColorHex = duotoneLightColorHex,
            Opacity = opacity,
            RelativeHeight = positioning.RelativeHeight,
            LayoutInCell = positioning.LayoutInCell,
            HorizontalPositionPoints = positioning.HorizontalPositionPoints,
            VerticalPositionPoints = positioning.VerticalPositionPoints,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            WrapType = wrap.Type,
            WrapTextSide = wrap.Side,
            WrapDistanceLeftPoints = wrap.DistLeft,
            WrapDistanceTopPoints = wrap.DistTop,
            WrapDistanceRightPoints = wrap.DistRight,
            WrapDistanceBottomPoints = wrap.DistBottom,
            BehindText = positioning.BehindText,
            RotationDegrees = rotationDegrees,
            FlipHorizontal = flipHorizontal,
            FlipVertical = flipVertical,
            Crop = crop,
            ClipToEllipse = clipToEllipse,
            ClipSubpaths = clipSubpaths,
            WidthPercent = positioning.WidthPercent,
            WidthRelativeFrom = positioning.WidthRelativeFrom,
            HeightPercent = positioning.HeightPercent,
            HeightRelativeFrom = positioning.HeightRelativeFrom,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            RasterFallbackData = rasterFallbackData,
            RasterFallbackContentType = rasterFallbackContentType
        };
    }

    /// <summary>
    /// Reads the anchor's wrap element (wp:wrapSquare / wrapTight / wrapThrough /
    /// wrapTopAndBottom), its side preference (@wrapText, both sides when absent) and clearance
    /// distances (EMU). A distance the wrap element leaves unsaid is the anchor's (wp:anchor@distL
    /// and the rest), which is where Word writes them: image_wrap_square's picture declares its
    /// 9pt sides on the anchor alone.
    /// </summary>
    static (WrapType Type, WrapTextSide Side, double DistLeft, double DistTop, double DistRight, double DistBottom) ParseWrap(DW.Anchor anchor)
    {
        static double Points(UInt32Value? own, UInt32Value? anchorDistance) =>
            (own?.Value ?? anchorDistance?.Value ?? 0) / OoxmlUnits.EmusPerPoint;

        if (anchor.GetFirstChild<DW.WrapSquare>() is { } square)
        {
            return (WrapType.Square,
                ParseWrapTextSide(square.WrapText),
                Points(square.DistanceFromLeft, anchor.DistanceFromLeft),
                Points(square.DistanceFromTop, anchor.DistanceFromTop),
                Points(square.DistanceFromRight, anchor.DistanceFromRight),
                Points(square.DistanceFromBottom, anchor.DistanceFromBottom));
        }

        if (anchor.GetFirstChild<DW.WrapTight>() is { } tight)
        {
            return (WrapType.Tight,
                ParseWrapTextSide(tight.WrapText),
                Points(tight.DistanceFromLeft, anchor.DistanceFromLeft),
                0,
                Points(tight.DistanceFromRight, anchor.DistanceFromRight),
                0);
        }

        if (anchor.GetFirstChild<DW.WrapThrough>() is { } through)
        {
            return (WrapType.Through,
                ParseWrapTextSide(through.WrapText),
                Points(through.DistanceFromLeft, anchor.DistanceFromLeft),
                0,
                Points(through.DistanceFromRight, anchor.DistanceFromRight),
                0);
        }

        if (anchor.GetFirstChild<DW.WrapTopBottom>() is { } topBottom)
        {
            return (WrapType.TopAndBottom,
                WrapTextSide.BothSides,
                0,
                Points(topBottom.DistanceFromTop, anchor.DistanceFromTop),
                0,
                Points(topBottom.DistanceFromBottom, anchor.DistanceFromBottom));
        }

        return (WrapType.None, WrapTextSide.BothSides, 0, 0, 0, 0);
    }

    static WrapTextSide ParseWrapTextSide(EnumValue<DW.WrapTextValues>? wrapText)
    {
        if (wrapText?.Value is not { } value)
        {
            return WrapTextSide.BothSides;
        }

        if (value == DW.WrapTextValues.Left)
        {
            return WrapTextSide.Left;
        }

        if (value == DW.WrapTextValues.Right)
        {
            return WrapTextSide.Right;
        }

        if (value == DW.WrapTextValues.Largest)
        {
            return WrapTextSide.Largest;
        }

        return WrapTextSide.BothSides;
    }
}
