using static Define;

// T — tables: Tab across and past the last cell, Enter in cells, what Delete and Backspace do to structure
// against contents, how a selection grows across cells, and the keys that move or split rows. Every row
// that changes the document runs again with Track Changes on.
//
// Not probed, for want of a fixture or a position grammar: T-25 (a table inside a header), T-27 (a cell
// with vertical text) and T-28 (resizing a column or the table by dragging a border; the handles are not
// positions). T-01's shaded, centred, bold second row has no fixture either, so whether a new row inherits
// the row above's shading and alignment is not asked here.
static class TablesFamily
{
    public static FamilyDefinition Definition { get; } = new("tables", "Tables",
        "Tab across and past the last cell, Enter in cells, structure versus contents on Delete and Backspace, selection growth across cells.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("T-01", "Tab at the last cell adds a row, and what the new row carries")
            .In("{fixture}")
            .Steps("select(C(2,2):end)", "Tab")
            .Axis("fixture", "table-2x2", "table-multi")
            .Observing(Observe.Default | Observe.Tables | Observe.Formatting | Observe.Lists | Observe.Markup)
            .Branch("type", S("type(\"x\")"))
            .Branch("tab-tab", S("Tab", "Tab"))
            .Branch("shift-tab", S("Shift+Tab"), Node("type", S("type(\"y\")")))
            .Branch("backspace", S("Backspace"))
            .Branch("rowbar-backspace", S("click(rowBar R3)", "Backspace"))
            .Branch("undo", S("undo"), Node("redo", S("redo")))
            .Tracked()
            .Hypotheses("In table-multi, (2,2) ends in a bullet item: the new row's cell (a) continues the same list, with a bullet; (b) carries the paragraph properties but no numbering; (c) is a plain Normal paragraph.")
            .Notes("Tab at the last cell is the row-adding key. Backspace right after shows whether an empty new row is removed as a whole; the rowBar click selects the new row R3 outright.")
            .Morph("TODO");

        yield return Probe("T-01b", "Tab at the last cell of a table with merged cells")
            .In("table-merged")
            .Steps("select(C(3,2):end)", "Tab")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Branch("type", S("type(\"x\")"))
            .Branch("tab-tab", S("Tab", "Tab"))
            .Branch("undo", S("undo"), Node("redo", S("redo")))
            .Tracked()
            .Hypotheses("(a) the new row has three cells, the vertical merge of (2,3)-(3,3) not extended; (b) the new row copies the last row's cell layout, two cells wide.")
            .Notes("(3,2) is the last cell in Tab order: (2,3) and (3,3) are merged down, so row 3 has two cells of its own.")
            .Morph("TODO");

        yield return Probe("T-02", "Tab, Shift+Tab and Ctrl+Tab in a cell that holds a list")
            .In("list-in-cell")
            .Steps("select({from})", "{key}")
            .Axis("from", "C(1,1):2", "C(1,1):0")
            .Axis("key", "Tab", "Shift+Tab", "Ctrl+Tab")
            .Observing(Observe.Default | Observe.Lists | Observe.Tables)
            .Tracked()
            .Hypotheses("(a) Tab moves to the next cell even in a list item, and Ctrl+Tab is the only way to type a tab; (b) Tab at the item's start changes the list level, as it does outside a table; (c) Tab at the item's start demotes the item and elsewhere moves to the next cell.")
            .Morph("TODO");

        yield return Probe("T-03", "Enter at a cell's start and end, in a table mid-document and one at the document start")
            .In("{fixture}")
            .Steps("select({from})", "Enter")
            .Axis("fixture", "table-2x2", "table-at-start")
            .Axis("from", "C(1,1):0", "C(1,2):0", "C(2,2):end")
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("(a) Enter adds a paragraph inside the cell everywhere; (b) at the start of the first cell of a table that opens the document, Enter puts a paragraph above the table instead.")
            .Notes("header-footer has no table and is not in this axis; a table inside a header is T-25, which has no fixture.")
            .Morph("UNKNOWN");

        yield return Probe("T-04", "Backspace and Delete at every cell edge and at the paragraphs on either side")
            .In("table-2x2")
            .Steps("select({from})", "{key}")
            .Axis("from", "C(1,1):0", "C(1,1):end", "C(2,2):end", "P1:end", "P2:0")
            .Axis("key", "Backspace", "Delete")
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("Backspace at a cell start and Delete at a cell end do nothing; Delete at P1:end (before the table) does nothing; Backspace at P2:0 (after the table) does nothing. The table at the document end is T-18.")
            .Morph("PARTIAL");

        yield return Probe("T-05", "Structure against contents: every structural selection under every destructive key")
            .In("table-3x3")
            .Steps("select({selection})", "{key}")
            .Axis("selection", "C(1,1):0-C(1,1):mark", "C(1,1):0-C(2,2):0", "R2", "COL2", "T1", "P1:5-C(1,2):1", "C(3,1):1-P2:3")
            .Axis("key", "Delete", "Backspace", "Ctrl+X", "type(\"x\")", "Enter", "Tab")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) Delete clears the contents and leaves the cells, Backspace removes the cells, rows, columns or table selected; (b) both clear; (c) Backspace over a partial rectangle of cells opens a Delete Cells dialog, which the harness records; (d) a selection that straddles the table's edge is cleared and the cells are kept; (e) Tab over a whole-cell selection moves on without clearing.")
            .Notes("C(1,1):0-C(1,1):mark is one whole cell; C(1,1):0-C(2,2):0 is a 2x2 rectangle of cells; P1:5-C(1,2):1 reaches into row 1 from the paragraph before; C(3,1):1-P2:3 reaches out of the last row into the paragraph after.")
            .Morph("PARTIAL");

