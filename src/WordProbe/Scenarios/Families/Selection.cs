using static Define;

// S — selection by keyboard: Shift with every navigation key, Ctrl+A, F8, column mode, the anchor, and
// what a selection does to the key pressed next. A selection changes nothing in the document, so the
// rows are traced (the selection read back after every press) and only the rows that then type or
// delete run again under Track Changes.
static class SelectionFamily
{
    public static FamilyDefinition Definition { get; } = new("selection", "Selection by keyboard",
        "Shift with every navigation key, Ctrl+A, F8, column mode, anchors and what a selection does to typing.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("S-01", "Shift+Right and Shift+Left across every boundary, three presses at a time")
            .In("{fixture}")
            .Steps("select({from})", "{key}", "{key}", "{key}")
            .Axis("fixture", "three-paragraphs", "tabs-breaks", "image-inline", "field-page", "hyperlink", "footnote", "special", "page-break", "section-break", "table-2x2")
            .Axis("from", "P1:start", "P1:end", "P2:start", "DOC:end")
            .Axis("key", "Shift+Right", "Shift+Left")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("The paragraph mark is selected as one character (Text ends in \\r) and the selection then runs into the next paragraph; a field, a picture and a note reference are each one press; a cell end grows into the next cell rather than selecting a character; the document ends do not move.")
            .Morph("PARTIAL");

        yield return Probe("S-01b", "Shift+Right and Shift+Left over an inline picture alone")
            .In("image-inline")
            .Steps("select({from})", "{key}", "{key}")
            .Axis("from", "P1:12", "P1:13")
            .Axis("key", "Shift+Right", "Shift+Left")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Shapes)
            .Notes("P1:12 is before the picture, P1:13 after it; the first press from P1:12 covers the picture and nothing else.")
            .Hypotheses("(a) the picture alone is Type InlineShape, with the picture's handles shown; (b) Type Normal, the picture highlighted like a character. The second press makes it Normal either way.")
            .Morph("PARTIAL");

