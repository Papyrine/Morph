/// <summary>
/// Writes one page of a <see cref="LaidOutDocument"/> as SVG markup for a browser to draw — the vector
/// counterpart of <see cref="ImageSharpPainter"/>, reading the same <see cref="PlacedItem"/> tree with the
/// same geometry helpers (<see cref="TextDecorations"/>, <see cref="BorderStroke"/>, <see cref="LeaderTiling"/>),
/// in the same order, so a page drawn either way puts its ink in the same places. The SVG is in points
/// (its <c>viewBox</c> is the page), so it scales to any zoom without being written again.
///
/// <para>Text is placed glyph by glyph: every character carries its own <c>x</c>, taken from the layout
/// SixLabors.Fonts gives the run — the layout <see cref="ImageSharpPainter"/> draws — so the browser's own
/// advances and kerning never move a glyph. The browser only rasterises, in the face
/// <c>fontFamily</c> names, which must be the face the context resolved (the viewer's Aptos).</para>
///
/// <para>A spike (the Morph.Blazor README, "Pages drawn as SVG"). A picture the browser cannot be handed
/// as it is — stored as SVG, tiled, recoloured, in a format no browser decodes — and warped WordArt are
/// embedded as the bitmap the raster painter draws of them. A placed item it does not know is listed in
/// <see cref="SvgPage.Unsupported"/>, and the caller paints that page as a PNG instead.</para>
/// </summary>
static class SvgPainter
{
    // Ids for clips and gradients must be unique in the whole HTML document the SVG lands in, not only
    // in this SVG: several pages share one id space.
    static long renders;

    public static SvgPage PaintPage(LaidOutPage page, ImageSharpRenderContext context, string fontFamily)
    {
        var writer = new Writer(context, fontFamily, $"m{Interlocked.Increment(ref renders)}x");
        writer.Page(page);
        return new(writer.Markup(), writer.Images, writer.Unsupported);
    }

    /// <summary>Empties the glyph positions kept across pages, to measure a page written from cold.</summary>
    internal static void ForgetGlyphs() => GlyphPositions.Clear();

    sealed class Writer(ImageSharpRenderContext context, string fontFamily, string idPrefix)
    {
        readonly StringBuilder svg = new(16384);
        readonly Dictionary<byte[], int> imageIndex = new(ReferenceEqualityComparer.Instance);
        int ids;

        public List<byte[]> Images { get; } = [];
        public SortedSet<string> Unsupported { get; } = [];

        public string Markup() => svg.ToString();

        public void Page(LaidOutPage page)
        {
            var width = page.Settings.WidthPoints;
            var height = page.Settings.HeightPoints;
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ");
            Number(width);
            svg.Append(' ');
            Number(height);
            // Ligatures off because a ligature is one glyph where the x list expects one per character.
            svg.Append("\" preserveAspectRatio=\"none\" style=\"font-family:'");
            svg.Append(fontFamily);
            svg.Append("';font-kerning:none;font-variant-ligatures:none;text-rendering:geometricPrecision\">");

            var background = page.Settings.BackgroundColorHex;
            svg.Append("<rect width=\"");
            Number(width);
            svg.Append("\" height=\"");
            Number(height);
            svg.Append('"');
            Paint("fill", string.IsNullOrEmpty(background) ? Color.White : ImageSharpRenderContext.ParseColor(background));
            svg.Append("/>");

            foreach (var item in page.Items)
            {
                Item(item);
            }

            if (page.Settings.PageBorders is {HasAnyBorder: true} pageBorders)
            {
                var (borderX, borderY, borderWidth, borderHeight) = pageBorders.EdgeRect(page.Settings);
                Edges(borderX, borderY, borderWidth, borderHeight, pageBorders.Edges, BorderStroke.Scope.Page);
            }

            svg.Append("</svg>");
        }

        void Item(PlacedItem item)
        {
            switch (item)
            {
                case PlacedLine line:
                    Line(line);
                    break;
                case PlacedTableRow row:
                    TableRow(row);
                    break;
                case PlacedImage image:
                    Image(image);
                    break;
                case PlacedShape shape:
                    Shape(shape);
                    break;
                case PlacedWordArt wordArt:
                    WordArt(wordArt);
                    break;
                case PlacedShading shading:
                    Rect(shading.X, shading.Y, shading.Width, shading.Height, ImageSharpRenderContext.ParseColor(shading.ColorHex));
                    break;
                case PlacedBorder border:
                    Edges(border.X, border.Y, border.Width, border.Height, border.Borders);
                    break;
                case PlacedRotatedGroup group:
                    svg.Append("<g transform=\"rotate(");
                    Number(group.RotationDegrees);
                    svg.Append(' ');
                    Number(group.X + group.Width / 2);
                    svg.Append(' ');
                    Number(group.Y + group.Height / 2);
                    svg.Append(")\">");
                    foreach (var child in group.Items)
                    {
                        Item(child);
                    }

                    svg.Append("</g>");
                    break;
                case PlacedScaledGroup scaled:
                    svg.Append("<g transform=\"matrix(");
                    Number3(scaled.Scale);
                    svg.Append(" 0 0 ");
                    Number3(scaled.Scale);
                    svg.Append(' ');
                    Number(scaled.X);
                    svg.Append(' ');
                    Number(scaled.Y);
                    svg.Append(")\">");
                    foreach (var child in scaled.Items)
                    {
                        Item(child);
                    }

                    svg.Append("</g>");
                    break;
                case PlacedClipGroup clip:
                    OpenClip(clip.X, clip.Y, clip.Width, clip.Height);
                    foreach (var child in clip.Items)
                    {
                        Item(child);
                    }

                    svg.Append("</svg>");
                    break;
                default:
                    Unsupported.Add(item.GetType().Name);
                    break;
            }
        }

        // A rectangular clip that needs no id: a nested viewport whose viewBox is its own box, so its
        // children keep page coordinates and are cut at its edges.
        void OpenClip(double x, double y, double width, double height)
        {
            svg.Append("<svg x=\"");
            Number(x);
            svg.Append("\" y=\"");
            Number(y);
            svg.Append("\" width=\"");
            Number(width);
            svg.Append("\" height=\"");
            Number(height);
            svg.Append("\" viewBox=\"");
            Number(x);
            svg.Append(' ');
            Number(y);
            svg.Append(' ');
            Number(width);
            svg.Append(' ');
            Number(height);
            svg.Append("\" preserveAspectRatio=\"none\" overflow=\"hidden\">");
        }

