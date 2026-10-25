using static Define;

// B — the ribbon where the selection is: which of the controls Morph's editor has a command for Word
// greys out and which toggles it shows pressed. Each case only places
// the selection (or makes an edit for the undo cases) and reads the ribbon through RibbonReader, whose
// table maps each idMso to the EditorCommand it stands for; MorphEditor.Commands' Enabled, Checked and
// Visible rules are what these are compared against.
static class RibbonFamily
{
    public static FamilyDefinition Definition { get; } = new("ribbon", "The ribbon's state where the selection is",
        "For each place the editor tells apart, the ribbon controls Word greys out and shows pressed. Only controls Morph has a command for are read; see `RibbonReader.Controls` for the idMso to command mapping. Recorded on Word build 16.0.19127 on a 1920x1200 display, not the build and display in Environment.",
        Scenarios);

    const Observe ribbon = Observe.Selection | Observe.Undo | Observe.Ribbon;

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("B-01", "Plain text: a caret, a word, the whole paragraph")
            .In("plain")
            .Steps("select({at})")
            .Axis("at", "P1:5", "P1:4-P1:9", "P1:0-P1:end", "P2:0")
            .Tracked()
            .Observing(ribbon)
            .Hypotheses("Cut and Copy are greyed out at a caret only; Align Left is pressed; no contextual tab shows; Track Changes is pressed when tracked.")
            .Morph("PARTIAL");

        yield return Probe("B-02", "Character formatting under the caret and across mixed runs")
            .In("runs")
            .Steps("select({at})")
            .Axis("at", "P1:7", "P1:13", "P1:21", "P1:6-P1:10", "P1:3-P1:8", "P1:0-P1:end")
            .Observing(ribbon)
            .Hypotheses("A caret in 'bold' shows Bold pressed, in 'italic' Italic, in 'under' the Underline gallery; a selection that is partly bold shows Bold not pressed (Word presses a toggle only when the whole selection has it).")
            .Morph("PARTIAL");

        yield return Probe("B-03", "Lists and headings")
            .In("{fixture}")
            .Steps("select(P2:2)")
            .Axis("fixture", "bullets", "bullets-nested", "numbers", "numbers-nested", "headings", "kinds")
            .Observing(ribbon)
            .Hypotheses("Bullets is pressed in a bulleted item and Numbering in a numbered one; Decrease Indent is enabled at level 1 of a list too.")
            .Morph("PARTIAL");

        yield return Probe("B-04", "Tables: a cell, cells, a column, a row, the table, merged cells")
            .In("{fixture}")
            .Steps("select({at})")
            .Axis("fixture", "table-3x3", "table-merged")
            .Axis("at", "C(2,2):0", "C(1,1)-C(2,2)", "COL1", "R1", "T1", "P1:0")
            .Observing(ribbon)
            .Hypotheses("The Table Design and Layout tabs show inside a table only; Merge Cells is greyed out for a single cell; Split Cells is enabled in a cell; the table commands are greyed out (or their tab gone) outside one.")
            .Morph("PARTIAL");

        yield return Probe("B-05", "A picture selected, inline and floating")
            .In("{fixture}")
            .Steps("select({at})")
            .Axis("fixture", "image-inline", "image-floating")
            .Axis("at", "IMG1", "SHP1", "P1:0")
            .Observing(ribbon)
            .Hypotheses("With a picture selected the Picture Format tab shows, Wrap Text is enabled, and the paragraph-only commands stay enabled for an inline picture; a floating picture greys out the text commands.")
            .Morph("PARTIAL");

        yield return Probe("B-06", "The caret in a header and a footer")
            .In("header-footer")
            .Steps("select({at})")
            .Axis("at", "HDR:P1:2", "FTR:P1:0")
            .Observing(ribbon)
            .Hypotheses("A page break, the page setup galleries, the notes and a comment are greyed out in a header and a footer.")
            .Morph("PARTIAL");

        yield return Probe("B-06b", "The caret in a footnote and an endnote")
            .In("footnote")
            .Steps("select({at})")
            .Axis("at", "FN1:P1:2", "EN1:P1:2")
            .Observing(ribbon)
            .Hypotheses("As in a header, and a footnote is greyed out in a footnote, an endnote in an endnote.")
            .Morph("PARTIAL");

        yield return Probe("B-06c", "The caret in a comment and a text box")
            .In("{fixture}")
            .Steps("select({at})")
            .Axis("fixture", "comment", "text-box")
            .Axis("at", "CMT1:P1:0", "TB1:P1:2")
            .Observing(ribbon)
            .Hypotheses("A comment greys out much what a note does; a text box greys out the page break and the notes but not the page setup.")
            .Morph("TODO");

        yield return Probe("B-07", "In a link, a field, a table of contents, a tracked change")
            .In("{fixture}")
            .Steps("select(P1:{at})")
            .Axis("fixture", "hyperlink", "field-page", "contents", "tracked")
            .Axis("at", "find(\"site\")", "6", "find(\"added\")")
            .Observing(ribbon)
            .Notes("Each fixture is paired with the one position that falls in its feature: 'site' in the link, 6 in the PAGE field, the table of contents' first entry, 'added' in the insertion; the other pairs are not applicable or land in plain text.")
            .Hypotheses("Remove Link is not on the ribbon; Update Table is enabled only with a table of contents in the document; Accept All and Reject All only with revisions in it.")
            .Morph("PARTIAL");

        yield return Probe("B-08", "Undo and Redo after typing")
            .In("plain")
            .Steps("select(P1:5)", "type(\"x\")")
            .Observing(ribbon)
            .Notes("A fresh fixture opens with an empty undo list, so B-01 records Undo greyed out; Paste depends on what is on the clipboard and is not staged.")
            .Hypotheses("Undo is enabled; Redo is greyed out (or stands for Repeat Typing, which is enabled).")
            .Morph("PARTIAL");

        yield return Probe("B-09", "Undo and Redo after an undo")
            .In("plain")
            .Steps("select(P1:5)", "type(\"x\")", "Ctrl+Z")
            .Observing(ribbon)
            .Hypotheses("Undo is greyed out again and Redo is enabled.")
            .Morph("PARTIAL");

        yield return Probe("B-10", "Undo and Redo after an undo and a redo")
            .In("plain")
            .Steps("select(P1:5)", "type(\"x\")", "Ctrl+Z", "Ctrl+Y")
            .Observing(ribbon)
            .Hypotheses("Undo is enabled and Redo greyed out, as after the typing.")
            .Morph("PARTIAL");
    }
}
