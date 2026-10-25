using static Define;

// E — editing: what a typed character inherits at every kind of boundary, what Enter splits and which
// half keeps what, what Backspace and Delete merge and which paragraph's properties survive, the word
// deletions, and every direct-formatting key. Every scenario that changes the document is repeated with
// Track Changes on (Tracked), so each row also records the revision Word writes for it.
//
// Fixture texts are 0-based: in `mixed` P1 is "Strong soft plain words." (Strong bold, soft italic) and
// P2 "Serif face sans face." (Times 14 then Arial 10); `words` is "alpha beta,  gamma-delta 3.14 don't
// (eps) 1,000 10:30 end." at 24pt; `kinds` is one paragraph per kind, P1 Normal, P2 Heading 1, P3 bullet,
// P4 centred, P5 bordered, P6 left-indented, P7 empty, P8 keep-with-next, P9 plain. Paragraphs holding a
// field or a hyperlink are addressed with find("…"), since the hidden code occupies positions before the
// result text.
static class EditingFamily
{
    public static FamilyDefinition Definition { get; } = new("editing", "Typing, Enter, Backspace, Delete and direct formatting",
        "What a typed character inherits, what Enter splits and what Backspace and Delete merge, and every formatting key.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        // ---- Typing -----------------------------------------------------------------------------------

        yield return Probe("E-01", "A typed character takes the formatting of the run before the caret")
            .In("mixed")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:6", "P1:0", "P1:3", "P1:11", "P1:end", "P2:10")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Notes("P1:6 is after the bold run, P1:0 before it at the paragraph start, P1:3 inside it, P1:11 after the italic run, P2:10 at the boundary between the two fonts.")
            .Hypotheses("(a) the character takes the run before the caret everywhere, and the run after only at a paragraph start; (b) at a run boundary the character takes the run after.")
            .Morph("DONE");

        yield return Probe("E-02", "Typing into an empty paragraph takes the mark's formatting")
            .In("empty-paragraphs")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P2:0", "P3:0")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-02b", "Typing into the empty paragraph among the kinds")
            .In("kinds")
            .Steps("select(P7:0)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-03", "Typing before and after an inline picture in the middle of a line")
            .In("image-inline")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:12", "P1:13")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Notes("The picture is at P1:12, after 'Text before '.")
            .Hypotheses("(a) the character takes the picture run's own rPr; (b) the text run before the picture; (c) the text run after it.")
            .Morph("UNKNOWN");

        yield return Probe("E-03b", "Typing at a paragraph that starts with a picture and one that ends with one")
            .In("image-edges")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:0", "P1:1", "P2:end")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Notes("P1:0 is before the leading picture, P1:1 after it; P2:end is after the trailing picture and before the mark.")
            .Hypotheses("(a) the picture run's rPr; (b) the nearest text run; (c) the paragraph mark's rPr when no text run is adjacent.")
            .Morph("UNKNOWN");

        yield return Probe("E-04", "Typing before and after a DATE field whose label is bold")
            .In("field-page")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P2:find(\" end.\")", "P2:find(\"Date: \"):end")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Notes("P2 reads 'Date: <DATE> end.' with 'Date:' bold; the first position is after the field result, the second between the bold label and the field.")
            .Morph("PARTIAL");