        void Line(PlacedLine line)
        {
            // Word's change bar beside a line that carries a tracked change (ImageSharpPainter.PaintLine).
            foreach (var run in line.Runs)
            {
                if (run.Properties.IsRevisionMark)
                {
                    var barX = context.PageSettings.MarginLeft / 2;
                    Stroke(barX, line.Y, barX, line.Y + line.Height, Color.Black, 0.75, null);
                    break;
                }
            }

            var ascent = line.Baseline - line.Y;
            foreach (var run in line.Runs)
            {
                if (run.Leader != TabLeader.None)
                {
                    Leader(run, line.Baseline);
                    continue;
                }

                if (string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var properties = run.Properties;
                var color = ImageSharpRenderContext.ParseColor(properties.ColorHex);
                if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
                {
                    Rect(run.X, line.Y, run.Width, line.Height, ImageSharpRenderContext.ParseColor(properties.BackgroundColorHex));
                }

                var baseline = line.Baseline - run.BaselineShift;
                Text(run.Text, properties, run.X, baseline);

                if (properties.Underline || properties.Strikethrough || properties.DoubleStrikethrough)
                {
                    Decorations(run, properties, color, baseline, ascent);
                }

                if (properties.Border is { } runBorder &&
                    BorderStroke.Draws(runBorder))
                {
                    var (boxX, boxY, boxWidth, boxHeight) = BorderStroke.RunBorderBox(runBorder, run.X, run.Width, line.Y, line.Height, BorderStroke.LinePad(line.Runs), !run.BorderInside);
                    Edges(boxX, boxY, boxWidth, boxHeight, CellBorders.Uniform(runBorder));
                }
            }

            foreach (var image in line.Images)
            {
                if (image.Composed is { } composed)
                {
                    foreach (var item in composed)
                    {
                        Item(item);
                    }

                    continue;
                }

                Image(image);
            }
        }

        // A run's glyphs, each at the pen position ImageSharpPainter.DrawTracked draws it at: the run's own
        // layout when it is untracked, one glyph at a time with the tracking added when it is.
        //
        // Spaces are left out. They draw nothing, every glyph after one carries its own x anyway, and a
        // browser does not keep them: Chromium drops a run's leading space even under white-space:pre, and
        // the x list then shifts onto the characters before their own — a space's width left for the
        // rest of the run.
        void Text(string text, RunProperties properties, double penX, double baseline)
        {
            if (!HasInk(text))
            {
                return;
            }

            var font = context.GetFont(properties);
            var positions = properties.CharacterSpacingPoints == 0 || text.Length <= 1
                ? GlyphPositions.Of(font, TextShaping.ResolveKerningMode(properties), text)
                : Tracked(font, text, (float) properties.CharacterSpacingPoints);

            svg.Append("<text x=\"");
            var first = true;
            for (var index = 0; index < text.Length; index++)
            {
                // A surrogate pair is one character to the browser's list, positioned by its first half.
                if (Skipped(text[index]) ||
                    (char.IsLowSurrogate(text[index]) &&
                     index > 0))
                {
                    continue;
                }

                if (!first)
                {
                    svg.Append(' ');
                }

                first = false;
                Number(penX + positions[index]);
            }

            svg.Append("\" y=\"");
            Number(baseline);
            svg.Append("\" font-size=\"");
            Number(font.Size);
            svg.Append('"');
            if (font.IsBold)
            {
                svg.Append(" font-weight=\"700\"");
            }

            if (font.IsItalic)
            {
                svg.Append(" font-style=\"italic\"");
            }

            var color = ImageSharpRenderContext.ParseColor(properties.ColorHex);
            Paint("fill", color);

            // A bold run in a face that is not bold: the outline is stroked as well (BoldSimulation).
            if (properties.SyntheticBold != SyntheticBold.None)
            {
                Paint("stroke", color);
                svg.Append(" stroke-width=\"");
                Number(BoldSimulation.StrokeWidthPoints(properties.SyntheticBold, VerticalRunPosition.RenderSizePoints(properties), 72.0 / context.Dpi));
                svg.Append('"');
            }

            // The synthesised oblique and the w:w stretch as one matrix about the run's start and the
            // baseline, as ImageSharpPainter.DrawTracked builds it.
            var oblique = properties.Italic &&
                          !font.IsItalic;
            var scale = properties.CharacterScalePercent / 100;
            if (oblique || scale != 1)
            {
                var skew = oblique ? FontHelpers.SyntheticItalicSkew : 0;
                svg.Append(" transform=\"matrix(");
                Number3(scale);
                svg.Append(" 0 ");
                Number3(-scale * skew);
                svg.Append(" 1 ");
                Number(scale * skew * baseline + penX * (1 - scale));
                svg.Append(" 0)\"");
            }

            svg.Append('>');
            Escape(text);
            svg.Append("</text>");
        }

        // A space, or a control character, which is no character to a browser either.
        static bool Skipped(char ch) => ch <= ' ';

        static bool HasInk(string text)
        {
            foreach (var ch in text)
            {
                if (!Skipped(ch))
                {
                    return true;
                }
            }

            return false;
        }

        float[] Tracked(Font font, string text, float tracking)
        {
            var positions = new float[text.Length];
            var x = 0f;
            for (var index = 0; index < text.Length; index++)
            {
                var length = char.IsHighSurrogate(text[index]) &&
                             index + 1 < text.Length
                    ? 2
                    : 1;
                positions[index] = x;
                if (length == 2)
                {
                    positions[index + 1] = x;
                }

                x += context.MeasureText(font, text.Substring(index, length)) + tracking;
                index += length - 1;
            }

            return positions;
        }

        void Decorations(PlacedRun run, RunProperties properties, Color color, double baseline, double ascent)
        {
            var underlineColor = properties.UnderlineColorHex == null
                ? color
                : ImageSharpRenderContext.ParseColor(properties.UnderlineColorHex);
            var rules = new List<TextDecorations.Rule>();
            var waves = new List<TextDecorations.Wave>();
            var font = context.GetFont(properties);
            TextDecorations.Collect(properties, run.Text, run.X, run.Width, (float) baseline, (float) ascent, _ => (float) (context.MeasureText(font, _) + properties.CharacterSpacingPoints * _.Length), rules, waves);
            foreach (var rule in rules)
            {
                Stroke(rule.X0, rule.Y, rule.X1, rule.Y, rule.IsStrike ? color : underlineColor, rule.Thickness, null);
            }

            foreach (var wave in waves)
            {
                Polyline(wave.Points.Select(_ => ((double) _.X, (double) _.Y)), underlineColor, wave.Thickness);
            }
        }

        void Leader(PlacedRun run, double baseline)
        {
            if (run.Width <= 0)
            {
                return;
            }

            var color = ImageSharpRenderContext.ParseColor(run.Properties.ColorHex);
            var fontSize = run.Properties.FontSizePoints;
            if (run.Leader == TabLeader.Underscore)
            {
                var underscoreY = baseline + fontSize * 0.12;
                Stroke(run.X, underscoreY, run.X + run.Width, underscoreY, color, Math.Max(0.5, fontSize / 16), null);
                return;
            }

            var leaderChar = run.Leader switch
            {
                TabLeader.Hyphen => "-",
                TabLeader.MiddleDot => "·",
                TabLeader.Heavy => "—",
                _ => "."
            };

            var font = context.GetFont(run.Properties);
            var glyphWidth = context.MeasureText(font, leaderChar);
            if (glyphWidth <= 0 ||
                !LeaderTiling.TryGetRange(run.X, run.Width, glyphWidth, glyphWidth, context.PageSettings.WidthPoints, out var startX, out var count))
            {
                return;
            }

            // The leader's dots in one element, each at its own x: the kerning-free glyph KerningMode
            // Standard draws a lone leader glyph with.
            svg.Append("<text x=\"");
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    svg.Append(' ');
                }

                Number(startX + index * glyphWidth);
            }

