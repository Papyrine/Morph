using static Define;

// R — Track Changes: what Word writes when the keys of the other families are pressed with tracking on.
// Every scenario turns tracking on as its first step, so none repeats itself with Tracked(): the rest of
// the catalogue already runs twice, once plain and once tracked, and these rows are the ones whose whole
// point is the markup.
static class TrackingFamily
{
    public static FamilyDefinition Definition { get; } = new("tracking", "Track Changes",
        "Typing, Backspace and Delete over own and others' revisions, Enter, paste and move, formatting, rows and pictures under tracking.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("R-01", "Own insertion grows as typing continues")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "type(\"ab\")")
            .Branch("more", S("type(\"c\")"))
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("One w:ins holds the whole run; the second type step grows it rather than adding a second revision.")
            .Morph("DONE");

        yield return Probe("R-02", "Backspace over own insertion, then over existing text, one press at a time")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "type(\"xy\")", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("(a) the two own characters are removed outright, then each press marks the character before the caret deleted and the caret steps left over it; (b) once own deleted text sits before the caret, Backspace deletes the next undeleted character beyond it; (c) Backspace does nothing once it reaches own deleted text.")
            .Morph("PARTIAL");

        yield return Probe("R-02b", "After Backspace has reached existing text: the keys that follow")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "type(\"xy\")", "Backspace x3")
            .Branch("type", S("type(\"z\")"))
            .Branch("backspace", S("Backspace"))
            .Branch("delete", S("Delete"))
            .Branch("right-backspace", S("Right", "Backspace"))
            .Branch("undo", S("undo"))
            .Branch("undo-all", S("undo x3"))
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Notes("After the third Backspace the own insertion is gone and one existing character ('k' at P1:4) is marked deleted, with the caret before it. Each branch asks what the next key does from there.")
            .Hypotheses("(a) type inserts before the deleted character; Backspace marks the next character to the left; Delete marks the deleted character's right neighbour, skipping own deleted text; Right steps over the deleted character and Backspace then (b) removes the mark, or (c) marks nothing. Undo removes one deletion mark per step.")
            .Morph("PARTIAL");

