// The open fixture: positions resolved to Word ranges, ranges to screen rectangles, and the page geometry
// the hot spots are computed from.
sealed class ProbeDocument
{
    readonly WordSession session;

    public ProbeDocument(WordSession session, Word.Document document, Word.Window window)
    {
        this.session = session;
        Document = document;
        Window = window;
    }

    public Word.Document Document { get; }

    public Word.Window Window { get; }

    public Word.Selection Selection => Window.Selection;

    public float PixelsPerPoint => NativeMethods.GetDpiForWindow(session.Window) / 72f;

    public Word.Range StoryRange(Story story) =>
        story.Kind switch
        {
            StoryKind.Main => Document.Content,
            StoryKind.Header => Document.Sections[1].Headers[Word.WdHeaderFooterIndex.wdHeaderFooterPrimary].Range,
            StoryKind.Footer => Document.Sections[1].Footers[Word.WdHeaderFooterIndex.wdHeaderFooterPrimary].Range,
            StoryKind.Footnote => Document.Footnotes[story.Index].Range,
            StoryKind.Endnote => Document.Endnotes[story.Index].Range,
            StoryKind.Comment => Document.Comments[story.Index].Range,
            _ => Document.Shapes[story.Index].TextFrame.TextRange
        };

    public Word.Range ParagraphRange(Story story, int paragraph) =>
        StoryRange(story).Paragraphs[paragraph].Range;

    public Word.Table Table(int index) =>
        Document.Tables[index];

    public Word.Range CellRange(int table, int row, int column) =>
        Table(table).Cell(row, column).Range;

    /// <summary>A collapsed range for a text position, the whole object's range for a structural one.</summary>
    public Word.Range Resolve(Position position)
    {
        switch (position)
        {
            case ParagraphPosition paragraphPosition:
            {
                var range = ParagraphRange(paragraphPosition.Story, paragraphPosition.Paragraph);
                return Place(range, paragraphPosition.Offset, isCell: false);
            }
            case CellPosition cellPosition:
            {
                var range = CellRange(cellPosition.Table, cellPosition.Row, cellPosition.Column);
                return Place(range, cellPosition.Offset, isCell: true);
            }
            case TablePosition tablePosition:
                return Table(tablePosition.Table).Range;
            case RowPosition rowPosition:
                return Table(rowPosition.Table).Rows[rowPosition.Row].Range;
            case ColumnPosition columnPosition:
            {
                var table = Table(columnPosition.Table);
                var first = table.Cell(1, columnPosition.Column).Range;
                var last = table.Cell(table.Rows.Count, columnPosition.Column).Range;
                return Document.Range(first.Start, last.End);
            }
            case InlineShapePosition inlineShape:
                return Document.InlineShapes[inlineShape.Index].Range;
            case ShapePosition shapePosition:
                return Document.Shapes[shapePosition.Index].Anchor;
            case DocumentEdgePosition edge:
            {
                var content = Document.Content;
                var at = edge.AtEnd ? content.End - 1 : content.Start;
                return Document.Range(at, at);
            }
            case BelowPosition belowPosition:
            {
                var range = ParagraphRange(Story.Main, belowPosition.Paragraph);
                return Document.Range(range.End - 1, range.End - 1);
            }
        }

        throw new ArgumentException($"Cannot resolve {position}.");
    }

    public Word.Range Resolve(PositionRange range)
    {
        var from = Resolve(range.From);
        if (range.To == null)
        {
            return from;
        }

        var to = Resolve(range.To);
        var start = Math.Min(from.Start, to.Start);
        var end = Math.Max(from.End, to.End);
        // Both ends in the same story: build the range on the first end's own range object.
        from.SetRange(start, end);
        return from;
    }

    static Word.Range Place(Word.Range container, Offset offset, bool isCell)
    {
        var start = container.Start;
        var markAt = container.End - 1;
        switch (offset.Kind)
        {
            case OffsetKind.Start:
                return Collapsed(container, start);
            case OffsetKind.End:
                return Collapsed(container, markAt);
            case OffsetKind.Mark:
                container.SetRange(markAt, container.End);
                return container;
            case OffsetKind.Index:
                return Collapsed(container, start + offset.Value);
            case OffsetKind.Word:
                return Collapsed(container, container.Words[offset.Value].Start);
            case OffsetKind.WordEnd:
                return Collapsed(container, container.Words[offset.Value].End);
            case OffsetKind.Find:
            case OffsetKind.FindEnd:
            {
                var text = StoryText(container, isCell);
                var index = text.IndexOf(offset.Text!, StringComparison.Ordinal);
                if (index < 0)
                {
                    throw new InvalidOperationException($"'{offset.Text}' is not in \"{text}\".");
                }

                var at = start + index + (offset.Kind == OffsetKind.FindEnd ? offset.Text!.Length : 0);
                return Collapsed(container, at);
            }
        }

        throw new ArgumentException($"Unknown offset {offset}.");
    }

    static Word.Range Collapsed(Word.Range container, int at)
    {
        container.SetRange(at, at);
        return container;
    }