            svg.Append("\" y=\"");
            Number(baseline);
            svg.Append("\" font-size=\"");
            Number(font.Size);
            svg.Append('"');
            if (font.IsBold)
            {
                svg.Append(" font-weight=\"700\"");
            }

            if (font.IsItalic)
            {
                svg.Append(" font-style=\"italic\"");
            }

            Paint("fill", color);
            svg.Append('>');
            svg.Append(leaderChar[0], count);
            svg.Append("</text>");
        }

        // A warped WordArt figure is the rasteriser's bitmap, as ImageSharpPainter.PaintWordArt draws it:
        // the box with WordArtRasterPage.Padding on every side.
        void WordArt(PlacedWordArt wordArt)
        {
            var options = new WordArtRasterOptions
            {
                Dpi = context.Dpi,
                FontWidthScale = context.FontWidthScale,
                FontFallback = context.FontFallback,
                FontDirectory = context.FontDirectory,
                Deterministic = context.DeterministicRendering
            };

            if (new ImageSharpWordArtRasterizer().Render(wordArt.Visual, options) is not { } png)
            {
                return;
            }

            var pad = WordArtRasterPage.Padding(wordArt.Visual);
            ImageElement(AddImage(png), wordArt.X - pad, wordArt.Y - pad, wordArt.Width + 2 * pad, wordArt.Height + 2 * pad, 1);
        }

        void Image(PlacedImage image)
        {
            if (image.ShapeGroup is { } group)
            {
                InlineGroup(image, group);
                return;
            }

            if (image.Vector != null ||
                image.Tile != null ||
                image.Recolor != null ||
                image.Data is { } raw &&
                !Decodable(raw))
            {
                RasterPicture(image);
                return;
            }

            if (image.Data is not { } data)
            {
                return;
            }

            var index = AddImage(data);
            var centreX = image.X + image.Width / 2;
            var centreY = image.Y + image.Height / 2;
            var rotated = Math.Abs(image.RotationDegrees) > 0.01;
            var transformed = rotated || image.FlipHorizontal || image.FlipVertical;
            if (transformed)
            {
                // The flips mirror the picture before the turn spins it, both about the box's centre
                // (ImageSharpRenderContext.GetProcessedImage, and the box-centred placement after it).
                svg.Append("<g transform=\"");
                if (rotated)
                {
                    svg.Append("rotate(");
                    Number(image.RotationDegrees);
                    svg.Append(' ');
                    Number(centreX);
                    svg.Append(' ');
                    Number(centreY);
                    svg.Append(") ");
                }

                svg.Append("matrix(");
                svg.Append(image.FlipHorizontal ? "-1" : "1");
                svg.Append(" 0 0 ");
                svg.Append(image.FlipVertical ? "-1" : "1");
                svg.Append(' ');
                Number(image.FlipHorizontal ? 2 * centreX : 0);
                svg.Append(' ');
                Number(image.FlipVertical ? 2 * centreY : 0);
                svg.Append(")\">");
            }

            // The outline the picture is cut to, inside the flips so it mirrors with the picture — the
            // raster painter mirrors the outline to the mirrored pixels. A turned picture is left uncut.
            var cut = (image.ClipToEllipse || image.ClipSubpaths != null) &&
                      !rotated;
            if (cut)
            {
                var id = NextId();
                svg.Append("<clipPath id=\"");
                svg.Append(id);
                svg.Append("\">");
                if (image.ClipSubpaths is { } contours)
                {
                    svg.Append("<path d=\"");
                    foreach (var contour in contours)
                    {
                        if (contour.Count >= 3)
                        {
                            Contour(contour.Select(_ => (image.X + _.X * image.Width, image.Y + _.Y * image.Height)));
                        }
                    }

                    svg.Append("\"/>");
                }
                else
                {
                    Ellipse(centreX, centreY, image.Width / 2, image.Height / 2);
                    svg.Append("/>");
                }

                svg.Append("</clipPath><g clip-path=\"url(#");
                svg.Append(id);
                svg.Append(")\">");
            }

            if (image.Crop is {IsCropped: true} crop)
            {
                var (expandedX, expandedY, expandedWidth, expandedHeight) = crop.Expand(image.X, image.Y, image.Width, image.Height);
                OpenClip(image.X, image.Y, image.Width, image.Height);
                ImageElement(index, expandedX, expandedY, expandedWidth, expandedHeight, image.Opacity);
                svg.Append("</svg>");
            }
            else
            {
                ImageElement(index, image.X, image.Y, image.Width, image.Height, image.Opacity);
            }

            if (cut)
            {
                svg.Append("</g>");
            }

            if (transformed)
            {
                svg.Append("</g>");
            }

            Outline(image, cut);
        }

