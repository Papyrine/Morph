// Builds one fixture document over COM from a copy of blank.docx. Every helper appends or decorates; the
// result is saved, normalised and committed so the Blazor tests can later open the identical file.
sealed class FixtureBuilder
{
    readonly List<int> paragraphs = [];

    public FixtureBuilder(Word.Document document) =>
        Document = document;

    public Word.Document Document { get; }

    public static string PicturePath => Path.Combine(Paths.Fixtures, "square.png");

    public int Count => Document.Paragraphs.Count;

    /// <summary>Appends a paragraph. The first fills the blank document's one paragraph; a trailing empty paragraph (after a table) is filled too.</summary>
    public Word.Paragraph Paragraph(string text, Word.WdBuiltinStyle? style = null, Word.WdParagraphAlignment? alignment = null)
    {
        var last = Document.Paragraphs[Document.Paragraphs.Count];
        var lastRange = last.Range;
        var inTable = (bool) lastRange.Information[Word.WdInformation.wdWithInTable];
        if (lastRange.Text == "\r" &&
            !inTable &&
            !paragraphs.Contains(Document.Paragraphs.Count))
        {
            lastRange.InsertBefore(text);
        }
        else
        {
            Document.Content.InsertParagraphAfter();
            Document.Content.InsertAfter(text);
        }

        var paragraph = Document.Paragraphs[Document.Paragraphs.Count];
        paragraphs.Add(Document.Paragraphs.Count);
        // A new paragraph inherits the previous one's paragraph and run formatting; each fixture paragraph
        // starts from the Normal style and the style's own font unless the fixture says otherwise.
        object styleValue = style ?? Word.WdBuiltinStyle.wdStyleNormal;
        paragraph.set_Style(ref styleValue);
        paragraph.Reset();
        paragraph.Range.Font.Reset();
        if (alignment != null)
        {
            paragraph.Alignment = alignment.Value;
        }

        return paragraph;
    }

    public Word.Paragraph Empty()
    {
        Document.Content.InsertParagraphAfter();
        var paragraph = Document.Paragraphs[Document.Paragraphs.Count];
        paragraphs.Add(Document.Paragraphs.Count);
        object styleValue = Word.WdBuiltinStyle.wdStyleNormal;
        paragraph.set_Style(ref styleValue);
        paragraph.Reset();
        paragraph.Range.Font.Reset();
        return paragraph;
    }

    public Word.Range Find(int paragraph, string text)
    {
        var range = Document.Paragraphs[paragraph].Range;
        // Field codes count as positions, so the text searched has to include them.
        var content = ProbeDocument.StoryText(range, isCell: false);
        var index = content.IndexOf(text, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"'{text}' is not in paragraph {paragraph} (\"{content.TrimEnd('\r')}\").");
        }

