/// <summary>What a tracked change did to the document.</summary>
enum ReviewChangeKind
{
    /// <summary>Text (and paragraph marks) added: <c>w:ins</c>.</summary>
    Insertion,

    /// <summary>Text (and paragraph marks) removed: <c>w:del</c>.</summary>
    Deletion,

    /// <summary>Text moved from one place to another: <c>w:moveFrom</c> and <c>w:moveTo</c>, one change.</summary>
    Move,

    /// <summary>Character formatting changed: <c>w:rPrChange</c>.</summary>
    Formatting,

    /// <summary>Paragraph formatting changed: <c>w:pPrChange</c>, <c>w:numberingChange</c>.</summary>
    ParagraphFormatting,

    /// <summary>Page setup changed: <c>w:sectPrChange</c>.</summary>
    SectionFormatting,

    /// <summary>
    /// Table formatting changed: <c>w:tblPrChange</c>, <c>w:tblPrExChange</c>, <c>w:tblGridChange</c>,
    /// <c>w:trPrChange</c>, <c>w:tcPrChange</c>.
    /// </summary>
    TableFormatting,

    /// <summary>Table rows added: <c>w:trPr/w:ins</c>.</summary>
    RowInsertion,

    /// <summary>Table rows removed: <c>w:trPr/w:del</c>.</summary>
    RowDeletion,

    /// <summary>A table cell added: <c>w:cellIns</c>.</summary>
    CellInsertion,

    /// <summary>A table cell removed: <c>w:cellDel</c>.</summary>
    CellDeletion,

    /// <summary>Table cells merged or split: <c>w:cellMerge</c>.</summary>
    CellMerge
}