        yield return Probe("R-03", "Delete over existing text, repeated")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "Delete", "Delete", "Delete")
            .Trace()
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("(a) the caret stays at P1:5 and each press marks the next undeleted character deleted, so the deletion grows rightwards; (b) the caret steps right over each character it marks.")
            .Morph("PARTIAL");

        yield return Probe("R-04", "Enter splits a paragraph: the new mark is an insertion")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P2:4)", "Enter")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("The mark of the first half carries w:ins in its rPr; the second half keeps P2's properties and the bold run.")
            .Morph("PARTIAL");

        yield return Probe("R-04b", "Backspace at a paragraph's start marks the previous mark deleted")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P2:0)", "Backspace")
            .Branch("undo", S("undo"))
            .Observing(Observe.Default | Observe.Revisions | Observe.Formatting | Observe.Markup)
            .Hypotheses("P1's mark is marked deleted and the two paragraphs stay separate in the markup; the merged display shows (a) P2's properties (centred) (b) P1's. Undo restores the mark.")
            .Morph("PARTIAL");

        yield return Probe("R-04c", "Delete at a paragraph's end marks its own mark deleted")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P1:end)", "Delete")
            .Observing(Observe.Default | Observe.Revisions | Observe.Formatting | Observe.Markup)
            .Hypotheses("The same markup as Backspace from P2:0; the caret stays at P1:end.")
            .Morph("PARTIAL");

        yield return Probe("R-05", "A selection of existing text replaced by typing")
            .In("plain")
            .Steps("track(on)", "select(P1:10-P1:15)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("'brown' is marked deleted and 'x' inserted after it, caret after the insertion.")
            .Morph("DONE");

        yield return Probe("R-05b", "A selection of own insertion replaced by typing")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "type(\"own\")", "select(P1:5-P1:8)", "type(\"y\")")
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("'own' is removed outright, not marked; 'y' is a fresh insertion at P1:5.")
            .Morph("DONE");

        yield return Probe("R-06", "Cut and paste a few words: move marks or deletion plus insertion")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P1:0-P1:5)", "Ctrl+X", "select(P3:end)", "Ctrl+V")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("(a) w:moveFrom / w:moveTo for any text; (b) move marks only for a sentence or longer, so this is a plain deletion plus insertion; (c) always a plain deletion plus insertion.")
            .Morph("TODO");

        yield return Probe("R-06b", "Cut and paste a whole paragraph")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P1:0-P2:0)", "Ctrl+X", "select(P3:end)", "Ctrl+V")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("A whole paragraph is long enough for move marks; the moved mark carries moveFrom and the pasted one moveTo.")
            .Morph("TODO");

        yield return Probe("R-06c", "Copy and paste never makes a move")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P1:0-P1:5)", "Ctrl+C", "select(P3:end)", "Ctrl+V")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("A plain insertion at P3:end; the source is untouched.")
            .Morph("TODO");

        yield return Probe("R-07", "Formatting keys under tracking: bold, alignment, style and clear")
            .In("plain")
            .Steps("track(on)")
            .Branch("bold", S("select(P1:4-P1:9)", "Ctrl+B"))
            .Branch("bold-own", S("select(P1:5)", "type(\"own\")", "select(P1:5-P1:8)", "Ctrl+B"))
            .Branch("centre", S("select(P1:2)", "Ctrl+E"))
            .Branch("heading", S("select(P1:2)", "Ctrl+Alt+1"))
            .Branch("clear", S("select(P1:4-P1:9)", "Ctrl+Space"))
            .Observing(Observe.Default | Observe.Revisions | Observe.Formatting | Observe.Markup)
            .Hypotheses("Bold on existing text writes w:rPrChange holding the old rPr; bold on own insertion writes none (the inserted run is formatted in place); Ctrl+E and Ctrl+Alt+1 write w:pPrChange; Ctrl+Space on plain text writes nothing since nothing changed.")
            .Morph("TODO");

        yield return Probe("R-08", "Table rows and cells under tracking")
            .In("table-2x2")
            .Steps("track(on)")
            .Branch("tab-new-row", S("select(C(2,2):end)", "Tab"))
            .Branch("rowbar-backspace", S("click(rowBar R1)", "Backspace"))
            .Branch("row-delete", S("select(R1)", "Delete"))
            .Branch("cell-text", S("select(C(1,1):0-C(1,1):mark)", "type(\"x\")"))
            .Observing(Observe.Default | Observe.Revisions | Observe.Tables | Observe.Markup)
            .Hypotheses("(a) the new row carries w:ins in its trPr and a deleted row w:del, both still in the table until accepted; Delete with a row selected clears its text as deletions rather than removing the row; (b) row structure changes are untracked.")
            .Morph("PARTIAL");

        yield return Probe("R-09", "Delete an inline picture under tracking")
            .In("image-inline")
            .Steps("track(on)", "click(IMG1)", "Delete")
            .Observing(Observe.Default | Observe.Revisions | Observe.Shapes | Observe.Markup)
            .Hypotheses("The drawing's run is wrapped in w:del and the picture still shows, struck through in the markup view.")
            .Morph("PARTIAL");

        yield return Probe("R-09b", "Cut and paste an inline picture under tracking")
            .In("image-inline")
            .Steps("track(on)", "click(IMG1)", "Ctrl+X", "select(P2:3)", "Ctrl+V")
            .Observing(Observe.Default | Observe.Revisions | Observe.Shapes | Observe.Markup)
            .Hypotheses("A deletion at the source and an insertion at P2:3, each holding its own copy of the drawing; no move marks.")
            .Morph("TODO");

        yield return Probe("R-09c", "Delete a floating picture under tracking")
            .In("image-floating")
            .Steps("track(on)", "click(SHP1)", "Delete")
            .Observing(Observe.Default | Observe.Revisions | Observe.Shapes | Observe.Markup)
            .Hypotheses("The anchor's run is wrapped in w:del; the picture stays on the page until accepted.")
            .Morph("PARTIAL");

        yield return Probe("R-10", "Keys on another author's revisions")
            .In("tracked")
            .Steps("track(on)")
            .Branch("delete-on-deletion", S("select(P1:11)", "Delete"))
            .Branch("backspace-in-insertion", S("select(P1:7)", "Backspace"))
            .Branch("type-in-insertion", S("select(P1:7)", "type(\"x\")"))
            .Branch("delete-span", S("select(P1:5-P1:16)", "Delete"))
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Notes("P1:11 is the start of the other author's deletion 'gone '; P1:7 is inside the other author's insertion 'added '.")
            .Hypotheses("(a) Delete on another author's deletion skips it and marks the next undeleted character; (b) it does nothing. Backspace inside another's insertion marks the character deleted (nested w:del inside w:ins) rather than removing it; typing inside splits the other's insertion around an own one. Deleting the span marks the insertion deleted and leaves the deletion as it was.")
            .Morph("PARTIAL");

        yield return Probe("R-11", "Right over another author's deletion, per markup view")
            .In("tracked")
            .Steps("view({view})", "select(P1:10)", "Right", "Right", "Right")
            .Axis("view", "all", "simple", "none")
            .Trace()
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("(a) the collapsed views skip the deleted text as one atom; (b) every character is counted in every view.")
            .Morph("PARTIAL");

        yield return Probe("R-11b", "Left over another author's deletion, per markup view")
            .In("tracked")
            .Steps("view({view})", "select(P1:17)", "Left", "Left", "Left")
            .Axis("view", "all", "simple", "none")
            .Trace()
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("The mirror of R-11: Left at the deletion's end lands before it in the collapsed views.")
            .Morph("PARTIAL");

        yield return Probe("R-11c", "A click inside another author's deletion, per markup view")
            .In("tracked")
            .Steps("view({view})", "click(P1:13)")
            .Axis("view", "all", "simple", "none")
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("In the all-markup view the click lands inside the deleted text; in the collapsed views the deleted text has no pixels and the click lands at the nearest visible position.")
            .Morph("PARTIAL");

        yield return Probe("R-12", "Undo under tracking: type, Backspace and paste")
            .In("plain")
            .Steps("track(on)")
            .Branch("type", S("select(P1:5)", "type(\"xy\")", "undo"))
            .Branch("backspace", S("select(P1:5)", "Backspace", "undo"))
            .Branch("paste", S("clipboard(\"pasted\")", "select(P1:5)", "Ctrl+V", "undo"))
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("Each undo removes the revision it made, leaving no mark behind; the revision count returns to zero.")
            .Morph("PARTIAL");

        yield return Probe("R-13", "Typing beside own deleted text")
            .In("plain")
            .Steps("track(on)", "select(P1:9)", "Backspace x3", "type(\"z\")")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("(a) the insertion is written after the deletion in the markup; (b) before it, where the caret stood.")
            .Morph("PARTIAL");

        yield return Probe("R-14", "Enter inside an own insertion")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "type(\"abc\")", "Left", "Enter")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("The insertion is split in two with the inserted mark between; both halves keep the same author and date.")
            .Morph("PARTIAL");

        yield return Probe("R-14b", "Enter inside another author's insertion")
            .In("tracked")
            .Steps("track(on)", "select(P1:8)", "Enter")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("The other author's insertion is split around an own inserted mark; both halves keep the other author.")
            .Morph("PARTIAL");

        yield return Probe("R-15", "Tracking toggled mid-typing")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "type(\"ab\")", "Ctrl+Shift+E", "type(\"cd\")")
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("'ab' stays an insertion and 'cd' is plain text in the same run position; TrackRevisions reads false afterwards.")
            .Morph("TODO");

        yield return Probe("R-16", "Tab in a list under tracking")
            .In("bullets")
            .Steps("track(on)")
            .Branch("second", S("select(P2:2)", "Tab"))
            .Branch("first", S("select(P1:2)", "Tab"))
            .Observing(Observe.Default | Observe.Revisions | Observe.Lists | Observe.Markup)
            .Hypotheses("Tab mid-paragraph in a list item demotes it and writes w:pPrChange; Tab in the first item moves the whole list's indents and is written untracked.")
            .Morph("PARTIAL");

        yield return Probe("R-17", "Paste a table under tracking")
            .In("table-2x2")
            .Steps("track(on)", "select(T1)", "Ctrl+C", "select(P2:end)", "Enter", "Ctrl+V")
            .Observing(Observe.Default | Observe.Revisions | Observe.Tables | Observe.Markup)
            .Hypotheses("Every row of the pasted table carries w:ins in its trPr and every cell's text is an insertion.")
            .Morph("PARTIAL");

        yield return Probe("R-18", "Cell keys under tracking: type, Enter and a column break")
            .In("table-2x2")
            .Steps("track(on)")
            .Branch("type", S("select(C(1,1):2)", "type(\"x\")"))
            .Branch("enter", S("select(C(1,1):end)", "Enter"))
            .Branch("column-break", S("select(C(2,1):0)", "Ctrl+Shift+Enter"))
            .Observing(Observe.Default | Observe.Revisions | Observe.Tables | Observe.Markup)
            .Hypotheses("Typing and Enter in a cell write the same marks as in the body; Ctrl+Shift+Enter at a cell's start splits the table and the new paragraph between the halves is an inserted mark.")
            .Morph("PARTIAL");

        yield return Probe("R-19", "A selection spanning own insertion and existing text deleted")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "type(\"xy\")", "select(P1:3-P1:9)", "Delete")
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("The own characters are removed outright and the existing characters on either side are marked deleted, leaving two deletions or one around the gap.")
            .Morph("PARTIAL");

        yield return Probe("R-20", "Selection.Text over another author's deleted text, tracking off")
            .In("tracked")
            .Steps("select(P1:8-P1:14)")
            .Observing(Observe.Default | Observe.Revisions)
            .Hypotheses("The range's text includes the deleted characters; the revision list shows both revisions the range touches.")
            .Morph("PARTIAL");
    }
}
