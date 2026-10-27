/// <summary>
/// A <see cref="MetafileDrawing"/> drawn into a picture's box once layout has given the box a place on
/// the page — a Word OLE object's EMF or WMF preview, in the line or floating. It is the slide path's
/// <see cref="MetafileElements"/> carried through to placed items: each path a <see cref="PlacedShape"/>,
/// each text laid out in the box <see cref="MetafileElements"/> gives it, and turned about that box's
/// centre where the metafile turned it (an axis title), in the metafile's own order.
/// </summary>
static class MetafileComposer
{
    public static List<PlacedItem> Compose(
        MetafileDrawing drawing,
        float x,
        float y,
        float width,
        float height,
        Func<IReadOnlyList<DocumentElement>, float, float, float, float, CellVerticalAlignment, IReadOnlyList<PlacedItem>> layoutText)
    {
        var items = new List<PlacedItem>();
        foreach (var element in MetafileElements.For(drawing, x, y, width, height, 0))
        {
            switch (element)
            {
                case FloatingShapeElement shape:
                    items.Add(new PlacedShape((float) shape.HorizontalPositionPoints, (float) shape.VerticalPositionPoints, (float) shape.WidthPoints, (float) shape.HeightPoints, shape));
                    break;
                case FloatingTextBoxElement text:
                {
                    var boxX = (float) text.HorizontalPositionPoints;
                    var boxY = (float) text.VerticalPositionPoints;
                    var boxWidth = (float) text.WidthPoints;
                    var boxHeight = (float) text.HeightPoints;
                    var content = layoutText(text.Content, boxX, boxY, boxWidth, boxHeight, CellVerticalAlignment.Top);
                    if (text.RotationDegrees != 0)
                    {
                        items.Add(new PlacedRotatedGroup(boxX, boxY, boxWidth, boxHeight, content, text.RotationDegrees));
                    }
                    else
                    {
                        items.AddRange(content);
                    }

                    break;
                }
            }
        }

        return items;
    }
}
