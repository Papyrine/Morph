// Reads an Observation off the open document over COM. Everything here is a read; nothing moves the
// selection or changes the document.
static class Observer
{
    const int undefined = 9999999;

    public static Observation Read(ProbeDocument probe, Observe observe, List<HotspotProbe> hotspots, DialogInfo? dialog, string? markupBefore, List<string> remarks)
    {
        var document = probe.Document;
        var selection = ReadSelection(probe.Selection);
        var formatting = observe.HasFlag(Observe.Formatting);
        // With a comment card selected in the pane (after Ctrl+Alt+M) Word refuses TrackRevisions outright;
        // the value cannot have changed since the case set it, so the refusal is noted and the read goes on.
        var tracking = false;
        try
        {
            tracking = document.TrackRevisions;
        }
        catch (COMException exception)
        {
            remarks.Add($"TrackRevisions unreadable: {exception.Message}");
        }

        // Read first: walking a list paragraph's ListFormat over COM left a "Bullets and Numbering" entry on
        // top of the undo list in every list fixture (lists family, 2026-10-25), so the names are taken
        // before anything else touches the document.
        var (undo, undoSource) = observe.HasFlag(Observe.Undo) ? UndoStack.Read(probe) : ([], "off");
        var revisions = ReadRevisions(document);
        var fields = ReadFields(document);
        var notes = ReadNotes(document);
        var comments = ReadComments(document);
        var sectionEnds = ReadSectionEnds(document);
        var tables = ReadTables(document);
        var lists = ReadLists(document);
        var paragraphs = ReadParagraphs(document.Content, tables, lists, formatting, remarks, revisions.Count > 0 || tracking);
        var shapes = ReadShapes(document, paragraphs, selection);
        var controls = ReadContentControls(document);
        var story = selection.StoryType == nameof(Word.WdStoryType.wdMainTextStory) && !observe.HasFlag(Observe.Story)
            ? null
            : ReadStory(document, selection, tables, lists, formatting, remarks);
        string? markupDiff = null;
        if (markupBefore != null)
        {
            markupDiff = MarkupDiff.Diff(markupBefore, Markup.Body(document));
        }

        var inlinePictures = new List<int>();
        foreach (Word.InlineShape inlineShape in document.InlineShapes)
        {
            inlinePictures.Add(inlineShape.Range.Start);
        }

        return new(selection, paragraphs, tables, shapes, fields, revisions, notes, comments, controls, inlinePictures, sectionEnds, undo, undoSource, tracking, story, dialog, hotspots, markupDiff, remarks);
    }

    static List<ContentControlState> ReadContentControls(Word.Document document)
    {
        var controls = new List<ContentControlState>();
        var index = 0;
        foreach (Word.ContentControl control in document.ContentControls)
        {
            index++;
            var range = control.Range;
            controls.Add(new(index, control.Type.ToString().Replace("wdContentControl", ""), range.Start, range.End));
        }

        return controls;
    }

    public static SelectionState ReadSelection(Word.Selection selection)
    {
        var type = selection.Type;
        var inTable = Information<bool>(selection, Word.WdInformation.wdWithInTable);
        int? startRow = null, startColumn = null, endRow = null, endColumn = null;
        if (inTable)
        {
            startRow = Information<int>(selection, Word.WdInformation.wdStartOfRangeRowNumber);
            startColumn = Information<int>(selection, Word.WdInformation.wdStartOfRangeColumnNumber);
            endRow = Information<int>(selection, Word.WdInformation.wdEndOfRangeRowNumber);
            endColumn = Information<int>(selection, Word.WdInformation.wdEndOfRangeColumnNumber);
        }

        var text = selection.Text ?? "";
        return new(
            selection.Start,
            selection.End,
            type.ToString().Replace("wdSelection", ""),
            (int) selection.Flags,
            selection.StartIsActive,
            selection.IPAtEndOfLine,
            selection.StoryType.ToString(),
            selection.ExtendMode,
            selection.ColumnSelectMode,
            Information<int>(selection, Word.WdInformation.wdSelectionMode),
            inTable,
            startRow,
            startColumn,
            endRow,
            endColumn,
            inTable && Information<bool>(selection, Word.WdInformation.wdAtEndOfRowMarker),
            inTable ? Com.Try(() => selection.Cells.Count) : 0,
            inTable ? Com.Try(() => selection.Rows.Count) : 0,
            inTable ? Com.Try(() => selection.Columns.Count) : 0,
            Com.Try(() => selection.InlineShapes.Count),
            type == Word.WdSelectionType.wdSelectionShape ? Com.Try(() => selection.ShapeRange.Count) : 0,
            Information<bool>(selection, Word.WdInformation.wdOverType),
            text);
    }