        // A picture the browser cannot be handed as it is — an SVG one, a tiled or recoloured one, a format
        // no browser decodes: the bitmap ImageSharpPainter.PaintImage draws of it, at the context's DPI and
        // at the same pixel position, as a PNG of its own.
        void RasterPicture(PlacedImage image)
        {
            var width = (int) Math.Round(Pixels(image.Width));
            var height = (int) Math.Round(Pixels(image.Height));
            Image<Rgba32>? processed = null;
            if (image.Vector is { } vector)
            {
                processed = context.GetVectorImage(vector, width, height, image.Crop, image.Recolor, (float) image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, image.Opacity);
            }
            else if (image.Data is { } data)
            {
                processed = image.Tile is { } tile &&
                            Math.Abs(image.RotationDegrees) <= 0.01
                    ? context.GetTiledImage(data, width, height, tile, image.Width, image.Height, image.Recolor, image.Opacity)
                    : null;
                processed ??= context.GetProcessedImage(data, width, height, image.Crop, image.Recolor, (float) image.RotationDegrees, image.FlipHorizontal, image.FlipVertical, image.Opacity);
            }

            if (processed == null)
            {
                return;
            }

            var cut = (image.ClipToEllipse || image.ClipSubpaths != null) &&
                      Math.Abs(image.RotationDegrees) <= 0.01;
            if (cut)
            {
                processed = context.GetClippedImage(processed, image.ClipSubpaths, image.FlipHorizontal, image.FlipVertical) ?? processed;
            }

            var left = (int) Math.Round(Pixels(image.X));
            var top = (int) Math.Round(Pixels(image.Y));
            if (Math.Abs(image.RotationDegrees) > 0.01)
            {
                left = (int) Math.Round(Pixels(image.X + image.Width / 2) - processed.Width / 2f);
                top = (int) Math.Round(Pixels(image.Y + image.Height / 2) - processed.Height / 2f);
            }

            var pixels = new byte[processed.Width * processed.Height * 4];
            processed.CopyPixelDataTo(pixels);
            using var png = new MemoryStream();
            PngWriter.Write(png, pixels, processed.Width, processed.Height, processed.Width * 4);
            ImageElement(AddImage(png.ToArray()), Points(left), Points(top), Points(processed.Width), Points(processed.Height), 1);
            Outline(image, cut);
        }

        double Pixels(double points) => points * context.Dpi / 72.0;

        double Points(double pixels) => pixels * 72.0 / context.Dpi;

        void Outline(PlacedImage image, bool cut)
        {
            // The picture's a:ln: a band outside the unrotated box (ImageSharpPainter.PaintImage).
            if (image.Outline is { } outline &&
                !cut)
            {
                var strokeWidth = outline.WidthPoints;
                var half = strokeWidth / 2;
                svg.Append("<rect");
                Attribute("x", image.X - half);
                Attribute("y", image.Y - half);
                Attribute("width", image.Width + strokeWidth);
                Attribute("height", image.Height + strokeWidth);
                svg.Append(" fill=\"none\"");
                Paint("stroke", ImageSharpShapeDrawing.ParseColor(outline.ColorHex, outline.Alpha));
                Attribute("stroke-width", strokeWidth);
                svg.Append("/>");
            }
        }

        // An inline shape group, its children scaled from the group's own coordinates into the box
        // (ImageSharpPainter.PaintInlineGroup); the geometry comes from the same path builder, flattened.
        void InlineGroup(PlacedImage placed, InlineShapeGroup group)
        {
            var sx = placed.Width / group.ChildExtentX;
            var sy = placed.Height / group.ChildExtentY;
            var rotated = group.RotationDegrees != 0;
            if (rotated)
            {
                svg.Append("<g transform=\"rotate(");
                Number(group.RotationDegrees);
                svg.Append(' ');
                Number(placed.X + placed.Width / 2);
                svg.Append(' ');
                Number(placed.Y + placed.Height / 2);
                svg.Append(")\">");
            }

            foreach (var shape in group.Shapes)
            {
                var x1 = placed.X + shape.X * sx;
                var y1 = placed.Y + shape.Y * sy;
                var width = shape.Width * sx;
                var height = shape.Height * sy;
                if (shape.Geometry == GroupShapeGeometry.Line)
                {
                    var (startX, endX) = shape.FlipHorizontal ? (x1 + width, x1) : (x1, x1 + width);
                    var (startY, endY) = shape.FlipVertical ? (y1 + height, y1) : (y1, y1 + height);
                    var lineWidth = shape.LineWidthEmu > 0 ? shape.LineWidthEmu / OoxmlUnits.EmusPerPointF : 0.75;
                    svg.Append("<line");
                    Attribute("x1", startX);
                    Attribute("y1", startY);
                    Attribute("x2", endX);
                    Attribute("y2", endY);
                    Paint("stroke", ImageSharpShapeDrawing.ParseColor(shape.ColorHex, shape.LineAlpha));
                    Attribute("stroke-width", lineWidth);
                    Dash(shape.LineDashPattern, lineWidth);
                    svg.Append(" stroke-linecap=\"square\" stroke-linejoin=\"bevel\"/>");
                    continue;
                }

                var isEllipse = shape.Geometry == GroupShapeGeometry.Ellipse;
                if (shape.Shadow is { } shadow)
                {
                    GroupGeometry(shape, x1 + shadow.OffsetX * sx, y1 + shadow.OffsetY * sy, width, height, isEllipse, close: true);
                    Paint("fill", ImageSharpShapeDrawing.ParseColor(shadow.ColorHex, shadow.Alpha));
                    svg.Append("/>");
                }

                if (shape.ImageData != null)
                {
                    GroupPicture(shape, x1, y1, width, height, isEllipse);
                }
                else if (shape.FillColorHex is { } fillHex)
                {
                    GroupGeometry(shape, x1, y1, width, height, isEllipse, close: true);
                    Paint("fill", ImageSharpShapeDrawing.ParseColor(fillHex, shape.FillAlpha));
                    svg.Append("/>");
                }

                if (shape.LineWidthEmu > 0)
                {
                    var strokeWidth = shape.LineWidthEmu / OoxmlUnits.EmusPerPointF;
                    GroupGeometry(shape, x1, y1, width, height, isEllipse, close: !shape.OpenOutline);
                    svg.Append(" fill=\"none\"");
                    Paint("stroke", ImageSharpShapeDrawing.ParseColor(shape.ColorHex, shape.LineAlpha));
                    Attribute("stroke-width", strokeWidth);
                    if (shape.LineDashPattern is not {Count: >= 2} &&
                        shape.RoundCap)
                    {
                        svg.Append(" stroke-linecap=\"round\"");
                    }

                    Dash(shape.LineDashPattern, strokeWidth);
                    svg.Append("/>");
                }
            }

            if (rotated)
            {
                svg.Append("</g>");
            }
        }