        yield return Probe("T-06", "Shift+Right grows a selection out of a cell")
            .In("table-3x3")
            .Steps("select(C(1,1):end)", "Shift+Right", "Shift+Right", "Shift+Right", "Shift+Right")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("(a) the first press takes the cell mark and the selection becomes the whole cell, the next the next cell, cell by cell; (b) it steps character by character into b1.")
            .Morph("UNKNOWN");

        yield return Probe("T-06b", "Shift+Down grows a selection down a column")
            .In("table-3x3")
            .Steps("select(C(1,1):1)", "Shift+Down", "Shift+Down", "Shift+Down")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("(a) the first press selects a1 and a2 as whole cells; (b) it extends from a1:1 to the same x in a2 as text; the third press leaves the table into the paragraph after.")
            .Morph("UNKNOWN");

        yield return Probe("T-07", "A mouse drag across cells, and one from the paragraph before into a cell")
            .In("table-3x3")
            .Steps("{drag}")
            .Axis("drag", "drag(C(1,1):1→C(2,2):1)", "drag(P1:2→C(2,2):1)")
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("A drag that leaves its starting cell selects whole cells, a rectangle from (1,1) to (2,2); a drag from the paragraph before selects into the table as text, row 1 whole and the start of row 2.")
            .Morph("TODO");

        yield return Probe("T-08", "Down through a table, row by row, into the paragraph after")
            .In("{fixture}")
            .Steps("select(P1:3)", "Down", "Down", "Down", "Down", "Down")
            .Axis("fixture", "table-3x3", "table-merged")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("Down enters the cell under the caret's x and keeps that column; a cell merged across is one stop; the last press lands in the paragraph after the table.")
            .Morph("UNKNOWN");

        yield return Probe("T-08b", "Down from the last row of a table that ends the document")
            .In("table-at-end")
            .Steps("select(C(2,1):1)", "Down", "Down")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("The first press lands in the final paragraph mark after the table; the second does not move.")
            .Morph("UNKNOWN");

        yield return Probe("T-09", "Alt+Home, Alt+End, Alt+PgUp and Alt+PgDn from a middle cell")
            .In("{fixture}")
            .Steps("select(C(2,2):1)", "{key}")
            .Axis("fixture", "table-3x3", "table-merged", "table-nested")
            .Axis("key", "Alt+Home", "Alt+End", "Alt+PgUp", "Alt+PgDn")
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("First and last cell of the row, top and bottom cell of the column, each with the cell's contents selected; in table-merged the column's bottom cell is the merged (2,3)-(3,3) when the caret is in column 3, and column 2's top cell is the merged (1,1)-(1,2).")
            .Morph("TODO");

