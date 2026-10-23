// Home > Borders and Shading on paragraphs. Word-probed 2026-10-23 (Word 365 over COM, on a pair of
// paragraphs):
//
//   - Outside Borders, run as the ribbon's own command (ExecuteMso "BorderOutside"), gives both
//     paragraphs the same w:pBdr: w:top, w:left, w:bottom and w:right, each single, w:sz="4" (½pt),
//     w:color="auto", standing w:space="1" from the text above and below and "4" either side.
//     Word draws one box round paragraphs whose borders are alike, so a box round several is the
//     same borders on each. The ribbon's other border ids would not run over COM; set through the
//     object model the same sides come out w:space="0", so the ribbon's spacing is taken for every side.
//   - All Borders is Outside Borders and w:between; Inside Horizontal is w:between alone. Its
//     spacing, 1, is the Borders and Shading dialog's for it, not probed.
//   - Shading is <w:shd w:val="clear" w:color="auto" w:fill="FFFF00"/>.
//
// Each is tracked as a paragraph's formatting changing (w:pPrChange).
static partial class DocumentEditor
{
    /// <summary>
    /// Sets paragraphs' borders as Home > Borders does: a side added to what they have, or every side
    /// taken off (<see cref="ParagraphBorder.None"/>), at <paramref name="size"/> eighths of a point.
    /// </summary>
    public static byte[] SetParagraphBorders(byte[] docx, IReadOnlyList<int> paragraphs, ParagraphBorder border, int size, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                foreach (var paragraph in edit.FindAll(paragraphs))
                {
                    edit.Border(paragraph, border, size);
                }

                return null;
            }).Document;

    /// <summary>Shades paragraphs in a colour, as six hex digits; null takes the shading off.</summary>
    public static byte[] SetParagraphShading(byte[] docx, IReadOnlyList<int> paragraphs, string? fill, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                foreach (var paragraph in edit.FindAll(paragraphs))
                {
                    edit.Shade(paragraph, fill);
                }

                return null;
            }).Document;

    sealed partial class Editing
    {
        public void Border(Paragraph paragraph, ParagraphBorder border, int size)
        {
            var properties = Reformat(paragraph);
            if (border == ParagraphBorder.None)
            {
                properties.ParagraphBorders = null;
                return;
            }

            var borders = properties.ParagraphBorders ?? new ParagraphBorders();
            var sides = border switch
            {
                ParagraphBorder.Bottom => [ParagraphBorder.Bottom],
                ParagraphBorder.Top => [ParagraphBorder.Top],
                ParagraphBorder.Left => [ParagraphBorder.Left],
                ParagraphBorder.Right => [ParagraphBorder.Right],
                ParagraphBorder.Inside => [ParagraphBorder.Inside],
                ParagraphBorder.Outside => [ParagraphBorder.Top, ParagraphBorder.Left, ParagraphBorder.Bottom, ParagraphBorder.Right],
                _ => new[] {ParagraphBorder.Top, ParagraphBorder.Left, ParagraphBorder.Bottom, ParagraphBorder.Right, ParagraphBorder.Inside}
            };
            foreach (var side in sides)
            {
                var space = side is ParagraphBorder.Left or ParagraphBorder.Right ? 4u : 1u;
                switch (side)
                {
                    case ParagraphBorder.Top:
                        borders.TopBorder = Line<TopBorder>(size, space);
                        break;
                    case ParagraphBorder.Left:
                        borders.LeftBorder = Line<LeftBorder>(size, space);
                        break;
                    case ParagraphBorder.Bottom:
                        borders.BottomBorder = Line<BottomBorder>(size, space);
                        break;
                    case ParagraphBorder.Right:
                        borders.RightBorder = Line<RightBorder>(size, space);
                        break;
                    default:
                        borders.BetweenBorder = Line<BetweenBorder>(size, space);
                        break;
                }
            }

            properties.ParagraphBorders = borders;
        }

        static T Line<T>(int size, uint space)
            where T : BorderType, new() =>
            new()
            {
                Val = BorderValues.Single,
                Size = (uint) size,
                Space = space,
                Color = "auto"
            };

        public void Shade(Paragraph paragraph, string? fill)
        {
            var properties = Reformat(paragraph);
            properties.Shading = fill == null
                ? null
                : new Shading
                {
                    Val = ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = fill
                };
        }
    }
}
