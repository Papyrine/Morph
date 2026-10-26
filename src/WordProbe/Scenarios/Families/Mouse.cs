using static Define;

// M — the mouse: where a click puts the caret, what double, triple, Ctrl and Shift clicks select, how a
// drag grows, the selection bar, the table and picture hot spots, and drag and drop. Every row is real
// input sent to a visible Word window, with the selection, text and undo list read back over COM.
//
// Offsets are 0-based in the paragraph's text. In `words` ("alpha beta,  gamma-delta 3.14 don't (eps)
// 1,000 10:30 end." at 24pt): beta 6, the comma 10, the two spaces 11 and 12, gamma 13, the hyphen 18,
// 3.14 at 25, the apostrophe 33, the parentheses 36 and 40, 1,000 at 42, 10:30 at 48, the last period 57.
// In `image-inline` ("Text before  after end.") the picture is offset 12, between the two spaces. In
// `tabs-breaks` the tab is P1:4 and the manual line break P2:9. In `footnote` the reference is P1:4.
static class MouseFamily
{
    public static FamilyDefinition Definition { get; } = new("mouse", "Mouse",
        "Click placement, double and triple click, Ctrl and Shift clicks, drags, the selection bar, table and picture targets, drag and drop.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        // ----- single clicks -----

        yield return Probe("M-01", "Click places the caret at the nearer glyph edge")
            .In("words")
            .Steps("click({at}, side={side})")
            .Axis("at", "P1:find(\"beta\")", "P1:15", "P1:find(\",\")", "P1:5", "P1:12", "P1:57")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("P1:15 is the middle of gamma, P1:5 the single space after alpha, P1:12 the second of the two spaces, P1:57 the final period. The 24pt face makes a glyph half many pixels wide.")
            .Hypotheses("The left half of a glyph places the caret before it and the right half after it, for letters, punctuation and spaces alike.")
            .Morph("PARTIAL");

        yield return Probe("M-01b", "Click at glyph halves in a centred paragraph and at a paragraph's end")
            .In("three-paragraphs")
            .Steps("click({at}, side={side})")
            .Axis("at", "P1:0", "P2:find(\"bold\")", "P2:end")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text | Observe.Formatting)
            .Hypotheses("The half rule holds at a paragraph's first glyph and inside a bold run; P2:end, side=right lands before the mark, not in the next paragraph.")
            .Morph("PARTIAL");