    static T Information<T>(Word.Selection selection, Word.WdInformation what)
    {
        try
        {
            var value = selection.Information[what];
            if (value is T typed)
            {
                return typed;
            }

            return (T) Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return default!;
        }
    }

    static List<ParagraphState> ReadParagraphs(Word.Range storyRange, List<TableState> tables, List<(int Start, int End)> lists, bool formatting, List<string> remarks, bool tracked)
    {
        var result = new List<ParagraphState>();
        var index = 0;
        foreach (Word.Paragraph paragraph in storyRange.Paragraphs)
        {
            index++;
            var range = paragraph.Range;
            var start = range.Start;
            var end = range.End;
            var owner = tables.FirstOrDefault(_ => start >= _.Start && start < _.End);
            var inTable = owner != null;
            var table = 0;
            var row = 0;
            var column = 0;
            var nesting = 0;
            if (inTable)
            {
                var cell = Com.Try(() => range.Cells[1]);
                if (cell == null &&
                    end - start == 1)
                {
                    // A nested table's end-of-row mark is enumerated as a paragraph of its own; it is not content.
                    continue;
                }

                if (cell != null)
                {
                    nesting = cell.NestingLevel;
                    table = owner!.Index;
                    // A paragraph inside a nested table is shown inside the outer cell that holds the nested table.
                    var outer = owner.Cells.FirstOrDefault(_ => start >= _.Start && start < _.End);
                    if (outer != null)
                    {
                        row = outer.Row;
                        column = outer.Column;
                    }
                    else
                    {
                        row = cell.RowIndex;
                        column = cell.ColumnIndex;
                    }
                }
            }

            var text = ProbeDocument.StoryText(range, isCell: inTable);
            var mismatch = text.Length != end - start;
            if (mismatch)
            {
                remarks.Add($"P{index}: text length {text.Length} differs from range {end - start}");
            }

            var style = Com.Try(() => ((Word.Style) paragraph.get_Style()).NameLocal) ?? "";
            var alignment = paragraph.Alignment switch
            {
                Word.WdParagraphAlignment.wdAlignParagraphCenter => "center",
                Word.WdParagraphAlignment.wdAlignParagraphRight => "right",
                Word.WdParagraphAlignment.wdAlignParagraphJustify => "both",
                Word.WdParagraphAlignment.wdAlignParagraphDistribute => "distribute",
                Word.WdParagraphAlignment.wdAlignParagraphLeft => "",
                _ => paragraph.Alignment.ToString()
            };
            int? listIndex = null;
            int? listLevel = null;
            string? listString = null;
            var listFormat = range.ListFormat;
            if (listFormat.ListType != Word.WdListType.wdListNoNumbering)
            {
                listLevel = listFormat.ListLevelNumber - 1;
                listString = Com.Try(() => listFormat.ListString);
                for (var i = 0; i < lists.Count; i++)
                {
                    if (start >= lists[i].Start && start < lists[i].End)
                    {
                        listIndex = i + 1;
                        break;
                    }
                }
            }

            var runs = formatting || tracked ? RunScanner.Runs(range, start, end, text) : [new(start, end, "")];
            var bordered = Com.Try(() => paragraph.Borders.Enable != 0);
            result.Add(new(index, start, end, text, runs, style.Replace(" ", ""), alignment, paragraph.LeftIndent, paragraph.FirstLineIndent, listIndex, listLevel, listString, table, row, column, nesting, mismatch,
                paragraph.SpaceBefore, paragraph.SpaceAfter, bordered, paragraph.KeepWithNext != 0));
        }

        return result;
    }

    static List<(int Start, int End)> ReadLists(Word.Document document)
    {
        var lists = new List<(int, int)>();
        foreach (Word.List list in document.Lists)
        {
            var range = list.Range;
            lists.Add((range.Start, range.End));
        }

        return lists;
    }

    static List<TableState> ReadTables(Word.Document document)
    {
        var tables = new List<TableState>();
        var index = 0;
        foreach (Word.Table table in document.Tables)
        {
            index++;
            tables.Add(ReadTable(table, index));
        }

        return tables;
    }

    static TableState ReadTable(Word.Table table, int index)
    {
        var range = table.Range;
        var cells = new List<CellState>();
        var rowEnds = new Dictionary<int, int>();
        foreach (Word.Cell cell in range.Cells)
        {
            if (cell.NestingLevel != table.NestingLevel)
            {
                continue;
            }

            var cellRange = cell.Range;
            cells.Add(new(cell.RowIndex, cell.ColumnIndex, cellRange.Start, cellRange.End, 0));
        }

        // The end-of-row mark sits after the row's last cell; its position is that cell's End.
        var withRowEnds = cells
            .GroupBy(_ => _.Row)
            .SelectMany(group =>
            {
                var rowEnd = group.Max(_ => _.End);
                return group.Select(_ => _ with
                {
                    RowEnd = rowEnd
                });
            })
            .OrderBy(_ => _.Start)
            .ToList();
        return new(index, Com.Try(() => table.Rows.Count), Com.Try(() => table.Columns.Count), table.Uniform, range.Start, range.End, withRowEnds, table.NestingLevel);
    }