    /// <summary>
    /// The text with field codes and hidden text, so indexes line up with story positions. Where Word's one
    /// Text call leaves positions out (shape anchors, content control edges, some field marks), the range is
    /// read one position at a time and a missing character is a NUL the notation resolves by position.
    /// </summary>
    public static string StoryText(Word.Range range, bool isCell)
    {
        range.TextRetrievalMode.IncludeFieldCodes = true;
        range.TextRetrievalMode.IncludeHiddenText = true;
        var text = range.Text ?? "";
        if (isCell &&
            text.EndsWith("\r\a", StringComparison.Ordinal))
        {
            text = text[..^2] + "\a";
        }

        var length = range.End - range.Start;
        if (text.Length == length)
        {
            return text;
        }

        var builder = new StringBuilder(length);
        var cursor = range.Duplicate;
        cursor.TextRetrievalMode.IncludeFieldCodes = true;
        cursor.TextRetrievalMode.IncludeHiddenText = true;
        for (var position = range.Start; position < range.End; position++)
        {
            cursor.SetRange(position, position + 1);
            var one = cursor.Text ?? "";
            if (one.Length == 0)
            {
                builder.Append('\0');
            }
            else if (one == "\r\a")
            {
                builder.Append('\a');
            }
            else
            {
                builder.Append(one[0]);
            }
        }

        return builder.ToString();
    }

    public void Select(PositionRange range)
    {
        switch (range.From)
        {
            case TablePosition tablePosition when range.To == null:
                Table(tablePosition.Table).Select();
                return;
            case RowPosition rowPosition when range.To == null:
                Table(rowPosition.Table).Rows[rowPosition.Row].Select();
                return;
            case ColumnPosition columnPosition when range.To == null:
                Table(columnPosition.Table).Columns[columnPosition.Column].Select();
                return;
            case ShapePosition shapePosition when range.To == null:
                Document.Shapes[shapePosition.Index].Select();
                return;
            case InlineShapePosition inlineShape when range.To == null:
                Document.InlineShapes[inlineShape.Index].Select();
                return;
            case CellPosition first when range.To is CellPosition second && first.Offset.Kind == OffsetKind.Start && second.Offset.Kind == OffsetKind.Start:
            {
                var table = Table(first.Table);
                var topLeft = table.Cell(first.Row, first.Column).Range;
                var bottomRight = table.Cell(second.Row, second.Column).Range;
                Document.Range(topLeft.Start, bottomRight.End).Select();
                return;
            }
        }

        Resolve(range).Select();
    }

    /// <summary>Screen rectangle of a range or shape, scrolling it into view first.</summary>
    public ScreenRect Rectangle(object target)
    {
        if (target is Word.Range range)
        {
            Window.ScrollIntoView(range);
        }

        Window.GetPoint(out var left, out var top, out var width, out var height, target);
        return new(left, top, width, height);
    }

    /// <summary>The one-character rectangle at a text position (the character after the position).</summary>
    public ScreenRect GlyphRectangle(Word.Range collapsed)
    {
        var at = collapsed.Start;
        var glyph = collapsed.Duplicate;
        glyph.SetRange(at, at + 1);
        return Rectangle(glyph);
    }

    public ScreenRect ShapeRectangle(int index) =>
        Rectangle(Document.Shapes[index]);

    public ScreenRect InlineShapeRectangle(int index) =>
        Rectangle(Document.InlineShapes[index].Range);

    /// <summary>What lies under a screen point: a range (start–end), a shape, or nothing.</summary>
    public string Under(ScreenPoint point)
    {
        object? hit;
        try
        {
            hit = Window.RangeFromPoint(point.X, point.Y);
        }
        catch (COMException)
        {
            return "error";
        }

        return hit switch
        {
            null => "none",
            Word.Range range => $"Range {range.Start}-{range.End}",
            Word.Shape shape => $"Shape {shape.Name}",
            _ => hit.GetType().Name
        };
    }

    /// <summary>Page geometry in screen pixels, from the first paragraph's first glyph and the page setup.</summary>
    public PageGeometry Page()
    {
        var setup = Document.PageSetup;
        var scale = PixelsPerPoint;
        var first = Document.Paragraphs[1];
        var firstRange = first.Range;
        firstRange.SetRange(firstRange.Start, firstRange.Start + 1);
        var glyph = Rectangle(firstRange);
        var indent = first.LeftIndent + Math.Max(0, first.FirstLineIndent);
        var textLeft = glyph.Left - (int) Math.Round(indent * scale);
        var pageLeft = textLeft - (int) Math.Round(setup.LeftMargin * scale);
        var textWidth = (int) Math.Round((setup.PageWidth - setup.LeftMargin - setup.RightMargin) * scale);
        return new(pageLeft, textLeft, textLeft + textWidth, (int) Math.Round(setup.PageWidth * scale), glyph.Top, glyph.Height);
    }

    /// <summary>
    /// Where the pointer rests between steps: grey area to the right of the page. Hovering there shows no UI;
    /// it is never clicked, since a click anywhere beside the page places the caret (left: selects the line).
    /// </summary>
    public ScreenPoint ParkingSpot()
    {
        var page = Page();
        NativeMethods.GetWindowRect(session.Window, out var window);
        var x = Math.Min(page.PageLeft + page.PageWidth + 60, window.Right - 40);
        return new(x, page.FirstLineTop + 200);
    }

    /// <summary>A click target that activates Word without touching the document: the empty ribbon tab row.</summary>
    public static ScreenPoint ActivationSpot(IntPtr window)
    {
        NativeMethods.GetWindowRect(window, out var rect);
        return new(rect.Left + (rect.Right - rect.Left) * 11 / 20, rect.Top + 58);
    }
}

record PageGeometry(int PageLeft, int TextLeft, int TextRight, int PageWidth, int FirstLineTop, int FirstLineHeight);
