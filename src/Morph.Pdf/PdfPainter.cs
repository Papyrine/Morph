/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> onto a PDF through Krilla — the layout
/// engine's first painter (<c>docs/layout-engine.md</c>, step 5). It performs **no** measurement and
/// **no** pagination: every page size, line and run position comes from the tree the <c>Fragmenter</c>
/// already produced, so a paint is a pure draw pass.
///
/// <para>Draws paragraph text (one <see cref="PlacedRun"/> per source run at the line's baseline, in its
/// resolved face and colour, each glyph at the advance the layout measured it at), tables (each
/// <see cref="PlacedCell"/>'s shading, content and borders), run decorations, list markers, tabs and
/// leaders, images, shapes and nested tables. <c>PdfRenderer</c> routes every document here.</para>
/// </summary>
static class PdfPainter
{
    // Resolution the WordArt raster is produced at. PDF is vector, so the shape has no natural pixel size;
    // 300 dpi keeps the embedded bitmap sharp in print without bloating the file.
    const int wordArtRasterDpi = 300;

    /// <summary>
    /// Paints at the default font settings. Prefer the overload taking a context when the conversion's
    /// font settings matter.
    /// </summary>
    public static byte[] Paint(LaidOutDocument document, string? fontDirectory)
    {
        using var context = NewContext(document, compatibility: null, fontWidthScale: 1, fontFallback: null, fontDirectory);
        Paint(document, context);
        return context.Document.Finish();
    }

    /// <summary>
    /// The context is created once for the fonts, caches and Krilla document; its PageSettings is only a
    /// constructor formality here (each page's size comes from the tree, positions are absolute, and the
    /// painter never measures), so the first page's settings serve.
    /// </summary>
    public static PdfRenderContext NewContext(
        LaidOutDocument document,
        CompatibilitySettings? compatibility,
        double fontWidthScale,
        Func<string, string?>? fontFallback,
        string? fontDirectory)
    {
        var pages = document.Pages;

        return new(pages.Count > 0 ? pages[0].Settings : new(),
            compatibility,
            fontWidthScale,
            fontFallback,
            fontDirectory);
    }

    /// <summary>
    /// Draws the tree onto <paramref name="context"/>'s document. The caller owns the context so it can
    /// resolve fonts with the conversion's own width scale, fallback and compatibility settings — the
    /// measurer already does, and a painter resolving a different face would draw text off its measured
    /// line. Only the pages in <paramref name="pages"/> (1-based, inclusive) are drawn, when given.
    /// </summary>
    public static void Paint(
        LaidOutDocument document,
        PdfRenderContext context,
        bool rasterizeWordArt = true,
        Action<ExportWarning>? onWarning = null,
        PageRange? pages = null,
        Cancel cancel = default)
    {
        for (var index = 0; index < document.Pages.Count; index++)
        {
            var number = index + 1;
            if (pages is { } range &&
                (number < range.Start || number > range.End))
            {
                continue;
            }

            cancel.ThrowIfCancellationRequested();

            var laidOutPage = document.Pages[index];
            var settings = laidOutPage.Settings;
            var width = settings.WidthPoints;
            var height = settings.HeightPoints;
            using var page = context.Document.StartPage((float) width, (float) height);
            var surface = page.Surface;
            context.Surface = surface;

            // Page background fill (w:background) behind everything — without it a dark-themed page's
            // white text lands invisibly on white.
            var background = settings.BackgroundColorHex;
            if (!string.IsNullOrEmpty(background))
            {
                PdfShapeDrawing.FillRectangle(context, surface, PdfRenderContext.ParseColor(background), 0, 0, width, height);
            }

            foreach (var item in laidOutPage.Items)
            {
                PaintItem(context, surface, item, rasterizeWordArt, onWarning);
            }

            // Decorative page borders (w:pgBorders) frame every page, over the content (Word's
            // default zOrder is front). Geometry is shared via PageBorders.EdgeRect.
            if (settings.PageBorders is { HasAnyBorder: true } pageBorders)
            {
                var (borderX, borderY, borderWidth, borderHeight) = pageBorders.EdgeRect(settings);
                PaintBorder(context, surface, new((float) borderX, (float) borderY, (float) borderWidth, (float) borderHeight, pageBorders.Edges), BorderStroke.Scope.Page);
            }

            context.Surface = null;
        }
    }

    static void PaintItem(PdfRenderContext context, Surface surface, PlacedItem item, bool rasterizeWordArt = true, Action<ExportWarning>? onWarning = null)
    {
        switch (item)
        {
            case PlacedLine line:
                PaintLine(context, surface, line);
                break;
            case PlacedTableRow row:
                PaintTableRow(context, surface, row);
                break;
            case PlacedImage image:
                PaintImage(context, surface, image);
                break;
            case PlacedShape shape:
                PaintShape(context, surface, shape);
                break;
            case PlacedWordArt wordArt:
                PaintWordArt(context, surface, wordArt, rasterizeWordArt, onWarning);
                break;
            case PlacedShading shading:
                PdfShapeDrawing.FillRectangle(context, surface, PdfRenderContext.ParseColor(shading.ColorHex), shading.X, shading.Y, shading.Width, shading.Height);
                break;
            case PlacedBorder border:
                PaintBorder(context, surface, border);
                break;
            case PlacedRotatedGroup group:
                PaintRotatedGroup(context, surface, group);
                break;
            case PlacedClipGroup clip:
                PaintClipGroup(context, surface, clip, rasterizeWordArt, onWarning);
                break;
            case PlacedScaledGroup scaled:
                PaintScaledGroup(context, surface, scaled, rasterizeWordArt, onWarning);
                break;
        }
    }