        yield return Probe("T-10", "Alt+Shift+Down and Alt+Shift+Up move rows, from each row and a two-row selection")
            .In("table-3x3")
            .Steps("select({from})", "Alt+Shift+Down", "Alt+Shift+Down", "Alt+Shift+Up")
            .Axis("from", "C(1,1):0", "C(2,1):0", "C(3,1):0", "C(1,1):0-C(2,3):0")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("(a) the caret's row swaps with the one below and the caret stays in the moved row; at the last row Alt+Shift+Down (b) does nothing, (c) moves the row out of the table as paragraphs; a two-row selection moves as a block.")
            .Morph("TODO");

        yield return Probe("T-10b", "Alt+Shift+Down from a cell that holds a nested table")
            .In("table-nested")
            .Steps("select(C(1,2):0)", "Alt+Shift+Down", "Alt+Shift+Down", "Alt+Shift+Up")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("C(1,2):0 lands in the inner table's first cell; (a) the inner row moves within the inner table; (b) the outer row moves, inner table and all.")
            .Morph("TODO");

        yield return Probe("T-11", "Ctrl+Shift+Enter splits a table above the caret's row, and undo rejoins it")
            .In("table-3x3")
            .Steps("select({from})", "Ctrl+Shift+Enter")
            .Axis("from", "C(1,1):0", "C(2,1):0", "C(3,2):1", "C(2,1):0-C(3,1):0")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses("A Normal paragraph is put above the caret's row and the table becomes two; at row 1 (a) a paragraph is put above the table, (b) nothing; with two rows selected the split is above the first.")
            .Morph("TODO");

        yield return Probe("T-12", "Ctrl+A from inside a cell, pressed twice")
            .In("{fixture}")
            .Steps("select({from})", "Ctrl+A", "Ctrl+A")
            .Axis("fixture", "table-2x2", "table-nested")
            .Axis("from", "C(1,1):1", "C(1,2):0")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("(a) the whole document on the first press; (b) the cell first, then the document; (c) the cell, the table, then the document. In table-nested, C(1,2):0 is inside the inner table.")
            .Morph("UNKNOWN");

        yield return Probe("T-13", "Typing over a selected cell, row and table")
            .In("table-3x3")
            .Steps("select({selection})", "type(\"x\")")
            .Axis("selection", "C(1,1):0-C(1,1):mark", "R2", "T1")
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("(a) the typed text replaces the contents of the first selected cell and clears the rest, the structure kept; (b) the structure is replaced by one paragraph.")
            .Morph("UNKNOWN");

        yield return Probe("T-14", "Tab, Shift+Tab and Right through merged cells, one press at a time")
            .In("table-merged")
            .Steps("select({from})", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}")
            .Axis("from", "C(1,1):0", "C(3,2):0")
            .Axis("key", "Tab", "Shift+Tab", "Right")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("Tab visits (1,1) (1,3) (2,1) (2,2) (2,3) (3,1) (3,2), the merged cells once each, then adds a row; Shift+Tab walks the same order backwards and stops at (1,1); Right steps the characters and the cell marks in the same order.")
            .Morph("TODO");

        yield return Probe("T-14b", "Down from the top of column 3 into a vertically merged cell")
            .In("table-merged")
            .Steps("select(C(1,3):0)", "Down", "Down", "Down")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("c1 to the merged (2,3), once, then out to the paragraph after.")
            .Morph("TODO");

        yield return Probe("T-15", "Navigation keys inside a nested table")
            .In("table-nested")
            .Steps("select(C(1,2):0)", "{key}", "{key}", "{key}", "{key}")
            .Axis("key", "Tab", "Shift+Tab", "Down", "Alt+End")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("C(1,2):0 is the start of the outer cell, which opens with the inner table, so the caret starts in i1. Tab walks i2, i3, i4 and then (a) adds a row to the inner table, (b) leaves it for the outer cell's text; Shift+Tab from i1 (a) stops, (b) steps to the outer (1,1); Down walks i3 then 'inner after' then b2; Alt+End goes to the inner row's end, then (a) stays, (b) the outer row's.")
            .Morph("TODO");