    static List<ShapeState> ReadShapes(Word.Document document, List<ParagraphState> paragraphs, SelectionState selection)
    {
        var shapes = new List<ShapeState>();
        var selectedNames = new HashSet<string>(StringComparer.Ordinal);
        if (selection.Type == "Shape")
        {
            Com.Try(() =>
            {
                foreach (Word.Shape selected in document.ActiveWindow.Selection.ShapeRange)
                {
                    selectedNames.Add(selected.Name);
                }

                return true;
            });
        }

        var index = 0;
        foreach (Word.Shape shape in document.Shapes)
        {
            index++;
            var anchor = Com.Try(() => shape.Anchor.Start);
            var anchorParagraph = paragraphs.FirstOrDefault(_ => anchor >= _.Start && anchor < _.End)?.Index ?? 0;
            var kind = ((dynamic) shape).Type is int type
                ? type switch
                {
                    13 => "picture",
                    17 => "textbox",
                    1 => "autoshape",
                    6 => "group",
                    9 => "line",
                    _ => $"type{type}"
                }
                : "shape";
            shapes.Add(new(index, shape.Name, kind, anchor, anchorParagraph, shape.Width, shape.Height, shape.WrapFormat.Type.ToString().Replace("wdWrap", "").ToLowerInvariant(), selectedNames.Contains(shape.Name), shape.Left, shape.Top));
        }

        return shapes;
    }

    static List<FieldState> ReadFields(Word.Document document)
    {
        var fields = new List<FieldState>();
        foreach (Word.Field field in document.Fields)
        {
            var code = field.Code;
            var result = Com.Try(() => field.Result);
            var begin = code.Start - 1;
            var separator = code.End;
            var end = result != null ? result.End : code.End;
            fields.Add(new((int) field.Type, field.Type.ToString().Replace("wdField", ""), begin, separator, end, (code.Text ?? "").Trim()));
        }

        return fields;
    }

    static List<RevisionState> ReadRevisions(Word.Document document)
    {
        var revisions = new List<RevisionState>();
        if (document.Revisions.Count == 0)
        {
            return revisions;
        }

        foreach (Word.Revision revision in document.Revisions)
        {
            var range = revision.Range;
            revisions.Add(new(revision.Type.ToString().Replace("wdRevision", ""), range.Start, range.End, revision.Author));
        }

        return revisions;
    }

    static List<NoteState> ReadNotes(Word.Document document)
    {
        var notes = new List<NoteState>();
        var index = 0;
        foreach (Word.Footnote footnote in document.Footnotes)
        {
            index++;
            notes.Add(new("footnote", index, footnote.Reference.Start, footnote.Range.Text ?? ""));
        }

        index = 0;
        foreach (Word.Endnote endnote in document.Endnotes)
        {
            index++;
            notes.Add(new("endnote", index, endnote.Reference.Start, endnote.Range.Text ?? ""));
        }

        return notes;
    }

    static List<CommentState> ReadComments(Word.Document document)
    {
        var comments = new List<CommentState>();
        var index = 0;
        foreach (Word.Comment comment in document.Comments)
        {
            index++;
            var scope = comment.Scope;
            comments.Add(new(index, scope.Start, scope.End, comment.Reference.Start, comment.Range.Text ?? ""));
        }

        return comments;
    }

    static List<int> ReadSectionEnds(Word.Document document)
    {
        var ends = new List<int>();
        var count = document.Sections.Count;
        for (var i = 1; i < count; i++)
        {
            ends.Add(document.Sections[i].Range.End - 1);
        }

        return ends;
    }

    static StoryState? ReadStory(Word.Document document, SelectionState selection, List<TableState> tables, List<(int Start, int End)> lists, bool formatting, List<string> remarks)
    {
        if (!Enum.TryParse<Word.WdStoryType>(selection.StoryType, out var storyType) ||
            storyType == Word.WdStoryType.wdMainTextStory)
        {
            return null;
        }

        var range = Com.Try(() => document.StoryRanges[storyType]);
        if (range == null)
        {
            return null;
        }

        var paragraphs = ReadParagraphs(range, tables, lists, formatting, remarks, tracked: false);
        return new(selection.StoryType, paragraphs);
    }

    public static bool IsUndefined(int value) =>
        value == undefined;
}
