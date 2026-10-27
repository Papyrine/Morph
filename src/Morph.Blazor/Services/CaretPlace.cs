/// <summary>
/// One step of undo or redo: the whole file to go back to, where the reader was in it before the edit
/// (<see cref="Before"/>, what undo puts back) and where the edit left them (<see cref="After"/>, what redo
/// puts back), either unknown for an edit that did not start or end at a place — a change accepted in the
/// review pane.
/// </summary>
readonly record struct HistoryStep(byte[] Bytes, CaretPlace? Before, CaretPlace? After);

/// <summary>
/// A place in the document's paragraphs, by ordinal and offset as <see cref="EditParagraph.Length"/> counts:
/// a caret where its two ends are the same, a selection where they are not. Each undo step keeps where the
/// reader was before its edit and where the edit left them, which undo and redo put back (WordProbe R-U-2).
/// </summary>
sealed record CaretPlace(int StartParagraph, int StartOffset, int EndParagraph, int EndOffset)
{
    public static CaretPlace At(int paragraph, int offset) =>
        new(paragraph, offset, paragraph, offset);

    public static CaretPlace Between(int paragraph, int from, int to) =>
        new(paragraph, from, paragraph, to);

    /// <summary>
    /// Where typing handed in for one paragraph began and ended, by what the paragraph read before and reads
    /// now have in common at either end: text put in leaves the caret where it went in, and text taken out
    /// or typed over is selected again, as Word's undo does; redo leaves the caret after what went in, in
    /// whichever of the paragraphs an Enter split it into.
    /// </summary>
    public static (CaretPlace Before, CaretPlace After) Typing(EditParagraph before, IReadOnlyList<NewParagraph> after)
    {
        var old = Read(before);
        var now = string.Join('\n', after.Select(Read));
        var prefix = 0;
        while (prefix < old.Length &&
               prefix < now.Length &&
               old[prefix] == now[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < old.Length - prefix &&
               suffix < now.Length - prefix &&
               old[^(suffix + 1)] == now[^(suffix + 1)])
        {
            suffix++;
        }

        var ordinal = before.Ordinal;
        var end = now.Length - suffix;
        var lineStart = 0;
        if (end > 0)
        {
            lineStart = now.LastIndexOf('\n', end - 1) + 1;
        }

        var line = now[..lineStart].Count(_ => _ == '\n');
        return (Between(ordinal, prefix, old.Length - suffix), At(ordinal + line, end - lineStart));
    }

    // A paragraph's places as text: a character for each of its own, and one for each thing that is not text.
    static string Read(EditParagraph paragraph) =>
        string.Concat(paragraph.Units.Select(_ => _.Editable ? _.Text : "￼"));

    static string Read(NewParagraph paragraph) =>
        string.Concat(paragraph.Items.Select(_ => _.Text ?? "￼"));
}