    // See SkiaPainter.PaintScaledGroup.
    static void PaintScaledGroup(PdfRenderContext context, Surface surface, PlacedScaledGroup group, bool rasterizeWordArt, Action<ExportWarning>? onWarning)
    {
        using var layer = surface.PushTransform(Matrix3x2.CreateScale(group.Scale) * Matrix3x2.CreateTranslation(group.X, group.Y));
        foreach (var item in group.Items)
        {
            PaintItem(context, surface, item, rasterizeWordArt, onWarning);
        }
    }

    // See SkiaPainter.PaintClipGroup.
    static void PaintClipGroup(PdfRenderContext context, Surface surface, PlacedClipGroup clip, bool rasterizeWordArt, Action<ExportWarning>? onWarning)
    {
        using var layer = PdfShapeDrawing.ClipRectangle(surface, clip.X, clip.Y, clip.Width, clip.Height);
        foreach (var item in clip.Items)
        {
            PaintItem(context, surface, item, rasterizeWordArt, onWarning);
        }
    }

    // See SkiaPainter.PaintRotatedGroup.
    static void PaintRotatedGroup(PdfRenderContext context, Surface surface, PlacedRotatedGroup group)
    {
        using var layer = PdfShapeDrawing.RotateAt(surface, group.RotationDegrees, group.X + group.Width / 2, group.Y + group.Height / 2);
        foreach (var item in group.Items)
        {
            PaintItem(context, surface, item);
        }
    }

    // A warped WordArt figure. Krilla cannot draw the warp geometry, so a raster backend rasterizes the
    // shape to a transparent PNG (discovered reflectively, so Morph.Pdf keeps no compile-time dependency on
    // either engine) and the PNG is embedded. The PNG is the box surrounded by WordArtRasterPage.Padding on
    // every side, since several warps draw past the declared box, so the draw origin steps back by that
    // padding and the rectangle grows to match — leaving the box region at (X, Y) with the overflow
    // spilling onto the page. When no raster backend is present the shape is dropped rather than drawn
    // wrong; the caller reserved its height either way, so pagination is unaffected.
    static void PaintWordArt(PdfRenderContext context, Surface surface, PlacedWordArt wordArt, bool rasterizeWordArt, Action<ExportWarning>? onWarning)
    {
        if (!rasterizeWordArt)
        {
            // The conversion asked for no rasterized WordArt (a text-only or size-sensitive PDF), so the
            // warp is dropped rather than approximated. Its height was reserved either way, so the flow
            // below it is unaffected.
            return;
        }

        if (WordArtRasterizerFactory.TryGet() is not { } rasterizer)
        {
            onWarning?.Invoke(new(WarningKind.UnsupportedElement,
                "WordArt could not be drawn in the PDF: no raster backend (Morph.Skia or Morph.ImageSharp) is loaded."));
            return;
        }

        var options = new WordArtRasterOptions
        {
            Dpi = wordArtRasterDpi,
            FontWidthScale = context.FontWidthScale,
            FontFallback = context.FontFallback,
            FontDirectory = context.FontDirectory,
            Deterministic = true
        };

        byte[]? png;
        try
        {
            png = rasterizer.Render(wordArt.Visual, options);
        }
        catch (Exception exception)
        {
            onWarning?.Invoke(
                new(
                    WarningKind.UnsupportedElement,
                    $"WordArt could not be rasterized for the PDF and was dropped: {exception.Message}"));
            return;
        }

        if (png == null)
        {
            return;
        }

        var pad = WordArtRasterPage.Padding(wordArt.Visual);
        PdfPicture.Of(context.GetImage(png))?.Draw(surface, wordArt.X - pad, wordArt.Y - pad, wordArt.Width + 2 * pad, wordArt.Height + 2 * pad);
    }

