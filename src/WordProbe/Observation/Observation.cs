// What is read off Word after a case's steps. Positions are story positions (Range.Start/End); the text of
// every paragraph is read with field codes and hidden text included so that its length equals End - Start
// and a position maps to a character index.
record Observation(
    SelectionState Selection,
    List<ParagraphState> Paragraphs,
    List<TableState> Tables,
    List<ShapeState> Shapes,
    List<FieldState> Fields,
    List<RevisionState> Revisions,
    List<NoteState> Notes,
    List<CommentState> Comments,
    List<ContentControlState> ContentControls,
    List<int> InlinePictures,
    List<int> SectionEnds,
    List<string> Undo,
    string UndoSource,
    bool Tracking,
    StoryState? Story,
    DialogInfo? Dialog,
    List<HotspotProbe> Hotspots,
    string? MarkupDiff,
    List<string> Remarks);

record SelectionState(
    int Start,
    int End,
    string Type,
    int Flags,
    bool StartIsActive,
    bool AtEndOfLine,
    string StoryType,
    bool ExtendMode,
    bool ColumnSelectMode,
    int SelectionMode,
    bool InTable,
    int? StartRow,
    int? StartColumn,
    int? EndRow,
    int? EndColumn,
    bool AtEndOfRowMarker,
    int Cells,
    int Rows,
    int Columns,
    int InlineShapes,
    int Shapes,
    bool Overtype,
    string Text)
{
    public bool IsCollapsed => Start == End;
}

record RunState(int Start, int End, string Marks);

record ParagraphState(
    int Index,
    int Start,
    int End,
    string Text,
    List<RunState> Runs,
    string Style,
    string Alignment,
    float LeftIndent,
    float FirstLineIndent,
    int? ListIndex,
    int? ListLevel,
    string? ListString,
    int Table,
    int Row,
    int Column,
    int NestingLevel,
    bool LengthMismatch,
    float SpaceBefore = 0,
    float SpaceAfter = 0,
    bool Bordered = false,
    bool KeepWithNext = false);

record TableState(int Index, int Rows, int Columns, bool Uniform, int Start, int End, List<CellState> Cells, int NestingLevel);

record CellState(int Row, int Column, int Start, int End, int RowEnd);

record ShapeState(int Index, string Name, string Kind, int Anchor, int AnchorParagraph, float Width, float Height, string Wrap, bool Selected, float Left, float Top);

record FieldState(int Type, string TypeName, int Begin, int Separator, int End, string Code);

record RevisionState(string Type, int Start, int End, string Author);

record NoteState(string Kind, int Index, int Reference, string Text);

record CommentState(int Index, int ScopeStart, int ScopeEnd, int Reference, string Text);

/// <summary>A content control's content range; its start and end markers occupy the positions just outside it.</summary>
record ContentControlState(int Index, string Kind, int Start, int End);

record StoryState(string StoryType, List<ParagraphState> Paragraphs);

record HotspotProbe(string Step, int X, int Y, string Under, string Cursor);
