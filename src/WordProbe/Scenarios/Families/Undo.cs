using static Define;

// U — undo, redo and repeat: what one undo entry covers, the entry names Word gives each class of
// action, where the selection lands after an undo, what Ctrl+Y repeats, and how deep the list goes.
// Every scenario observes the undo list (Observe.Undo is in Default); most also trace the selection.
static class UndoFamily
{
    public static FamilyDefinition Definition { get; } = new("undo", "Undo, redo and repeat",
        "What one undo entry covers, the entry names per action, where the selection lands after undo, redo and Ctrl+Y as repeat.",
        Scenarios);

    /// <summary>The same steps, count times over, for the depth probe.</summary>
    static string[] Repeated(int count, params string[] steps) =>
        Enumerable.Range(0, count).SelectMany(_ => steps).ToArray();

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        // U-01: typing group boundaries. One scenario per key sequence, since a step list cannot be an axis.
        // The undo list is read after the last step; the "undo" branch then shows what one undo removes.
        const string groupHypotheses = "(a) any caret move (arrow, click, Enter) closes the typing group, so the final character is its own entry; (b) only an edit elsewhere closes it; Backspace and Delete (a) stay in the group (b) open a new entry; a 5 s pause (a) does nothing (b) closes the group.";

        yield return Probe("U-01a", "Typing group: three characters in a row")
            .In("plain")
            .Steps("select(P1:5)", "type(\"abc\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        yield return Probe("U-01b", "Typing group: Left between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "Left", "type(\"c\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        yield return Probe("U-01c", "Typing group: a click at the caret's own position between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "click(P1:7)", "type(\"c\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Notes("P1:7 is where the caret already stands after 'ab', so the click moves nothing; a click that moves the caret is U-01b's Left.")
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        yield return Probe("U-01d", "Typing group: a space inside the typed text")
            .In("plain")
            .Steps("select(P1:5)", "type(\"a b\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses("(a) one entry for the whole string; (b) the space closes a group, so two entries.")
            .Morph("PARTIAL");

        yield return Probe("U-01e", "Typing group: Enter between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "Enter", "type(\"c\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        yield return Probe("U-01f", "Typing group: Backspace between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "Backspace", "type(\"c\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        yield return Probe("U-01g", "Typing group: Ctrl+B between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "Ctrl+B", "type(\"c\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("(a) Ctrl+B on an IP writes no entry and the typing continues the group; (b) a Bold entry splits the typing in two.")
            .Morph("PARTIAL");

        yield return Probe("U-01h", "Typing group: a five second pause between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "wait(5000)", "type(\"c\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        yield return Probe("U-01i", "Typing group: Shift+Enter between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "Shift+Enter", "type(\"b\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        yield return Probe("U-01j", "Typing group: Delete between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "Delete", "type(\"c\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        yield return Probe("U-01k", "Typing group: Right between typed characters")
            .In("plain")
            .Steps("select(P1:5)", "type(\"ab\")", "Right", "type(\"c\")")
            .Trace()
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses(groupHypotheses)
            .Morph("PARTIAL");

        // U-02: the entry name Word writes for each class of action. The keys that act on one text
        // selection share an axis; the actions that need their own fixture or set-up follow one by one.
        yield return Probe("U-02a", "Undo entry names: keys applied to a selected word")
            .In("words")
            .Steps("select(P1:6-10)", "{key}")
            .Axis("key", "Delete", "Ctrl+X", "Ctrl+B", "Ctrl+Shift+>", "Ctrl+E", "Ctrl+Alt+1", "Ctrl+Shift+L", "Shift+F3")
            .Tracked()
            .Also(Observe.Formatting | Observe.Lists)
            .Notes("P1:6-10 is 'beta'. The entry names are read off the Undo dropdown's list, most recent first.")
            .Morph("TODO");

        yield return Probe("U-02b", "Undo entry names: copy then paste at the paragraph end")
            .In("words")
            .Steps("select(P1:6-10)", "Ctrl+C", "select(P1:end)", "Ctrl+V")
            .Tracked()
            .Hypotheses("Copy writes no entry; the paste is one entry named Paste.")
            .Morph("TODO");

        yield return Probe("U-02c", "Undo entry names: an AutoCorrect replacement")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"teh \")")
            .Tracked()
            .Hypotheses("(a) one AutoCorrect entry over the typing entry; (b) the typing and the correction in one entry.")
            .Morph("TODO");

        yield return Probe("U-02d", "Undo entry names: an automatic bullet on Enter")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"* item\")", "Enter")
            .Tracked()
            .Also(Observe.Lists)
            .Hypotheses("An AutoFormat entry sits over the typing entries.")
            .Morph("TODO");

        yield return Probe("U-02e", "Undo entry names: Tab past the last cell adds a row")
            .In("table-2x2")
            .Steps("select(C(2,2):end)", "Tab")
            .Tracked()
            .Also(Observe.Tables)
            .Morph("TODO");

        yield return Probe("U-02f", "Undo entry names: Backspace with a row selected")
            .In("table-2x2")
            .Steps("click(rowBar R2)", "Backspace")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) the row is deleted (Delete Rows); (b) only its contents are cleared (Clear).")
            .Morph("TODO");

        yield return Probe("U-02g", "Undo entry names: a word moved by drag and drop")
            .In("words")
            .Steps("dblclick(P1:find(\"beta\"))", "drag(P1:find(\"beta\")-P1:find(\"beta\"):end→P1:find(\"1,000\"))")
            .Tracked()
            .Hypotheses("(a) one Move entry; (b) a Cut and a Paste entry.")
            .Morph("TODO");

        yield return Probe("U-02h", "Undo entry names: a word copied by Ctrl+drag")
            .In("words")
            .Steps("dblclick(P1:find(\"beta\"))", "ctrl+drag(P1:find(\"beta\")-P1:find(\"beta\"):end→P1:find(\"1,000\"))")
            .Tracked()
            .Hypotheses("(a) one Copy entry; (b) a Paste entry.")
            .Morph("TODO");

        yield return Probe("U-02i", "Undo entry names: an inline picture deleted")
            .In("image-inline")
            .Steps("click(IMG1)", "Delete")
            .Tracked()
            .Also(Observe.Shapes)
            .Morph("TODO");

        yield return Probe("U-02j", "Undo entry names: a floating picture nudged")
            .In("image-floating")
            .Steps("click(SHP1)", "Right")
            .Tracked()
            .Also(Observe.Shapes)
            .Morph("TODO");

        yield return Probe("U-02k", "Undo entry names: a footnote inserted")
            .In("plain")
            .Steps("select(P1:5)", "Ctrl+Alt+F")
            .Tracked()
            .Also(Observe.Story)
            .Morph("TODO");

        yield return Probe("U-02l", "Undo entry names: a comment inserted")
            .In("plain")
            .Steps("select(P1:5)", "Ctrl+Alt+M")
            .Tracked()
            .Also(Observe.Story)
            .Morph("TODO");

        yield return Probe("U-02m", "Undo entry names: Enter, Shift+Enter and Ctrl+Enter in a paragraph")
            .In("plain")
            .Steps("select(P1:5)", "{key}")
            .Axis("key", "Enter", "Shift+Enter", "Ctrl+Enter")
            .Tracked()
            .Hypotheses("Each is named Typing, or each has a name of its own (New Paragraph, Line Break, Page Break).")
            .Morph("TODO");

        yield return Probe("U-02n", "Undo entry names: Ctrl+Shift+Enter splits a table")
            .In("table-3x3")
            .Steps("select(C(2,1):0)", "Ctrl+Shift+Enter")
            .Tracked()
            .Also(Observe.Tables)
            .Morph("TODO");

        yield return Probe("U-02o", "Undo entry names: Tab in a list item demotes it")
            .In("bullets")
            .Steps("select(P2:2)", "Tab")
            .Tracked()
            .Also(Observe.Lists)
            .Morph("TODO");

        yield return Probe("U-02p", "Undo entry names: Format Painter")
            .In("mixed")
            .Steps("select(P1:0-P1:6)", "Ctrl+Shift+C", "select(P1:12-P1:17)", "Ctrl+Shift+V")
            .Tracked()
            .Also(Observe.Formatting)
            .Notes("P1:0-6 is the bold 'Strong'; P1:12-17 is 'plain'.")
            .Morph("TODO");

        yield return Probe("U-02q", "Undo entry names: Ctrl+Space clears direct character formatting")
            .In("mixed")
            .Steps("select(P1:0-P1:11)", "Ctrl+Space")
            .Tracked()
            .Also(Observe.Formatting)
            .Morph("TODO");

        yield return Probe("U-02r", "Undo entry names: Ctrl+Q clears direct paragraph formatting")
            .In("kinds")
            .Steps("select(P4:2)", "Ctrl+Q")
            .Tracked()
            .Also(Observe.Formatting)
            .Notes("P4 is the centred paragraph.")
            .Morph("TODO");

        yield return Probe("U-02s", "Undo entry names: Alt+Shift+D inserts a DATE field")
            .In("plain")
            .Steps("select(P1:5)", "Alt+Shift+D")
            .Tracked()
            .Also(Observe.Markup)
            .Morph("TODO");

        yield return Probe("U-02t", "Undo entry names: F9 updates a field")
            .In("field-page")
            .Steps("select(P1:5)", "F9")
            .Tracked()
            .Also(Observe.Markup)
            .Hypotheses("(a) an Update Field entry even when the result is unchanged; (b) no entry.")
            .Morph("TODO");

        yield return Probe("U-02u", "Undo entry names: typing in Overtype mode")
            .In("plain")
            .Steps("option(Options.Overtype=true)", "select(P1:5)", "type(\"xy\")")
            .Tracked()
            .Hypotheses("(a) one Typing entry restoring both overwritten characters; (b) one per character.")
            .Morph("TODO");

        yield return Probe("U-03", "Undo after AutoCorrect: the correction, then the typing")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"teh \")", "undo", "undo")
            .Trace()
            .Tracked()
            .Hypotheses("The first undo puts 'teh ' back; the second removes the typing.")
            .Morph("TODO");

        // U-04: where the selection lands after an undo, per kind of edit.
        const string selectionHypotheses = "(a) an IP at the start of the undone edit; (b) the restored text is selected; (c) the selection as it was before the edit.";

        yield return Probe("U-04a", "Selection after undoing typing")
            .In("words")
            .Steps("select(P1:6)", "type(\"xyz\")", "undo")
            .Trace()
            .Tracked()
            .Hypotheses(selectionHypotheses)
            .Morph("TODO");

        yield return Probe("U-04b", "Selection after undoing Delete over a selection")
            .In("words")
            .Steps("select(P1:6-10)", "Delete", "undo")
            .Trace()
            .Tracked()
            .Hypotheses(selectionHypotheses)
            .Morph("TODO");

        yield return Probe("U-04c", "Selection after undoing a paste")
            .In("words")
            .Steps("clipboard(\"pasted\")", "select(P1:6)", "Ctrl+V", "undo")
            .Trace()
            .Tracked()
            .Hypotheses(selectionHypotheses)
            .Morph("TODO");

        yield return Probe("U-04d", "Selection after undoing Bold")
            .In("words")
            .Steps("select(P1:6-10)", "Ctrl+B", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses(selectionHypotheses)
            .Morph("TODO");

        yield return Probe("U-04e", "Selection after undoing the row Tab adds")
            .In("table-2x2")
            .Steps("select(C(2,2):end)", "Tab", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) back in cell (2,2) at its end; (b) the cell's contents selected.")
            .Morph("TODO");

        yield return Probe("U-04f", "Selection after undoing a drag move")
            .In("words")
            .Steps("dblclick(P1:find(\"beta\"))", "drag(P1:find(\"beta\")-P1:find(\"beta\"):end→P1:find(\"1,000\"))", "undo")
            .Trace()
            .Tracked()
            .Hypotheses("(a) the moved word is selected back at its origin; (b) an IP at the origin; (c) an IP at the drop point.")
            .Morph("TODO");

        yield return Probe("U-04g", "Selection after undoing a Backspace merge")
            .In("three-paragraphs")
            .Steps("select(P2:0)", "Backspace", "undo")
            .Trace()
            .Tracked()
            .Hypotheses(selectionHypotheses)
            .Morph("TODO");

        yield return Probe("U-04h", "Selection after undoing an Enter split")
            .In("three-paragraphs")
            .Steps("select(P2:4)", "Enter", "undo")
            .Trace()
            .Tracked()
            .Hypotheses(selectionHypotheses)
            .Morph("TODO");

        yield return Probe("U-05a", "Redo puts an undone character back")
            .In("plain")
            .Steps("select(P1:5)", "type(\"a\")", "undo", "redo")
            .Trace()
            .Tracked()
            .Hypotheses("Redo restores 'a' with the caret after it; the redo list is then empty.")
            .Morph("PARTIAL");

        yield return Probe("U-05b", "A new edit clears the redo stack, and Ctrl+Y becomes Repeat")
            .In("plain")
            .Steps("select(P1:5)", "type(\"a\")", "undo", "type(\"b\")", "Ctrl+Y")
            .Trace()
            .Tracked()
            .Hypotheses("Ctrl+Y repeats the 'b' rather than redoing the 'a', giving 'bb'.")
            .Morph("PARTIAL");

        // U-06: what F4 repeats, elsewhere in the document.
        yield return Probe("U-06a", "Repeat typing elsewhere with F4")
            .In("three-paragraphs")
            .Steps("select(P1:5)", "type(\"abc\")", "select(P3:2)", "F4")
            .Trace()
            .Tracked()
            .Hypotheses("'abc' is typed again at P3:2.")
            .Morph("TODO");

        yield return Probe("U-06b", "Repeat Bold elsewhere with F4")
            .In("three-paragraphs")
            .Steps("select(P1:0-P1:5)", "Ctrl+B", "select(P3:0-P3:5)", "F4")
            .Trace()
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("The second range is made bold.")
            .Morph("TODO");

        yield return Probe("U-06c", "Repeat a paste elsewhere with F4")
            .In("three-paragraphs")
            .Steps("clipboard(\"pasted\")", "select(P1:5)", "Ctrl+V", "select(P3:2)", "F4")
            .Trace()
            .Tracked()
            .Hypotheses("(a) the paste is repeated at P3:2; (b) F4 does nothing after a paste.")
            .Morph("TODO");

        yield return Probe("U-06d", "Repeat a deletion elsewhere with F4")
            .In("three-paragraphs")
            .Steps("select(P1:0-P1:5)", "Delete", "select(P3:2)", "F4")
            .Trace()
            .Tracked()
            .Hypotheses("(a) Delete repeats as deleting the next character at P3:2; (b) nothing happens; (c) five characters are deleted.")
            .Morph("TODO");

        yield return Probe("U-06e", "Repeat Heading 1 elsewhere with F4")
            .In("three-paragraphs")
            .Steps("select(P1:2)", "Ctrl+Alt+1", "select(P3:2)", "F4")
            .Trace()
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("P3 becomes Heading 1.")
            .Morph("TODO");

        yield return Probe("U-06f", "Repeat an AutoCorrected typing elsewhere with F4")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"teh \")", "select(P3:0)", "F4")
            .Trace()
            .Tracked()
            .Hypotheses("(a) the corrected 'the ' is repeated; (b) the raw 'teh ' is repeated and corrected again; (c) only the correction (the last entry) is repeated.")
            .Morph("TODO");

        // U-07: undo and redo of structural edits, with the table and paragraph structure observed.
        yield return Probe("U-07a", "Undo and redo the row Tab adds")
            .In("table-2x2")
            .Steps("select(C(2,2):end)", "Tab", "undo", "redo")
            .Trace()
            .Tracked()
            .Also(Observe.Tables)
            .Morph("PARTIAL");

        yield return Probe("U-07b", "Undo Enter in the first cell of a table at the document start")
            .In("table-at-start")
            .Steps("select(C(1,1):0)", "Enter", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("Enter at (1,1):0 of a table at the document start inserts a paragraph before the table; undo removes it and the caret returns to the cell.")
            .Morph("PARTIAL");

        yield return Probe("U-07c", "Undo and redo a Backspace merge of two paragraphs")
            .In("three-paragraphs")
            .Steps("select(P2:0)", "Backspace", "undo", "redo")
            .Trace()
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("Undo restores P2 with its centring; redo merges again.")
            .Morph("PARTIAL");

        yield return Probe("U-07d", "Undo an Enter split")
            .In("three-paragraphs")
            .Steps("select(P2:4)", "Enter", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Formatting)
            .Morph("PARTIAL");

        yield return Probe("U-07e", "Undo a drag move")
            .In("words")
            .Steps("dblclick(P1:find(\"beta\"))", "drag(P1:find(\"beta\")-P1:find(\"beta\"):end→P1:find(\"1,000\"))", "undo")
            .Trace()
            .Tracked()
            .Hypotheses("(a) one undo restores the text and the smart-cut spacing; (b) the move takes two undos.")
            .Morph("PARTIAL");

        yield return Probe("U-07f", "Undo a row deletion")
            .In("table-3x3")
            .Steps("click(rowBar R2)", "Backspace", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Tables)
            .Morph("PARTIAL");

        yield return Probe("U-07g", "Undo typing over a selected row")
            .In("table-3x3")
            .Steps("select(R2)", "type(\"x\")", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("Typing over a selected row replaces (a) the whole row's text with 'x' in its first cell (b) only the first cell; one undo restores the cells.")
            .Morph("PARTIAL");

        yield return Probe("U-07h", "Undo Backspace over a cell block")
            .In("table-3x3")
            .Steps("select(C(1,1):0-C(2,2):0)", "Backspace", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("Backspace over selected cells (a) clears their contents (b) deletes the cells; undo restores them in one step.")
            .Morph("PARTIAL");

        yield return Probe("U-08a", "Undo an inline picture deletion")
            .In("image-inline")
            .Steps("click(IMG1)", "Delete", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Shapes)
            .Hypotheses("The picture returns (a) selected (b) with an IP before it.")
            .Morph("TODO");

        yield return Probe("U-08b", "Undo three nudges of a floating picture")
            .In("image-floating")
            .Steps("click(SHP1)", "Right", "Right", "Right")
            .Branch("undo", S("undo"))
            .Tracked()
            .Also(Observe.Shapes)
            .Hypotheses("(a) one entry per nudge, so one undo steps the picture back one grid step; (b) the nudges are grouped and one undo returns it to the start.")
            .Morph("TODO");

        yield return Probe("U-09", "Undo depth: 120 separate typing entries, then 120 undos")
            .In("plain")
            .Steps("select(P1:5)")
            .Steps(Repeated(120, "type(\"a\")", "Left"))
            .Steps("Ctrl+Z x120")
            .Observing(Observe.Text | Observe.Undo | Observe.Selection)
            .Tracked()
            .Notes("Each Left closes the typing group, so each 'a' is its own entry (U-01b). The text after the undos says how many were undone; the undo list before them is not read, only what remains.")
            .Hypotheses("(a) the list is capped near 100 entries, leaving about 20 characters in the text; (b) unlimited, the text back to the fox sentence.")
            .Morph("UNKNOWN");

        // U-10: undo under Track Changes, with the tracking turned on as a step (no Tracked repeat).
        yield return Probe("U-10a", "Undo tracked typing")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "type(\"xy\")", "undo")
            .Trace()
            .Also(Observe.Revisions)
            .Hypotheses("The insertion revision is removed, not marked as deleted.")
            .Morph("UNKNOWN");

        yield return Probe("U-10b", "Undo a tracked Backspace")
            .In("plain")
            .Steps("track(on)", "select(P1:5)", "Backspace", "undo")
            .Trace()
            .Also(Observe.Revisions)
            .Hypotheses("The deletion revision is removed and the character is plain text again.")
            .Morph("UNKNOWN");

        yield return Probe("U-10c", "Undo a tracked paste")
            .In("plain")
            .Steps("track(on)", "clipboard(\"pasted\")", "select(P1:5)", "Ctrl+V", "undo")
            .Trace()
            .Also(Observe.Revisions)
            .Morph("UNKNOWN");

        // U-11 (undo across a save) is not probed: the fixture is unsaved, so Ctrl+S opens Save As.

        yield return Probe("U-12a", "Undo an automatic bullet: the list, then the typing")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"* item\")", "Enter", "undo", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Lists | Observe.Markup)
            .Hypotheses("The first undo takes the bullet off and puts '* ' back, leaving the new paragraph; the second removes the Enter.")
            .Morph("TODO");

        yield return Probe("U-12b", "Undo an automatic border from three hyphens")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"---\")", "Enter", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Markup)
            .Hypotheses("One undo removes the border and restores '---' with the Enter undone too, or leaves the paragraph split.")
            .Morph("TODO");

        yield return Probe("U-12c", "Undo an automatic hyperlink")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"www.example.com \")", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Markup | Observe.Formatting)
            .Hypotheses("One undo removes the hyperlink and keeps the text and the space.")
            .Morph("TODO");

        yield return Probe("U-13a", "Undo a footnote and its typed text")
            .In("plain")
            .Steps("select(P1:5)", "Ctrl+Alt+F", "type(\"n\")", "undo", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Story)
            .Hypotheses("The first undo removes 'n' with the caret still in the footnote; the second removes the footnote and returns the caret to P1:5.")
            .Morph("PARTIAL");

        yield return Probe("U-13b", "Undo a comment and its typed text")
            .In("plain")
            .Steps("select(P1:5)", "Ctrl+Alt+M", "type(\"c\")", "undo", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Story)
            .Hypotheses("As for the footnote: the typing first, then the comment, with the caret back in the body.")
            .Morph("PARTIAL");

        yield return Probe("U-14", "Undo and redo with nothing to undo or redo")
            .In("plain")
            .Steps("select(P1:5)", "{key}")
            .Axis("key", "undo", "Ctrl+Y")
            .Hypotheses("Nothing changes; Ctrl+Y with no last action does nothing.")
            .Morph("DONE");

        yield return Probe("U-15", "Undo typing over a selection")
            .In("words")
            .Steps("select(P1:6-10)", "type(\"x\")", "undo")
            .Trace()
            .Tracked()
            .Hypotheses("(a) one undo restores 'beta' and removes 'x'; (b) two entries, the deletion and the typing, so one undo leaves the selection deleted.")
            .Morph("PARTIAL");

        yield return Probe("U-16a", "Undo Shift+F3 case cycling")
            .In("sentences")
            .Steps("select(P1:0-P1:12)", "Shift+F3", "undo")
            .Trace()
            .Tracked()
            .Hypotheses("One entry (Change Case) restores the text.")
            .Morph("UNKNOWN");

        yield return Probe("U-16b", "Undo Ctrl+Space")
            .In("mixed")
            .Steps("select(P1:0-P1:11)", "Ctrl+Space", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("One entry restores the bold and italic runs.")
            .Morph("UNKNOWN");

        yield return Probe("U-16c", "Undo Ctrl+Q")
            .In("kinds")
            .Steps("select(P4:2)", "Ctrl+Q", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("One entry restores the centring.")
            .Morph("UNKNOWN");

        yield return Probe("U-16d", "Undo Format Painter")
            .In("mixed")
            .Steps("select(P1:0-P1:6)", "Ctrl+Shift+C", "select(P1:12-P1:17)", "Ctrl+Shift+V", "undo")
            .Trace()
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("One entry; the copy (Ctrl+Shift+C) wrote none.")
            .Morph("UNKNOWN");
    }
}