        // A group's pic:pic in its child rectangle, cut to the shape's outline when that is an ellipse
        // (ImageSharpShapeDrawing.RenderGroupPicture): the picture's own bytes where the browser can take
        // them, otherwise the bitmap the raster painter draws.
        void GroupPicture(GroupShape shape, double x, double y, double width, double height, bool isEllipse)
        {
            var vector = SvgDrawing.For(shape.ImageData, shape.ImageContentType);
            var bytes = shape.ImageContentType == "image/svg+xml"
                ? shape.ImageRasterFallbackData
                : shape.ImageData;
            if (vector == null &&
                bytes == null)
            {
                return;
            }

            string? clipId = null;
            if (isEllipse)
            {
                clipId = NextId();
                svg.Append("<clipPath id=\"");
                svg.Append(clipId);
                svg.Append("\">");
                GroupGeometry(shape, x, y, width, height, isEllipse, close: true);
                svg.Append("/></clipPath><g clip-path=\"url(#");
                svg.Append(clipId);
                svg.Append(")\">");
            }

            if (vector == null &&
                Decodable(bytes!))
            {
                var index = AddImage(bytes!);
                if (shape.ImageCrop is {IsCropped: true} crop)
                {
                    var (expandedX, expandedY, expandedWidth, expandedHeight) = crop.Expand(x, y, width, height);
                    OpenClip(x, y, width, height);
                    ImageElement(index, expandedX, expandedY, expandedWidth, expandedHeight, 1);
                    svg.Append("</svg>");
                }
                else
                {
                    ImageElement(index, x, y, width, height, 1);
                }
            }
            else
            {
                var pixelWidth = (int) Pixels(width);
                var pixelHeight = (int) Pixels(height);
                var processed = vector != null
                    ? context.GetVectorImage(vector, pixelWidth, pixelHeight, shape.ImageCrop, recolor: null, rotationDegrees: 0)
                    : context.GetProcessedImage(bytes!, pixelWidth, pixelHeight, shape.ImageCrop, recolor: null, rotationDegrees: 0);
                if (processed != null)
                {
                    var pixels = new byte[processed.Width * processed.Height * 4];
                    processed.CopyPixelDataTo(pixels);
                    using var png = new MemoryStream();
                    PngWriter.Write(png, pixels, processed.Width, processed.Height, processed.Width * 4);
                    ImageElement(AddImage(png.ToArray()), Points((int) Pixels(x)), Points((int) Pixels(y)), Points(processed.Width), Points(processed.Height), 1);
                }
            }

            if (clipId != null)
            {
                svg.Append("</g>");
            }
        }

        // Opens the element for a group shape's outline, leaving it for the caller to paint and close.
        void GroupGeometry(GroupShape shape, double x, double y, double width, double height, bool isEllipse, bool close)
        {
            if (ImageSharpShapeDrawing.BuildGroupShapePath(shape, (float) x, (float) y, (float) width, (float) height, close) is { } path)
            {
                svg.Append("<path fill-rule=\"evenodd\" d=\"");
                foreach (var simple in path.Flatten())
                {
                    Contour(ToPoints(simple.Points.Span), simple.IsClosed);
                }

                svg.Append('"');
                return;
            }

            if (isEllipse)
            {
                Ellipse(x + width / 2, y + height / 2, width / 2, height / 2);
                return;
            }

            svg.Append("<rect");
            Attribute("x", x);
            Attribute("y", y);
            Attribute("width", width);
            Attribute("height", height);
        }

        static List<(double X, double Y)> ToPoints(ReadOnlySpan<PointF> points)
        {
            var list = new List<(double X, double Y)>(points.Length);
            foreach (var point in points)
            {
                list.Add((point.X, point.Y));
            }

            return list;
        }