    static void PaintLine(PdfRenderContext context, Surface surface, PlacedLine line)
    {
        var ascent = line.Baseline - line.Y;
        var revisionBarDrawn = false;
        foreach (var run in line.Runs)
        {
            // A tab-leader filler fills its span with the leader (tiled glyph, or a baseline rule for
            // underscore) instead of text — its Text is empty.
            if (run.Leader != TabLeader.None)
            {
                DrawLeader(context, surface, run, line.Baseline);
                continue;
            }

            if (string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            // A line carrying a tracked change gets Word's change bar — see SkiaPainter.PaintLine.
            if (run.Properties.IsRevisionMark &&
                !revisionBarDrawn)
            {
                revisionBarDrawn = true;
                var barX = context.PageSettings.MarginLeft / 2;
                PdfShapeDrawing.StrokeLine(surface, context.GetStroke(PdfColor.Black, 0.75), barX, line.Y, barX, line.Y + line.Height);
            }

            var properties = run.Properties;
            var color = PdfRenderContext.ParseColor(properties.ColorHex);

            // Highlight (w:highlight / run shading) fills behind the glyphs, over the line box.
            if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
            {
                PdfShapeDrawing.FillRectangle(context, surface, PdfRenderContext.ParseColor(properties.BackgroundColorHex), run.X, line.Y, run.Width, line.Height);
            }

            // A superscript or subscript sits off the line baseline by its shift (VerticalRunPosition).
            var baseline = line.Baseline - run.BaselineShift;
            // w:w stretches the run about its own start, glyphs and advances alike (SkiaPainter.PaintLine).
            Layer? scaled = null;
            if (properties.CharacterScalePercent != 100)
            {
                scaled = surface.PushTransform(Matrix3x2.CreateScale((float) (properties.CharacterScalePercent / 100), 1, new(run.X, baseline)));
            }

            DrawTracked(context, surface, run.Text, properties, color, run.X, baseline);
            scaled?.Dispose();

            // Underline below the baseline, strike through the x-height (TextDecorations).
            if (properties.Underline || properties.Strikethrough || properties.DoubleStrikethrough)
            {
                PaintDecorations(context, surface, run, properties, color, baseline, ascent);
            }

            // w:bdr — see SkiaPainter.PaintLine.
            if (properties.Border is {} runBorder &&
                BorderStroke.Draws(runBorder))
            {
                var (boxX, boxY, boxWidth, boxHeight) = BorderStroke.RunBorderBox(runBorder, run.X, run.Width, line.Y, line.Height, BorderStroke.LinePad(line.Runs), !run.BorderInside);
                PaintRunBorder(context, surface, boxX, boxY, boxWidth, boxHeight, runBorder);
            }
        }

        foreach (var image in line.Images)
        {
            // An inline chart arrives drawn: its shapes and labels, placed (PlacedImage.Composed).
            if (image.Composed is { } composed)
            {
                foreach (var item in composed)
                {
                    PaintItem(context, surface, item);
                }

                continue;
            }

            PaintImage(context, surface, image);
        }
    }

    // The run's underline and strike rules — see SkiaPainter.PaintDecorations. PDF space is points. A
    // word's width is the face's own advances plus tracking, the steps DrawTracked draws by.
    static void PaintDecorations(PdfRenderContext context, Surface surface, PlacedRun run, RunProperties properties, PdfColor color, float baseline, float ascent)
    {
        var underlineColor = properties.UnderlineColorHex == null
            ? color
            : PdfRenderContext.ParseColor(properties.UnderlineColorHex);
        var face = context.GetFace(properties);
        var sizePoints = VerticalRunPosition.RenderSizePoints(properties);
        var rules = new List<TextDecorations.Rule>();
        var waves = new List<TextDecorations.Wave>();
        TextDecorations.Collect(
            properties,
            run.Text,
            run.X,
            run.Width,
            baseline,
            ascent,
            _ => (float) (Advance(face, _, sizePoints) + properties.CharacterSpacingPoints * _.Length),
            rules,
            waves);
        foreach (var rule in rules)
        {
            PdfShapeDrawing.StrokeLine(surface, context.GetStroke(rule.IsStrike ? color : underlineColor, rule.Thickness), rule.X0, rule.Y, rule.X1, rule.Y);
        }

        foreach (var wave in waves)
        {
            if (PdfShapeDrawing.Polyline(wave.Points.Select(_ => ((double) _.X, (double) _.Y)).ToArray()) is { } path)
            {
                PdfShapeDrawing.Stroke(surface, path, context.GetStroke(underlineColor, wave.Thickness));
            }
        }
    }

    static double Advance(PdfFace face, string text, double sizePoints)
    {
        var metrics = face.Metrics;
        var units = 0d;
        var i = 0;
        while (i < text.Length)
        {
            var codepoint = char.ConvertToUtf32(text, i);
            units += metrics.AdvanceUnits(codepoint);
            i += char.IsHighSurrogate(text[i]) &&
                 i + 1 < text.Length ? 2 : 1;
        }

        return units / metrics.UnitsPerEm * sizePoints;
    }

    // Draws a run's text as one glyph run, each glyph at the face's own advance for its character,
    // spread by w:spacing tracking (letter-spacing) and pulled by its kern. The run's placed width already
    // includes both (the canonical measurer kerned and tracked the advances), so a following run starts
    // past it. Glyphs map one to one onto characters through the face's cmap, as the measurer reads them,
    // so no shaping can move a glyph off the position the layout gave it.
    static void DrawTracked(PdfRenderContext context, Surface surface, string text, RunProperties properties, PdfColor color, double penX, double baseline)
    {
        var face = context.GetFace(properties);
        var sizePoints = VerticalRunPosition.RenderSizePoints(properties);
        var kernShifts = KernShifts(face, text, properties, context.FontWidthScale);
        var glyphs = Glyphs(face, text, properties.CharacterSpacingPoints, sizePoints, kernShifts);
        if (glyphs.Count == 0)
        {
            return;
        }

        surface.SetFill(context.GetFill(color));

        // A bold run in a face that is not bold: the glyphs are filled and stroked, which thickens every
        // stem by the stroke's width (BoldSimulation) — the construction the raster painters use.
        if (properties.SyntheticBold != SyntheticBold.None)
        {
            var width = BoldSimulation.StrokeWidthPoints(properties.SyntheticBold, sizePoints, 0);
            surface.SetStroke(context.GetStroke(color, width) with
            {
                LineJoin = LineJoin.Miter
            });
        }
        else
        {
            surface.SetStroke(null);
        }

        // An italic run whose family bundles no italic face: shear the glyphs right about the
        // baseline by the Word-measured oblique.
        Layer? sheared = null;
        if (context.NeedsSyntheticItalic(properties))
        {
            var skew = (float) FontHelpers.SyntheticItalicSkew;
            sheared = surface.PushTransform(new(1, 0, -skew, 1, skew * (float) baseline, 0));
        }

        surface.DrawGlyphs(new((float) penX, (float) baseline), face.Font, (float) sizePoints, text, glyphs);
        sheared?.Dispose();
        surface.SetStroke(null);
    }

    // One glyph per character (a surrogate pair intact), advancing by the face's own advance plus the
    // tracking; a kern is the glyph's offset from where the advances alone put it.
    static List<Glyph> Glyphs(PdfFace face, string text, double trackingPoints, double sizePoints, double[]? kernShifts)
    {
        var metrics = face.Metrics;
        var unitsPerEm = face.Font.UnitsPerEm;
        var pointsToUnits = sizePoints > 0 ? unitsPerEm / sizePoints : 0;
        var glyphs = new List<Glyph>(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var length = char.IsHighSurrogate(text[i]) &&
                         i + 1 < text.Length ? 2 : 1;
            var codepoint = char.ConvertToUtf32(text, i);
            var advance = metrics.AdvanceUnits(codepoint) * unitsPerEm / metrics.UnitsPerEm + trackingPoints * pointsToUnits;
            var offset = kernShifts == null ? 0 : kernShifts[i] * pointsToUnits;
            glyphs.Add(new(metrics.GlyphId(codepoint), (float) advance, i, length, (float) offset));
            i += length;
        }

        return glyphs;
    }

    // The kern the layout gave each glyph of a run, at the face and size it was measured with (a
    // superscript's reduced size included), or null for a run that does not kern.
    static double[]? KernShifts(PdfFace face, string text, RunProperties properties, double fontWidthScale)
    {
        if (!CanonicalParagraphMeasurer.KerningEnabled(properties))
        {
            return null;
        }

        return CanonicalTextMeasurer.KernShiftsPoints(face.Metrics, text, VerticalRunPosition.RenderSizePoints(properties), fontWidthScale);
    }

    // Fills a tab-leader gap: a baseline rule for underscore, otherwise the leader glyph tiled across the
    // span.
    static void DrawLeader(PdfRenderContext context, Surface surface, PlacedRun run, double baseline)
    {
        if (run.Width <= 0)
        {
            return;
        }

        var color = PdfRenderContext.ParseColor(run.Properties.ColorHex);
        var fontSize = run.Properties.FontSizePoints;

        if (run.Leader == TabLeader.Underscore)
        {
            var underscoreY = baseline + fontSize * 0.12;
            PdfShapeDrawing.StrokeLine(surface, context.GetStroke(color, Math.Max(0.5, fontSize / 16)), run.X, underscoreY, run.X + run.Width, underscoreY);
            return;
        }

        var leaderChar = run.Leader switch
        {
            TabLeader.Hyphen => '-',
            TabLeader.MiddleDot => '·',
            TabLeader.Heavy => '—',
            _ => '.'
        };

        var face = context.GetFace(run.Properties);
        var sizePoints = VerticalRunPosition.RenderSizePoints(run.Properties);
        var glyphWidth = (double) face.Metrics.AdvanceUnits(leaderChar) / face.Metrics.UnitsPerEm * sizePoints;
        if (glyphWidth <= 0)
        {
            return;
        }

        // Word draws a leader as the glyph repeated at its NATURAL advance — a literal run of
        // dots — not at a doubled stride. Measured on table_of_contents/01: Word's dot pitch is
        // ~6.3px at 150 DPI (the '.' advance) with the last dot within one advance of the page
        // number, where the doubled stride tiled half as many dots and stopped ~14px short.
        if (!LeaderTiling.TryGetRange(run.X, run.Width, glyphWidth, glyphWidth, context.PageSettings.WidthPoints, out var startX, out var count))
        {
            return;
        }

        var text = new string(leaderChar, count);
        var glyphs = Glyphs(face, text, 0, sizePoints, null);
        surface.SetStroke(null)
            .SetFill(context.GetFill(color))
            .DrawGlyphs(new((float) startX, (float) baseline), face.Font, (float) sizePoints, text, glyphs);
    }

    static void PaintImage(PdfRenderContext context, Surface surface, PlacedImage image)
    {
        if (image.ShapeGroup is { } group)
        {
            PaintInlineGroup(context, surface, image, group);
            return;
        }

        // A picture stored as SVG is placed as a graphic of the paths the reader made of it
        // (PlacedImage.Vector), so it stays vector in the PDF and its raster fallback is not embedded
        // (see SkiaPainter.PaintImage). A translucent one keeps to its fallback when it has one: a
        // bitmap fades as a whole but a graphic only path by path.
        var vector = image.Opacity >= 1 || image.Data is not { Length: > 0 } ? image.Vector : null;
        if (vector == null &&
            image.Data is not { Length: > 0 })
        {
            return;
        }

        var picture = vector != null
            ? PdfPicture.Of(context, vector, image.Recolor, image.Opacity)
            : PdfPicture.Of(context.GetImage(image.Data!, image.Recolor, image.Opacity));
        if (picture == null)
        {
            // Undecodable image bytes: skip this image rather than fail the whole paint.
            return;
        }

        // a:xfrm transforms happen about the box centre, then draw; an ellipse/freeform clip is an
        // alternative (Word does not combine the two).
        if (Math.Abs(image.RotationDegrees) > 0.01 || image.FlipHorizontal || image.FlipVertical)
        {
            var centerX = image.X + image.Width / 2;
            var centerY = image.Y + image.Height / 2;
            Layer? rotated = null;
            if (Math.Abs(image.RotationDegrees) > 0.01)
            {
                rotated = PdfShapeDrawing.RotateAt(surface, image.RotationDegrees, centerX, centerY);
            }

            Layer? flipped = null;
            if (image.FlipHorizontal || image.FlipVertical)
            {
                flipped = surface.PushTransform(Matrix3x2.CreateScale(image.FlipHorizontal ? -1 : 1, image.FlipVertical ? -1 : 1, new(centerX, centerY)));
            }

            DrawIntoBox(surface, picture, image);
            PaintImageOutline(context, surface, image);
            flipped?.Dispose();
            rotated?.Dispose();
        }
        else if (image.ClipToEllipse ||
                 image.ClipSubpaths != null)
        {
            using (var builder = new PathBuilder())
            {
                if (image.ClipToEllipse)
                {
                    PdfShapeDrawing.AddEllipse(builder, image.X, image.Y, image.Width, image.Height);
                }
                else
                {
                    foreach (var contour in image.ClipSubpaths!)
                    {
                        if (contour.Count == 0)
                        {
                            continue;
                        }

                        for (var pointIndex = 0; pointIndex < contour.Count; pointIndex++)
                        {
                            var (unitX, unitY) = contour[pointIndex];
                            var x = (float) (image.X + unitX * image.Width);
                            var y = (float) (image.Y + unitY * image.Height);
                            if (pointIndex == 0)
                            {
                                builder.MoveTo(x, y);
                            }
                            else
                            {
                                builder.LineTo(x, y);
                            }
                        }

                        builder.Close();
                    }
                }

                using var clipPath = builder.Build();
                using (surface.PushClip(clipPath))
                {
                    DrawIntoBox(surface, picture, image);
                }
            }

            PaintImageOutline(context, surface, image);
        }
        else
        {
            DrawIntoBox(surface, picture, image);
            PaintImageOutline(context, surface, image);
        }
    }

    // The picture's a:ln (see SkiaPainter.PaintImageOutline).
    static void PaintImageOutline(PdfRenderContext context, Surface surface, PlacedImage image)
    {
        if (image.Outline is not { } outline)
        {
            return;
        }

        var half = outline.WidthPoints / 2;
        var color = PdfRenderContext.ParseColor(outline.ColorHex).WithAlpha(outline.Alpha);
        PdfShapeDrawing.Stroke(
            surface,
            PdfShapeDrawing.Rectangle(image.X - half, image.Y - half, image.Width + outline.WidthPoints, image.Height + outline.WidthPoints),
            context.GetStroke(color, outline.WidthPoints));
    }

    // Draws the picture into its box, honouring a source-rectangle crop by enlarging the picture so its
    // visible sub-rectangle fills the box and clipping back.
    static void DrawIntoBox(Surface surface, PdfPicture picture, PlacedImage image)
    {
        // A tiled fill (see SkiaPainter.DrawPicture): the picture at the tile's size, corner after
        // corner, cut to the box. Each tile is drawn a hair past its own edges so a viewer's
        // antialiasing leaves no line where two meet. One too finely tiled falls through to the stretch.
        if (image.Tile is { } tile)
        {
            const int maxTiles = 4096;
            const double overlap = 0.05;
            var corners = tile.Corners(image.Width, image.Height).Take(maxTiles + 1).ToList();
            if (corners.Count is > 0 and <= maxTiles)
            {
                using var tiled = PdfShapeDrawing.ClipRectangle(surface, image.X, image.Y, image.Width, image.Height);
                foreach (var (cornerX, cornerY) in corners)
                {
                    picture.Draw(surface, image.X + cornerX, image.Y + cornerY, tile.WidthPoints + overlap, tile.HeightPoints + overlap);
                }

                return;
            }
        }

        if (image.Crop is { IsCropped: true } crop)
        {
            var (dx, dy, dw, dh) = crop.Expand(image.X, image.Y, image.Width, image.Height);
            using var clipped = PdfShapeDrawing.ClipRectangle(surface, image.X, image.Y, image.Width, image.Height);
            picture.Draw(surface, dx, dy, dw, dh);
        }
        else
        {
            picture.Draw(surface, image.X, image.Y, image.Width, image.Height);
        }
    }

    // An inline shape group (a grouped drawing embedded in a run): its child shapes scaled from the group's
    // child coordinate space into the inline box, painted back to front. The placed box is already in
    // points, its top at Y = baseline − height, so it is drawn directly.
    static void PaintInlineGroup(PdfRenderContext context, Surface surface, PlacedImage placed, InlineShapeGroup group)
    {
        var penX = placed.X;
        var top = placed.Y;
        var scaleX = placed.Width / group.ChildExtentX;
        var scaleY = placed.Height / group.ChildExtentY;

        Layer? rotated = null;
        if (group.RotationDegrees != 0)
        {
            rotated = PdfShapeDrawing.RotateAt(surface, group.RotationDegrees, penX + placed.Width / 2, top + placed.Height / 2);
        }

        foreach (var shape in group.Shapes)
        {
            var x = penX + shape.X * scaleX;
            var y = top + shape.Y * scaleY;
            var width = shape.Width * scaleX;
            var height = shape.Height * scaleY;

            if (shape.Geometry == GroupShapeGeometry.Line)
            {
                var startX = x;
                var startY = y;
                var endX = x + width;
                var endY = y + height;
                if (shape.FlipVertical)
                {
                    (startY, endY) = (endY, startY);
                }

                if (shape.FlipHorizontal)
                {
                    (startX, endX) = (endX, startX);
                }

                var strokeWidth = shape.LineWidthEmu > 0 ? shape.LineWidthEmu / OoxmlUnits.EmusPerPoint : 0.75;
                PdfShapeDrawing.StrokeLine(surface, PdfShapeDrawing.StrokePen(context, shape, strokeWidth), startX, startY, endX, endY);
                continue;
            }

            var isEllipse = shape.Geometry == GroupShapeGeometry.Ellipse;

            if (shape.Shadow is { } shadow)
            {
                var shadowColor = PdfRenderContext.ParseColor(shadow.ColorHex).WithAlpha(shadow.Alpha);
                var shadowX = x + shadow.OffsetX * scaleX;
                var shadowY = y + shadow.OffsetY * scaleY;
                if (GroupShapePath(shape, shadowX, shadowY, width, height, isEllipse) is { } shadowPath)
                {
                    PdfShapeDrawing.Fill(surface, shadowPath, context.GetFill(shadowColor, GroupFillRule(shape)));
                }
            }

            if (shape.ImageData != null)
            {
                PdfShapeDrawing.DrawGroupPicture(context, surface, shape, x, y, width, height, isEllipse);
            }
            else if (shape.FillColorHex is { } fillHex &&
                     GroupShapePath(shape, x, y, width, height, isEllipse) is { } fillPath)
            {
                var color = PdfRenderContext.ParseColor(fillHex).WithAlpha(shape.FillAlpha);
                PdfShapeDrawing.Fill(surface, fillPath, context.GetFill(color, GroupFillRule(shape)));
            }

            if (shape.LineWidthEmu > 0)
            {
                var widthPoints = shape.LineWidthEmu / OoxmlUnits.EmusPerPoint;
                if (shape.OpenOutline &&
                    PdfShapeDrawing.BuildGroupShapePath(shape, x, y, width, height, close: false) is { } openPath)
                {
                    // An open stroke shows its ends, so it takes the declared cap rather than the
                    // square one the connector pieces need.
                    var pen = PdfShapeDrawing.StrokePen(context, shape, widthPoints, shape.RoundCap ? LineCap.Round : LineCap.Butt);
                    PdfShapeDrawing.Stroke(surface, openPath, pen);
                }
                else if (GroupShapePath(shape, x, y, width, height, isEllipse) is { } outlinePath)
                {
                    PdfShapeDrawing.Stroke(surface, outlinePath, PdfShapeDrawing.StrokePen(context, shape, widthPoints));
                }
            }
        }

        rotated?.Dispose();
    }

    // The group shape's outline: its contours when it has them, else its ellipse or rectangle.
    static PdfPath? GroupShapePath(GroupShape shape, double x, double y, double width, double height, bool isEllipse)
    {
        if (shape.Subpaths != null)
        {
            return PdfShapeDrawing.BuildGroupShapePath(shape, x, y, width, height);
        }

        if (isEllipse)
        {
            return PdfShapeDrawing.Ellipse(x, y, width, height);
        }

        return PdfShapeDrawing.Rectangle(x, y, width, height);
    }

    // Contours fill even-odd, which keeps ring shapes (frame) hollow.
    static FillRule GroupFillRule(GroupShape shape)
    {
        if (shape.Subpaths != null)
        {
            return FillRule.EvenOdd;
        }

        return FillRule.NonZero;
    }

    // A behind-text floating shape: fill and outline of either its freeform contours (subpaths, scaled from
    // the unit square into the box with flip/rotation baked in by BuildShapePath) or, absent those, its
    // preset box (rect or ellipse). The fill is a linear gradient or a solid colour. An image-fill shape is
    // painted as a plain image by the body-float path, so it is skipped here.
    static void PaintShape(PdfRenderContext context, Surface surface, PlacedShape placed)
    {
        var shape = placed.Shape;
        if (shape.ImageData is { Length: > 0 })
        {
            return;
        }

        // Honour the shape's fill/line opacity (a:alpha) — e.g. cover-letters/10's header banner is a 10%
        // accent tint that reads as near-white, not a solid panel.
        Paint? gradientPaint = null;
        Fill? fill;
        if (shape.Gradient is { } gradient)
        {
            gradientPaint = PdfShapeDrawing.BuildGradientPaint(gradient, placed.X, placed.Y, placed.Width, placed.Height);
            fill = new Fill(gradientPaint);
        }
        else if (shape.FillColorHex is { } fillHex)
        {
            fill = context.GetFill(PdfRenderContext.ParseColor(fillHex).WithAlpha(shape.FillAlpha));
        }
        else
        {
            fill = null;
        }

        Stroke? stroke = null;
        if (shape.LineColorHex is { } lineHex)
        {
            var color = PdfRenderContext.ParseColor(lineHex).WithAlpha(shape.LineAlpha);
            stroke = PdfShapeDrawing.Dashed(context.GetStroke(color, ShapeStroke.WidthPoints(shape.LineWidthPoints), shape.RoundCap ? LineCap.Round : LineCap.Butt), shape.LineDashPattern);
        }

        if (fill == null &&
            stroke == null)
        {
            return;
        }

        if (shape.Subpaths is { Count: > 0 })
        {
            Draw(PdfShapeDrawing.BuildShapePath(shape, placed.X, placed.Y, placed.Width, placed.Height));
            gradientPaint?.Dispose();
            return;
        }

        // A preset box turns about its own centre (a contour has its rotation baked into its points).
        // Without this a rotated rectangle drew lying down: resumes/06's black strip up the page's left
        // edge, a 235x43pt rectangle turned 90°, came out as a wide bar across the top.
        Layer? turned = null;
        if (shape.RotationDegrees != 0)
        {
            turned = PdfShapeDrawing.RotateAt(surface, shape.RotationDegrees, placed.X + placed.Width / 2, placed.Y + placed.Height / 2);
        }

        if (shape.Preset == PresetShape.Ellipse)
        {
            Draw(PdfShapeDrawing.Ellipse(placed.X, placed.Y, placed.Width, placed.Height));
        }
        else
        {
            Draw(PdfShapeDrawing.Rectangle(placed.X, placed.Y, placed.Width, placed.Height));
        }

        turned?.Dispose();
        gradientPaint?.Dispose();

        void Draw(PdfPath path)
        {
            using (path)
            {
                surface.SetFill(fill)
                    .SetStroke(stroke)
                    .DrawPath(path);
            }

            surface.SetStroke(null);
        }
    }

    // Each cell: shading first, then its content, then its borders on top — Word's cell paint order.
    static void PaintTableRow(PdfRenderContext context, Surface surface, PlacedTableRow row)
    {
        foreach (var cell in row.Cells)
        {
            if (!string.IsNullOrEmpty(cell.BackgroundColorHex))
            {
                PdfShapeDrawing.FillRectangle(context, surface, PdfRenderContext.ParseColor(cell.BackgroundColorHex), cell.X, cell.Y, cell.Width, cell.Height);
            }

            foreach (var floating in cell.Floats)
            {
                PaintItem(context, surface, floating);
            }

            // See SkiaPainter.PaintTableRow — the clip bounds the content only, never the shading or
            // the borders.
            Layer? clip = null;
            if (cell.ClipContent)
            {
                var (clipX, clipWidth) = cell.ClipHorizontally
                    ? (cell.X - cell.ClipSpillLeft, cell.Width + cell.ClipSpillLeft + cell.ClipSpillRight)
                    : (-100000f, 200000f);
                var (clipY, clipHeight) = cell.ClipVertically
                    ? (cell.Y, cell.Height)
                    : (-100000f, 200000f);
                clip = PdfShapeDrawing.ClipRectangle(surface, clipX, clipY, clipWidth, clipHeight);
            }

            foreach (var content in cell.Content)
            {
                PaintItem(context, surface, content);
            }

            clip?.Dispose();

            if (cell.Borders is { } borders)
            {
                PaintCellBorders(context, surface, cell, borders);
            }

            if (cell.Diagonals is {HasAny: true} diagonals)
            {
                PaintCellDiagonals(context, surface, cell, diagonals);
            }
        }
    }

    // See SkiaPainter.PaintCellDiagonals.
    static void PaintCellDiagonals(PdfRenderContext context, Surface surface, PlacedCell cell, CellDiagonals diagonals)
    {
        double left = cell.X, top = cell.Y, right = cell.X + cell.Width, bottom = cell.Y + cell.Height;
        Diagonal(diagonals.Down, left, top, right, bottom);
        Diagonal(diagonals.Up, right, top, left, bottom);

        void Diagonal(BorderEdge edge, double x0, double y0, double x1, double y1)
        {
            if (BorderStroke.DiagonalThickness(edge) is > 0 and var thickness)
            {
                PdfShapeDrawing.StrokeLine(surface, EdgeStroke(context, edge, thickness, BorderStroke.DashPattern(edge.Style, edge.WidthPoints), 1), x0, y0, x1, y1);
            }
        }
    }

    // See SkiaPainter.PaintCellEdges — Word's cell geometry (BorderStroke.CellEdgeLines), waves along the
    // box edge.
    static void PaintCellBorders(PdfRenderContext context, Surface surface, PlacedCell cell, CellBorders borders)
    {
        foreach (var line in BorderStroke.CellEdgeLines(cell.X, cell.Y, cell.Width, cell.Height, borders, cell.BottomEdgeInset))
        {
            var stroke = EdgeStroke(context, line.Edge, line.Thickness, BorderStroke.DashPattern(line.Edge.Style, line.Edge.WidthPoints), line.Shade);
            if (line.Horizontal)
            {
                PdfShapeDrawing.StrokeLine(surface, stroke, line.From, line.At, line.To, line.At);
            }
            else
            {
                PdfShapeDrawing.StrokeLine(surface, stroke, line.At, line.From, line.At, line.To);
            }
        }

        float left = cell.X, top = cell.Y, right = cell.X + cell.Width, bottom = cell.Y + cell.Height;
        Wave(borders.Top, horizontal: true, left, right, top, outward: -1);
        Wave(borders.Bottom, horizontal: true, left, right, bottom, outward: 1);
        Wave(borders.Left, horizontal: false, top, bottom, left, outward: -1);
        Wave(borders.Right, horizontal: false, top, bottom, right, outward: 1);

        void Wave(BorderEdge edge, bool horizontal, double from, double to, double at, int outward)
        {
            if (BorderStroke.Draws(edge) &&
                BorderStroke.Waves(edge.Style) is {Length: > 0} waves)
            {
                StrokeWaves(context, surface, edge, waves, horizontal, from, to, at, outward);
            }
        }
    }

    // A paragraph border box: stroke each visible edge around the box the Fragmenter already expanded by
    // the edge spaces. Same edge geometry as a table cell, just around a paragraph rather than a cell.
    static void PaintBorder(PdfRenderContext context, Surface surface, PlacedBorder border, BorderStroke.Scope scope = BorderStroke.Scope.Paragraph)
    {
        float left = border.X, top = border.Y, right = border.X + border.Width, bottom = border.Y + border.Height;
        var borders = border.Borders;

        // See SkiaPainter.StrokeEdge — one band per line of a multi-line style, offset
        // perpendicular to the edge; single-line styles come back as one band at offset 0.
        // See SkiaPainter.PaintEdges — corner fill.
        var dl = BorderStroke.Draws(borders.Left);
        var dr = BorderStroke.Draws(borders.Right);
        var dt = BorderStroke.Draws(borders.Top);
        var db = BorderStroke.Draws(borders.Bottom);
        StrokeEdge(context, surface, borders.Top, horizontal: true, left, right, top, scope, outward: -1, extendStart: dl, extendEnd: dr);
        StrokeEdge(context, surface, borders.Bottom, horizontal: true, left, right, bottom, scope, outward: 1, extendStart: dl, extendEnd: dr);
        StrokeEdge(context, surface, borders.Left, horizontal: false, top, bottom, left, scope, outward: -1, extendStart: dt, extendEnd: db);
        StrokeEdge(context, surface, borders.Right, horizontal: false, top, bottom, right, scope, outward: 1, extendStart: dt, extendEnd: db);
    }

    // See SkiaPainter.StrokeWaves.
    static void StrokeWaves(PdfRenderContext context, Surface surface, BorderEdge edge, BorderStroke.WaveBand[] waves, bool horizontal, double from, double to, double at, int outward)
    {
        var color = PdfRenderContext.ParseColor(edge.ColorHex ?? "000000");
        foreach (var wave in waves)
        {
            var centre = at + outward * wave.Offset;
            var points = new List<(double X, double Y)>();
            foreach (var (along, across) in BorderStroke.WavePoints(from, to, wave.Period, wave.Amplitude))
            {
                points.Add(horizontal ? (along, centre + across) : (centre + across, along));
            }

            if (PdfShapeDrawing.Polyline(points) is { } path)
            {
                PdfShapeDrawing.Stroke(surface, path, context.GetStroke(color, Math.Max(0.5, wave.Thickness)));
            }
        }
    }

    static void PaintRunBorder(PdfRenderContext context, Surface surface, double x, double y, double width, double height, BorderEdge edge)
    {
        double right = x + width, bottom = y + height;
        StrokeEdge(context, surface, edge, horizontal: true, x, right, y, outward: -1, extendStart: true, extendEnd: true);
        StrokeEdge(context, surface, edge, horizontal: true, x, right, bottom, outward: 1, extendStart: true, extendEnd: true);
        StrokeEdge(context, surface, edge, horizontal: false, y, bottom, x, outward: -1, extendStart: true, extendEnd: true);
        StrokeEdge(context, surface, edge, horizontal: false, y, bottom, right, outward: 1, extendStart: true, extendEnd: true);
    }

    // See SkiaPainter.StrokeEdge: outward is -1 for top/left and +1 for bottom/right, and the span
    // grows by the same offset so the bands close into concentric rectangles.
    static void StrokeEdge(PdfRenderContext context, Surface surface, BorderEdge edge, bool horizontal, double from, double to, double at, BorderStroke.Scope scope = BorderStroke.Scope.Paragraph, int outward = -1, bool extendStart = false, bool extendEnd = false)
    {
        if (!BorderStroke.Draws(edge))
        {
            return;
        }

        if (BorderStroke.Waves(edge.Style) is {Length: > 0} waves)
        {
            StrokeWaves(context, surface, edge, waves, horizontal, from, to, at, outward);
            return;
        }

        var dash = BorderStroke.DashPattern(edge.Style, edge.WidthPoints);
        var bands = BorderStroke.Bands(edge.Style, edge.WidthPoints, scope, trailingEdge: outward > 0);
        var shift = BorderStroke.OutwardShift(bands, scope);
        foreach (var band in bands)
        {
            var half = band.Thickness / 2;
            var offset = band.Offset + shift;
            var line = at + outward * offset;
            var start = from - offset - (extendStart ? half : 0);
            var end = to + offset + (extendEnd ? half : 0);
            var stroke = EdgeStroke(context, edge, band.Thickness, dash, band.Shade);
            if (horizontal)
            {
                PdfShapeDrawing.StrokeLine(surface, stroke, start, line, end, line);
            }
            else
            {
                PdfShapeDrawing.StrokeLine(surface, stroke, line, start, line, end);
            }
        }
    }

    // The dash pattern is in points, as Krilla takes it.
    static Stroke EdgeStroke(PdfRenderContext context, BorderEdge edge, double thickness, float[]? dashPoints, double shade) =>
        context.GetStroke(PdfRenderContext.ParseColor(edge.ColorHex ?? "000000").Shaded(shade), Math.Max(0.5, thickness), dash: dashPoints);
}