        yield return Probe("T-16", "Delete clears a selected row, then Backspace")
            .In("table-3x3")
            .Steps("{pick}", "Delete", "Backspace")
            .Axis("pick", "click(rowBar R2)", "select(R2)")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("Delete empties the three cells and keeps the row selected; Backspace then removes the row. A rowBar click and a COM row selection behave alike.")
            .Morph("TODO");

        yield return Probe("T-17", "The end-of-row mark selected, then Delete, Backspace or typing")
            .In("table-2x2")
            .Steps("select(C(1,2):end)", "Shift+Right")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Branch("delete", S("Delete"))
            .Branch("backspace", S("Backspace"))
            .Branch("type", S("type(\"x\")"))
            .Tracked()
            .Hypotheses("Shift+Right from the last cell's end selects the cell, or the cell and the end-of-row mark; then (a) Delete clears and Backspace deletes the row, (b) both clear; typing replaces the cell's contents.")
            .Morph("TODO");

        yield return Probe("T-18", "A table that ends the document: Backspace and Delete at and over the final mark")
            .In("table-at-end")
            .Steps("select({from})", "{key}")
            .Axis("from", "DOC:end", "C(2,2):0-DOC:end", "C(2,2):end")
            .Axis("key", "Backspace", "Delete")
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("The final paragraph mark after a table cannot be removed: Backspace at DOC:end does nothing, or steps into (2,2); Delete over C(2,2):0-DOC:end clears the cell and keeps the mark; Delete at C(2,2):end does nothing. Down from the last row is T-08b.")
            .Morph("PARTIAL");

        yield return Probe("T-19", "Ctrl+Enter inside a cell")
            .In("table-2x2")
            .Steps("select({from})", "Ctrl+Enter")
            .Axis("from", "C(1,1):2", "C(2,2):end")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the table is split and a page break put between the halves; (b) a page break goes inside the cell's text; (c) nothing.")
            .Morph("UNKNOWN");

        yield return Probe("T-20", "Delete and Backspace in the paragraph between two tables")
            .In("tables-adjacent")
            .Steps("select(P2:0)", "{key}")
            .Axis("key", "Delete", "Backspace")
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("(a) the paragraph goes and the tables merge into one; (b) the paragraph goes and the tables stay separate; (c) nothing, the paragraph being the only thing holding them apart.")
            .Morph("UNKNOWN");

        yield return Probe("T-20b", "Right from the first table's last cell across the paragraph between")
            .In("tables-adjacent")
            .Steps("select(C(2,2):end)", "Right", "Right")
            .Trace()
            .Observing(Observe.Default | Observe.Tables)
            .Hypotheses("The first press lands in the empty P2, the second in the second table's (1,1).")
            .Morph("UNKNOWN");

        yield return Probe("T-21", "The table move handle clicked, then Delete, Backspace or typing")
            .In("table-2x2")
            .Steps("click(tableHandle T1)")
            .Observing(Observe.Default | Observe.Tables)
            .Branch("delete", S("Delete"))
            .Branch("backspace", S("Backspace"))
            .Branch("type", S("type(\"x\")"))
            .Tracked()
            .Hypotheses("The click selects the whole table; Delete clears every cell, Backspace removes the table, typing replaces the first cell's contents and clears the rest.")
            .Morph("TODO");

        yield return Probe("T-22", "A column selected from its top edge, then Delete, Backspace or Ctrl+X, each undone")
            .In("table-3x3")
            .Steps("click(columnTop C(1,2))")
            .Observing(Observe.Default | Observe.Tables)
            .Branch("delete", S("Delete"), Node("undo", S("undo")))
            .Branch("backspace", S("Backspace"), Node("undo", S("undo")))
            .Branch("cut", S("Ctrl+X"), Node("undo", S("undo")))
            .Tracked()
            .Hypotheses("Delete clears the column's cells; Backspace and Ctrl+X remove the column, leaving a 3x2 table; undo puts it back with the selection restored.")
            .Morph("TODO");