        yield return Probe("M-01c", "Click on either half of an inline picture")
            .In("image-inline")
            .Steps("click(P1:12, side={side})")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes)
            .Hypotheses("(a) either half selects the picture; (b) the left half places an IP before it and the right half after it.")
            .Morph("PARTIAL");

        yield return Probe("M-01d", "Click on either half of a tab's expanse")
            .In("tabs-breaks")
            .Steps("click(P1:4, side={side})")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The tab is one glyph: its left half places the caret before it, its right half after it.")
            .Morph("PARTIAL");

        yield return Probe("M-01e", "Click inside a hyperlink's display text")
            .In("hyperlink")
            .Steps("click(P1:find(\"site\"), side={side})")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text | Observe.Formatting)
            .Hypotheses("An IP is placed at the glyph edge inside the link; the link is not followed with Ctrl+click set to open.")
            .Morph("PARTIAL");

        yield return Probe("M-01f", "Click inside a field result")
            .In("field-page")
            .Steps("click(P1:find(\"1\"), side={side})")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("find(\"1\") is the PAGE field's result; the field codes hold no digit.")
            .Hypotheses("(a) the IP lands at the result glyph's edge; (b) the whole field is selected; (c) the IP snaps to the field's start or end.")
            .Morph("PARTIAL");

        yield return Probe("M-01g", "Click on either half of a footnote reference")
            .In("footnote")
            .Steps("click(P1:4, side={side})")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The reference is one glyph with the half rule; neither half enters the note.")
            .Morph("PARTIAL");

        yield return Probe("M-01h", "Click on either half of a non-breaking space")
            .In("special")
            .Steps("click(P1:1, side={side})")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The non-breaking space behaves as any space: left half before, right half after.")
            .Morph("PARTIAL");

        yield return Probe("M-02", "Click past the end of a wrapped line")
            .In("wrap")
            .Steps("click(P1:find(\"{word}\"):end, side={side})")
            .Axis("word", "zeta", "eta")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("At 24pt the first line ends near zeta or eta; the word that ends the line is the one whose trailing space hangs in the margin, the other is a mid-line control. The DSL has no target in the margin itself, so the right half of the wrap space stands for it.")
            .Hypotheses("(a) the caret lands before the wrap space; (b) after it, which draws at the next line's start; (c) at the last glyph.")
            .Morph("UNKNOWN");

        yield return Probe("M-02b", "Click past the last glyph of an unwrapped line")
            .In("{fixture}")
            .Steps("click({at}, side=right)")
            .Axis("fixture", "three-paragraphs", "tabs-breaks")
            .Axis("at", "P1:end", "P2:end")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The caret lands before the paragraph mark, never in the next paragraph.")
            .Morph("UNKNOWN");

        yield return Probe("M-02c", "Click past the end of a line that ends in a manual line break")
            .In("tabs-breaks")
            .Steps("click({at}, side={side})")
            .Axis("at", "P2:8", "P2:9")
            .Axis("side", "left", "right")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("P2 is 'Line one ' then the break: P2:8 is the trailing space, P2:9 the break glyph.")
            .Hypotheses("(a) the caret lands before the break; (b) the right half of the break glyph lands after it, at the next line's start.")
            .Morph("UNKNOWN");

        yield return Probe("M-03", "Click left of a line's first glyph, inside the text area")
            .In("{fixture}")
            .Steps("click(P1:0, side=left)")
            .Axis("fixture", "wrap", "numbers", "bullets", "three-paragraphs")
            .Observing(Observe.Selection | Observe.Text | Observe.Lists)
            .Hypotheses("The caret lands at the line start; in a list the number is not selected.")
            .Morph("DONE");

        yield return Probe("M-03b", "Click left of the first glyph of a bulleted, centred and indented paragraph")
            .In("kinds")
            .Steps("click({at}, side=left)")
            .Axis("at", "P3:0", "P4:0", "P6:0")
            .Observing(Observe.Selection | Observe.Text | Observe.Lists)
            .Hypotheses("The caret lands at the paragraph start in each kind.")
            .Morph("DONE");

        yield return Probe("M-04", "Click and double-click below the last line (Click and Type)")
            .In("plain")
            .Steps("click(below P1)")
            .Toggle("Options.AllowClickAndTypeMouse", "true", "false")
            .Branch("dbl-left", S("dblclick(below P1, side=left)"))
            .Branch("dbl-centre", S("dblclick(below P1, side=centre)"))
            .Branch("dbl-right", S("dblclick(below P1, side=right)"))
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Formatting)
            .Hypotheses("(a) with the option on, a double-click inserts empty paragraphs down to the point and sets the alignment by zone: left, centred, right; (b) with it off the double-click selects the last word of the last line; (c) a single click lands at the end of the last line either way.")
            .Morph("TODO");

        yield return Probe("M-04b", "Click and double-click below the last item of a list")
            .In("numbers")
            .Steps("click(below P3)")
            .Toggle("Options.AllowClickAndTypeMouse", "true", "false")
            .Branch("dbl-centre", S("dblclick(below P3, side=centre)"), Node("undo", S("undo")))
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Lists)
            .Hypotheses("(a) the paragraphs Click and Type inserts continue the list; (b) they are Normal paragraphs; (c) Click and Type does not act under a list.")
            .Morph("TODO");

        yield return Probe("M-05", "Click on a paragraph mark and on an end-of-cell mark")
            .In("table-2x2")
            .Steps("click({at})")
            .Axis("at", "P1:mark", "P2:mark", "C(1,1):mark", "C(2,2):mark")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Notes("Formatting marks are hidden in the harness view, so this is a click at the mark's position; P2 is the document's last paragraph.")
            .Hypotheses("An IP before the mark; a click on a cell mark places the IP at the cell's end rather than selecting the cell.")
            .Morph("DONE");

        yield return Probe("M-06", "Click inside a tab's expanse and at its edges")
            .In("tabs-breaks")
            .Steps("click({at})")
            .Axis("at", "P1:3, side=right", "P1:4, side=left", "P1:4, side=right", "P1:5, side=left")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The tab's expanse splits at its middle: the left part places the caret before the tab, the right part after it; the glyphs beside it keep their own halves.")
            .Morph("PARTIAL");

        yield return Probe("M-07", "Cell padding versus the cell's left edge")
            .In("table-2x2")
            .Steps("{target}")
            .Axis("target", "click(C(1,1):0, side=left)", "click(cellLeftEdge C(1,1))", "click(C(2,2):0, side=left)", "click(cellLeftEdge C(2,2))")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Notes("The 0.5in cell margin makes the padding a wide target: a click in it is a click in the cell, a click on the edge selects the cell.")
            .Hypotheses("The text-start click is an IP; the edge click selects the whole cell (Cells 1).")
            .Morph("TODO");

        // ----- double clicks -----

        yield return Probe("M-10", "Double-click a word, by what follows it")
            .In("words")
            .Steps("dblclick(P1:{at})")
            .Axis("at", "2", "7", "15", "21", "38", "55")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("alpha (one space follows), beta (a comma), gamma (a hyphen), delta (a space), eps (a closing parenthesis), end (a period then the mark).")
            .Hypotheses("The word plus its trailing spaces; a word before punctuation is selected without the punctuation; a hyphen splits gamma-delta into words.")
            .Morph("TODO");

        yield return Probe("M-10b", "Double-click a word in each of three plain paragraphs")
            .In("three-paragraphs")
            .Steps("dblclick({at})")
            .Axis("at", "P1:2", "P1:17", "P2:find(\"bold\")", "P3:11")
            .Observing(Observe.Selection | Observe.Text | Observe.Formatting)
            .Notes("P1:17 is 'here' before the period, P3:11 'last' before the final period of the document.")
            .Hypotheses("The word and its trailing space; a last word before a period takes neither the period nor the mark.")
            .Morph("TODO");

        yield return Probe("M-10c", "Double-click a word before a tab, a line break and two spaces")
            .In("tabs-breaks")
            .Steps("dblclick({at})")
            .Axis("at", "P1:2", "P1:7", "P2:6", "P3:1", "P3:7")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("P1:2 is Left before the tab, P1:7 right after it, P2:6 'one' before the space and line break, P3:1 Two before two spaces, P3:7 spaces after them.")
            .Hypotheses("(a) the tab is taken as the word's trailing whitespace; (b) it is not. Both spaces after Two are taken; the line break is not.")
            .Morph("TODO");

        yield return Probe("M-10d", "Double-click the words either side of an inline picture")
            .In("image-inline")
            .Steps("dblclick({at})")
            .Axis("at", "P1:7", "P1:16")
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes)
            .Hypotheses("'before ' stops at the picture; 'after ' starts after it; the picture is never part of a word.")
            .Morph("TODO");

        yield return Probe("M-11", "Double-click a punctuation glyph")
            .In("words")
            .Steps("dblclick({at})")
            .Axis("at", "P1:find(\",\")", "P1:find(\"-\")", "P1:find(\"'\")", "P1:find(\"(\")", "P1:find(\")\")", "P1:57")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The punctuation alone, with its trailing spaces where there are any; the apostrophe inside don't selects the whole word; the hyphen is its own word.")
            .Morph("TODO");

        yield return Probe("M-12", "Double-click on an inter-word space")
            .In("words")
            .Steps("dblclick({at})")
            .Axis("at", "P1:5", "P1:11", "P1:12")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("(a) the spaces alone are selected; (b) the previous word with its spaces; (c) the next word.")
            .Morph("TODO");

        yield return Probe("M-13", "Double-click a number token with inner punctuation")
            .In("words")
            .Steps("dblclick({at})")
            .Axis("at", "P1:27", "P1:44", "P1:50", "P1:51")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("P1:27 is inside 3.14, P1:44 inside 1,000, P1:50 the colon of 10:30 and P1:51 its 3.")
            .Hypotheses("3.14 and 1,000 are one word each; 10:30 (a) one word (b) splits at the colon.")
            .Morph("TODO");

        yield return Probe("M-14", "Double-click exactly at a word boundary")
            .In("words")
            .Steps("dblclick({at})")
            .Axis("at", "P1:5, side=right", "P1:6, side=left", "P1:17, side=right", "P1:18, side=left")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("The boundary between alpha's space and beta, and between gamma and its hyphen, approached from either side.")
            .Hypotheses("The glyph under the pointer decides: the space's right half selects alpha with its space, beta's left half selects beta.")
            .Morph("TODO");

        yield return Probe("M-15", "Double-click the last word of the document and its period")
            .In("three-paragraphs")
            .Steps("dblclick({at})")
            .Axis("at", "P3:13", "P3:14", "P3:mark")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("The word excludes the period and the mark; the period alone excludes the mark; a double-click at the mark (a) selects the mark (b) selects the period (c) leaves an IP.")
            .Morph("TODO");

        yield return Probe("M-15b", "Double-click in an empty paragraph")
            .In("empty-paragraphs")
            .Steps("dblclick({at})")
            .Axis("at", "P2:0", "P2:mark", "P3:0")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("(a) the paragraph mark is selected; (b) an IP is left in the empty paragraph.")
            .Morph("TODO");

        yield return Probe("M-16", "Double-click an inline picture")
            .In("image-inline")
            .Steps("dblclick(IMG1)")
            .Observing(Observe.Selection | Observe.Text | Observe.Undo | Observe.Shapes)
            .Notes("Current Word shows the Picture Format tab rather than a dialog; the record shows whether anything opened.")
            .Hypotheses("The picture is selected and the document and undo list are unchanged.")
            .Morph("TODO");

        yield return Probe("M-17", "Double-click inside a hyperlink")
            .In("hyperlink")
            .Steps("dblclick({at})")
            .Axis("at", "P1:find(\"the\")", "P1:find(\"site\")")
            .Observing(Observe.Selection | Observe.Text | Observe.Formatting)
            .Hypotheses("(a) the word under the pointer only; (b) the whole link's display text.")
            .Morph("TODO");

        yield return Probe("M-17b", "Double-click inside a field result")
            .In("field-page")
            .Steps("dblclick({at})")
            .Axis("at", "P1:find(\"1\")", "P2:find(\"/20\")")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("find(\"/20\") lands in the DATE result, past the code's own d/MM/yyyy switch.")
            .Hypotheses("(a) the whole field is selected as one word; (b) the word of the result under the pointer, in the date the day or the month alone.")
            .Morph("TODO");

        yield return Probe("M-18", "Double-click a cell's only word")
            .In("table-2x2")
            .Steps("dblclick({at})")
            .Axis("at", "C(1,1):1", "C(2,2):2")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("(a) the word alone, the cell mark excluded; (b) the whole cell.")
            .Morph("TODO");

        // ----- triple clicks -----

        yield return Probe("M-20", "Triple-click a paragraph")
            .In("{fixture}")
            .Steps("tripleclick({at})")
            .Axis("fixture", "three-paragraphs", "numbers", "kinds", "table-2x2")
            .Axis("at", "P1:2", "P2:1")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Lists)
            .Notes("kinds P2 is a heading, numbers P2 a list item, table-2x2 P2 the last paragraph of the document.")
            .Hypotheses("The whole paragraph with its mark; a list number is not in Text; the document's last paragraph is selected with its final mark.")
            .Morph("TODO");

        yield return Probe("M-20b", "Triple-click a bulleted, an empty and the last paragraph")
            .In("kinds")
            .Steps("tripleclick({at})")
            .Axis("at", "P3:2", "P7:0", "P9:4")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Lists)
            .Hypotheses("An empty paragraph's triple-click selects its mark alone; the last paragraph is selected with the final mark.")
            .Morph("TODO");

        yield return Probe("M-21", "Triple-click in a cell")
            .In("{fixture}")
            .Steps("tripleclick({at})")
            .Axis("fixture", "table-2x2", "table-multi")
            .Axis("at", "C(1,1):1", "C(2,2):1")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Notes("table-multi's (1,1) holds two paragraphs and its (2,2) a two-item bullet list.")
            .Hypotheses("(a) the clicked paragraph; (b) the whole cell; (c) in a two-paragraph cell only the clicked paragraph, in a one-paragraph cell the cell.")
            .Morph("TODO");

        yield return Probe("M-21b", "Triple-click the second paragraph of a cell")
            .In("table-multi")
            .Steps("tripleclick({at})")
            .Axis("at", "C(1,1):7", "C(2,2):10")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables | Observe.Lists)
            .Hypotheses("The second paragraph alone, its end being the cell mark, which is (a) included (b) excluded.")
            .Morph("TODO");

        yield return Probe("M-22", "Triple-click in a text box")
            .In("text-box")
            .Steps("tripleclick(TB1:P1:2)")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Story | Observe.Shapes)
            .Hypotheses("The first paragraph of the text box story, not the box itself.")
            .Morph("TODO");

        yield return Probe("M-22b", "Triple-click in a footnote")
            .In("footnote")
            .Steps("seek(footnotes)", "tripleclick(FN1:P1:2)")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Story)
            .Hypotheses("The note's paragraph, with its reference mark (a) included (b) excluded.")
            .Morph("TODO");

        yield return Probe("M-22c", "Triple-click in a header")
            .In("header-footer")
            .Steps("seek(header)", "tripleclick(HDR:P1:2)")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Story)
            .Hypotheses("The header paragraph with its mark; the body is untouched.")
            .Morph("TODO");

        // ----- Ctrl and Shift clicks -----

        yield return Probe("M-24", "Ctrl+click selects a sentence")
            .In("sentences")
            .Steps("ctrl+click({at})")
            .Axis("at", "P1:find(\"first\")", "P1:find(\"Two\")", "P1:find(\"Three\")", "P1:find(\"four\")", "P1:find(\"Five\")", "P2:find(\"ends\")")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("Terminators: a period, a question mark, the e.g. abbreviation, a period before the paragraph mark.")
            .Hypotheses("The sentence with its trailing space; a question mark terminates; e.g. (a) ends a sentence (b) is skipped as an abbreviation; the last sentence of a paragraph takes no mark.")
            .Morph("TODO");

        yield return Probe("M-25", "Ctrl+click with an existing selection")
            .In("sentences")
            .Steps("dblclick(P1:find(\"One\"))", "ctrl+click(P1:find(\"Two\"))")
            .Trace()
            .Branch("third", S("ctrl+click(P1:find(\"Five\"))"))
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("(a) the sentence replaces the word; (b) a second range is added (a multi-range selection); (c) the selection extends to cover both.")
            .Morph("TODO");

        yield return Probe("M-26", "Click on a hyperlink, with Ctrl+click set to open and not")
            .In("hyperlink")
            .Steps("click({at})")
            .Axis("at", "P1:find(\"See\")", "P1:find(\"site\")")
            .Toggle("Options.CtrlClickHyperlinkToOpen", "true", "false")
            .Observing(Observe.Selection | Observe.Text | Observe.Formatting)
            .Notes("With the option off a plain click on the link follows it and a browser may open; Ctrl+click is not sent for the same reason. The See click is the control off the link.")
            .Hypotheses("With the option on an IP is placed in the link text; with it off the link is followed and the selection stays where it was.")
            .Morph("TODO");

        yield return Probe("M-27", "Shift+click extends from the anchor, within and across paragraphs")
            .In("three-paragraphs")
            .Steps("click(P2:5)", "shift+click(P2:12)", "shift+click(P1:3)", "shift+click(P3:4)")
            .Trace()
            .Toggle("Options.AutoWordSelection", "true", "false")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("The anchor at P2:5 stays through every Shift+click; a selection crossing a paragraph includes its mark; no word snapping on a Shift+click.")
            .Morph("TODO");

        yield return Probe("M-28", "Shift+click after a double-click")
            .In("words")
            .Steps("dblclick(P1:find(\"beta\"))", "shift+click(P1:27)")
            .Trace()
            .Toggle("Options.AutoWordSelection", "true", "false")
            .Branch("leftward", S("shift+click(P1:2)"))
            .Observing(Observe.Selection | Observe.Text)
            .Notes("P1:27 is inside 3.14, P1:2 inside alpha.")
            .Hypotheses("(a) the extension snaps to the end of 3.14 while automatic word selection is on; (b) it extends by character; leftward the anchor is beta's end and the start snaps to alpha's start or not.")
            .Morph("TODO");

        yield return Probe("M-29", "Shift+click from the body into a table")
            .In("table-3x3")
            .Steps("click({from})", "shift+click(C(2,2):1)")
            .Axis("from", "P1:2", "P2:3")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("(a) whole rows 1 and 2 (from P2: rows 2 and 3) join the text; (b) the cells up to (2,2); (c) a plain text run ending inside b2.")
            .Morph("TODO");

        yield return Probe("M-30", "Shift+click from a cell into the body")
            .In("table-3x3")
            .Steps("click(C(2,2):1)", "shift+click({to})")
            .Axis("to", "P2:3", "P1:2")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("(a) the rows from the anchor's row to the table edge plus the text; (b) the anchor cell's text run to the click.")
            .Morph("TODO");

        yield return Probe("M-31", "Shift+click from cell to cell: same row, same column, diagonal, same cell")
            .In("table-3x3")
            .Steps("click(C(1,1):1)", "shift+click({to})")
            .Axis("to", "C(1,3):1", "C(3,1):1", "C(3,3):1", "C(1,1):end")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("Across cells the selection is the rectangle of cells spanned (Cells); inside one cell it is a text run.")
            .Morph("TODO");

        yield return Probe("M-31b", "Shift+click across merged cells")
            .In("table-merged")
            .Steps("{first}", "shift+click(C(2,3):1)")
            .Axis("first", "click(C(1,1):1)", "click(C(3,2):1)", "click(C(1,2):1)")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Notes("(1,1) spans two columns and (2,3) two rows; (3,2) is the cell left of the vertical merge's lower half.")
            .Hypotheses("(a) the rectangle grows to hold every merged cell whole; (b) merged cells are counted by their top-left position only.")
            .Morph("TODO");

        yield return Probe("M-32", "Shift+click between a text box and the body")
            .In("text-box")
            .Steps("{first}", "{second}")
            .Axis("first", "click(TB1:P1:2)", "click(P2:2)")
            .Axis("second", "shift+click(P2:2)", "shift+click(TB1:P1:2)")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Story | Observe.Shapes)
            .Hypotheses("A Shift+click into another story (a) is refused and the selection stays; (b) places a new IP there; (c) selects the box.")
            .Morph("TODO");

        // ----- drags -----

        yield return Probe("M-33", "Alt+drag block selection, then typing, Delete and copy")
            .In("long-paragraph")
            .Steps("alt+drag(P2:2→{to})")
            .Axis("to", "P2:110", "P2:195")
            .Branch("type", S("type(\"x\")"))
            .Branch("delete", S("Delete"))
            .Branch("copy", S("Ctrl+C", "click(P3:0)", "Ctrl+V"))
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Revisions)
            .Notes("P2 wraps to three lines at the default size; P2:110 is on its second line and P2:195 on its third, so the block spans two or three lines.")
            .Hypotheses("Type Block; typing (a) replaces each line's segment with the character (b) replaces only the first and deletes the rest; Delete removes every segment; the paste puts one segment per paragraph.")
            .Morph("TODO");

        yield return Probe("M-34", "A drag selection snaps to words")
            .In("words")
            .Steps("drag({path})")
            .Axis("path", "P1:7→P1:8", "P1:7→P1:15", "P1:15→P1:7", "P1:2→P1:21")
            .Toggle("Options.AutoWordSelection", "true", "false")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("Within beta; from beta into gamma; the same leftwards; from alpha to delta.")
            .Hypotheses("With automatic word selection a drag that leaves its first word snaps both ends to word boundaries and takes the trailing space; within one word it stays by character; off, every drag is by character.")
            .Morph("TODO");

        yield return Probe("M-34b", "A drag across a line wrap inside one paragraph")
            .In("wrapped-title")
            .Steps("drag({path})")
            .Axis("path", "P1:2→P1:11", "P1:0→P1:13", "P1:2→P1:end", "P1:0→P1:end", "P1:11→P1:2", "P1:end→P1:2")
            .Toggle("Options.AutoWordSelection", "true", "false")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("MEETING is the first line and MINUTES the second; P1:11 is inside MINUTES, P1:13 one glyph short of its end.")
            .Hypotheses("The release on the second line sets the active end there, as on one line: with automatic word selection both ends snap to whole words, so MEETING MINUTES is selected whole whether the release is inside MINUTES or past it; the selection never falls back to the first line.")
            .Morph("TODO");

        yield return Probe("M-35","A drag across a paragraph boundary and to a paragraph's end")
            .In("three-paragraphs")
            .Steps("drag({path})")
            .Axis("path", "P1:3→P2:4", "P1:0→P1:end", "P2:4→P1:3", "P1:3→P3:end")
            .Toggle("Options.SmartParaSelection", "true", "false")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("A drag crossing a mark includes it; a drag from a paragraph's start to its end (a) takes the mark with smart paragraph selection (b) stops before it.")
            .Morph("PARTIAL");

        yield return Probe("M-36", "A drag into, within and out of a table")
            .In("table-3x3")
            .Steps("drag({path})")
            .Axis("path", "P1:2→C(2,2):1", "C(1,1):1→C(2,2):1", "C(1,1):0→C(1,1):end", "C(3,3):1→P2:2", "C(1,1):1→C(1,3):1", "C(2,2):1→C(1,1):1")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("Into the table: whole rows; cell to cell: the rectangle of cells; inside a cell: text; out of the table: the rows to the table's end plus the paragraph.")
            .Morph("TODO");

        // ----- the selection bar -----

        yield return Probe("M-37", "Down the selection bar: click then Shift+click lower")
            .In("three-paragraphs")
            .Steps("click(selectionbar P1)", "shift+click(selectionbar P2)", "shift+click(selectionbar P3)")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Notes("The DSL's drag takes text positions, not hot spots, so a drag down the bar is stood in for by a click and Shift+clicks on the bar.")
            .Hypotheses("Each Shift+click extends the line selection down by whole lines with their marks.")
            .Morph("TODO");

        yield return Probe("M-40", "Selection bar click beside a line")
            .In("{fixture}")
            .Steps("click(selectionbar {line})")
            .Axis("fixture", "three-paragraphs", "numbers", "image-floating", "empty-paragraphs")
            .Axis("line", "P1", "P2")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Lists | Observe.Shapes)
            .Notes("numbers P1 is a list item; image-floating P2 carries the picture's anchor; empty-paragraphs P2 is empty.")
            .Hypotheses("(a) the line with its mark; (b) beside a list item the number is not in Text; beside the anchor paragraph the picture is not selected; an empty line is its mark.")
            .Morph("TODO");

        yield return Probe("M-40b", "Selection bar click beside the first line of a wrapped paragraph")
            .In("wrap")
            .Steps("click(selectionbar P1)")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("(a) the first line only, ending at the wrap space, no mark; (b) the whole paragraph.")
            .Morph("TODO");

        yield return Probe("M-40c", "Selection bar beside a table row")
            .In("table-2x2")
            .Steps("{step}")
            .Axis("step", "click(selectionbar C(1,1):0)", "click(selectionbar C(2,2):0)", "click(selectionbar P2)", "dblclick(selectionbar C(1,1):0)")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("(a) a click beside a row selects the row (Type Row); (b) the line in the first cell; a double-click beside a row (a) the row (b) the table.")
            .Morph("TODO");

        yield return Probe("M-41", "Selection bar double-click")
            .In("{fixture}")
            .Steps("dblclick(selectionbar P1)")
            .Axis("fixture", "three-paragraphs", "wrap", "table-2x2")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("The whole paragraph with its mark, every line of a wrapped one.")
            .Morph("TODO");

        yield return Probe("M-42", "Selection bar triple-click and Ctrl+click")
            .In("table-2x2")
            .Steps("{step}")
            .Axis("step", "tripleclick(selectionbar P1)", "ctrl+click(selectionbar P1)", "tripleclick(selectionbar C(1,1):0)", "ctrl+click(selectionbar C(2,1):0)")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("Both select the whole document, table included, from beside a paragraph and from beside a row alike.")
            .Morph("TODO");

        yield return Probe("M-43", "Shift+click into text from a selection bar anchor")
            .In("three-paragraphs")
            .Steps("click(selectionbar P2)", "shift+click(P3:4)", "shift+click(P1:3)")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("(a) the line anchor holds and the extension is by whole lines; (b) the extension is by character from the line's start.")
            .Morph("TODO");

        yield return Probe("M-44", "Shift+Down and Shift+Up from a selection bar anchor")
            .In("three-paragraphs")
            .Steps("click(selectionbar P2)", "{key}", "{key}")
            .Axis("key", "Shift+Down", "Shift+Up")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("(a) the selection grows by whole lines; (b) the keys extend from the line's start by the usual Down and Up positions.")
            .Morph("TODO");

        // ----- table hot spots -----

        yield return Probe("M-50", "Cell edge click, then Delete, Backspace, typing, Tab and Enter")
            .In("table-3x3")
            .Steps("click(cellLeftEdge C(2,2))")
            .Branch("delete", S("Delete"), Node("undo", S("undo")))
            .Branch("backspace", S("Backspace"), Node("undo", S("undo")))
            .Branch("type", S("type(\"x\")"), Node("undo", S("undo")))
            .Branch("tab", S("Tab"), Node("undo", S("undo")))
            .Branch("enter", S("Enter"), Node("undo", S("undo")))
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Tables | Observe.Revisions)
            .Hypotheses("The cell is selected (Cells 1); Delete clears its contents and leaves the cell; Backspace (a) the same (b) deletes the cell and asks how to shift; typing replaces the contents; Tab moves to the next cell; Enter (a) adds a paragraph in the cell (b) replaces the contents.")
            .Morph("TODO");

        yield return Probe("M-51", "Column top click")
            .In("{fixture}")
            .Steps("click(columnTop {cell})")
            .Axis("fixture", "table-3x3", "table-merged")
            .Axis("cell", "C(1,1)", "C(1,2)")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("Type Column; in the merged table a column under a merged cell (a) takes the merged cell whole (b) takes the cells of one grid column.")
            .Morph("TODO");

        yield return Probe("M-52", "Row bar click")
            .In("{fixture}")
            .Steps("click(rowBar {row})")
            .Axis("fixture", "table-3x3", "table-merged")
            .Axis("row", "R1", "R2")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("Type Row with every cell of the row; a row holding the upper half of a vertical merge takes the merged cell.")
            .Morph("TODO");

        yield return Probe("M-53", "Table move handle click")
            .In("table-2x2")
            .Steps("click(tableHandle T1)")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Notes("A drag of the handle is not expressible: drag takes text positions. Only the click is recorded.")
            .Hypotheses("(a) the whole table is selected (Type Table); (b) only the handle is shown and the selection stays.")
            .Morph("TODO");

        yield return Probe("M-54", "End-of-row mark click, then Delete, Backspace and typing")
            .In("table-2x2")
            .Steps("click(C(1,2):mark, side=right)")
            .Branch("delete", S("Delete"))
            .Branch("backspace", S("Backspace"))
            .Branch("type", S("type(\"x\")"))
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Tables)
            .Notes("The end-of-row mark sits right of the last cell; the click is at the right half of the last cell's mark position, the nearest the DSL comes to it.")
            .Hypotheses("(a) the whole row is selected; (b) the mark alone; (c) an IP at the cell's end. Delete on a row (a) clears the cells (b) nothing; typing (a) replaces the row's contents (b) nothing.")
            .Morph("TODO");

        // ----- pictures and shapes -----

        yield return Probe("M-60", "Click an inline picture, then Shift+click")
            .In("image-inline")
            .Steps("click(IMG1)", "shift+click({to})")
            .Axis("to", "P1:end", "P1:0", "P2:3")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes)
            .Hypotheses("The click selects the picture (InlineShapes 1); the Shift+click turns it into a text selection from the picture's edge to the click, with the picture in it.")
            .Morph("DONE");

        yield return Probe("M-61", "Click a floating picture, a shape and a text box")
            .In("{fixture}")
            .Steps("click(SHP1)")
            .Axis("fixture", "image-floating", "shape", "text-box")
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes | Observe.Story)
            .Hypotheses("The picture and the shape are selected (Type Shape); the text box click lands an IP in its story rather than selecting the box.")
            .Morph("DONE");

        yield return Probe("M-62", "Drag an inline picture, moving and copying")
            .In("image-inline")
            .Steps("{drag}")
            .Axis("drag", "drag(IMG1→P2:3)", "ctrl+drag(IMG1→P2:3)", "drag(IMG1→P1:0)")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Shapes | Observe.Revisions)
            .Hypotheses("The picture is moved (copied with Ctrl) and stays inline at the drop; the undo entry is Move; tracked, the source shows as deleted and the drop as inserted.")
            .Morph("TODO");

        yield return Probe("M-63", "Drag a floating picture")
            .In("image-floating")
            .Steps("{drag}")
            .Axis("drag", "drag(SHP1→P3:2)", "ctrl+drag(SHP1→P3:2)")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Undo | Observe.Shapes | Observe.Revisions)
            .Hypotheses("Left and Top change by the drag; the anchor (a) moves to the paragraph under the drop (b) stays on P2; Ctrl makes a copy with its own anchor.")
            .Morph("TODO");

        // ----- fields, lists, notes, content controls -----

        yield return Probe("M-70", "Click a field result and its edges")
            .In("field-page")
            .Steps("click({at})")
            .Axis("at", "P1:find(\"Page \"):end, side=left", "P1:find(\"1\"), side=left", "P1:find(\"1\"), side=right", "P2:find(\"/20\"), side=left")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("find(\"Page \"):end is the PAGE field's start; find(\"/20\") is inside the DATE result.")
            .Hypotheses("(a) an IP inside the result at the glyph edge; (b) the whole field is selected on any click in it; (c) the IP snaps to the field's edges.")
            .Morph("UNKNOWN");

        yield return Probe("M-72", "Click a list number, then Ctrl+B and typing")
            .In("numbers-two-lists")
            .Steps("click({at}, side=left)")
            .Axis("at", "P1:start", "P4:start")
            .Branch("bold", S("Ctrl+B"))
            .Branch("type", S("type(\"x\")"))
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Lists | Observe.Formatting)
            .Notes("The number is not a text position; the click lands at the left half of the item's first glyph, the nearest the DSL comes to the number. P4 starts the second list.")
            .Hypotheses("On the number itself Word selects every number of the list at that level, Ctrl+B then bolds them and typing does nothing; (a) this click reaches the number (b) it is an IP at the text start and typing inserts.")
            .Morph("TODO");

        yield return Probe("M-73", "Click and double-click a footnote reference")
            .In("footnote")
            .Steps("{step}")
            .Axis("step", "click(P1:4, side=left)", "click(P1:4, side=right)", "dblclick(P1:4)")
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Hypotheses("The halves place the IP before and after the reference; a double-click (a) selects the reference (b) jumps to the note's text.")
            .Morph("TODO");

        yield return Probe("M-74", "Click a content control's text, checkbox and dropdown")
            .In("content-control")
            .Steps("click({at})")
            .Axis("at", "P1:find(\"content\")", "P2:find(\"plain\")", "P3:7", "P4:find(\"choice\")")
            .Observing(Observe.Selection | Observe.Text | Observe.Markup | Observe.Undo)
            .Hypotheses("An IP inside the rich and plain controls; the checkbox toggles on the click with an undo entry; the dropdown places an IP and shows its arrow without opening.")
            .Morph("TODO");

        // ----- right click -----

        yield return Probe("M-76", "Right-click inside and outside a selection")
            .In("words")
            .Steps("dblclick(P1:find(\"beta\"))", "rightclick({at})")
            .Axis("at", "P1:find(\"beta\")", "P1:find(\"3.14\")")
            .Observing(Observe.Selection | Observe.Text)
            .Notes("The context menu is dismissed by the harness after the step.")
            .Hypotheses("Inside the selection it is kept; outside the IP moves to the click.")
            .Morph("PARTIAL");

        yield return Probe("M-76b", "Right-click an inline picture")
            .In("image-inline")
            .Steps("rightclick(IMG1)")
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes)
            .Hypotheses("The picture is selected before the menu opens.")
            .Morph("PARTIAL");

        yield return Probe("M-76c", "Right-click in a cell, as text and with the cell selected")
            .In("table-2x2")
            .Steps("{first}", "rightclick(C(1,1):1)")
            .Axis("first", "click(P1:2)", "click(cellLeftEdge C(1,1))")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("From the body the IP moves into the cell; with the cell selected the selection is kept.")
            .Morph("PARTIAL");

        // ----- drag and drop -----

        yield return Probe("M-80", "Drag and drop a word within its paragraph")
            .In("words")
            .Steps("drag(P1:find(\"beta\")-P1:find(\"beta\"):end→{to})")
            .Axis("to", "P1:find(\"1,000\")", "P1:15", "P1:end", "P1:0")
            .Toggle("Options.SmartCutPaste", "true", "false")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Undo | Observe.Revisions)
            .Notes("Drops after a space, mid-word in gamma, before the mark and at the paragraph start; the dragged range is beta without its comma.")
            .Hypotheses("The word moves and stays selected at the drop; smart cut and paste adds and removes the spaces so words stay separated, off it does not; the undo entry is Move.")
            .Morph("TODO");

        yield return Probe("M-81", "Drag and drop across paragraphs, with and without the mark")
            .In("three-paragraphs")
            .Steps("drag({path})")
            .Axis("path", "P1:0-P2:0→P3:4", "P1:0-P1:end→P3:4", "P1:0-P2:0→P3:end", "P1:6-P1:end→P2:0")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Revisions)
            .Hypotheses("With the mark the paragraph is moved whole and P3 is split at the drop; without it the text joins P3's paragraph; a drop at P2:0 puts the text before Second.")
            .Morph("TODO");

        yield return Probe("M-82", "Ctrl+drag copies")
            .In("words")
            .Steps("ctrl+drag(P1:find(\"beta\")-P1:find(\"beta\"):end→{to})")
            .Axis("to", "P1:find(\"1,000\")", "P1:end")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Undo | Observe.Revisions)
            .Hypotheses("The source stays and a copy is inserted at the drop and selected; the undo entry names a copy or a paste.")
            .Morph("TODO");

        yield return Probe("M-83", "Drop onto the selection itself and next to it")
            .In("words")
            .Steps("drag(P1:find(\"beta\")-P1:find(\"beta\"):end→{to})")
            .Axis("to", "P1:find(\"beta\"):end", "P1:find(\"eta\")", "P1:find(\"beta\")")
            .Observing(Observe.Selection | Observe.Text | Observe.Undo)
            .Hypotheses("A drop inside or at either end of the selection changes nothing and adds no undo entry; the selection is kept.")
            .Morph("TODO");

        yield return Probe("M-84", "Drop into and out of a cell")
            .In("table-2x2")
            .Steps("drag({path})")
            .Axis("path", "P1:0-P1:6→C(1,2):1", "C(1,1):0-C(1,1):end→P2:2", "C(1,1):0-C(1,1):end→C(2,2):1")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Tables | Observe.Revisions)
            .Hypotheses("Text dropped into a cell is inserted at the drop (a) as text (b) replacing the cell; a cell's contents dropped into the body arrive as text; cell to cell (a) inserts (b) replaces.")
            .Morph("TODO");

        yield return Probe("M-85", "Drop at an empty paragraph, at a mark and past the final mark")
            .In("empty-paragraphs")
            .Steps("drag(P1:0-P1:5→{to})")
            .Axis("to", "P2:0", "P4:mark", "DOC:end")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo | Observe.Revisions)
            .Hypotheses("Above moves into the empty paragraph, leaving P1 empty; at P4's mark it lands before the mark; past the final mark (a) it is refused (b) it lands before the final mark.")
            .Morph("TODO");

        yield return Probe("M-87", "Drag text between the body and a text box")
            .In("text-box")
            .Steps("drag({path})")
            .Axis("path", "P2:0-P2:6→TB1:P1:2", "TB1:P1:0-TB1:P1:3→P2:2")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Undo | Observe.Story | Observe.Shapes | Observe.Revisions)
            .Hypotheses("(a) the text moves into the other story; (b) the drop is refused and the selection is kept.")
            .Morph("TODO");
    }
}