        // A floating shape: its freeform contours (rotation already baked in) or its preset box turned
        // about its centre, filled solid or with a linear gradient and outlined (ImageSharpPainter.PaintShape).
        void Shape(PlacedShape placed)
        {
            var shape = placed.Shape;
            if (shape.ImageData is {Length: > 0})
            {
                return;
            }

            double x = placed.X, y = placed.Y, width = placed.Width, height = placed.Height;
            string? gradientId = null;
            if (shape.Gradient is { } gradient)
            {
                gradientId = Gradient(gradient, x, y, width, height);
            }

            var hasFill = gradientId != null || shape.FillColorHex != null;
            var hasLine = shape.LineColorHex != null;
            if (!hasFill &&
                !hasLine)
            {
                return;
            }

            if (shape.Subpaths is {Count: > 0})
            {
                var rotation = shape.RotationDegrees * Math.PI / 180.0;
                var cos = Math.Cos(rotation);
                var sin = Math.Sin(rotation);
                svg.Append("<path d=\"");
                foreach (var contour in shape.Subpaths)
                {
                    Contour(contour.Select(_ =>
                    {
                        var localX = (shape.FlipHorizontal ? 1 - _.X : _.X) * width - width / 2;
                        var localY = (shape.FlipVertical ? 1 - _.Y : _.Y) * height - height / 2;
                        return (x + width / 2 + localX * cos - localY * sin, y + height / 2 + localX * sin + localY * cos);
                    }));
                }

                svg.Append('"');
            }
            else
            {
                if (shape.Preset == PresetShape.Ellipse)
                {
                    Ellipse(x + width / 2, y + height / 2, width / 2, height / 2);
                }
                else
                {
                    // A path rather than a <rect>: a line drawn as a box of no height is a rect SVG does
                    // not render at all, where its outline is the line.
                    svg.Append("<path d=\"");
                    Contour([(x, y), (x + width, y), (x + width, y + height), (x, y + height)]);
                    svg.Append('"');
                }

                if (shape.RotationDegrees != 0)
                {
                    svg.Append(" transform=\"rotate(");
                    Number(shape.RotationDegrees);
                    svg.Append(' ');
                    Number(x + width / 2);
                    svg.Append(' ');
                    Number(y + height / 2);
                    svg.Append(")\"");
                }
            }

            if (gradientId != null)
            {
                svg.Append(" fill=\"url(#");
                svg.Append(gradientId);
                svg.Append(")\"");
            }
            else if (shape.FillColorHex is { } fillHex)
            {
                Paint("fill", ImageSharpShapeDrawing.ParseColor(fillHex, shape.FillAlpha));
            }
            else
            {
                svg.Append(" fill=\"none\"");
            }

            if (shape.LineColorHex is { } lineHex)
            {
                var lineWidth = ShapeStroke.WidthPoints(shape.LineWidthPoints);
                Paint("stroke", ImageSharpShapeDrawing.ParseColor(lineHex, shape.LineAlpha));
                Attribute("stroke-width", lineWidth);
                Dash(shape.LineDashPattern, lineWidth);
                if (shape.RoundCap)
                {
                    svg.Append(" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
                }
            }

            svg.Append("/>");
        }

        // ImageSharpPainter.BuildGradientBrush: corner to corner through the box centre along the angle.
        string Gradient(GradientFill gradient, double x, double y, double width, double height)
        {
            var radians = gradient.DirectionDegrees * Math.PI / 180.0;
            var dx = Math.Cos(radians);
            var dy = Math.Sin(radians);
            var centreX = x + width / 2;
            var centreY = y + height / 2;
            var halfDiagonal = Math.Sqrt(width * width + height * height) / 2;
            var id = NextId();
            svg.Append("<linearGradient id=\"");
            svg.Append(id);
            svg.Append("\" gradientUnits=\"userSpaceOnUse\"");
            Attribute("x1", centreX - dx * halfDiagonal);
            Attribute("y1", centreY - dy * halfDiagonal);
            Attribute("x2", centreX + dx * halfDiagonal);
            Attribute("y2", centreY + dy * halfDiagonal);
            svg.Append("><stop offset=\"0\"");
            Paint("stop-color", ImageSharpShapeDrawing.ParseColor(gradient.StartColorHex, gradient.StartAlpha));
            svg.Append("/><stop offset=\"1\"");
            Paint("stop-color", ImageSharpShapeDrawing.ParseColor(gradient.EndColorHex, gradient.EndAlpha));
            svg.Append("/></linearGradient>");
            return id;
        }

        void TableRow(PlacedTableRow row)
        {
            foreach (var cell in row.Cells)
            {
                if (!string.IsNullOrEmpty(cell.BackgroundColorHex))
                {
                    Rect(cell.X, cell.Y, cell.Width, cell.Height, ImageSharpRenderContext.ParseColor(cell.BackgroundColorHex));
                }

                foreach (var floating in cell.Floats)
                {
                    Item(floating);
                }

                // The clip bounds the content only, never the shading or the borders.
                var clipped = cell.ClipContent;
                if (clipped)
                {
                    var (clipX, clipWidth) = cell.ClipHorizontally
                        ? (cell.X - cell.ClipSpillLeft, cell.Width + cell.ClipSpillLeft + cell.ClipSpillRight)
                        : (-100000d, 200000d);
                    var (clipY, clipHeight) = cell.ClipVertically
                        ? (cell.Y, cell.Height)
                        : (-100000d, 200000d);
                    OpenClip(clipX, clipY, clipWidth, clipHeight);
                }

                foreach (var content in cell.Content)
                {
                    Item(content);
                }

                if (clipped)
                {
                    svg.Append("</svg>");
                }

                if (cell.Borders is { } borders)
                {
                    CellEdges(cell, borders);
                }

                if (cell.Diagonals is {HasAny: true} diagonals)
                {
                    Diagonal(diagonals.Down, cell.X, cell.Y, cell.X + cell.Width, cell.Y + cell.Height);
                    Diagonal(diagonals.Up, cell.X + cell.Width, cell.Y, cell.X, cell.Y + cell.Height);
                }
            }
        }

        void Diagonal(BorderEdge edge, double x0, double y0, double x1, double y1)
        {
            if (BorderStroke.DiagonalThickness(edge) is > 0 and var thickness)
            {
                Stroke(x0, y0, x1, y1, Shaded(ImageSharpRenderContext.ParseColor(edge.ColorHex), 1), thickness, BorderStroke.DashPattern(edge.Style, edge.WidthPoints));
            }
        }

        // Word's cell geometry, waves along the box edge (ImageSharpPainter.PaintCellEdges).
        void CellEdges(PlacedCell cell, CellBorders borders)
        {
            foreach (var line in BorderStroke.CellEdgeLines(cell.X, cell.Y, cell.Width, cell.Height, borders, cell.BottomEdgeInset))
            {
                var color = Shaded(ImageSharpRenderContext.ParseColor(line.Edge.ColorHex), line.Shade);
                var dash = BorderStroke.DashPattern(line.Edge.Style, line.Edge.WidthPoints);
                if (line.Horizontal)
                {
                    Stroke(line.From, line.At, line.To, line.At, color, line.Thickness, dash);
                }
                else
                {
                    Stroke(line.At, line.From, line.At, line.To, color, line.Thickness, dash);
                }
            }

            double left = cell.X, top = cell.Y, right = cell.X + cell.Width, bottom = cell.Y + cell.Height;
            Wave(borders.Top, horizontal: true, left, right, top, outward: -1);
            Wave(borders.Bottom, horizontal: true, left, right, bottom, outward: 1);
            Wave(borders.Left, horizontal: false, top, bottom, left, outward: -1);
            Wave(borders.Right, horizontal: false, top, bottom, right, outward: 1);

            void Wave(BorderEdge edge, bool horizontal, double from, double to, double at, int outward)
            {
                if (BorderStroke.Draws(edge) &&
                    BorderStroke.Waves(edge.Style) is {Length: > 0} waves)
                {
                    Waves(edge, waves, horizontal, from, to, at, outward);
                }
            }
        }

        void Edges(double x, double y, double width, double height, CellBorders borders, BorderStroke.Scope scope = BorderStroke.Scope.Paragraph)
        {
            double right = x + width, bottom = y + height;
            var drawsLeft = BorderStroke.Draws(borders.Left);
            var drawsRight = BorderStroke.Draws(borders.Right);
            var drawsTop = BorderStroke.Draws(borders.Top);
            var drawsBottom = BorderStroke.Draws(borders.Bottom);
            Edge(borders.Top, horizontal: true, x, right, y, scope, outward: -1, drawsLeft, drawsRight);
            Edge(borders.Bottom, horizontal: true, x, right, bottom, scope, outward: 1, drawsLeft, drawsRight);
            Edge(borders.Left, horizontal: false, y, bottom, x, scope, outward: -1, drawsTop, drawsBottom);
            Edge(borders.Right, horizontal: false, y, bottom, right, scope, outward: 1, drawsTop, drawsBottom);
        }

        // ImageSharpPainter.StrokeEdge: one band per line of a multi-line style, offset away from the box,
        // each grown at both ends to meet the perpendicular edges' matching band.
        void Edge(BorderEdge edge, bool horizontal, double from, double to, double at, BorderStroke.Scope scope, int outward, bool extendStart, bool extendEnd)
        {
            if (!BorderStroke.Draws(edge))
            {
                return;
            }

            if (BorderStroke.Waves(edge.Style) is {Length: > 0} waves)
            {
                Waves(edge, waves, horizontal, from, to, at, outward);
                return;
            }

            var dash = BorderStroke.DashPattern(edge.Style, edge.WidthPoints);
            var bands = BorderStroke.Bands(edge.Style, edge.WidthPoints, scope, trailingEdge: outward > 0);
            var shift = BorderStroke.OutwardShift(bands, scope);
            var baseColor = ImageSharpRenderContext.ParseColor(edge.ColorHex);
            foreach (var band in bands)
            {
                var offset = band.Offset + shift;
                var half = band.Thickness / 2;
                var line = at + outward * offset;
                var start = from - offset - (extendStart ? half : 0);
                var end = to + offset + (extendEnd ? half : 0);
                var color = Shaded(baseColor, band.Shade);
                if (horizontal)
                {
                    Stroke(start, line, end, line, color, band.Thickness, dash);
                }
                else
                {
                    Stroke(line, start, line, end, color, band.Thickness, dash);
                }
            }
        }

        void Waves(BorderEdge edge, BorderStroke.WaveBand[] waves, bool horizontal, double from, double to, double at, int outward)
        {
            var color = ImageSharpRenderContext.ParseColor(edge.ColorHex);
            foreach (var wave in waves)
            {
                var centre = at + outward * wave.Offset;
                var points = BorderStroke.WavePoints(from, to, wave.Period, wave.Amplitude)
                    .Select(_ => horizontal ? (_.Along, centre + _.Across) : (centre + _.Across, _.Along))
                    .ToList();
                if (points.Count > 1)
                {
                    Polyline(points, color, wave.Thickness);
                }
            }
        }

        static Color Shaded(Color color, double shade)
        {
            if (shade >= 1)
            {
                return color;
            }

            var pixel = color.ToPixel<Rgba32>();
            return Color.FromPixel(new Rgba32((byte) (pixel.R * shade), (byte) (pixel.G * shade), (byte) (pixel.B * shade), pixel.A));
        }

        // Primitives

        // A straight stroke: an axis-aligned solid one as the rectangle it covers, which a browser fills
        // without a stroker; anything else as a line with butt ends, ImageSharp's default.
        void Stroke(double x0, double y0, double x1, double y1, Color color, double thickness, float[]? dash)
        {
            if (thickness <= 0)
            {
                return;
            }

            if (dash == null &&
                (y0 == y1 || x0 == x1))
            {
                if (y0 == y1)
                {
                    Rect(Math.Min(x0, x1), y0 - thickness / 2, Math.Abs(x1 - x0), thickness, color);
                }
                else
                {
                    Rect(x0 - thickness / 2, Math.Min(y0, y1), thickness, Math.Abs(y1 - y0), color);
                }

                return;
            }

            svg.Append("<line");
            Attribute("x1", x0);
            Attribute("y1", y0);
            Attribute("x2", x1);
            Attribute("y2", y1);
            Paint("stroke", color);
            Attribute("stroke-width", thickness);
            if (dash != null)
            {
                svg.Append(" stroke-dasharray=\"");
                for (var index = 0; index < dash.Length; index++)
                {
                    if (index > 0)
                    {
                        svg.Append(' ');
                    }

                    Number(dash[index]);
                }

                svg.Append('"');
            }

            svg.Append("/>");
        }

        // A preset dash (a:prstDash) is declared in multiples of the stroke's width.
        void Dash(IReadOnlyList<double>? pattern, double strokeWidth)
        {
            if (pattern is not {Count: >= 2} || strokeWidth <= 0)
            {
                return;
            }

            svg.Append(" stroke-dasharray=\"");
            for (var index = 0; index < pattern.Count; index++)
            {
                if (index > 0)
                {
                    svg.Append(' ');
                }

                Number(pattern[index] * strokeWidth);
            }

            svg.Append('"');
        }

        void Polyline(IEnumerable<(double X, double Y)> points, Color color, double thickness)
        {
            svg.Append("<polyline fill=\"none\" points=\"");
            var first = true;
            foreach (var (x, y) in points)
            {
                if (!first)
                {
                    svg.Append(' ');
                }

                first = false;
                Number(x);
                svg.Append(',');
                Number(y);
            }

            svg.Append('"');
            Paint("stroke", color);
            Attribute("stroke-width", thickness);
            svg.Append("/>");
        }

        void Contour(IEnumerable<(double X, double Y)> points, bool close = true)
        {
            var first = true;
            foreach (var (x, y) in points)
            {
                svg.Append(first ? 'M' : 'L');
                first = false;
                Number(x);
                svg.Append(' ');
                Number(y);
            }

            if (!first &&
                close)
            {
                svg.Append('Z');
            }
        }

        // Opens an ellipse, leaving it for the caller to paint and close.
        void Ellipse(double centreX, double centreY, double radiusX, double radiusY)
        {
            svg.Append("<ellipse");
            Attribute("cx", centreX);
            Attribute("cy", centreY);
            Attribute("rx", radiusX);
            Attribute("ry", radiusY);
        }

        void Rect(double x, double y, double width, double height, Color color)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            svg.Append("<rect");
            Attribute("x", x);
            Attribute("y", y);
            Attribute("width", width);
            Attribute("height", height);
            Paint("fill", color);
            svg.Append("/>");
        }