        yield return Probe("T-23", "Enter twice at the end of the last list item in a cell")
            .In("table-multi")
            .Steps("select(C(2,2):end)", "Enter", "Enter")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Tables)
            .Tracked()
            .Hypotheses("The first Enter adds an empty bulleted item; the second takes the bullet off and leaves a Normal paragraph in the cell, as outside a table.")
            .Morph("UNKNOWN");

        yield return Probe("T-24", "Backspace at the start of a cell's second paragraph")
            .In("table-multi")
            .Steps("select(C(1,1):6)", "Backspace")
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("The two paragraphs merge into 'firstsecond'; the cell stays.")
            .Notes("C(1,1):6 is the start of 'second', after 'first' and its mark.")
            .Morph("UNKNOWN");

        yield return Probe("T-26", "A 1x1 table: Tab at its end, and Backspace and Delete with the only cell selected")
            .In("table-1x1")
            .Steps("{setup}", "{key}")
            .Axis("setup", "select(C(1,1):end)", "click(cellLeftEdge C(1,1))")
            .Axis("key", "Tab", "Backspace", "Delete")
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("Tab at the end adds a second row; with the cell selected from its left edge, Backspace (a) removes the table, (b) removes the cell and leaves nothing; Delete clears it; Tab over the selected cell adds a row.")
            .Morph("TODO");

        yield return Probe("T-29", "Paste and the Insert key over a selected cell")
            .In("table-2x2")
            .Steps("clipboard(\"pasted\")", "select(C(1,1):0-C(1,1):mark)", "{key}")
            .Axis("key", "Ctrl+V", "Insert")
            .Observing(Observe.Default | Observe.Tables)
            .Tracked()
            .Hypotheses("Ctrl+V replaces the cell's contents with the clipboard text and keeps the cell; Insert (a) toggles Overtype and leaves the selection, (b) pastes when the paste-on-Insert option is on.")
            .Morph("UNKNOWN");

        yield return Probe("T-30", "Enter at the first cell of a table that opens the document, then undo")
            .In("table-at-start")
            .Steps("select(C(1,1):0)", "Enter")
            .Observing(Observe.Default | Observe.Tables)
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses("A Normal paragraph appears above the table with the caret in it; undo removes it and puts the caret back in (1,1).")
            .Morph("UNKNOWN");

        yield return Probe("T-31", "Backspace with a cell or a block of cells selected: the Delete Cells dialog recorded")
            .In("table-3x3")
            .Steps("select({selection})", "dialog(Backspace)")
            .Axis("selection", "C(1,1):0-C(1,1):mark", "C(1,1):0-C(2,2):0")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Notes("T-05 saw these cases as 'nothing changed' with a settle timeout: the message filter dismissed the dialog before it was seen. The dialog step records its title and controls, then dismisses it with Esc, so the table is expected unchanged.")
            .Hypotheses("Word opens its Delete Cells dialog (shift cells left or up, delete the entire row or column) for one cell and for a block alike; tracked, (a) the same dialog (b) a message that the action will not be marked as a change.")
            .Morph("TODO");

        yield return Probe("T-31b", "Backspace with a cell selected from its left edge")
            .In("table-3x3")
            .Steps("click(cellLeftEdge C(1,1))", "dialog(Backspace)")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup)
            .Tracked()
            .Hypotheses("The mouse-selected cell gets the same Delete Cells dialog as the COM-selected one (T-31).")
            .Morph("TODO");

        yield return Probe("T-32", "A column backspaced or cut under Track Changes: the dialog recorded")
            .In("table-3x3")
            .Steps("track(on)", "select(COL2)", "dialog({key})")
            .Axis("key", "Backspace", "Ctrl+X")
            .Observing(Observe.Default | Observe.Tables | Observe.Markup | Observe.Revisions)
            .Notes("T-05 COL2 tracked and T-22 tracked changed nothing and a dialog was dismissed unrecorded; this row records it. Untracked, Backspace removes the column outright (T-05), so only the tracked form is run.")
            .Hypotheses("Word says that deleting a column cannot be tracked and asks whether to go on; Esc declines and the table is unchanged.")
            .Morph("TODO");
    }
}