        yield return Probe("S-02", "Shift+Right from a cell end grows by cells, and what an edit then does")
            .In("table-3x3")
            .Steps("select(C(1,1):end)", "Shift+Right")
            .Trace()
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables | Observe.Undo)
            .Branch("grow", S("Shift+Right", "Shift+Right", "Shift+Right", "Shift+Down"))
            .Branch("type", S("type(\"x\")"), Node("undo", S("undo")))
            .Branch("delete", S("Delete"), Node("undo", S("undo")))
            .Branch("backspace", S("Backspace"), Node("undo", S("undo")))
            .Branch("cut", S("Ctrl+X"), Node("undo", S("undo")))
            .Branch("tab", S("Tab"), Node("undo", S("undo")))
            .Hypotheses("(a) the first press selects the cell, the next ones the cells to its right one at a time, then the whole row, then rows; (b) a rectangle that grows by one cell per press. Shift+Down with cells selected adds the row under them. With one cell selected: typing replaces the cell's text; Delete clears the contents and keeps the cell; Backspace (a) clears like Delete (b) deletes the cell; Ctrl+X cuts the contents only; Tab moves on to the next cell.")
            .Morph("TODO");

        yield return Probe("S-03", "Shift+Down and Shift+Up into, through and out of a table")
            .In("table-3x3")
            .Steps("select({from})", "{key}", "{key}", "{key}", "{key}")
            .Axis("from", "P1:3", "C(1,1):1", "C(3,3):1")
            .Axis("key", "Shift+Down", "Shift+Up")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Notes("P1:3 is above the table, C(1,1):1 inside its first cell, C(3,3):1 inside its last; Shift+Up from P1:3 and Shift+Down from C(3,3):1 leave the table at once.")
            .Hypotheses("(a) Shift+Down from text above the table selects to the table and then whole rows, one per press, then runs on into the paragraph after; (b) it selects cells in the caret's column; (c) it selects text through the cells as if they were paragraphs. From inside a cell the first press (a) selects the rest of the cell and the cell under it (b) selects the whole row.")
            .Morph("TODO");

        yield return Probe("S-04", "Shift+End twice from a paragraph start")
            .In("{fixture}")
            .Steps("select(P1:0)", "Shift+End", "Shift+End")
            .Axis("fixture", "wrap", "three-paragraphs", "table-2x2")
            .Toggle("Options.SmartParaSelection", "true", "false")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("(a) Shift+End never includes the mark, so the second press changes nothing; (b) the mark is included once the whole text is covered, with smart paragraph selection on only. On the wrapped paragraph the first press stops at the line end.")
            .Morph("PARTIAL");

        yield return Probe("S-04b", "Shift+Home twice from a paragraph end")
            .In("{fixture}")
            .Steps("select(P1:end)", "Shift+Home", "Shift+Home")
            .Axis("fixture", "wrap", "three-paragraphs", "table-2x2")
            .Toggle("Options.SmartParaSelection", "true", "false")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("The first press selects back to the line start, the second changes nothing; on the wrapped paragraph the first press stops at the last line's start and never reaches P1:0.")
            .Morph("PARTIAL");

        yield return Probe("S-04c", "Shift+End twice from a cell start")
            .In("table-2x2")
            .Steps("select(C(1,1):0)", "Shift+End", "Shift+End")
            .Toggle("Options.SmartParaSelection", "true", "false")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("(a) the cell's text to before the cell mark, and the second press changes nothing; (b) the second press selects the whole cell.")
            .Morph("TODO");

        yield return Probe("S-05", "Ctrl+Shift+Right and Ctrl+Shift+Left word by word, twelve presses")
            .In("{fixture}")
            .Steps("select({from})", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}")
            .Axis("fixture", "words", "table-2x2")
            .Axis("from", "P1:start", "P1:end")
            .Axis("key", "Ctrl+Shift+Right", "Ctrl+Shift+Left")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("A press adds the word and its trailing spaces; punctuation alone is a word; the last word of a paragraph is added without its mark, then the mark alone, then the next paragraph's first word. From the paragraph before the table the presses (a) run into the cells word by word (b) select the table whole once they reach it. Ctrl+Shift+Left from a word's middle takes the start of that word first.")
            .Morph("PARTIAL");

        yield return Probe("S-06", "Ctrl+Shift+Down and Ctrl+Shift+Up between paragraphs")
            .In("{fixture}")
            .Steps("select({from})", "{key}", "{key}")
            .Axis("fixture", "three-paragraphs", "table-multi")
            .Axis("from", "P2:3", "P1:end")
            .Axis("key", "Ctrl+Shift+Down", "Ctrl+Shift+Up")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("Ctrl+Shift+Down selects to the paragraph's end including its mark, then the next paragraph whole; Ctrl+Shift+Up selects back to the paragraph's start, then the paragraph before, whole. P1:end in table-multi is before the table, so the second press reaches it.")
            .Morph("TODO");

        yield return Probe("S-06b", "Ctrl+Shift+Down and Ctrl+Shift+Up inside a two-paragraph cell")
            .In("table-multi")
            .Steps("select(C(1,1):1)", "{key}", "{key}")
            .Axis("key", "Ctrl+Shift+Down", "Ctrl+Shift+Up")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("(a) the cell's first paragraph to its mark, then the cell's second paragraph, staying inside the cell; (b) the second press selects the whole cell; (c) the second press leaves the cell for the next one.")
            .Morph("TODO");

        yield return Probe("S-07", "Ctrl+Shift+End and Ctrl+Shift+Home from inside a cell")
            .In("table-2x2")
            .Steps("select(C(1,1):1)", "{key}")
            .Axis("key", "Ctrl+Shift+End", "Ctrl+Shift+Home")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("(a) to the document's end or start, through the cells and the paragraphs outside the table; (b) to the table's last or first cell only; (c) to the cell's end or start only.")
            .Morph("TODO");

        yield return Probe("S-07b", "Ctrl+Shift+End and Ctrl+Shift+Home from the header")
            .In("header-footer")
            .Steps("seek(header)", "select(HDR:P1:3)", "{key}")
            .Axis("key", "Ctrl+Shift+End", "Ctrl+Shift+Home")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Story)
            .Hypotheses("(a) the selection stays in the header story and reaches its end or start; (b) it leaves for the main document.")
            .Morph("TODO");

        yield return Probe("S-08", "The paragraph mark selected alone, then Delete, a typed letter, Backspace or Enter")
            .In("three-paragraphs")
            .Steps("select(P1:end)", "Shift+Right")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Formatting | Observe.Undo)
            .Branch("delete", S("Delete"), Node("undo", S("undo")))
            .Branch("type", S("type(\"x\")"), Node("undo", S("undo")))
            .Branch("backspace", S("Backspace"), Node("undo", S("undo")))
            .Branch("enter", S("Enter"), Node("undo", S("undo")))
            .Hypotheses("Delete and Backspace join the two paragraphs, and the joined paragraph keeps (a) the first paragraph's properties (b) the second's (centred); a typed letter replaces the mark, joining them with the letter between; Enter replaces the mark with a new one, so nothing changes but the formatting of the mark. Tracked, the mark is struck through and the paragraphs stay apart.")
            .Morph("TODO");

        yield return Probe("S-09", "Ctrl+A in the body of documents with tables, floating pictures, a text box and notes")
            .In("{fixture}")
            .Steps("select(P1:1)", "Ctrl+A")
            .Axis("fixture", "table-2x2", "header-footer", "text-box", "footnote", "image-floating", "table-nested")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Story | Observe.Shapes | Observe.Tables)
            .Hypotheses("The whole main story including the table and the final mark; the header, the footnote text and the text box's text are other stories and are not included; the floating picture (a) is included as an anchored shape (b) is not part of the selection.")
            .Morph("TODO");

        yield return Probe("S-09b", "Ctrl+A from inside a cell, and from inside a nested table")
            .In("{fixture}")
            .Steps("select(C(1,1):1)", "Ctrl+A")
            .Axis("fixture", "table-2x2", "table-nested")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("(a) the whole document, as from the body; (b) the table only, with a second Ctrl+A needed for the document; (c) the cell only.")
            .Morph("TODO");

        yield return Probe("S-09c", "Ctrl+A in the header")
            .In("header-footer")
            .Steps("seek(header)", "Ctrl+A")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Story)
            .Hypotheses("The header story only, the selection's StoryType staying the header's.")
            .Morph("TODO");

        yield return Probe("S-09d", "Ctrl+A in a text box")
            .In("text-box")
            .Steps("select(TB1:P1:1)", "Ctrl+A")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Story | Observe.Shapes)
            .Hypotheses("(a) the text box's two paragraphs only; (b) the body, with the text box deselected.")
            .Morph("TODO");

        yield return Probe("S-09e", "Ctrl+A in the footnotes")
            .In("footnote")
            .Steps("seek(footnotes)", "Ctrl+A")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Story)
            .Hypotheses("The footnote story only, every note's text in one selection.")
            .Morph("TODO");

        yield return Probe("S-09f", "Ctrl+A then Delete: which paragraph's properties the one left keeps")
            .In("{fixture}")
            .Steps("select(P1:1)", "Ctrl+A", "Delete")
            .Axis("fixture", "kinds", "table-2x2", "image-floating")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Formatting | Observe.Lists | Observe.Tables | Observe.Shapes | Observe.Undo)
            .Branch("undo", S("undo"))
            .Hypotheses("One empty paragraph is left and it carries (a) the last paragraph's properties (the final mark survives) (b) the first paragraph's (c) Normal with no direct formatting; the table goes with the text; the floating picture (a) goes with its anchor paragraph (b) stays. Tracked, every paragraph is struck and nothing is removed.")
            .Morph("TODO");

        yield return Probe("S-10", "F8 cascade from a word, then Shift+F8 back, Right, or a typed letter")
            .In("sentences")
            .Steps("select(P1:find(\"Two\"))", "F8")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Branch("cascade", S("F8", "F8", "F8", "F8", "F8", "Shift+F8", "Shift+F8", "Shift+F8", "Esc"))
            .Branch("arrow", S("Right x3", "Esc"))
            .Branch("letter", S("type(\"f\")", "Esc"))
            .Branch("esc", S("Esc"))
            .Hypotheses("The first F8 turns extend mode on with nothing selected; then word, sentence, paragraph, (a) section, (b) document with the section step skipped; Shift+F8 steps back down the same ladder; Esc ends the mode and keeps the selection. In extend mode Right extends by a character and a typed letter extends to the next occurrence of that letter rather than replacing the selection.")
            .Morph("TODO");

        yield return Probe("S-10b", "F8 cascade from inside a table cell")
            .In("table-3x3")
            .Steps("select(C(2,2):1)", "F8", "F8", "F8", "F8", "F8", "F8", "Esc")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("(a) word, cell, row, table, document; (b) word, sentence, paragraph (the cell), then the document, with no row or table step; (c) column before row.")
            .Morph("TODO");

        yield return Probe("S-10c", "F8 cascade from inside a numbered item")
            .In("numbers")
            .Steps("select(P2:1)", "F8", "F8", "F8", "F8", "F8", "Esc")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Lists)
            .Hypotheses("Word, sentence and paragraph coincide for a one-word item, so the presses (a) stop at the item for three presses before the document (b) climb once per press regardless; the list itself is never a step.")
            .Morph("TODO");

        yield return Probe("S-11", "Ctrl+Shift+F8 column mode over a wrapped paragraph, then typing or Delete")
            .In("long-paragraph")
            .Steps("select(P2:2)", "Ctrl+Shift+F8", "Down", "Down", "Right x3")
            .Trace()
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo)
            .Branch("esc-type", S("Esc", "type(\"x\")"))
            .Branch("type", S("type(\"x\")"))
            .Branch("delete", S("Delete"))
            .Hypotheses("Type is Block, one segment per line, three characters wide on each of three lines; Esc ends column mode and keeps the block. A typed letter (a) replaces every segment with one copy of the letter each (b) replaces only the first; Delete removes every segment.")
            .Morph("TODO");

        yield return Probe("S-12", "Alt+Num5 inside and outside a table")
            .In("table-2x2")
            .Steps("select({from})", "Alt+Num5")
            .Axis("from", "C(1,1):1", "P1:1")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Notes("Num5 is the numeric keypad's 5 with Num Lock off (the Clear key); with Num Lock on the chord is a different key and does nothing.")
            .Hypotheses("Inside a table the whole table is selected; outside it nothing changes.")
            .Morph("TODO");

        yield return Probe("S-13", "Shift+click after a keyboard selection moves the active end, not the anchor")
            .In("words")
            .Steps("click(P1:10)", "Shift+Right", "Shift+Right", "Shift+Right", "shift+click(P1:5)")
            .Trace()
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The anchor stays at 10, so the result is 5-10 with the start active; the three characters to its right drop out.")
            .Morph("PARTIAL");

        yield return Probe("S-14", "The anchor stays put when Shift+Left passes back over it, and over a Shift+Up")
            .In("words")
            .Steps("select(P1:10)", "Shift+Right", "Shift+Right", "Shift+Right")
            .Trace()
            .Observing(Observe.Selection | Observe.Text)
            .Branch("reverse", S("Shift+Left", "Shift+Left", "Shift+Left", "Shift+Left", "Shift+Left"))
            .Branch("up", S("Shift+Up"))
            .Hypotheses("Five Shift+Left presses after three Shift+Right give 8-10 with StartIsActive true: the selection passes through empty and reverses; Shift+Up on the only line selects back to the paragraph start, from the anchor at 10.")
            .Morph("DONE");

        yield return Probe("S-15", "Smart paragraph selection on a drag that reaches a paragraph's end")
            .In("three-paragraphs")
            .Steps("{drag}")
            .Axis("drag", "drag(P1:0→P1:end)", "drag(P1:4→P2:end)", "drag(P1:0→P2:3)")
            .Toggle("Options.SmartParaSelection", "true", "false")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("With the option on, a drag from a paragraph's start to its end takes the mark too; from inside a paragraph to another's end it (a) takes that mark (b) stops before it. With the option off the mark is never added. A drag that ends mid-paragraph is unchanged either way.")
            .Morph("TODO");

        yield return Probe("S-16", "Typing over a selection with ReplaceSelection off")
            .In("words")
            .Steps("option(Options.ReplaceSelection=false)", "select(P1:6-10)")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Undo)
            .Branch("type", S("type(\"x\")"))
            .Branch("enter", S("Enter"))
            .Branch("tab", S("Tab"))
            .Branch("backspace", S("Backspace"))
            .Branch("delete", S("Delete"))
            .Hypotheses("(a) the typed character, Enter and Tab are inserted at the selection's start and the selection collapses there; (b) at its end. Backspace and Delete still remove the selected text.")
            .Morph("TODO");

        yield return Probe("S-17", "Tab and Shift+Tab with a cell's contents selected")
            .In("table-2x2")
            .Steps("select(C(1,1):0-C(1,1):end)", "{key}")
            .Axis("key", "Tab", "Shift+Tab")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables | Observe.Undo)
            .Hypotheses("Tab moves to the next cell and selects its contents, inserting nothing; Shift+Tab in the first cell (a) does nothing (b) leaves the table.")
            .Morph("UNKNOWN");

        yield return Probe("S-17b", "Tab and Shift+Tab with a word selected in plain text")
            .In("words")
            .Steps("select(P1:6-10)", "{key}")
            .Axis("key", "Tab", "Shift+Tab")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Formatting | Observe.Undo)
            .Hypotheses("Tab replaces the selection with a tab character; Shift+Tab (a) does nothing (b) also replaces it (c) outdents the paragraph.")
            .Morph("UNKNOWN");

        yield return Probe("S-17c", "Tab and Shift+Tab with a numbered item's text selected")
            .In("numbers")
            .Steps("select(P2:0-P2:3)", "{key}")
            .Axis("key", "Tab", "Shift+Tab")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Lists | Observe.Undo)
            .Hypotheses("(a) Tab changes the item's level as it does with the caret at the item's start, and Shift+Tab at level 1 does nothing; (b) with text selected Tab replaces the text with a tab character.")
            .Morph("UNKNOWN");

        yield return Probe("S-18", "Shift+Right from the last cell's end: cell, row mark, table, then what follows")
            .In("{fixture}")
            .Steps("select(C(2,2):end)", "Shift+Right", "Shift+Right", "Shift+Right")
            .Axis("fixture", "table-2x2", "table-at-end")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Hypotheses("(a) the cell, then the row (its end-of-row mark), then the whole table, then the paragraph after; (b) the cell, then straight to the paragraph after. With only the final mark after the table the last press (a) selects that mark (b) changes nothing.")
            .Morph("TODO");

        yield return Probe("S-19", "Shift+Right and Shift+Left from a clicked inline picture")
            .In("image-inline")
            .Steps("click(IMG1)", "{key}")
            .Axis("key", "Shift+Right", "Shift+Left")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Shapes)
            .Hypotheses("The picture stays selected and a character is added on the pressed side: Shift+Right anchors before the picture and takes the space after it; Shift+Left anchors after it and takes the space before.")
            .Morph("PARTIAL");

        yield return Probe("S-20", "Shift+Right with a floating picture selected")
            .In("image-floating")
            .Steps("click(SHP1)", "Shift+Right")
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes)
            .Hypotheses("(a) nothing changes and the shape stays selected; (b) the shape grows wider by a nudge step; (c) the selection falls back to text at the anchor.")
            .Morph("TODO");

        yield return Probe("S-21", "A selection to the document's end then Delete keeps the final mark")
            .In("three-paragraphs")
            .Steps("select(P3:2)", "Ctrl+Shift+End", "Delete")
            .Tracked()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Formatting | Observe.Undo)
            .Branch("undo", S("undo"))
            .Hypotheses("The text from P3:2 is removed and the final paragraph mark survives, so the document still ends with P3's mark and its properties; the selection collapses at P3:2.")
            .Morph("TODO");

        yield return Probe("S-22", "A keyboard selection, a click, then a Shift+click")
            .In("words")
            .Steps("select(P1:0)", "Shift+Right x4", "click(P1:20)", "shift+click(P1:25)")
            .Trace()
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The click collapses the selection at 20 and becomes the new anchor; the Shift+click then selects 20-25, with no memory of the first selection.")
            .Morph("PARTIAL");

        yield return Probe("S-23", "Shift+Home and Shift+End on a wrapped line stop at the line's edges")
            .In("wrap")
            .Steps("select(P1:60)", "{key}")
            .Axis("key", "Shift+Home", "Shift+End")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("Shift+Home selects back to the start of the line P1:60 sits on, not to P1:0; Shift+End to that line's end, (a) before the wrap space (b) after it.")
            .Morph("DONE");

        yield return Probe("S-25", "Shift+Right into another author's tracked deletion, per markup view")
            .In("tracked")
            .Steps("view({view})", "select(P1:5)", "Shift+Right", "Shift+Right", "Shift+Right")
            .Axis("view", "all", "simple", "none")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Revisions)
            .Notes("P1:5 is where the deleted 'gone ' begins.")
            .Hypotheses("(a) with all markup the deleted text is stepped through and Selection.Text includes it; in the collapsed views the first press jumps past the deletion, and Text omits it; (b) the deleted text is selected in every view and Text includes it in every view.")
            .Morph("UNKNOWN");

        yield return Probe("S-26", "Shift+Right and Shift+Left at a rich text content control's edges")
            .In("content-control")
            .Steps("select({from})", "{key}", "{key}", "{key}", "{key}")
            .Axis("from", "P1:5", "P1:find(\" end\")")
            .Axis("key", "Shift+Right", "Shift+Left")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Markup)
            .Notes("P1 reads 'Rich: content here end.' with the control on 'content here'; P1:5 is the space before it, P1:find(\" end\") the position right after it.")
            .Hypotheses("(a) the control's edges add a position each, so one press at an edge changes the selection's Start or End by nothing while the trace shows the step; (b) no extra positions and the selection crosses the edge like plain text. The selection's Text never includes a control boundary character.")
            .Morph("UNKNOWN");

        yield return Probe("S-27", "Shift+Up and Shift+Down keep the remembered x across a short paragraph")
            .In("long-paragraph")
            .Steps("select(P2:40)", "Shift+Up", "Shift+Down", "Shift+Down")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("Shift+Up from the second line of P2 selects to the point above on its first line; Shift+Down returns to P2:40 (an empty selection, the anchor unchanged); the second Shift+Down lands on the third line at the same x, the x being kept through the empty selection.")
            .Morph("DONE");

        yield return Probe("S-28", "Shift+PgDn and Shift+PgUp over three pages")
            .In("three-pages")
            .Steps("select(P1:3)", "Shift+PgDn", "Shift+PgDn", "Shift+PgUp")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("Each Shift+PgDn extends by one screen of lines at the same x, not by a page of the document; Shift+PgUp takes the last screen back and the anchor stays at P1:3 throughout.")
            .Morph("TODO");
    }
}