        void ImageElement(int index, double x, double y, double width, double height, double opacity)
        {
            svg.Append("<image data-i=\"");
            svg.Append(index);
            svg.Append('"');
            Attribute("x", x);
            Attribute("y", y);
            Attribute("width", width);
            Attribute("height", height);
            svg.Append(" preserveAspectRatio=\"none\"");
            if (opacity < 1)
            {
                Attribute("opacity", opacity);
            }

            svg.Append("/>");
        }

        int AddImage(byte[] data)
        {
            if (!imageIndex.TryGetValue(data, out var index))
            {
                index = Images.Count;
                Images.Add(data);
                imageIndex[data] = index;
            }

            return index;
        }

        // The raster formats every browser decodes in an SVG <image>.
        static bool Decodable(byte[] data) =>
            data.Length > 12 &&
            ((data[0] == 0x89 &&
              data[1] == 0x50 &&
              data[2] == 0x4E &&
              data[3] == 0x47) ||
             (data[0] == 0xFF &&
              data[1] == 0xD8) ||
             (data[0] == 0x47 &&
              data[1] == 0x49 &&
              data[2] == 0x46) ||
             (data[0] == 0x42 &&
              data[1] == 0x4D) ||
             (data[0] == 0x52 &&
              data[1] == 0x49 &&
              data[2] == 0x46 &&
              data[3] == 0x46 &&
              data[8] == 0x57 &&
              data[9] == 0x45 &&
              data[10] == 0x42 &&
              data[11] == 0x50));