        return Document.Range(range.Start + index, range.Start + index + text.Length);
    }

    public Word.Range After(int paragraph, string text)
    {
        var found = Find(paragraph, text);
        return Document.Range(found.End, found.End);
    }

    public Word.Range Before(int paragraph, string text)
    {
        var found = Find(paragraph, text);
        return Document.Range(found.Start, found.Start);
    }

    public Word.Range EndOf(int paragraph)
    {
        var range = Document.Paragraphs[paragraph].Range;
        return Document.Range(range.End - 1, range.End - 1);
    }

    public Word.Range StartOf(int paragraph)
    {
        var range = Document.Paragraphs[paragraph].Range;
        return Document.Range(range.Start, range.Start);
    }

    public void Bold(int paragraph, string text) =>
        Find(paragraph, text).Font.Bold = 1;

    public void Italic(int paragraph, string text) =>
        Find(paragraph, text).Font.Italic = 1;

    public void Underline(int paragraph, string text) =>
        Find(paragraph, text).Font.Underline = Word.WdUnderline.wdUnderlineSingle;

    public void Font(int paragraph, string text, string name, float size)
    {
        var font = Find(paragraph, text).Font;
        font.Name = name;
        font.Size = size;
    }

    public void Size(float points) =>
        Document.Content.Font.Size = points;

    public void Bullets(int first, int last) =>
        Span(first, last).ListFormat.ApplyBulletDefault();

    public void Numbers(int first, int last) =>
        Span(first, last).ListFormat.ApplyNumberDefault();

    public void Indent(int paragraph, int levels)
    {
        for (var i = 0; i < levels; i++)
        {
            Document.Paragraphs[paragraph].Range.ListFormat.ListIndent();
        }
    }

    Word.Range Span(int first, int last) =>
        Document.Range(Document.Paragraphs[first].Range.Start, Document.Paragraphs[last].Range.End);

    /// <summary>Appends a table after the current last paragraph; cells by row then column, null leaves a cell empty.</summary>
    public Word.Table Table(int rows, int columns, string?[,]? cells = null, float? cellMarginPoints = null)
    {
        Document.Content.InsertParagraphAfter();
        var anchor = Document.Paragraphs[Document.Paragraphs.Count].Range;
        var table = Document.Tables.Add(anchor, rows, columns, Word.WdDefaultTableBehavior.wdWord9TableBehavior, Word.WdAutoFitBehavior.wdAutoFitFixed);
        table.Borders.Enable = 1;
        if (cellMarginPoints != null)
        {
            table.LeftPadding = cellMarginPoints.Value;
            table.RightPadding = cellMarginPoints.Value;
            table.TopPadding = cellMarginPoints.Value / 2;
            table.BottomPadding = cellMarginPoints.Value / 2;
        }

        if (cells != null)
        {
            for (var row = 1; row <= rows; row++)
            {
                for (var column = 1; column <= columns; column++)
                {
                    var text = cells[row - 1, column - 1];
                    if (!string.IsNullOrEmpty(text))
                    {
                        table.Cell(row, column).Range.Text = text;
                    }
                }
            }
        }

        return table;
    }

    public Word.InlineShape InlinePicture(Word.Range at) =>
        Document.InlineShapes.AddPicture(PicturePath, false, true, at);

    public Word.Shape FloatingPicture(int paragraph, float left = 300, float top = 0, float size = 72)
    {
        var anchor = Document.Paragraphs[paragraph].Range;
        var shape = Document.Shapes.AddPicture(PicturePath, false, true, left, top, size, size, anchor);
        shape.WrapFormat.Type = Word.WdWrapType.wdWrapSquare;
        shape.RelativeHorizontalPosition = Word.WdRelativeHorizontalPosition.wdRelativeHorizontalPositionColumn;
        shape.RelativeVerticalPosition = Word.WdRelativeVerticalPosition.wdRelativeVerticalPositionParagraph;
        return shape;
    }

    public Word.Shape TextBox(int paragraph, string text, float left = 300, float top = 0, float width = 160, float height = 80)
    {
        var anchor = Document.Paragraphs[paragraph].Range;
        Word.Shape shape = ((dynamic) Document.Shapes).AddTextbox(1, left, top, width, height, anchor);
        shape.TextFrame.TextRange.Text = text;
        shape.WrapFormat.Type = Word.WdWrapType.wdWrapSquare;
        return shape;
    }

    public Word.Shape Rectangle(int paragraph, float left = 300, float top = 0, float width = 90, float height = 60)
    {
        var anchor = Document.Paragraphs[paragraph].Range;
        Word.Shape shape = ((dynamic) Document.Shapes).AddShape(1, left, top, width, height, anchor);
        shape.WrapFormat.Type = Word.WdWrapType.wdWrapSquare;
        return shape;
    }

    public Word.Hyperlink Hyperlink(int paragraph, string text, string address) =>
        Document.Hyperlinks.Add(Find(paragraph, text), address, Type.Missing, Type.Missing, text);

    /// <summary>A hyperlink to a bookmark in the same document: following it moves the caret instead of opening a browser.</summary>
    public Word.Hyperlink InternalHyperlink(int paragraph, string text, string bookmark) =>
        Document.Hyperlinks.Add(Find(paragraph, text), Type.Missing, bookmark, Type.Missing, text);

    public Word.Field Field(Word.Range at, Word.WdFieldType type, string? text = null) =>
        Document.Fields.Add(at, type, text ?? Type.Missing, false);

    public static void LineBreak(Word.Range at) =>
        at.InsertBreak(Word.WdBreakType.wdLineBreak);

    public static void PageBreak(Word.Range at) =>
        at.InsertBreak(Word.WdBreakType.wdPageBreak);

    public static void SectionBreak(Word.Range at) =>
        at.InsertBreak(Word.WdBreakType.wdSectionBreakNextPage);

    public void Header(string text) =>
        Document.Sections[1].Headers[Word.WdHeaderFooterIndex.wdHeaderFooterPrimary].Range.Text = text;

    public void Footer(string text) =>
        Document.Sections[1].Footers[Word.WdHeaderFooterIndex.wdHeaderFooterPrimary].Range.Text = text;

    public Word.Footnote Footnote(Word.Range at, string text) =>
        Document.Footnotes.Add(at, Type.Missing, text);

    public Word.Endnote Endnote(Word.Range at, string text) =>
        Document.Endnotes.Add(at, Type.Missing, text);

    public Word.Comment Comment(Word.Range scope, string text) =>
        Document.Comments.Add(scope, text);

    public Word.Bookmark Bookmark(string name, Word.Range range) =>
        Document.Bookmarks.Add(name, range);

    public Word.ContentControl ContentControl(Word.Range range, Word.WdContentControlType type = Word.WdContentControlType.wdContentControlRichText) =>
        Document.ContentControls.Add(type, range);

    /// <summary>Runs edits with Track Changes on, as another author, then turns tracking off again.</summary>
    public void Tracked(Action<FixtureBuilder> edits, string author = "Other")
    {
        var application = Document.Application;
        var previous = application.UserName;
        application.UserName = author;
        Document.TrackRevisions = true;
        try
        {
            edits(this);
        }
        finally
        {
            Document.TrackRevisions = false;
            application.UserName = previous;
        }
    }

    public void Columns(int count)
    {
        var columns = Document.Sections[1].PageSetup.TextColumns;
        columns.SetCount(count);
    }
}
