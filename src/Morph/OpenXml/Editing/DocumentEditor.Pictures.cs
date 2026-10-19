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
                main.GetPartById(id) is not ImagePart image)
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