        string NextId() => $"{idPrefix}{ids++}";

        void Paint(string attribute, Color color)
        {
            var pixel = color.ToPixel<Rgba32>();
            svg.Append(' ');
            svg.Append(attribute);
            svg.Append("=\"#");
            Hex(pixel.R);
            Hex(pixel.G);
            Hex(pixel.B);
            svg.Append('"');
            if (pixel.A < 255)
            {
                svg.Append(' ');
                svg.Append(attribute == "stop-color" ? "stop" : attribute);
                svg.Append("-opacity=\"");
                Number3(pixel.A / 255.0);
                svg.Append('"');
            }
        }

        void Hex(byte value)
        {
            const string digits = "0123456789abcdef";
            svg.Append(digits[value >> 4]);
            svg.Append(digits[value & 15]);
        }

        void Attribute(string name, double value)
        {
            svg.Append(' ');
            svg.Append(name);
            svg.Append("=\"");
            Number(value);
            svg.Append('"');
        }

        // Points to two decimals — a fiftieth of a pixel at 144 dpi — written by hand: number formatting
        // is the hot path of the whole writer on an interpreted runtime.
        void Number(double value) => Fixed(value, 100);

        void Number3(double value) => Fixed(value, 1000);

        void Fixed(double value, long scale)
        {
            if (!double.IsFinite(value))
            {
                svg.Append('0');
                return;
            }

            var scaled = (long) Math.Round(value * scale);
            if (scaled < 0)
            {
                svg.Append('-');
                scaled = -scaled;
            }

            svg.Append(scaled / scale);
            var fraction = scaled % scale;
            if (fraction == 0)
            {
                return;
            }

            svg.Append('.');
            for (var unit = scale / 10; unit > 0 && fraction > 0; unit /= 10)
            {
                svg.Append((char) ('0' + fraction / unit));
                fraction %= unit;
            }
        }

        void Escape(string text)
        {
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '&':
                        svg.Append("&amp;");
                        break;
                    case '<':
                        svg.Append("&lt;");
                        break;
                    case '>':
                        svg.Append("&gt;");
                        break;
                    case <= ' ':
                        // Left out, with its x (Text).
                        break;
                    default:
                        svg.Append(ch);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Where each character of a run starts, in points from the run's start, as SixLabors.Fonts lays the
    /// run out for ImageSharpPainter. Kept across documents, since an edit lays out a fresh document whose
    /// lines are nearly all the lines it had: the key is the face and the text, never a font object.
    /// </summary>
    static class GlyphPositions
    {
        const int limit = 100000;
        static readonly Lock gate = new();
        static readonly Dictionary<(string Family, bool Bold, bool Italic, float Size, KerningMode Kerning, string Text), float[]> cache = [];

        public static float[] Of(Font font, KerningMode kerning, string text)
        {
            var key = (font.Family.Name, font.IsBold, font.IsItalic, font.Size, kerning, text);
            lock (gate)
            {
                if (cache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            var positions = Lay(font, kerning, text);
            lock (gate)
            {
                if (cache.Count >= limit)
                {
                    cache.Clear();
                }

                cache[key] = positions;
            }

            return positions;
        }

        public static void Clear()
        {
            lock (gate)
            {
                cache.Clear();
            }
        }

        static float[] Lay(Font font, KerningMode kerning, string text)
        {
            var positions = new float[text.Length];
            Array.Fill(positions, float.NaN);
            var options = new TextOptions(font)
            {
                Dpi = 72,
                KerningMode = kerning
            };
            foreach (var glyph in TextMeasurer.GetGlyphMetrics(text, options).Span)
            {
                var index = glyph.StringIndex;
                if (index >= 0 &&
                    index < positions.Length &&
                    float.IsNaN(positions[index]))
                {
                    positions[index] = glyph.Advance.X;
                }
            }

            // A character no glyph starts at — a ligature's tail, a pair's second half — stands where the
            // one before it does.
            var last = 0f;
            for (var index = 0; index < positions.Length; index++)
            {
                if (float.IsNaN(positions[index]))
                {
                    positions[index] = last;
                }
                else
                {
                    last = positions[index];
                }
            }

            return positions;
        }
    }
}