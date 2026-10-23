using A = DocumentFormat.OpenXml.Drawing;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

// A picture in the text made larger or smaller. Word-probed 2026-10-19 (Word 365 over COM, a 150pt
// by 75pt picture doubled in width with its aspect locked, then made 30pt high with it free): the
// size is written twice, as the drawing's extent (wp:extent) and the picture's own (a:xfrm/a:ext),
// in EMUs, 12,700 to the point — 300pt by 150pt is cx="3810000" cy="1905000". Nothing else of the
// drawing changes, and nothing of it is tracked.
static partial class DocumentEditor
{
    const double emusPerPoint = 12700;

    /// <summary>Sizes the picture at a place of a paragraph, in points.</summary>
    public static byte[] ResizePicture(byte[] docx, int paragraph, int offset, double widthPoints, double heightPoints, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Resize(paragraph, offset, widthPoints, heightPoints);
                return null;
            }).Document;

    /// <summary>
    /// Sets how the picture at a place stands with the text, as Layout > Wrap Text does. Word-probed
    /// 2026-10-23 (an inline picture converted to a floating shape over COM, each wrap set):
    ///
    ///   - Floating, a picture is a wp:anchor — distT/distB 0, distL/distR 114300, simplePos 0,
    ///     behindDoc, locked 0, layoutInCell 1, allowOverlap 1 — then wp:simplePos 0,0, wp:positionH
    ///     from the column and wp:positionV from the paragraph, the extent, the effect extent, the
    ///     wrap, and the inline's own docPr, frame properties and graphic.
    ///   - Square is wp:wrapSquare wrapText="bothSides"; Top and Bottom wp:wrapTopAndBottom; Behind
    ///     Text and In Front of Text wp:wrapNone, behindDoc 1 and 0; Tight wp:wrapTight bothSides
    ///     round a polygon, which Word drew a little inside the picture's box (21283 by 21384 of 21600)
    ///     and which is the whole box here. Word wrote Tight's behindDoc as 1.
    ///   - Converted, Word placed the picture where it had stood in the line (positionH 419100 for one
    ///     after "Before "). The layout is not known here, so a picture made floating stands at the
    ///     column's left, level with its paragraph's top.
    ///   - Back in line, it is a wp:inline with every dist 0, holding the extent, effect extent,
    ///     docPr, frame properties and graphic.
    ///
    /// Nothing of it is tracked.
    /// </summary>
    public static byte[] SetPictureWrap(byte[] docx, int paragraph, int offset, PictureWrap wrap, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Wrap(paragraph, offset, wrap);
                return null;
            }).Document;

    /// <summary>How the picture at a place stands with the text; null when there is no picture there.</summary>
    public static PictureWrap? PictureWrapOf(byte[] docx, int paragraph, int offset)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var editing = new Editing(document, new("", DateTimeOffset.MinValue, false));
        try
        {
            return WrapOf(editing.DrawingOf(paragraph, offset));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    static PictureWrap WrapOf(OpenXmlElement drawing)
    {
        if (drawing.Descendants<Wp.Anchor>().FirstOrDefault() is not { } anchor)
        {
            return PictureWrap.Inline;
        }

        if (anchor.GetFirstChild<Wp.WrapSquare>() != null)
        {
            return PictureWrap.Square;
        }

        if (anchor.GetFirstChild<Wp.WrapTight>() != null ||
            anchor.GetFirstChild<Wp.WrapThrough>() != null)
        {
            return PictureWrap.Tight;
        }

        if (anchor.GetFirstChild<Wp.WrapTopBottom>() != null)
        {
            return PictureWrap.TopAndBottom;
        }

        return anchor.BehindDoc?.Value == true ? PictureWrap.BehindText : PictureWrap.InFrontOfText;
    }

    /// <summary>The image file of the picture at a place of a paragraph, and its media type; null when there is no picture there.</summary>
    public static (byte[] Data, string ContentType)? PictureData(byte[] docx, int paragraph, int offset)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var editing = new Editing(document, new("", DateTimeOffset.MinValue, false));
        return editing.PictureData(paragraph, offset);
    }

    sealed partial class Editing
    {
        // The drawing at a place: the unit there, if it is one.
        OpenXmlElement DrawingAt(int ordinal, int offset)
        {
            var at = 0;
            foreach (var unit in Units(Find(ordinal)))
            {
                if (at == offset &&
                    unit.Kind == EditUnitKind.Drawing)
                {
                    return unit.First as Drawing ??
                           unit.First.Descendants<Drawing>().FirstOrDefault() ??
                           throw new InvalidOperationException("There is no picture there.");
                }

                at += Span(unit);
                if (at > offset)
                {
                    break;
                }
            }

            throw new InvalidOperationException("There is no picture there.");
        }

        public OpenXmlElement DrawingOf(int ordinal, int offset) =>
            DrawingAt(ordinal, offset);

        public void Wrap(int ordinal, int offset, PictureWrap wrap)
        {
            var drawing = DrawingAt(ordinal, offset);
            var inline = drawing.Descendants<Wp.Inline>().FirstOrDefault();
            var anchor = drawing.Descendants<Wp.Anchor>().FirstOrDefault();
            var frame = (OpenXmlElement?) inline ?? anchor ?? throw new InvalidOperationException("There is no picture there.");

            // What a picture is, whether in the line or floating: its size, its effects' reach, its
            // name, its frame's locks and the picture itself.
            var extent = frame.GetFirstChild<Wp.Extent>()?.CloneNode(true);
            var effect = frame.GetFirstChild<Wp.EffectExtent>()?.CloneNode(true);
            var name = frame.GetFirstChild<Wp.DocProperties>()?.CloneNode(true);
            var locks = frame.GetFirstChild<Wp.NonVisualGraphicFrameDrawingProperties>()?.CloneNode(true);
            var graphic = frame.GetFirstChild<A.Graphic>()?.CloneNode(true);
            OpenXmlElement[] content = [.. new[] {extent, effect, name, locks, graphic}.OfType<OpenXmlElement>()];
            if (wrap == PictureWrap.Inline)
            {
                if (inline != null)
                {
                    return;
                }

                var made = new Wp.Inline
                {
                    DistanceFromTop = 0,
                    DistanceFromBottom = 0,
                    DistanceFromLeft = 0,
                    DistanceFromRight = 0
                };
                made.Append(content);
                frame.InsertBeforeSelf(made);
                frame.Remove();
                return;
            }

            if (anchor != null)
            {
                foreach (var old in anchor.ChildElements.Where(_ => _ is Wp.WrapNone or Wp.WrapSquare or Wp.WrapTight or Wp.WrapThrough or Wp.WrapTopBottom).ToList())
                {
                    old.Remove();
                }

                anchor.BehindDoc = wrap is PictureWrap.BehindText or PictureWrap.Tight;
                anchor.InsertAfter(WrapElement(wrap), anchor.GetFirstChild<Wp.EffectExtent>() ?? (OpenXmlElement?) anchor.GetFirstChild<Wp.Extent>());
                return;
            }

            var heights = Stories().SelectMany(_ => _.Descendants<Wp.Anchor>()).Select(_ => _.RelativeHeight?.Value ?? 0).DefaultIfEmpty(251658239u).Max();
            var floating = new Wp.Anchor
            {
                DistanceFromTop = 0,
                DistanceFromBottom = 0,
                DistanceFromLeft = 114300,
                DistanceFromRight = 114300,
                SimplePos = false,
                RelativeHeight = Math.Max(heights, 251658239u) + 1,
                BehindDoc = wrap is PictureWrap.BehindText or PictureWrap.Tight,
                Locked = false,
                LayoutInCell = true,
                AllowOverlap = true
            };
            floating.Append(
                new Wp.SimplePosition {X = 0, Y = 0},
                new Wp.HorizontalPosition(new Wp.PositionOffset("0")) {RelativeFrom = Wp.HorizontalRelativePositionValues.Column},
                new Wp.VerticalPosition(new Wp.PositionOffset("0")) {RelativeFrom = Wp.VerticalRelativePositionValues.Paragraph});
            OpenXmlElement[] size = [.. new[] {extent, effect}.OfType<OpenXmlElement>()];
            floating.Append(size);
            floating.AppendChild(WrapElement(wrap));
            floating.Append(content.Where(_ => _ is not (Wp.Extent or Wp.EffectExtent)));
            frame.InsertBeforeSelf(floating);
            frame.Remove();
        }

        static OpenXmlElement WrapElement(PictureWrap wrap) =>
            wrap switch
            {
                PictureWrap.Square => new Wp.WrapSquare {WrapText = Wp.WrapTextValues.BothSides},
                PictureWrap.Tight => new Wp.WrapTight(
                    new Wp.WrapPolygon(
                        new Wp.StartPoint {X = 0, Y = 0},
                        new Wp.LineTo {X = 0, Y = 21600},
                        new Wp.LineTo {X = 21600, Y = 21600},
                        new Wp.LineTo {X = 21600, Y = 0},
                        new Wp.LineTo {X = 0, Y = 0}) {Edited = false}) {WrapText = Wp.WrapTextValues.BothSides},
                PictureWrap.TopAndBottom => new Wp.WrapTopBottom(),
                _ => new Wp.WrapNone()
            };

        public void Resize(int ordinal, int offset, double widthPoints, double heightPoints)
        {
            var drawing = DrawingAt(ordinal, offset);
            var width = (long) Math.Round(Math.Max(widthPoints, 1) * emusPerPoint);
            var height = (long) Math.Round(Math.Max(heightPoints, 1) * emusPerPoint);
            if (drawing.Descendants<Wp.Extent>().FirstOrDefault() is { } extent)
            {
                extent.Cx = width;
                extent.Cy = height;
            }

            // The picture's own size, where it says one: the first shape of the drawing's.
            if (drawing.Descendants<A.Transform2D>().FirstOrDefault()?.Extents is { } own)
            {
                own.Cx = width;
                own.Cy = height;
            }
        }

        public (byte[] Data, string ContentType)? PictureData(int ordinal, int offset)
        {
            var drawing = DrawingAt(ordinal, offset);
            if (drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value is not { } id ||
                PartOf(drawing).GetPartById(id) is not ImagePart image)
            {
                return null;
            }

            using var data = image.GetStream(FileMode.Open, FileAccess.Read);
            using var copy = new MemoryStream();
            data.CopyTo(copy);
            return (copy.ToArray(), image.ContentType);
        }
    }
}
