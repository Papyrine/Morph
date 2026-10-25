using static Define;

// C — the clipboard and drag and drop: what copy, cut and paste do with text, paragraph marks, whole
// paragraphs, list items, tables, pictures and the other stories, under each paste option; smart cut
// and paste spacing; and the undo entries each leaves. Every scenario copies from one place of a
// fixture and pastes in another place of the same fixture, since the harness opens one document.
static class ClipboardFamily
{
    public static FamilyDefinition Definition { get; } = new("clipboard", "Clipboard and drag and drop",
        "Copy, cut and paste of text, paragraph marks, paragraphs, list items, tables and pictures; smart cut and paste.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("C-01", "Paste a word inside a paragraph, with smart cut and paste on and off")
            .In("mixed")
            .Steps("select(P1:0-P1:6)", "Ctrl+C", "select({target})", "Ctrl+V")
            .Axis("target", "P1:find(\"plain\")", "P1:find(\"soft\"):end", "P1:9", "P1:0", "P1:end", "P2:0")
            .Toggle("Options.PasteSmartCutPaste", "true", "false")
            .Tracked()
            .Also(Observe.Formatting)
            .Notes("P1:0-6 is the bold word 'Strong' with no space. P1:9 is inside 'soft'; P2:0 starts the Times New Roman paragraph.")
            .Hypotheses("With the option on a space is added before or after the pasted word where it lands against another word, and none inside a word; the pasted run stays bold under the default paste option.")
            .Morph("PARTIAL");

        yield return Probe("C-02", "Paste text that ends in a paragraph mark")
            .In("kinds")
            .Steps("select(P2:4-P3:0)", "Ctrl+C", "select({target})", "Ctrl+V")
            .Axis("target", "P1:3", "P1:0", "P1:end", "P7:0")
            .Branch("undo", S("undo"))
            .Branch("type", S("type(\"x\")"))
            .Branch("backspace", S("Backspace"))
            .Tracked()
            .Also(Observe.Formatting | Observe.Lists | Observe.Markup)
            .Notes("P2 is the Heading 1 'Heading kind'; P2:4-P3:0 is 'ing kind' plus its mark. P7 is the empty paragraph.")
            .Hypotheses("(a) the pasted mark carries Heading 1 to the text before it, so the destination's first half becomes a heading; (b) the destination paragraph's properties win and the mark only splits; (c) an empty destination is replaced by the pasted paragraph.")
            .Morph("PARTIAL");

        yield return Probe("C-03", "Paste a whole paragraph with its mark, per paste option")
            .In("kinds")
            .Steps("select(P2:0-P3:0)", "Ctrl+C", "select({target})", "Ctrl+V")
            .Axis("target", "P1:3", "P1:0", "P1:end", "P7:0")
            .Toggle("Options.PasteFormatWithinDocument", "0", "1", "2")
            .Tracked()
            .Also(Observe.Formatting | Observe.Lists | Observe.Markup)
            .Notes("WdPasteOptions: 0 keeps the source formatting, 1 merges with the destination, 2 keeps text only.")
            .Hypotheses("(a) under 0 the heading arrives whole and the destination splits around it; (b) under 1 the pasted paragraph takes the destination's style; (c) under 2 the mark is kept but the style dropped, or the mark dropped too.")
            .Morph("PARTIAL");

        yield return Probe("C-04", "Paste two paragraphs whose last is partial")
            .In("three-paragraphs")
            .Steps("select(P1:0-P2:4)", "Ctrl+C", "select(P3:5)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("(a) the first pasted paragraph keeps its own properties and the partial last joins the destination paragraph, taking its properties; (b) the partial last carries the centring of P2.")
            .Morph("PARTIAL");

        yield return Probe("C-05a", "Paste a list item with its mark into another list and into a Normal paragraph")
            .In("numbers-two-lists")
            .Steps("select(P2:0-P3:0)", "Ctrl+C", "select({target})", "Ctrl+V")
            .Axis("target", "P5:end", "P3:5", "P3:0")
            .Toggle("Options.PasteMergeLists", "true", "false")
            .Tracked()
            .Also(Observe.Lists | Observe.Markup)
            .Notes("P2 is item 2 of the first list; P5 is the last item of the second list; P3 is the Normal paragraph between them.")
            .Hypotheses("(a) with merge on the item joins the destination list and takes its numbering; off, it keeps its own list and restarts; (b) pasted into the Normal paragraph the item makes it numbered from the mark back.")
            .Morph("PARTIAL");

        yield return Probe("C-05b", "Paste a Normal paragraph between two list items")
            .In("numbers-two-lists")
            .Steps("select(P3:0-P4:0)", "Ctrl+C", "select(P2:0)", "Ctrl+V")
            .Toggle("Options.PasteMergeLists", "true", "false")
            .Tracked()
            .Also(Observe.Lists | Observe.Markup)
            .Hypotheses("(a) the list is split and the numbering continues across the Normal paragraph; (b) the pasted paragraph becomes an item.")
            .Morph("PARTIAL");

        yield return Probe("C-05c", "Paste item text without its mark into another item")
            .In("numbers-two-lists")
            .Steps("select(P1:0-P1:3)", "Ctrl+C", "select(P5:2)", "Ctrl+V")
            .Toggle("Options.PasteMergeLists", "true", "false")
            .Tracked()
            .Also(Observe.Lists | Observe.Markup)
            .Hypotheses("Text without a mark changes no list: 'One' lands inside 'Beta' and the numbering stands.")
            .Morph("PARTIAL");

        yield return Probe("C-06", "Paste into an empty paragraph")
            .In("empty-paragraphs")
            .Steps("select({source})", "Ctrl+C", "select(P3:0)", "Ctrl+V")
            .Axis("source", "P1:0-P1:5", "P1:0-P2:0", "P1:0-P3:0")
            .Tracked()
            .Also(Observe.Markup)
            .Notes("P1:0-5 is 'Above'; P1:0-P2:0 adds its mark; P1:0-P3:0 adds the empty P2 too. P3 is the second empty paragraph.")
            .Hypotheses("Text fills the empty paragraph; text with a mark (a) replaces the empty paragraph (b) is inserted before it, leaving the empty one after.")
            .Morph("PARTIAL");

        yield return Probe("C-07a", "Paste a whole table into a paragraph")
            .In("table-2x2")
            .Steps("click(tableHandle T1)", "Ctrl+C", "select({target})", "Ctrl+V")
            .Axis("target", "P2:5", "P2:0", "P2:end")
            .Tracked()
            .Also(Observe.Tables)
            .Notes("P2 is 'After the table.'")
            .Hypotheses("(a) the paragraph is split around the table, with an empty remainder after it when pasted at the end; (b) at P2:0 the table lands before the paragraph with no split.")
            .Morph("TODO");

        yield return Probe("C-07b", "Paste a whole table into the empty paragraph between two tables")
            .In("tables-adjacent")
            .Steps("select(T1)", "Ctrl+C", "select(P2:0)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) the empty paragraph is consumed and three tables run into one; (b) the paragraph is kept and the pasted table stands alone; (c) the pasted table merges with the table above only.")
            .Morph("TODO");

        yield return Probe("C-08a", "Paste a whole table into one of its own cells")
            .In("table-3x3")
            .Steps("select(T1)", "Ctrl+C", "select(C(2,2):0)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) a nested table in cell (2,2); (b) the cells are merged into the grid from (2,2) outward, growing the table.")
            .Morph("TODO");

        yield return Probe("C-08b", "Paste a block of cells into a cell")
            .In("table-3x3")
            .Steps("select(C(1,1):0-C(2,2):0)", "Ctrl+C", "select(C(2,2):0)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) the 2x2 block overwrites (2,2)-(3,3); (b) a nested table; (c) rows are inserted.")
            .Morph("TODO");

        yield return Probe("C-08c", "Paste a row into a cell")
            .In("table-3x3")
            .Steps("select(R1)", "Ctrl+C", "select(C(2,1):0)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) a row is inserted above row 2; (b) row 2's cells are overwritten; (c) nested.")
            .Morph("TODO");

        yield return Probe("C-08d", "Paste a column into a cell")
            .In("table-3x3")
            .Steps("select(COL1)", "Ctrl+C", "select(C(1,2):0)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) a column is inserted to the left of column 2; (b) column 2 is overwritten.")
            .Morph("TODO");

        yield return Probe("C-09", "Paste a row or a cell into the body")
            .In("table-3x3")
            .Steps("{source}", "Ctrl+C", "select(P2:5)", "Ctrl+V")
            .Axis("source", "click(rowBar R2)", "select(C(1,1):0-C(1,1):mark)")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) a new one-row (or one-cell) table splits the paragraph; (b) the cell pastes as text; (c) a tab-separated line.")
            .Morph("TODO");

        yield return Probe("C-10a", "Paste an inline picture into another paragraph")
            .In("image-inline")
            .Steps("click(IMG1)", "Ctrl+C", "select(P2:3)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Shapes)
            .Hypotheses("A second inline picture at P2:3, the first unchanged, the pasted one selected or an IP after it.")
            .Morph("TODO");

        yield return Probe("C-10b", "Paste a floating picture at another paragraph")
            .In("image-floating")
            .Steps("click(SHP1)", "Ctrl+C", "select(P3:0)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Shapes)
            .Hypotheses("(a) a second floating picture anchored to P3 at the same offsets; (b) it is pasted inline.")
            .Morph("TODO");

        yield return Probe("C-11a", "Paste header text into the body")
            .In("header-footer")
            .Steps("seek(header)", "select(HDR:P1:0-HDR:P1:6)", "Ctrl+C", "seek(main)", "select(P1:4)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Story | Observe.Formatting)
            .Hypotheses("'Header' lands at P1:4 in the body's own style, since no mark came with it.")
            .Morph("UNKNOWN");

        yield return Probe("C-11b", "Paste body text into the header")
            .In("header-footer")
            .Steps("select(P1:0-P1:4)", "Ctrl+C", "seek(header)", "select(HDR:P1:end)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Story | Observe.Formatting)
            .Hypotheses("'Body' lands at the header's end in the Header style.")
            .Morph("UNKNOWN");

        yield return Probe("C-11c", "Paste footnote text into the body")
            .In("footnote")
            .Steps("seek(footnotes)", "select(FN1:P1:0-FN1:P1:4)", "Ctrl+C", "seek(main)", "select(P2:0)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Story | Observe.Formatting)
            .Hypotheses("'Note' lands at P2:0 (a) in Normal (b) keeping the Footnote Text size as direct formatting.")
            .Morph("UNKNOWN");

        // C-12: tracking is turned on as a step; no Tracked repeat.
        yield return Probe("C-12a", "Tracked paste over a selection")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P3:0-P3:5)", "Ctrl+C", "select(P1:0-P1:5)", "Ctrl+V")
            .Also(Observe.Revisions | Observe.Markup)
            .Hypotheses("The replaced text is a deletion and the pasted text an insertion, both by the probe author.")
            .Morph("UNKNOWN");

        yield return Probe("C-12b", "Tracked cut")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P1:0-P1:5)", "Ctrl+X")
            .Also(Observe.Revisions | Observe.Markup)
            .Hypotheses("The text stays as a tracked deletion; the clipboard holds it plain.")
            .Morph("UNKNOWN");

        yield return Probe("C-12c", "Tracked cut and paste of a word: a move or two revisions")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P1:0-P1:5)", "Ctrl+X", "select(P3:end)", "Ctrl+V")
            .Also(Observe.Revisions | Observe.Markup)
            .Hypotheses("(a) Word marks a move (moveFrom/moveTo) for any text; (b) only a whole sentence or longer is a move, a word is a deletion plus an insertion.")
            .Morph("UNKNOWN");

        yield return Probe("C-12d", "Tracked cut and paste of a whole paragraph")
            .In("three-paragraphs")
            .Steps("track(on)", "select(P1:0-P2:0)", "Ctrl+X", "select(P3:end)", "Ctrl+V")
            .Also(Observe.Revisions | Observe.Markup)
            .Hypotheses("A whole paragraph is marked as a move, with the mark's own revision.")
            .Morph("UNKNOWN");

        // C-13: every PasteFormatWithinDocument value Word accepts within one document (0 keep source, 1 use
        // destination styles, 2 text only); 3 (wdUseDestinationStylesRecovery) is refused for this option, so it is
        // not offered (the first run's attempt errored on every fixture).
        yield return Probe("C-13a", "PasteFormatWithinDocument on a bold run pasted into another font")
            .In("mixed")
            .Steps("select(P1:0-P1:6)", "Ctrl+C", "select(P2:0)", "Ctrl+V")
            .Toggle("Options.PasteFormatWithinDocument", "0", "1", "2")
            .Tracked()
            .Also(Observe.Formatting | Observe.Lists)
            .Notes("P1:0-6 is the bold 'Strong'; P2 starts in Times New Roman 14.")
            .Hypotheses("0 keeps the bold and the source font; 1 keeps the bold but takes the destination font; 2 drops both; 3 is the same as 1 for direct formatting.")
            .Morph("PARTIAL");

        yield return Probe("C-13b", "PasteFormatWithinDocument on a heading pasted at the document end")
            .In("kinds")
            .Steps("select(P2:0-P3:0)", "Ctrl+C", "select(P9:end)", "Ctrl+V")
            .Toggle("Options.PasteFormatWithinDocument", "0", "1", "2")
            .Tracked()
            .Also(Observe.Formatting | Observe.Lists)
            .Hypotheses("0 and 3 paste a Heading 1 paragraph; 1 pastes it as Normal; 2 pastes the text and a plain mark.")
            .Morph("PARTIAL");

        yield return Probe("C-13c", "PasteFormatWithinDocument on a list item pasted after the list's last item")
            .In("numbers")
            .Steps("select(P1:0-P2:0)", "Ctrl+C", "select(P3:end)", "Ctrl+V")
            .Toggle("Options.PasteFormatWithinDocument", "0", "1", "2")
            .Tracked()
            .Also(Observe.Formatting | Observe.Lists)
            .Hypotheses("0, 1 and 3 paste a fourth numbered item; 2 pastes 'One' and a Normal paragraph with no number.")
            .Morph("PARTIAL");

        // C-14: Ctrl+Shift+V (paste text only) per kind of content.
        yield return Probe("C-14a", "Ctrl+Shift+V of a bold run")
            .In("mixed")
            .Steps("select(P1:0-P1:6)", "Ctrl+C", "select(P2:end)", "Ctrl+Shift+V")
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("'Strong' arrives in the destination's font, not bold.")
            .Morph("PARTIAL");

        yield return Probe("C-14b", "Ctrl+Shift+V of a heading with its mark")
            .In("kinds")
            .Steps("select(P2:0-P3:0)", "Ctrl+C", "select(P9:end)", "Ctrl+Shift+V")
            .Tracked()
            .Also(Observe.Formatting)
            .Hypotheses("(a) the text and a Normal paragraph mark; (b) the mark is dropped and the text joins P9.")
            .Morph("PARTIAL");

        yield return Probe("C-14c", "Ctrl+Shift+V of two list items")
            .In("numbers")
            .Steps("select(P1:0-P3:0)", "Ctrl+C", "select(P3:end)", "Ctrl+Shift+V")
            .Tracked()
            .Also(Observe.Formatting | Observe.Lists)
            .Hypotheses("(a) the items arrive without numbers, as Normal paragraphs; (b) as literal '1.' and '2.' text; (c) they continue the list as items 4 and 5.")
            .Morph("PARTIAL");

        yield return Probe("C-14d", "Ctrl+Shift+V of a table")
            .In("table-2x2")
            .Steps("select(T1)", "Ctrl+C", "select(P2:end)", "Ctrl+Shift+V")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("The table arrives as tab-separated lines, one paragraph per row.")
            .Morph("TODO");

        yield return Probe("C-14e", "Ctrl+Shift+V of an inline picture")
            .In("image-inline")
            .Steps("click(IMG1)", "Ctrl+C", "select(P2:end)", "Ctrl+Shift+V")
            .Tracked()
            .Also(Observe.Shapes)
            .Hypotheses("Nothing is pasted, or the key is refused with the picture on the clipboard.")
            .Morph("TODO");

        yield return Probe("C-14f", "Ctrl+Shift+V of a hyperlink")
            .In("hyperlink")
            .Steps("select(P1:4-P1:12)", "Ctrl+C", "select(P2:end)", "Ctrl+Shift+V")
            .Tracked()
            .Also(Observe.Formatting | Observe.Markup)
            .Notes("P1:4-12 is the link's display text 'the site'.")
            .Hypotheses("'the site' arrives as plain text with no HYPERLINK field.")
            .Morph("PARTIAL");

        yield return Probe("C-16", "Smart cut spacing after a cut")
            .In("words")
            .Steps("select({range})", "Ctrl+X")
            .Axis("range", "P1:6-P1:11", "P1:6-P1:10", "P1:0-P1:6", "P1:find(\"end\")-P1:end")
            .Toggle("Options.SmartCutPaste", "true", "false")
            .Tracked()
            .Notes("P1:6-11 is 'beta,' with the two spaces after it; P1:6-10 is 'beta' alone; P1:0-6 is 'alpha ' with its space; the last range is 'end.' with the space before it.")
            .Hypotheses("(a) with the option on the two spaces left by the cut collapse to one; (b) only when the cut took a trailing space; the trailing space before 'end.' (a) is removed (b) kept.")
            .Morph("TODO");

        yield return Probe("C-17", "Cut whole and partial paragraphs")
            .In("three-paragraphs")
            .Steps("select({range})", "Ctrl+X")
            .Axis("range", "P2:0-P3:0", "P2:0-P2:end", "P1:4-P2:0", "P3:2-DOC:end")
            .Tracked()
            .Also(Observe.Formatting)
            .Notes("P2:0-P3:0 is the centred paragraph with its mark; P2:0-P2:end is its text alone; P1:4-P2:0 ends with P1's mark; P3:2-DOC:end reaches the final mark, which cannot be removed.")
            .Hypotheses("Cutting a paragraph with its mark removes it whole; cutting its text leaves an empty centred paragraph; cutting P1's mark gives the merged paragraph P2's centring or P1's; the final mark stays and keeps its properties.")
            .Morph("PARTIAL");

        yield return Probe("C-18a", "Cut a row, a cell, the table or a column")
            .In("table-3x3")
            .Steps("{setup}", "Ctrl+X")
            .Axis("setup", "select(R2)", "select(C(1,1):0-C(1,1):mark)", "select(T1)", "select(COL2)")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) a selected row, column or table is cut as structure, so the grid shrinks; (b) only the contents are cleared; a single cell (a) is cleared (b) is removed and the row shifts.")
            .Morph("TODO");

        yield return Probe("C-18b", "Cut every cell selected by a drag")
            .In("table-3x3")
            .Steps("drag(C(1,1):0→C(3,3):end)", "Ctrl+X")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("(a) the table is removed; (b) the cells are emptied and the table stays.")
            .Morph("TODO");

        yield return Probe("C-19a", "Copy and cut with nothing selected")
            .In("plain")
            .Steps("select(P1:3)", "Ctrl+C", "Ctrl+X")
            .Hypotheses("Nothing changes and no undo entry is written.")
            .Morph("DONE");

        yield return Probe("C-19b", "Paste an empty clipboard")
            .In("plain")
            .Steps("clipboard(\"\")", "select(P1:3)", "Ctrl+V")
            .Hypotheses("Nothing changes and no undo entry is written.")
            .Morph("DONE");

        yield return Probe("C-20", "Paste over a selection")
            .In("words")
            .Steps("clipboard(\"pasted\")", "select(P1:6-P1:10)", "Ctrl+V")
            .Tracked()
            .Hypotheses("'beta' is replaced by 'pasted' in one undo entry, with an IP after the pasted text.")
            .Morph("PARTIAL");

        yield return Probe("C-21", "Paste a paragraph carrying borders into a Normal paragraph")
            .In("kinds")
            .Steps("select(P5:0-P6:0)", "Ctrl+C", "select(P1:3)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Markup | Observe.Formatting)
            .Notes("P5 is the bordered paragraph; the copy includes its mark.")
            .Hypotheses("The borders travel with the mark: 'Nor' plus 'Bordered kind.' becomes the bordered paragraph and 'mal kind.' follows unbordered.")
            .Morph("UNKNOWN");

        yield return Probe("C-22", "Drag and drop a word: the undo entry and the spacing")
            .In("words")
            .Steps("dblclick(P1:find(\"beta\"))", "{drag}")
            .Axis("drag", "drag(P1:find(\"beta\")-P1:find(\"beta\"):end→P1:find(\"1,000\"))", "ctrl+drag(P1:find(\"beta\")-P1:find(\"beta\"):end→P1:find(\"1,000\"))")
            .Toggle("Options.SmartCutPaste", "true", "false")
            .Tracked()
            .Hypotheses("A double click selects 'beta' with its trailing space; the drop before '1,000' (a) adjusts the spaces at both ends with the option on (b) leaves them; the undo entry is Move for the plain drag and Copy for the Ctrl drag.")
            .Morph("TODO");

        yield return Probe("C-23a", "Paste text that carries a footnote reference")
            .In("footnote")
            .Steps("select(P1:0-P1:5)", "Ctrl+C", "select(P2:0)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Markup | Observe.Story)
            .Notes("P1:0-5 is 'Text' and its footnote reference.")
            .Hypotheses("A second footnote with the same text is made and the references renumber.")
            .Morph("UNKNOWN");

        yield return Probe("C-23b", "Paste a bookmarked or commented range")
            .In("{fixture}")
            .Steps("select(P1:10-P1:19)", "Ctrl+C", "select(P2:0)", "Ctrl+V")
            .Axis("fixture", "bookmark", "comment")
            .Tracked()
            .Also(Observe.Markup | Observe.Story)
            .Notes("P1:10-19 is 'brown fox', the bookmarked (or commented) range.")
            .Hypotheses("(a) the bookmark is not copied (a name can exist once); (b) the comment is copied with its anchor, so there are two comments.")
            .Morph("UNKNOWN");

        yield return Probe("C-23c", "Paste a paragraph holding a content control")
            .In("content-control")
            .Steps("select(P1:0-P1:end)", "Ctrl+C", "select(P4:end)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Markup | Observe.Story)
            .Hypotheses("The rich text control is copied as a control; pasted after the dropdown's paragraph it stands at P4's end.")
            .Morph("UNKNOWN");

        yield return Probe("C-24", "Paste a hyperlink, with and without its formatting")
            .In("hyperlink")
            .Steps("select(P1:4-P1:12)", "Ctrl+C", "select(P2:0)", "{key}")
            .Axis("key", "Ctrl+V", "Ctrl+Shift+V")
            .Tracked()
            .Also(Observe.Formatting | Observe.Markup)
            .Notes("P1:4-12 is the link's display text 'the site', bold.")
            .Hypotheses("Ctrl+V pastes a working link, bold; Ctrl+Shift+V pastes 'the site' as plain text with no field.")
            .Morph("PARTIAL");

        yield return Probe("C-25", "Copy a block selection and paste it")
            .In("long-paragraph")
            .Steps("alt+drag(P2:2→P2:60)", "Ctrl+C", "select(P3:0)", "Ctrl+V")
            .Tracked()
            .Hypotheses("The block pastes as one paragraph per line of the block, each carrying its slice.")
            .Morph("TODO");

        yield return Probe("C-26", "The Spike: Ctrl+F3 cuts to it and Ctrl+Shift+F3 empties it")
            .In("three-paragraphs")
            .Steps("select(P1:0-P1:5)", "Ctrl+F3", "select(P3:end)", "Ctrl+Shift+F3")
            .Tracked()
            .Hypotheses("'First' is cut from P1 and inserted at P3's end as its own paragraph; the clipboard is untouched.")
            .Morph("TODO");

        yield return Probe("C-27", "Ctrl+Alt+V opens Paste Special")
            .In("plain")
            .Steps("clipboard(\"pasted\")", "select(P1:3)", "dialog(Ctrl+Alt+V)")
            .Hypotheses("The dialog opens and, dismissed, leaves the document and the undo list unchanged.")
            .Morph("TODO");

        // C-28 (plain clipboard text with newlines) is not expressible: type and clipboard strings take no \n.

        yield return Probe("C-29", "Paste plain text over a selected cell")
            .In("table-2x2")
            .Steps("clipboard(\"plain\")", "select(C(1,1):0-C(1,1):mark)", "Ctrl+V")
            .Tracked()
            .Also(Observe.Tables)
            .Hypotheses("The cell's text is replaced by 'plain' and the cell mark stays; the table is unchanged.")
            .Morph("TODO");
    }
}