        yield return Probe("E-04b", "Typing inside a field result, then updating the field")
            .In("field-page")
            .Steps("select(P2:find(\" end.\"))", "Left", "type(\"x\")", "F9")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) Left steps over the whole field and the x lands before it, surviving F9; (b) Left enters the result, the x is typed into it and F9 discards it.")
            .Morph("PARTIAL");

        yield return Probe("E-05", "Typing at a hyperlink's end, start and inside it")
            .In("hyperlink")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:find(\"site\"):end", "P1:find(\"the site\")", "P1:find(\"site\")")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Notes("Regression row for the editor's hyperlink edges: a character typed at the link's end stays outside it, one at its start goes before it, one inside extends it.")
            .Morph("DONE");

        yield return Probe("E-05b", "Typing over the whole text of a hyperlink")
            .In("hyperlink")
            .Steps("select(P1:find(\"the site\")-find(\"the site\"):end)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the link survives with x as its text; (b) the link is removed and x is plain bold text.")
            .Morph("DONE");

        yield return Probe("E-06", "Typing after a tab and after a manual line break")
            .In("tabs-breaks")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:5", "P2:10")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Notes("P1:5 is after the tab, P2:10 after the line break.")
            .Morph("DONE");

        yield return Probe("E-06b", "Typing after a page break")
            .In("page-break")
            .Steps("select(P2:1)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Notes("The break is the first character of P2, so P2:1 is right after it on the new page.")
            .Morph("DONE");

        yield return Probe("E-06c", "Typing after a non-breaking space, a soft hyphen and hidden text")
            .In("special")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:2", "P1:9", "P1:find(\"hidden\"):end")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("After hidden text (a) the character is hidden too, as the run before it; (b) it is visible.")
            .Morph("DONE");

        yield return Probe("E-07", "Typing at a cell start and into an empty cell")
            .In("{fixture}")
            .Steps("select({at})", "type(\"x\")")
            .Axis("fixture", "table-2x2", "table-2x2-empty")
            .Axis("at", "C(1,1):0", "C(2,2):0")
            .Observing(Observe.Default | Observe.Formatting | Observe.Tables)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-08", "Typing at the end of a commented and a bookmarked span")
            .In("{fixture}")
            .Steps("select(P1:find(\"brown fox\"):end)", "type(\"x\")")
            .Axis("fixture", "comment", "bookmark")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the character falls outside the span, which keeps its end; (b) the span grows to include it.")
            .Morph("UNKNOWN");

        yield return Probe("E-08b", "Typing after a footnote reference")
            .In("footnote")
            .Steps("select(P1:5)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting | Observe.Story)
            .Tracked()
            .Notes("The reference is at P1:4, after 'Text'.")
            .Hypotheses("(a) the character takes the reference run's superscript style; (b) it takes the text before the reference.")
            .Morph("UNKNOWN");

        yield return Probe("E-08c", "Typing after another author's tracked deletion")
            .In("tracked")
            .Steps("select(P1:find(\"gone \"):end)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting | Observe.Revisions)
            .Tracked()
            .Hypotheses("(a) the character is plain and takes the deleted run's formatting; (b) it takes the live text before the deletion; with tracking on it is a new insertion by the probe author either way.")
            .Morph("UNKNOWN");

        yield return Probe("E-09", "Formatting pressed at an insertion point: pending until typed, cleared by a move")
            .In("plain")
            .Steps("select(P1:5)", "{key}")
            .Axis("key", "Ctrl+B", "Ctrl+I", "Ctrl+U", "Ctrl+Shift+>")
            .Branch("type", S("type(\"x\")"))
            .Branch("move-then-type", S("Right", "type(\"y\")"))
            .Branch("click-then-type", S("click(P1:5)", "type(\"z\")"))
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("(a) an arrow or a click clears the pending format, so y and z are plain; (b) the pending format survives a move and is applied to the next character typed anywhere.")
            .Morph("PARTIAL");

        yield return Probe("E-09b", "Formatting pressed between two spaces, where no word is adjacent: pending until typed, cleared by a move")
            .In("words")
            .Steps("select(P1:12)", "{key}")
            .Axis("key", "Ctrl+B", "Ctrl+I", "Ctrl+U", "Ctrl+Shift+>")
            .Branch("type", S("type(\"x\")"))
            .Branch("move-then-type", S("Right", "type(\"y\")"))
            .Branch("click-then-type", S("click(P1:12)", "type(\"z\")"))
            .Branch("space-then-type", S("type(\" x\")"))
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("E-09 put the caret inside a word and Word formatted the word; here the caret is between the two spaces of `beta,  gamma`, so nothing is adjacent. (a) the format is pending: x is bold, y and z are plain; (b) the format survives a move and y is bold too.")
            .Morph("PARTIAL");

        yield return Probe("E-10", "Typing over a selected word")
            .In("words")
            .Steps("select(P1:6-10)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Notes("P1:6-10 is 'beta'.")
            .Morph("DONE");

        yield return Probe("E-10b", "Typing over a selection that spans runs")
            .In("mixed")
            .Steps("select(P1:3-12)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("(a) the character takes the first run of the selection; (b) the run before the selection.")
            .Morph("DONE");

        yield return Probe("E-10c", "Typing over selections that reach a paragraph mark, cross one, or cover everything")
            .In("three-paragraphs")
            .Steps("select({range})", "type(\"x\")")
            .Axis("range", "P1:4-P1:end", "P1:4-P2:0", "P1:4-P2:5", "P1:0-P2:0", "DOC:start-DOC:end")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("A selection ending at P2:0 includes P1's mark, so the paragraphs join; which paragraph's properties the survivor keeps is the merge question of E-41. Over the whole document one paragraph remains with (a) the first paragraph's (b) the last paragraph's properties.")
            .Morph("TODO");

        yield return Probe("E-10d", "Typing over a cell, a row, a column, the whole table, and a range that spans the table")
            .In("table-3x3")
            .Steps("select({range})", "type(\"x\")")
            .Axis("range", "C(1,1):0-C(1,1):end", "R1", "COL1", "T1", "P1:5-P2:3")
            .Observing(Observe.Default | Observe.Formatting | Observe.Tables)
            .Tracked()
            .Hypotheses("Over a cell the text is replaced. Over a row, a column or the table (a) every selected cell is emptied and x goes into the first; (b) the structure is removed. A range from the paragraph before to the paragraph after (a) deletes the table and joins the text; (b) is refused.")
            .Morph("TODO");

        yield return Probe("E-10e", "Typing with an inline picture selected")
            .In("image-inline")
            .Steps("select(IMG1)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Morph("TODO");

        yield return Probe("E-10f", "Typing with a floating picture selected")
            .In("image-floating")
            .Steps("select(SHP1)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Shapes)
            .Tracked()
            .Hypotheses("(a) the picture is replaced by x at its anchor; (b) x is typed at the anchor and the picture stays; (c) nothing is typed.")
            .Morph("TODO");

        yield return Probe("E-11", "Overtype mode: typing replaces characters, and what Backspace then does")
            .In("words")
            .Steps("option(Options.Overtype=true)", "select({at})", "type(\"xy\")", "Backspace")
            .Axis("at", "P1:6", "P1:end")
            .Branch("insert-key", S("Insert", "type(\"z\")"))
            .Toggle("Options.INSKeyForOvertype", "true", "false")
            .Trace()
            .Observing(Observe.Default)
            .Tracked()
            .Hypotheses("At P1:end the mark is not overtyped: xy is inserted. Backspace in Overtype (a) deletes the character before the caret; (b) only moves the caret back. The Insert key leaves Overtype only when INSKeyForOvertype is on.")
            .Morph("TODO");

        yield return Probe("E-11b", "Overtype over a tab")
            .In("tabs-breaks")
            .Steps("option(Options.Overtype=true)", "select(P1:3)", "type(\"xy\")")
            .Trace()
            .Observing(Observe.Default)
            .Tracked()
            .Hypotheses("(a) the tab is overtyped like any character; (b) the tab is kept and y is inserted before it.")
            .Morph("TODO");

        yield return Probe("E-11c", "Overtype into an inline picture")
            .In("image-inline")
            .Steps("option(Options.Overtype=true)", "select(P1:11)", "type(\"xy\")")
            .Trace()
            .Observing(Observe.Default)
            .Tracked()
            .Hypotheses("(a) y overtypes the picture; (b) the picture is kept and y is inserted before it.")
            .Morph("TODO");

        yield return Probe("E-11d", "Overtype at a cell's end")
            .In("table-2x2")
            .Steps("option(Options.Overtype=true)", "select(C(1,1):2)", "type(\"xy\")")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("x overtypes the e of One; y is inserted before the cell mark rather than crossing into the next cell.")
            .Morph("TODO");

        yield return Probe("E-11e", "Overtype into a field")
            .In("field-page")
            .Steps("option(Options.Overtype=true)", "select(P1:4)", "type(\"xy\")")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("x overtypes the space before the PAGE field; y (a) overtypes the whole field as one character; (b) is inserted before it; (c) overtypes the first result character.")
            .Morph("TODO");

        // ---- Enter --------------------------------------------------------------------------------------

        yield return Probe("E-20", "Enter in each kind of paragraph: which half keeps which property")
            .In("kinds")
            .Steps("select({at})", "Enter")
            .Axis("at", "P1:0", "P1:6", "P1:end", "P2:0", "P2:4", "P2:end", "P3:0", "P3:4", "P3:end", "P4:3", "P5:3", "P6:3", "P7:0", "P8:0", "P8:3", "P8:end")
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("P1 Normal, P2 Heading 1, P3 bullet, P4 centred, P5 bordered, P6 left-indented, P7 empty, P8 keep-with-next.")
            .Hypotheses("(a) both halves copy the whole pPr, except that Enter at a heading's end gives the new paragraph the style's next style, and a bordered paragraph split in the middle draws one border round both halves; (b) Enter at a paragraph's start leaves an empty paragraph above with the same properties, so the heading's next-style rule applies only at the end; (c) a split bordered paragraph gets a border per half.")
            .Morph("PARTIAL");

        yield return Probe("E-21", "Enter at a heading's start, middle and end, plain and numbered")
            .In("{fixture}")
            .Steps("select({at})", "Enter")
            .Axis("fixture", "headings", "numbered-heading")
            .Axis("at", "P1:0", "P1:5", "P1:end", "P2:0", "P2:end")
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("At the end the new paragraph is Normal (the heading's next style); in the middle both halves are headings; at the start (a) an empty heading is left above; (b) an empty Normal paragraph is left above. A numbered heading split at the start (a) numbers the empty paragraph; (b) leaves it unnumbered.")
            .Morph("DONE");

        yield return Probe("E-22", "Enter at a paragraph's end carries the mark's run formatting to the new paragraph")
            .In("mixed")
            .Steps("select({at})", "Enter", "type(\"x\")")
            .Axis("at", "P1:end", "P2:end")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Notes("P1's mark is plain while its last run is plain; P2's last run is Arial 10, so the new paragraph's x shows whether the mark or the last run was copied.")
            .Morph("DONE");

        yield return Probe("E-23", "Enter in a list item, then Enter again, Tab, typing, or Backspace")
            .In("{fixture}")
            .Steps("select({at})", "Enter")
            .Axis("fixture", "numbers", "numbers-nested")
            .Axis("at", "P1:0", "P1:2", "P1:end", "P3:end")
            .Branch("enter-again", S("Enter"))
            .Branch("tab", S("Tab"))
            .Branch("type", S("type(\"x\")"))
            .Branch("backspace", S("Backspace"))
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("In numbers-nested P3 is a level 2 item, so P3:end splits at the inner level; in numbers it is the last level 1 item.")
            .Hypotheses("Enter at an item's end makes a new numbered item at the same level; a second Enter in the empty item ends the list (a) leaving a Normal paragraph at the margin (b) leaving the indent; Backspace in the empty item removes the number first; Tab demotes it; Enter at P1:0 (a) numbers the empty item above (b) leaves it unnumbered.")
            .Morph("DONE");

        yield return Probe("E-24", "Enter at cell starts and ends")
            .In("table-2x2")
            .Steps("select({at})", "Enter")
            .Axis("at", "C(1,1):0", "C(1,2):0", "C(2,2):end")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("Enter in a cell adds a paragraph inside the cell, never a row; at the start the text moves down one paragraph.")
            .Morph("PARTIAL");

        yield return Probe("E-24b", "Enter at the first cell of a table that starts the document")
            .In("table-at-start")
            .Steps("select(C(1,1):0)", "Enter")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) a paragraph is added inside the cell, as anywhere else; (b) Word's special case: an empty paragraph is inserted above the table when the caret is at the start of the first cell of a document-opening table.")
            .Morph("PARTIAL");

        yield return Probe("E-24c", "Enter at the start of a cell that holds a nested table")
            .In("table-nested")
            .Steps("select(C(1,2):0)", "Enter")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Notes("Nested tables are not reachable as a Document.Tables index, so the position is the outer cell's start, which is the first cell of the inner table in Word's own addressing.")
            .Hypotheses("(a) a paragraph is added inside the inner table's first cell; (b) an empty paragraph is inserted above the inner table, inside the outer cell.")
            .Morph("PARTIAL");

        yield return Probe("E-25", "Enter with a selection: the selection is deleted and the paragraph split")
            .In("three-paragraphs")
            .Steps("select({range})", "Enter")
            .Axis("range", "P1:4-P1:9", "P1:4-P2:5", "P1:0-P2:0")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("The selection is removed first, then the paragraph splits where it was; over a selection ending at P2:0 the join and the split cancel, so P1 ends up empty and P2 is left whole with (a) P1's (b) P2's properties.")
            .Morph("DONE");

        yield return Probe("E-25b", "Enter with a whole cell's contents selected")
            .In("table-2x2")
            .Steps("select(C(1,1):0-C(1,1):end)", "Enter")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-26", "Enter beside a page break")
            .In("page-break")
            .Steps("select({at})", "Enter")
            .Axis("at", "P2:0", "P2:1")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Notes("P2:0 is before the break character, P2:1 after it.")
            .Hypotheses("Before the break an empty paragraph is left above it on the first page; after it the break stays in the first half and the text moves to a new paragraph.")
            .Morph("DONE");

        yield return Probe("E-26b", "Enter at the end of the paragraph before a table")
            .In("table-2x2")
            .Steps("select(P1:end)", "Enter")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("A new empty paragraph between the text and the table; the table does not move into a cell or gain a row.")
            .Morph("DONE");

        yield return Probe("E-27", "Shift+Enter in text, before the mark, and over a selection")
            .In("words")
            .Steps("select({range})", "Shift+Enter")
            .Axis("range", "P1:6", "P1:end", "P1:6-10")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("A w:br is written at the caret, the paragraph is not split; over a selection the selection is replaced by the break.")
            .Morph("DONE");

        yield return Probe("E-27b", "Shift+Enter inside a numbered item")
            .In("numbers")
            .Steps("select(P1:2)", "Shift+Enter")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("The second line is unnumbered and aligns at the item's text indent.")
            .Morph("DONE");

        yield return Probe("E-27c", "Shift+Enter at a cell's end")
            .In("table-2x2")
            .Steps("select(C(1,1):end)", "Shift+Enter")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-27d", "Shift+Enter inside a heading")
            .In("headings")
            .Steps("select(P1:5)", "Shift+Enter")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("The heading keeps one paragraph of two lines, so the navigation pane and a table of contents show one entry.")
            .Morph("DONE");

        yield return Probe("E-28", "Ctrl+Enter: where the page break lands and which paragraph is new")
            .In("three-paragraphs")
            .Steps("select({range})", "Ctrl+Enter")
            .Axis("range", "P2:4", "P2:end", "P2:0", "P1:4-P1:9")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the paragraph is split and the break is the last character of the first half, so the text after the caret starts the next page in a new paragraph; (b) the break is the first character of the second half; (c) a break alone, in its own paragraph, is inserted between. Over a selection the selection is deleted first.")
            .Morph("TODO");

        yield return Probe("E-28b", "Ctrl+Enter in an empty paragraph")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "Ctrl+Enter")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the empty paragraph becomes the break's paragraph and a new empty paragraph follows on the next page; (b) the break is inserted into the paragraph and the caret stays in it.")
            .Morph("TODO");

        yield return Probe("E-28c", "Ctrl+Enter inside a table cell")
            .In("table-2x2")
            .Steps("select(C(1,1):2)", "Ctrl+Enter")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the table is split in two and the break goes between them; (b) a page break is written inside the cell; (c) nothing happens.")
            .Morph("TODO");

        yield return Probe("E-28d", "Ctrl+Enter inside a numbered item")
            .In("numbers")
            .Steps("select(P2:2)", "Ctrl+Enter")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the item is split and the second half is a new numbered item on the next page; (b) the break is in a paragraph of its own and that paragraph is numbered too.")
            .Morph("TODO");

        yield return Probe("E-29", "Ctrl+Shift+Enter in body text")
            .In("three-paragraphs")
            .Steps("select(P2:4)", "Ctrl+Shift+Enter")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("A column break (w:br type=column) is written; in a one-column section it acts as a page break.")
            .Morph("TODO");

        yield return Probe("E-29b", "Ctrl+Shift+Enter in a table: splitting at row 2, and at row 1")
            .In("table-3x3")
            .Steps("select({at})", "Ctrl+Shift+Enter")
            .Axis("at", "C(2,1):0", "C(1,1):0")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("At row 2 the table splits above the row with an empty paragraph between the halves. At row 1 (a) an empty paragraph is inserted above the table; (b) nothing happens.")
            .Morph("TODO");

        yield return Probe("E-30", "Tab at a paragraph's start and in its middle, with the AutoIndent option on and off")
            .In("three-paragraphs")
            .Steps("select({at})", "Tab")
            .Axis("at", "P2:0", "P1:5")
            .Branch("backspace", S("Backspace"))
            .Toggle("Options.TabIndentKey", "true", "false")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("With TabIndentKey on, Tab at the start sets a first-line indent (0.5in) instead of writing a tab character, and Backspace right after removes the indent; with it off a tab character is written. In the middle a tab character is written either way.")
            .Morph("TODO");

        yield return Probe("E-30b", "Tab in an empty paragraph")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "Tab")
            .Branch("backspace", S("Backspace"))
            .Toggle("Options.TabIndentKey", "true", "false")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the AutoIndent applies in an empty paragraph too; (b) an empty paragraph takes a tab character, since there is no text to indent.")
            .Morph("TODO");

        yield return Probe("E-30c", "Tab at the start of a hanging-indented paragraph")
            .In("indents")
            .Steps("select(P3:0)", "Tab")
            .Branch("backspace", S("Backspace"))
            .Toggle("Options.TabIndentKey", "true", "false")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the first-line indent moves to the left indent, removing the hang; (b) a tab character is written, which the hanging indent's implicit tab stop takes to the left indent.")
            .Morph("TODO");

        yield return Probe("E-30d", "Tab at the start of a wrapped paragraph's second line")
            .In("wrap")
            .Steps("select(P1:60)", "Home", "Tab")
            .Branch("backspace", S("Backspace"))
            .Toggle("Options.TabIndentKey", "true", "false")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) Tab at the start of a wrapped line sets the LEFT indent of the paragraph (the hanging-indent gesture); (b) it writes a tab character.")
            .Morph("TODO");

        yield return Probe("E-31", "Tab and Shift+Tab in a bulleted item: demote, promote, promote again")
            .In("bullets")
            .Steps("select({at})", "Tab", "Shift+Tab", "Shift+Tab")
            .Axis("at", "P1:2", "P2:2")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("Regression row: Tab in the FIRST item moves the whole list rather than demoting it (ListKeyTests); in a later item it demotes the item one level.")
            .Hypotheses("In P1 the first Tab indents every level of the list by 360 twips and the second Shift+Tab ends up (a) back where it started (b) with the list at the margin minus nothing, since Shift+Tab at level 1 is refused. In P2 Tab demotes and the first Shift+Tab promotes, the second (a) does nothing (b) ends the list.")
            .Morph("DONE");

        // ---- Backspace ------------------------------------------------------------------------------------

        yield return Probe("E-40", "Backspace in text, after a single space and between two spaces")
            .In("words")
            .Steps("select({at})", "Backspace")
            .Axis("at", "P1:7", "P1:6", "P1:12")
            .Observing(Observe.Default)
            .Tracked()
            .Notes("P1:7 is inside 'beta', P1:6 after the single space, P1:12 between the two spaces after the comma.")
            .Morph("DONE");

        yield return Probe("E-41", "Backspace at a paragraph's start merges it into the one before: whose properties survive")
            .In("kinds")
            .Steps("select({at})", "Backspace")
            .Axis("at", "P2:0", "P3:0", "P4:0", "P5:0", "P6:0", "P7:0", "P8:0", "P9:0")
            .Branch("type", S("type(\"y\")"))
            .Branch("undo", S("undo"), Node("redo", S("redo")))
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("Each kind appears once as the paragraph before and once as the paragraph after (Heading after Normal, bullet after heading, centred after bullet, bordered after centred, indented after bordered, empty after indented, keep-with-next after empty, plain after keep-with-next). A full 8x8 grid needs a fixture per pair, which one document cannot hold; the eight pairs here cover each kind in each role once.")
            .Hypotheses("(a) the merged paragraph keeps the FIRST paragraph's properties (the mark that survives is the second's, but Word copies the first's pPr onto it); (b) it keeps the second paragraph's properties, since its mark survives; (c) Word keeps the first's unless the first is empty, in which case the second's survive (the empty paragraph case, P8:0).")
            .Morph("PARTIAL");

        yield return Probe("E-42", "Backspace at the start of a level 2 bullet, pressed four times")
            .In("bullets-nested")
            .Steps("select(P2:0)", "Backspace", "Backspace", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("Regression row for the Unindent rule: the item steps out of the list before any text is deleted.")
            .Hypotheses("First press: the bullet is removed, the indent kept; second: the indent is removed; third: the paragraph merges into Alpha; fourth: a character of Alpha is deleted. Or (b) the first press promotes to level 1 before removing the bullet, adding a step.")
            .Morph("DONE");

        yield return Probe("E-42b", "Backspace at the start of a left-indented and a first-line-indented paragraph")
            .In("indents")
            .Steps("select({at})", "Backspace", "Backspace")
            .Axis("at", "P1:0", "P2:0")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("First press removes the indent (left indent to 0; first-line indent to 0), second merges with the paragraph before; for P1 there is no paragraph before, so the second press does nothing.")
            .Morph("DONE");

        yield return Probe("E-43", "Backspace after an inline picture, twice")
            .In("image-inline")
            .Steps("select(P1:13)", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("(a) the picture is deleted by the first press and the second deletes the space before it; (b) the first press selects the picture and the second deletes it.")
            .Morph("PARTIAL");

        yield return Probe("E-44", "Backspace after a PAGE field: selected first, or deleted at once")
            .In("field-page")
            .Steps("select(P1:find(\" of\"))", "Backspace")
            .Branch("again", S("Backspace"))
            .Branch("esc", S("Esc", "type(\"y\")"))
            .Branch("type", S("type(\"y\")"))
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the first press selects the whole field, so Esc keeps it and y replaces it while a second press deletes it; (b) the last character of the result is deleted and the field is marked dirty; (c) the whole field is deleted in one press.")
            .Morph("PARTIAL");

        yield return Probe("E-44b", "Backspace at the start of the paragraph after a table of contents")
            .In("contents")
            .Steps("select(P2:0)", "Backspace")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Notes("The TOC field occupies the first paragraphs of the document; P2 is either the TOC's second entry or the paragraph right after it, which the record shows.")
            .Hypotheses("(a) the whole TOC is selected first; (b) the paragraph merges into the TOC's last entry; (c) nothing happens.")
            .Morph("PARTIAL");

        yield return Probe("E-45", "Backspace through a hyperlink, one character at a time")
            .In("hyperlink")
            .Steps("select(P1:find(\"site\"):end)", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Notes("'the site' is eight characters; presses nine and ten reach the text before the link.")
            .Hypotheses("The link survives while any of its text remains; when the last character goes (a) the empty link is removed with it; (b) an empty HYPERLINK field is left, and the ninth press deletes it as one atom.")
            .Morph("DONE");

        yield return Probe("E-46", "Backspace after a tab and after a line break")
            .In("tabs-breaks")
            .Steps("select({at})", "Backspace")
            .Axis("at", "P1:5", "P2:10")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-46b", "Backspace after a page break")
            .In("page-break")
            .Steps("select(P2:1)", "Backspace")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("The break character is deleted as one character and the text returns to the first page in the same paragraph.")
            .Morph("DONE");

        yield return Probe("E-46c", "Backspace after a non-breaking space and after a soft hyphen")
            .In("special")
            .Steps("select({at})", "Backspace")
            .Axis("at", "P1:2", "P1:9")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-47", "Backspace at a cell's start")
            .In("{fixture}")
            .Steps("select({at})", "Backspace")
            .Axis("fixture", "table-2x2", "table-2x2-empty", "table-at-start")
            .Axis("at", "C(1,1):0", "C(1,2):0")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("Nothing is deleted and the caret stays, in a filled cell, an empty cell, a cell after another cell, and the first cell of a document-opening table alike.")
            .Morph("PARTIAL");

        yield return Probe("E-48", "Backspace at the start of the paragraph after a table")
            .In("{fixture}")
            .Steps("select(P2:0)", "Backspace")
            .Axis("fixture", "table-2x2", "tables-adjacent")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Notes("In tables-adjacent P2 is the empty paragraph between the two tables.")
            .Hypotheses("(a) nothing happens; (b) the paragraph merges into the table's last cell; (c) an empty paragraph between tables is removed, and the tables join.")
            .Morph("PARTIAL");

        yield return Probe("E-48b", "Backspace in the final paragraph mark after a document-ending table")
            .In("table-at-end")
            .Steps("select(DOC:end)", "Backspace")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) nothing; (b) the last row is selected; (c) the caret moves into the last cell.")
            .Morph("PARTIAL");

        yield return Probe("E-49", "Backspace at the document start, in a filled and in an empty first paragraph")
            .In("{fixture}")
            .Steps("select(P1:0)", "Backspace")
            .Axis("fixture", "three-paragraphs", "empty-paragraphs")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("Nothing is deleted; an indent, had there been one, would be removed first.")
            .Morph("DONE");

        yield return Probe("E-50", "Backspace with a selection: inside a paragraph, across two, and the mark alone")
            .In("three-paragraphs")
            .Steps("select({range})", "Backspace")
            .Axis("range", "P1:4-P1:9", "P1:4-P2:5", "P1:end-P2:0")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("The selection is deleted as Delete would; with P1's mark alone selected the paragraphs join and the survivor's properties follow the E-41 rule (P2 is centred, so the difference shows).")
            .Morph("DONE");

        yield return Probe("E-50b", "A selection from the middle of one paragraph to the middle of the next, removed by each key")
            .In("three-paragraphs")
            .Steps("select(P1:4-P2:5)", "{key}")
            .Axis("key", "Backspace", "Delete", "Ctrl+X", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("E-50 found Backspace left two paragraphs (`Firs` and `d bold one.`) where E-10c found typing merged them into one. (a) only typing merges, every deleting key keeps the mark; (b) Backspace alone keeps it; (c) the first run was a settle artefact and every key merges.")
            .Morph("PARTIAL");

        yield return Probe("E-51", "Backspace at the end of a commented and of a bookmarked span")
            .In("{fixture}")
            .Steps("select(P1:find(\"brown fox\"):end)", "Backspace")
            .Axis("fixture", "comment", "bookmark")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("The x of 'fox' is deleted and the span shrinks; the comment and the bookmark survive. (b) The comment's reference mark is a character and is deleted first, taking the comment with it.")
            .Morph("UNKNOWN");

        yield return Probe("E-51b", "Backspace after a footnote reference, twice")
            .In("footnote")
            .Steps("select(P1:5)", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the reference and its note are deleted by the first press and the second deletes the t of Text; (b) the reference is selected first and deleted by the second press.")
            .Morph("UNKNOWN");

        yield return Probe("E-51c", "Backspace right after a rich text content control")
            .In("content-control")
            .Steps("select(P1:find(\" end.\"))", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the caret enters the control and deletes its last character; (b) the whole control is selected first; (c) the control is deleted with its contents in one press.")
            .Morph("UNKNOWN");

        yield return Probe("E-52", "Backspace and Delete across a section break: which sectPr survives")
            .In("section-break")
            .Steps("select({at})", "{key}")
            .Axis("at", "P2:0", "P1:end")
            .Axis("key", "Backspace", "Delete")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Notes("P2:0 Backspace and P1:end Delete are the two ways of joining the sections; the other two cases delete a text character and are controls.")
            .Hypotheses("(a) the joined section takes the SECOND section's sectPr (landscape), since a section's properties live in the mark that ends it; (b) the first section's survive; (c) the break is deleted and the surviving section is whichever paragraph mark survived.")
            .Morph("TODO");

        // ---- Delete -----------------------------------------------------------------------------------------

        yield return Probe("E-60", "Delete at a paragraph's end merges the next into it: whose properties survive")
            .In("kinds")
            .Steps("select({at})", "Delete")
            .Axis("at", "P1:end", "P2:end", "P3:end", "P4:end", "P5:end", "P6:end", "P7:0", "P8:end")
            .Branch("type", S("type(\"y\")"))
            .Branch("undo", S("undo"))
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("The same eight pairs as E-41 reached from the other side: Delete at P1:end joins Heading into Normal, and so on through P8:end joining plain into keep-with-next. P7:0 is the empty paragraph.")
            .Hypotheses("(a) the result is the same paragraph as the Backspace merge of the same pair; (b) Delete keeps the FIRST paragraph's properties where Backspace keeps the second's, since the key names which mark goes.")
            .Morph("PARTIAL");

        yield return Probe("E-60b", "Delete at a paragraph's end where the two paragraphs differ in ONE property")
            .In("merge-pairs")
            .Steps("select({at})", "Delete")
            .Axis("at", "P1:end", "P2:end", "P3:end", "P4:end", "P5:end", "P6:end", "P7:end", "P8:end", "P9:end", "P10:end", "P11:end")
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("Added after the first run: E-60 on `kinds` merged neighbours that differed in several properties at once, and one pair (bullet before centred) kept the first's list AND took the second's centring, which the eight pairs there cannot explain. Here each odd paragraph carries one property and the even one after it is plain, so P(odd):end merges plain INTO the property paragraph and P(even):end merges a property paragraph into a plain one.")
            .Hypotheses("(a) the first paragraph's properties survive every time; (b) the second's survive; (c) alignment comes from the second paragraph and everything else from the first.")
            .Morph("PARTIAL");

        yield return Probe("E-41b", "Backspace at a paragraph's start where the two paragraphs differ in ONE property")
            .In("merge-pairs")
            .Steps("select({at})", "Backspace", "Backspace")
            .Axis("at", "P2:0", "P3:0", "P4:0", "P5:0", "P6:0", "P7:0", "P8:0", "P9:0", "P10:0", "P11:0", "P12:0")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("The Backspace side of E-60b, traced so the strip-first step (E-41: centring, indent and a list number go before any merge) shows apart from the merge; a second press is sent so the merge happens where the first press only stripped.")
            .Hypotheses("(a) once the first press has stripped, the merge keeps the first paragraph's properties as E-41 found; (b) a property that is not stripped first (border, space after, heading) comes out as in E-60b's hypotheses.")
            .Morph("PARTIAL");

        yield return Probe("E-60c", "Delete at a list item's end where the pair differs in indent, alignment or level only")
            .In("merge-lists")
            .Steps("select({at})", "Delete")
            .Axis("at", "P1:end", "P4:end", "P7:end", "P10:end")
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("Added after the first run for the two merges that kept something of the second paragraph (E-60 P3:end, L-20). P1:end merges a bullet indented 72pt into one at the list's own indent; P4:end merges a centred plain paragraph into a bullet; P7:end merges a level-2 bullet into a level-1 one; P10:end merges a bullet into a centred plain paragraph.")
            .Hypotheses("(a) the survivor keeps the first paragraph's properties throughout, so E-60 P3:end and L-20 were confounds of the kinds and numbers-nested fixtures; (b) when the first paragraph is a list item it keeps its list and level but takes the second paragraph's direct paragraph formatting (indent, alignment), which is what both open cases showed; (c) the second paragraph's indent and alignment survive whether or not a list is involved, and the single-property grid missed it because its plain paragraphs carried none.")
            .Morph("PARTIAL");

        yield return Probe("E-41c", "Backspace at the start of the second paragraph of the same list pairs, three presses")
            .In("merge-lists")
            .Steps("select({at})", "Backspace", "Backspace", "Backspace")
            .Axis("at", "P2:0", "P5:0", "P8:0", "P11:0")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("The Backspace side of E-60c, traced so the strip-first steps (bullet off, indent back, centring off) show apart from the merge.")
            .Hypotheses("The cascade strips the second paragraph's own bullet, indent and centring first; the merge, when it comes, keeps the first paragraph's properties as E-41b found, so the second's indent or alignment never reaches the survivor from this side.")
            .Morph("PARTIAL");

        yield return Probe("E-61", "Delete before a table and at cell ends")
            .In("table-2x2")
            .Steps("select({at})", "Delete")
            .Axis("at", "P1:end", "C(1,1):end", "C(2,2):end")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("Delete at the end of the paragraph before the table (a) does nothing; (b) selects the table; (c) merges the paragraph into the first cell. At a cell end nothing is deleted, in the last cell too.")
            .Morph("PARTIAL");

        yield return Probe("E-61b", "Delete before an inline picture")
            .In("image-inline")
            .Steps("select(P1:12)", "Delete")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("(a) the picture is deleted at once; (b) it is selected first.")
            .Morph("PARTIAL");

        yield return Probe("E-61c", "Delete before a PAGE field")
            .In("field-page")
            .Steps("select(P1:5)", "Delete", "Delete")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the first press selects the field and the second deletes it; (b) the field is deleted in one press; (c) the first result character is deleted.")
            .Morph("PARTIAL");

        yield return Probe("E-61d", "Delete at the end of a cell's first paragraph")
            .In("table-multi")
            .Steps("select(C(1,1):5)", "Delete")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Notes("Cell (1,1) holds 'first' and 'second' as two paragraphs; C(1,1):5 is the end of 'first'.")
            .Hypotheses("The two paragraphs of the cell join, as in body text.")
            .Morph("PARTIAL");

        yield return Probe("E-62", "Delete at the document's end")
            .In("three-paragraphs")
            .Steps("select(DOC:end)", "Delete")
            .Observing(Observe.Default)
            .Tracked()
            .Hypotheses("Nothing happens.")
            .Morph("DONE");

        yield return Probe("E-63", "Delete before a tab, a footnote reference and a hyperlink")
            .In("{fixture}")
            .Steps("select(P1:4)", "Delete")
            .Axis("fixture", "tabs-breaks", "footnote", "hyperlink")
            .Observing(Observe.Default | Observe.Formatting | Observe.Story | Observe.Markup)
            .Tracked()
            .Notes("P1:4 is before the tab in tabs-breaks, before the reference in footnote and at the field start of the link in hyperlink.")
            .Hypotheses("The tab is deleted as a character. The reference (a) goes with its note in one press (b) is selected first. Before the link (a) the whole link is selected; (b) the first display character is deleted and the link stays; (c) the field is deleted with its text.")
            .Morph("PARTIAL");

        yield return Probe("E-63b", "Delete before a manual line break")
            .In("tabs-breaks")
            .Steps("select(P2:9)", "Delete")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-63c", "Delete before a page break")
            .In("page-break")
            .Steps("select(P2:0)", "Delete")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-63d", "Delete before a non-breaking space")
            .In("special")
            .Steps("select(P1:1)", "Delete")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-63e", "Delete before a rich text content control")
            .In("content-control")
            .Steps("select(P1:6)", "Delete", "Delete")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the caret enters the control and deletes its first character; (b) the control is selected whole first; (c) the control is removed with its text.")
            .Morph("UNKNOWN");

        yield return Probe("E-64", "Delete with a selection partly inside a table")
            .In("table-2x2")
            .Steps("select({range})", "Delete")
            .Axis("range", "P1:2-C(1,2):1", "C(2,1):1-P2:3")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the text outside the table is deleted and the selected cells are emptied, the rows kept; (b) the selected rows are deleted with the text; (c) the key is refused.")
            .Morph("TODO");

        // ---- Word deletions ---------------------------------------------------------------------------------

        yield return Probe("E-70", "Ctrl+Backspace word rules, three presses from each start")
            .In("words")
            .Steps("select({at})", "Ctrl+Backspace", "Ctrl+Backspace", "Ctrl+Backspace")
            .Axis("at", "P1:10", "P1:6", "P1:11", "P1:8", "P1:0")
            .Trace()
            .Observing(Observe.Default)
            .Tracked()
            .Notes("P1:10 is after 'beta', P1:6 after the space that follows 'alpha', P1:11 after the comma, P1:8 inside 'beta', P1:0 the paragraph start.")
            .Hypotheses("A press deletes back to the start of the word the caret is in or follows; trailing spaces belong to the word before them; punctuation is a word of its own; at P1:0 nothing is deleted.")
            .Morph("TODO");

        yield return Probe("E-70b", "Ctrl+Backspace at the start of a list item")
            .In("numbers")
            .Steps("select(P2:0)", "Ctrl+Backspace", "Ctrl+Backspace", "Ctrl+Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("(a) the first press removes the number as Backspace would; (b) it joins the item to the one before; (c) it deletes the previous item's last word across the mark.")
            .Morph("TODO");

        yield return Probe("E-70c", "Ctrl+Backspace at a cell's start")
            .In("table-2x2")
            .Steps("select(C(1,1):0)", "Ctrl+Backspace", "Ctrl+Backspace", "Ctrl+Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("Nothing is deleted.")
            .Morph("TODO");

        yield return Probe("E-70d", "Ctrl+Backspace after a manual line break")
            .In("tabs-breaks")
            .Steps("select(P2:10)", "Ctrl+Backspace", "Ctrl+Backspace", "Ctrl+Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the break alone goes first, as its own word; (b) the break and the word before it go together.")
            .Morph("TODO");

        yield return Probe("E-71", "Ctrl+Delete word rules, three presses from each start")
            .In("words")
            .Steps("select({at})", "Ctrl+Delete", "Ctrl+Delete", "Ctrl+Delete")
            .Axis("at", "P1:0", "P1:10", "P1:end")
            .Trace()
            .Observing(Observe.Default)
            .Tracked()
            .Notes("P1:10 is before the comma that follows 'beta'.")
            .Hypotheses("A press deletes to the start of the next word, taking the spaces after the current one; from before the comma the comma and its two spaces go as one word; at P1:end (a) nothing (b) the mark goes and the paragraphs join.")
            .Morph("TODO");

        yield return Probe("E-71b", "Ctrl+Delete at a cell's end")
            .In("table-2x2")
            .Steps("select(C(1,1):end)", "Ctrl+Delete", "Ctrl+Delete", "Ctrl+Delete")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Morph("TODO");

        yield return Probe("E-71c", "Ctrl+Delete before an inline picture")
            .In("image-inline")
            .Steps("select(P1:12)", "Ctrl+Delete", "Ctrl+Delete", "Ctrl+Delete")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("(a) the picture is one word and goes alone; (b) the picture and the word after it go together.")
            .Morph("TODO");

        yield return Probe("E-71d", "Ctrl+Delete before a PAGE field")
            .In("field-page")
            .Steps("select(P1:5)", "Ctrl+Delete", "Ctrl+Delete", "Ctrl+Delete")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the field is one word; (b) it is selected first; (c) the field and ' of' go together.")
            .Morph("TODO");

        // ---- Character formatting keys ------------------------------------------------------------------------

        yield return Probe("E-80", "Shift+F3 cycles the case of a selection")
            .In("sentences")
            .Steps("select(P1:0-P1:12)", "Shift+F3", "Shift+F3", "Shift+F3", "Shift+F3")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Notes("'One is first' starts in sentence case.")
            .Hypotheses("(a) lower, UPPER, Title Case, back to sentence case; (b) lower, UPPER, sentence case, three steps; the selection is kept throughout and the change is to the text, not to w:caps.")
            .Morph("TODO");

        yield return Probe("E-80b", "Shift+F3 with the caret inside a word and at a word's end")
            .In("words")
            .Steps("select({at})", "Shift+F3", "Shift+F3", "Shift+F3")
            .Axis("at", "P1:8", "P1:10")
            .Trace()
            .Observing(Observe.Default)
            .Tracked()
            .Hypotheses("With no selection the word under the caret is changed, and at a word's end (a) the word before (b) nothing, since the caret is before punctuation.")
            .Morph("TODO");

        yield return Probe("E-81", "Ctrl+Space over a bold and an italic run")
            .In("mixed")
            .Steps("select(P1:0-P1:11)", "Ctrl+Space")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Direct formatting is removed and the runs return to the style's look; a character style would survive.")
            .Morph("PARTIAL");

        yield return Probe("E-81b", "Ctrl+Space over runs with a font and size change")
            .In("runs")
            .Steps("select(P1:0-P1:end)", "Ctrl+Space")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Bold, italic, underline, Arial and 20pt all go; the paragraph mark's formatting is untouched since it is not in the selection.")
            .Morph("PARTIAL");

        yield return Probe("E-81c", "Ctrl+Space over a hyperlink's text")
            .In("hyperlink")
            .Steps("select(P1:find(\"the site\")-find(\"the site\"):end)", "Ctrl+Space")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the bold goes but the Hyperlink character style stays, so the link keeps its blue underline; (b) the Hyperlink style is removed too.")
            .Morph("PARTIAL");

        yield return Probe("E-81d", "Ctrl+Space at an insertion point inside a bold run, then typing")
            .In("mixed")
            .Steps("select(P1:3)", "Ctrl+Space", "type(\"x\")")
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("(a) the word under the caret is reset to plain; (b) only the next typed character is plain, the run untouched.")
            .Morph("PARTIAL");

        yield return Probe("E-82", "Ctrl+Q resets paragraph formatting in each kind")
            .In("kinds")
            .Steps("select({at})", "Ctrl+Q")
            .Axis("at", "P4:3", "P5:3", "P6:3", "P3:3", "P8:3")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("Centring, the border, the indent and keep-with-next go, the style stays; a bullet (a) is removed with the indent, since numbering is paragraph formatting; (b) stays, with its indents reset to the list's.")
            .Morph("TODO");

        yield return Probe("E-82b", "Ctrl+Q on a paragraph with space after")
            .In("indents")
            .Steps("select(P4:3)", "Ctrl+Q")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("TODO");

        yield return Probe("E-83", "Ctrl+Shift+N returns a heading, a bullet and a centred paragraph to Normal")
            .In("kinds")
            .Steps("select({at})", "Ctrl+Shift+N")
            .Axis("at", "P2:3", "P3:3", "P4:3")
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("The style becomes Normal; direct formatting such as the centring (a) is kept (b) is removed with the style; the bullet (a) stays, as direct numbering (b) goes.")
            .Morph("TODO");

        yield return Probe("E-83b", "Ctrl+Shift+N on a numbered item")
            .In("numbers")
            .Steps("select(P1:1)", "Ctrl+Shift+N")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the item leaves the list and the others renumber; (b) the number stays, since the list is direct formatting on a paragraph already in List Paragraph style.")
            .Morph("TODO");

        yield return Probe("E-84", "A formatting key over a selection that is partly formatted, pressed twice")
            .In("mixed")
            .Steps("select(P1:0-P1:12)", "{key}", "{key}")
            .Axis("key", "Ctrl+B", "Ctrl+I", "Ctrl+U")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Hypotheses("The first press applies the property to the whole selection when the selection's first character lacks it, or removes it when the first character has it; the second press undoes the first, so the mixed selection does NOT return to mixed.")
            .Morph("DONE");

        yield return Probe("E-84b", "Ctrl+U twice over runs with one underlined word")
            .In("runs")
            .Steps("select(P1:0-P1:end)", "Ctrl+U", "Ctrl+U")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-85", "Double underline, words-only underline, all caps and small caps interplay")
            .In("plain")
            .Steps("select(P1:4-P1:15)")
            .Branch("double-then-single", S("Ctrl+Shift+D", "Ctrl+U", "Ctrl+U"))
            .Branch("words-only", S("Ctrl+Shift+W", "Ctrl+Shift+W"))
            .Branch("caps-then-small", S("Ctrl+Shift+A", "Ctrl+Shift+K", "Ctrl+Shift+A"))
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+U after a double underline (a) removes the underline, since any underline counts as on; (b) changes it to single. Small caps and all caps (a) exclude each other, the later key clearing the earlier; (b) both are written and caps wins in display.")
            .Morph("TODO");

        yield return Probe("E-86", "Subscript and superscript exclude each other")
            .In("plain")
            .Steps("select(P1:4-P1:9)", "Ctrl+=", "Ctrl+Shift+=", "Ctrl+Shift+=")
            .Branch("reverse", S("Ctrl+Shift+=", "Ctrl+="))
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+= writes vertAlign subscript; Ctrl+Shift+= replaces it with superscript rather than toggling it off; the third press removes superscript, leaving baseline.")
            .Morph("DONE");

        yield return Probe("E-87", "Ctrl+] and Ctrl+[ step the size by one point from each start")
            .In("sizes")
            .Steps("select({p}:0-{p}:end)", "Ctrl+]", "Ctrl+]", "Ctrl+[")
            .Axis("p", "P1", "P7", "P8", "P21", "P23")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Notes("P1 is 1pt, P7 10.5, P8 11, P21 72, P23 100.")
            .Hypotheses("One point per press, 10.5 to 11.5 (half points kept), with floors at 1 and ceilings at 1638.")
            .Morph("DONE");

        yield return Probe("E-87b", "Ctrl+] over a selection of three sizes")
            .In("sizes")
            .Steps("select(P7:0-P9:end)", "Ctrl+]")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Each run grows by one point independently (10.5, 11, 12 to 11.5, 12, 13) rather than every run taking the first's size.")
            .Morph("DONE");

        yield return Probe("E-88", "Ctrl+Shift+> and Ctrl+Shift+< walk the size ladder from every starting size")
            .In("sizes")
            .Steps("select({p}:0-{p}:end)", "Ctrl+Shift+>", "Ctrl+Shift+>", "Ctrl+Shift+<", "Ctrl+Shift+<", "Ctrl+Shift+<")
            .Axis("p", "P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8", "P9", "P10", "P11", "P12", "P13", "P14", "P15", "P16", "P17", "P18", "P19", "P20", "P21", "P22", "P23")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Notes("The starting sizes are 1, 5, 7, 8, 9, 10, 10.5, 11, 12, 13, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72, 80, 100 in paragraph order; the ladder in the Font Size box is 8 9 10 11 12 14 16 18 20 22 24 26 28 36 48 72.")
            .Hypotheses("(a) a size on the ladder moves to the next rung, and one off the ladder (1, 5, 7, 10.5, 13, 80, 100) snaps to the nearest rung in the direction pressed; (b) below 8 and above 72 the step is one point; (c) above 72 the step is 10.")
            .Morph("TODO");

        // ---- Paragraph formatting keys ----------------------------------------------------------------------

        yield return Probe("E-89", "The alignment keys pressed twice: toggle back to left, or to the style's alignment")
            .In("{fixture}")
            .Steps("select({at})", "{key}", "{key}")
            .Axis("fixture", "kinds", "headings")
            .Axis("at", "P1:3", "P4:3")
            .Axis("key", "Ctrl+E", "Ctrl+R", "Ctrl+J", "Ctrl+L")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Notes("kinds P1 is Normal (left) and P4 centred by direct formatting; headings P1 is Heading 1 and P4 Normal.")
            .Hypotheses("(a) a second press of the same key returns to LEFT alignment regardless of the style; (b) it returns to the style's alignment; Ctrl+L on a left paragraph (a) does nothing (b) writes jc=left explicitly; Ctrl+E on the centred paragraph goes to left at the first press.")
            .Morph("DONE");

        yield return Probe("E-89b", "The alignment keys over a selection spanning three paragraphs")
            .In("three-paragraphs")
            .Steps("select(P1:2-P3:2)", "{key}", "{key}")
            .Axis("key", "Ctrl+E", "Ctrl+R", "Ctrl+J", "Ctrl+L")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("All three paragraphs take the alignment; the second press (a) sets left on all three; (b) is decided by the first paragraph's state, as Ctrl+B is.")
            .Morph("DONE");

        yield return Probe("E-89c", "The alignment keys in a cell")
            .In("table-2x2")
            .Steps("select(C(1,1):1)", "{key}", "{key}")
            .Axis("key", "Ctrl+E", "Ctrl+R", "Ctrl+J", "Ctrl+L")
            .Trace()
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-90", "Line spacing and space-before keys")
            .In("plain")
            .Steps("select(P1:3)")
            .Branch("double", S("Ctrl+2"))
            .Branch("one-and-half", S("Ctrl+5"))
            .Branch("double-then-single", S("Ctrl+2", "Ctrl+1"))
            .Branch("space-before", S("Ctrl+0", "Ctrl+0"))
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+2 writes line=480 auto, Ctrl+5 line=360, Ctrl+1 line=240; Ctrl+0 adds 12pt before and a second Ctrl+0 (a) removes it (b) adds nothing more. Where the result equals the style's value the attribute (a) is still written (b) is omitted.")
            .Morph("PARTIAL");

        yield return Probe("E-90b", "Line spacing and space-before keys on a paragraph that has space after")
            .In("indents")
            .Steps("select(P4:3)")
            .Branch("double", S("Ctrl+2"))
            .Branch("one-and-half", S("Ctrl+5"))
            .Branch("double-then-single", S("Ctrl+2", "Ctrl+1"))
            .Branch("space-before", S("Ctrl+0", "Ctrl+0"))
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("PARTIAL");

        yield return Probe("E-91", "Ctrl+M and Ctrl+Shift+M step the left indent, in Normal and indented paragraphs")
            .In("kinds")
            .Steps("select({at})", "Ctrl+M", "Ctrl+M", "Ctrl+Shift+M", "Ctrl+Shift+M", "Ctrl+Shift+M")
            .Axis("at", "P1:3", "P6:3")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Notes("P6 starts at a 36pt left indent.")
            .Hypotheses("Each press moves the left indent to the next 0.5in tab stop, so 36pt (0.5in) goes to 1in rather than 1in plus 36pt; Ctrl+Shift+M below zero (a) stops at 0 (b) does nothing when already at 0.")
            .Morph("TODO");

        yield return Probe("E-91b", "Ctrl+M and Ctrl+Shift+M on a left-indented and a hanging-indented paragraph")
            .In("indents")
            .Steps("select({at})", "Ctrl+M", "Ctrl+M", "Ctrl+Shift+M", "Ctrl+Shift+M", "Ctrl+Shift+M")
            .Axis("at", "P1:3", "P3:3")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("The hanging indent moves with the left indent, so the first line stays 36pt to its left throughout; (b) only the left indent moves and the hang shrinks.")
            .Morph("TODO");

        yield return Probe("E-91c", "Ctrl+M and Ctrl+Shift+M on the first and on a later numbered item")
            .In("numbers")
            .Steps("select({at})", "Ctrl+M", "Ctrl+M", "Ctrl+Shift+M", "Ctrl+Shift+M", "Ctrl+Shift+M")
            .Axis("at", "P1:1", "P2:1")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) Ctrl+M in a list item demotes it, as Tab does, and Ctrl+Shift+M promotes; (b) the keys move the indent only and the level is unchanged; in the first item (a) the whole list moves (b) the item alone.")
            .Morph("TODO");

        yield return Probe("E-91d", "Ctrl+M and Ctrl+Shift+M on a heading")
            .In("headings")
            .Steps("select(P1:3)", "Ctrl+M", "Ctrl+M", "Ctrl+Shift+M", "Ctrl+Shift+M", "Ctrl+Shift+M")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("TODO");

        yield return Probe("E-92", "Ctrl+T and Ctrl+Shift+T step the hanging indent on a plain paragraph")
            .In("plain")
            .Steps("select(P1:3)", "Ctrl+T", "Ctrl+T", "Ctrl+Shift+T", "Ctrl+Shift+T")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+T moves the left indent to the next 0.5in stop and sets the first line back to where it was, so the hang grows by 0.5in per press; Ctrl+Shift+T reverses it and the second reverse (a) stops at 0 (b) does nothing.")
            .Morph("TODO");

        yield return Probe("E-92b", "Ctrl+T and Ctrl+Shift+T on a paragraph that already hangs")
            .In("indents")
            .Steps("select(P3:3)", "Ctrl+T", "Ctrl+T", "Ctrl+Shift+T", "Ctrl+Shift+T")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Morph("TODO");

        yield return Probe("E-92c", "Ctrl+T and Ctrl+Shift+T on a numbered item")
            .In("numbers")
            .Steps("select(P1:1)", "Ctrl+T", "Ctrl+T", "Ctrl+Shift+T", "Ctrl+Shift+T")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the item's own indents are overridden and the list definition untouched; (b) the list level's indents are changed for every item.")
            .Morph("TODO");

        yield return Probe("E-93", "Ctrl+Shift+L applies the List Bullet style, and again removes it")
            .In("{fixture}")
            .Steps("select(P1:1)", "Ctrl+Shift+L", "Ctrl+Shift+L")
            .Axis("fixture", "plain", "numbers", "bullets")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("On plain text the first press applies List Bullet (the style, not direct numbering) and the second (a) returns to Normal (b) stays. On a numbered item the first press (a) replaces the number with a bullet (b) adds List Bullet style over the direct number. On a bullet (a) the first press removes it (b) applies the style over the direct bullet.")
            .Morph("DONE");

        yield return Probe("E-94", "Ctrl+Alt+1, 2 and 3 apply Heading 1, 2 and 3")
            .In("{fixture}")
            .Steps("select(P1:1)", "{key}")
            .Axis("fixture", "plain", "numbers")
            .Axis("key", "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+3")
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("On a numbered item the heading style (a) keeps the number, now a numbered heading; (b) drops it.")
            .Morph("DONE");

        yield return Probe("E-94b", "Ctrl+Alt+1, 2 and 3 on a paragraph that is already Heading 3")
            .In("headings")
            .Steps("select(P3:1)", "{key}")
            .Axis("key", "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+3")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+Alt+3 on a Heading 3 (a) leaves it (b) returns it to Normal as a toggle.")
            .Morph("DONE");

        yield return Probe("E-94c", "Ctrl+Alt+1, 2 and 3 in a table cell")
            .In("table-2x2")
            .Steps("select(C(1,1):1)", "{key}")
            .Axis("key", "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+3")
            .Observing(Observe.Default | Observe.Formatting | Observe.Tables | Observe.Markup)
            .Tracked()
            .Morph("DONE");

        yield return Probe("E-95", "Alt+Shift+Right and Alt+Shift+Left demote and promote headings")
            .In("headings")
            .Steps("select({at})", "Alt+Shift+Right", "Alt+Shift+Left")
            .Axis("at", "P1:3", "P2:3")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("Heading 1 to Heading 2 and back; Alt+Shift+Left on a Heading 1 (a) does nothing (b) stays, and a Normal paragraph is a body level that Alt+Shift+Left (a) raises to Heading 1 (b) to the lowest heading in use.")
            .Morph("TODO");

        yield return Probe("E-95b", "Alt+Shift+Right and Alt+Shift+Left on a Normal paragraph")
            .In("plain")
            .Steps("select(P1:3)", "Alt+Shift+Right", "Alt+Shift+Left")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) Alt+Shift+Right on body text does nothing and Alt+Shift+Left makes it Heading 1; (b) Alt+Shift+Right indents it as Ctrl+M does.")
            .Morph("TODO");

        yield return Probe("E-95c", "Alt+Shift+Right and Alt+Shift+Left in a bulleted list, flat and nested")
            .In("{fixture}")
            .Steps("select({at})", "Alt+Shift+Right", "Alt+Shift+Left")
            .Axis("fixture", "bullets", "bullets-nested")
            .Axis("at", "P1:1", "P2:1")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("In a list the keys change the level as Tab and Shift+Tab do, including Tab's first-item rule; (b) they act as the outline keys and leave the list alone.")
            .Morph("TODO");

        yield return Probe("E-95d", "Alt+Shift+Left or Alt+Shift+Right alone, on each heading level and on the body paragraph under them")
            .In("headings")
            .Steps("select({at})", "{key}")
            .Axis("at", "P1:3", "P2:3", "P3:3", "P4:3")
            .Axis("key", "Alt+Shift+Left", "Alt+Shift+Right")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Notes("E-95 to E-95c sent both keys in one case, so only the pair's net effect was seen; here each case sends one key.")
            .Hypotheses("Alt+Shift+Right demotes Heading 1 to Heading 2 and Heading 2 to Heading 3, and on Heading 3 (a) goes to Heading 4 (b) does nothing; Alt+Shift+Left promotes a heading one level and on Heading 1 does nothing. On the Normal paragraph Alt+Shift+Left makes it (a) Heading 1 (b) Heading 3, the lowest level in use above it; Alt+Shift+Right on body text (a) does nothing (b) makes it Heading 1.")
            .Morph("TODO");

        yield return Probe("E-95e", "Alt+Shift+Left or Alt+Shift+Right alone on a lone Normal paragraph")
            .In("plain")
            .Steps("select(P1:3)", "{key}")
            .Axis("key", "Alt+Shift+Left", "Alt+Shift+Right")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("Alt+Shift+Left makes the paragraph Heading 1 (the net effect E-95b saw); Alt+Shift+Right alone (a) does nothing (b) makes it Heading 1 too, so E-95b's result was Alt+Shift+Right's.")
            .Morph("TODO");

        yield return Probe("E-96", "Alt+Shift+Down and Alt+Shift+Up move a paragraph, and a two-paragraph selection")
            .In("three-paragraphs")
            .Steps("select({range})", "Alt+Shift+Down", "Alt+Shift+Up")
            .Axis("range", "P2:3", "P1:2-P2:2")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("The paragraph swaps places with the one after it, keeping its properties and the caret; a selection moves every paragraph it touches as a block; at the last paragraph Alt+Shift+Down does nothing.")
            .Morph("TODO");

        yield return Probe("E-96b", "Alt+Shift+Down and Alt+Shift+Up on a numbered item")
            .In("numbers")
            .Steps("select(P2:1)", "Alt+Shift+Down", "Alt+Shift+Up")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("The items swap and renumber in place.")
            .Morph("TODO");

        yield return Probe("E-96c", "Alt+Shift+Down and Alt+Shift+Up move table rows")
            .In("table-3x3")
            .Steps("select({at})", "Alt+Shift+Down", "Alt+Shift+Up")
            .Axis("at", "C(1,1):0", "C(2,1):0", "C(3,1):0")
            .Trace()
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("The row moves down one; from the last row (a) nothing (b) the row leaves the table into the paragraph after it.")
            .Morph("TODO");

        yield return Probe("E-96d", "Alt+Shift+Down on the paragraph before a table")
            .In("table-2x2")
            .Steps("select(P1:3)", "Alt+Shift+Down", "Alt+Shift+Up")
            .Trace()
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the paragraph jumps over the whole table; (b) it moves into the first row as a cell paragraph; (c) nothing happens.")
            .Morph("TODO");

        yield return Probe("E-97", "Ctrl+Shift+C copies formatting; Ctrl+Shift+V pastes it onto a selection and onto an insertion point")
            .In("mixed")
            .Steps("select(P1:0-P1:6)", "Ctrl+Shift+C")
            .Branch("onto-selection", S("select(P1:12-P1:17)", "Ctrl+Shift+V"))
            .Branch("onto-ip", S("select(P2:3)", "Ctrl+Shift+V", "type(\"x\")"))
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Onto a selection 'plain' becomes bold; onto an insertion point (a) the word under the caret takes the format; (b) only the next typed character does; (c) the whole paragraph's pPr is pasted, since the copied selection carried no mark.")
            .Morph("TODO");

        // ---- Special characters, fields, notes, comments ----------------------------------------------------

        yield return Probe("E-98", "Keys that insert a special character")
            .In("plain")
            .Steps("select(P1:4)", "{key}")
            .Axis("key", "Ctrl+Shift+Space", "Ctrl+Shift+-", "Ctrl+-", "Alt+Ctrl+Minus", "Ctrl+NumMinus")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("A non-breaking space (U+00A0), a non-breaking hyphen (U+2011 as w:noBreakHyphen), an optional hyphen (w:softHyphen), an em dash and an en dash, in that order.")
            .Morph("TODO");

        yield return Probe("E-99", "Keys that insert a symbol")
            .In("plain")
            .Steps("select(P1:4)", "{key}")
            .Axis("key", "Alt+Ctrl+C", "Alt+Ctrl+R", "Alt+Ctrl+T", "Alt+Ctrl+E", "Alt+Ctrl+.")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Copyright, registered, trademark, euro and ellipsis, each a plain character in the run's own font; (b) the euro is written as a w:sym.")
            .Morph("TODO");

        yield return Probe("E-100", "Keys that insert a field")
            .In("plain")
            .Steps("select(P1:4)", "{key}")
            .Axis("key", "Alt+Shift+D", "Alt+Shift+P", "Alt+Shift+T", "Ctrl+Alt+L")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("DATE, PAGE, TIME and LISTNUM fields, each as a complex field (fldChar begin, instrText, separate, result, end) rather than a fldSimple.")
            .Morph("TODO");

        yield return Probe("E-101", "Ctrl+Shift+E turns tracking on mid-typing")
            .In("plain")
            .Steps("select(P1:4)", "type(\"ab\")", "Ctrl+Shift+E", "type(\"cd\")")
            .Observing(Observe.Default | Observe.Revisions | Observe.Markup)
            .Hypotheses("ab is plain text and cd is a tracked insertion; the two are in separate runs.")
            .Morph("TODO");

        yield return Probe("E-102", "Ctrl+F9 inserts field braces; typing the code and F9 evaluates it")
            .In("plain")
            .Steps("select(P1:4)", "Ctrl+F9", "type(\"PAGE\")", "F9")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+F9 leaves the caret inside the braces with codes shown for that field; F9 evaluates it to 1 and the field shows its result.")
            .Morph("TODO");

        yield return Probe("E-102b", "Shift+F9 and Alt+F9 toggle field codes for one field and for all")
            .In("field-page")
            .Steps("select(P1:5)")
            .Branch("one-field", S("Shift+F9", "Shift+F9"))
            .Branch("all-fields", S("Alt+F9", "Alt+F9"))
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("Shift+F9 at a field's start (a) toggles that field (b) the field before the caret, since the caret is on the boundary; Alt+F9 toggles the view, not the document, so the markup is unchanged and the undo list too.")
            .Morph("TODO");

        yield return Probe("E-102c", "Ctrl+Shift+F9 unlinks a PAGE field to its result text")
            .In("field-page")
            .Steps("select(P1:4-P1:8)", "Ctrl+Shift+F9")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("The field becomes the plain text 1, keeping the result's formatting.")
            .Morph("TODO");

        yield return Probe("E-102d", "Ctrl+Shift+F9 unlinks a hyperlink")
            .In("hyperlink")
            .Steps("select(P1:find(\"the site\")-find(\"the site\"):end)", "Ctrl+Shift+F9")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the text stays with the Hyperlink character style, so it still looks like a link; (b) the style is removed too.")
            .Morph("TODO");

        yield return Probe("E-103", "Ctrl+Alt+F and Ctrl+Alt+D insert a footnote or an endnote at a caret and over a selection")
            .In("plain")
            .Steps("select({range})", "{key}", "type(\"note\")")
            .Axis("range", "P1:4", "P1:4-P1:9")
            .Axis("key", "Ctrl+Alt+F", "Ctrl+Alt+D")
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Hypotheses("The reference goes at the caret and the selection moves to the note's story, where 'note' is typed; over a selection (a) the reference goes at the selection's END and the text is kept (b) the selection is replaced.")
            .Morph("PARTIAL");

        yield return Probe("E-103b", "Ctrl+Alt+F and Ctrl+Alt+D in a table cell")
            .In("table-2x2")
            .Steps("select(C(1,1):1)", "{key}", "type(\"note\")")
            .Axis("key", "Ctrl+Alt+F", "Ctrl+Alt+D")
            .Observing(Observe.Default | Observe.Story | Observe.Tables | Observe.Markup)
            .Tracked()
            .Morph("PARTIAL");

        yield return Probe("E-103c", "Ctrl+Alt+F and Ctrl+Alt+D in a header")
            .In("header-footer")
            .Steps("seek(header)", "{key}", "type(\"note\")")
            .Axis("key", "Ctrl+Alt+F", "Ctrl+Alt+D")
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Hypotheses("Notes are not allowed in a header: (a) the key is refused and 'note' is typed into the header; (b) a message is shown and dismissed.")
            .Morph("PARTIAL");

        yield return Probe("E-104", "Ctrl+Alt+M inserts a comment: at an insertion point mid-word, over a selection and at a paragraph's end")
            .In("plain")
            .Steps("select({range})", "Ctrl+Alt+M", "type(\"c\")", "Esc")
            .Axis("range", "P1:6", "P1:4-P1:9", "P1:end")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Hypotheses("At an insertion point inside a word the comment's scope is the word; at a paragraph's end (a) the word before (b) an empty scope at the caret; over a selection the scope is the selection. Esc returns the caret to the body at the scope's end.")
            .Morph("PARTIAL");

        yield return Probe("E-104b", "Ctrl+Alt+M in an empty paragraph")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "Ctrl+Alt+M", "type(\"c\")", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the scope is the paragraph mark; (b) the scope is empty; (c) the key is refused.")
            .Morph("PARTIAL");

        yield return Probe("E-105", "The Insert key, with and without the option that makes it toggle Overtype")
            .In("plain")
            .Steps("select(P1:4)", "Insert", "type(\"xy\")")
            .Toggle("Options.INSKeyForOvertype", "true", "false")
            .Observing(Observe.Default)
            .Tracked()
            .Hypotheses("With the option on xy overtypes 'qu'; with it off Insert (a) does nothing and xy is inserted; (b) pastes the clipboard (Word's legacy Insert-to-paste option is off by default).")
            .Morph("TODO");

        yield return Probe("E-107", "Typing at a bookmark's start and end")
            .In("bookmark")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:find(\"brown fox\")", "P1:find(\"brown fox\"):end")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("At the start the character goes before the bookmarkStart, so the bookmark is unchanged; at the end (a) the character is outside (b) the bookmark grows to include it.")
            .Morph("UNKNOWN");

        yield return Probe("E-107b", "Typing at a content control's start and end")
            .In("content-control")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:6", "P1:18")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the character goes inside the control at both ends, since a collapsed caret on the boundary is inside; (b) outside at both; (c) inside at the start, outside at the end.")
            .Morph("UNKNOWN");

        yield return Probe("E-108", "Typing over a rich text content control's whole contents")
            .In("content-control")
            .Steps("select(P1:6-P1:18)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the control survives with x as its contents; (b) the control is removed with its contents; (c) the placeholder returns.")
            .Morph("TODO");

        yield return Probe("E-108b", "Space on a checkbox content control")
            .In("content-control")
            .Steps("select(P3:7)", "Space")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Notes("P3 reads 'Check: <checkbox> end.' and P3:7 is before the checkbox glyph.")
            .Hypotheses("(a) the checkbox toggles to checked (w14:checked 1 and the glyph changes); (b) a space is typed before it.")
            .Morph("TODO");

        yield return Probe("E-108c", "Enter inside a plain text content control")
            .In("content-control")
            .Steps("select(P2:7)", "Enter")
            .Observing(Observe.Default | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) refused, since a plain text control allows no paragraphs unless multiline; (b) the paragraph splits with the control's halves in both.")
            .Morph("TODO");

        yield return Probe("E-109", "Paragraph keys over a selection spanning three paragraphs")
            .In("three-paragraphs")
            .Steps("select(P1:2-P3:2)", "{key}")
            .Axis("key", "Ctrl+E", "Ctrl+Alt+1", "Ctrl+M", "Ctrl+Shift+L")
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("Every paragraph the selection touches takes the change, including the third, which is entered by two characters only; the selection is kept.")
            .Morph("PARTIAL");

        yield return Probe("E-110", "Ctrl+Shift+Q, Ctrl+Shift+H and Ctrl+Shift+Z on a selection")
            .In("plain")
            .Steps("select(P1:4-P1:9)", "{key}")
            .Axis("key", "Ctrl+Shift+Q", "Ctrl+Shift+H", "Ctrl+Shift+Z")
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+Shift+Q applies the Symbol font; Ctrl+Shift+H hides the text (w:vanish); Ctrl+Shift+Z on unformatted text writes nothing.")
            .Morph("TODO");
    }
}
