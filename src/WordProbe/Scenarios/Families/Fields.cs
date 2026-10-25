using static Define;

// F — fields, hyperlinks, bookmarks, content controls, comment anchors and hidden text: whether each is
// one atom to the caret and to Backspace and Delete, and what typing and Enter at its edges do to it.
// Positions in field-page and hyperlink count the hidden field code, so the rows use find("…") wherever
// the display text is the target.
static class FieldsFamily
{
    public static FamilyDefinition Definition { get; } = new("fields", "Fields, hyperlinks, bookmarks and content controls",
        "Whether a field is one atom to the caret and to Backspace; editing at a hyperlink's edges; bookmark and content control edges.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("F-01", "Right over a field, one press at a time")
            .In("field-page")
            .Steps("select({from})", "Right", "Right", "Right")
            .Axis("from", "P1:4", "P1:find(\" of\"):end")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Notes("P1:4 is before the space that precedes the PAGE field; find(\" of\"):end is before the space that precedes NUMPAGES.")
            .Hypotheses("(a) the whole field is one position: the second Right lands after it; (b) one position per result character; (c) the field is selected first and the next Right steps past it.")
            .Morph("PARTIAL");

        yield return Probe("F-01b", "Shift+Right over a field, one press at a time")
            .In("field-page")
            .Steps("select(P1:4)", "Shift+Right", "Shift+Right", "Shift+Right")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the selection grows over the whole field at once; (b) over one result character at a time.")
            .Morph("PARTIAL");

        yield return Probe("F-01c", "Backspace right after a field, then Esc or typing")
            .In("field-page")
            .Steps("select(P1:find(\" of\"))", "Backspace", "Backspace")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Branch("esc", S("Esc"))
            .Branch("type", S("type(\"y\")"))
            .Hypotheses("(a) the first Backspace selects the field and the second removes it whole; (b) the first Backspace removes it whole and the second eats the space before it; (c) one result character at a time. With the field selected, Esc keeps it and typing replaces it.")
            .Morph("PARTIAL");

        yield return Probe("F-01d", "Delete right before a field")
            .In("field-page")
            .Steps("select(P1:5)", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Notes("P1:5 is directly before the PAGE field's start.")
            .Hypotheses("(a) the field is selected; (b) the field goes whole; (c) one result character goes.")
            .Morph("PARTIAL");

        yield return Probe("F-01e", "Typing right after a field: what the character inherits")
            .In("field-page")
            .Steps("select(P1:find(\" of\"))", "type(\"x\")")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("The character lands outside the field, after its end, and takes the run formatting of the text after rather than the field result's.")
            .Morph("DONE");

        yield return Probe("F-01f", "Right over a field with its codes shown")
            .In("field-page")
            .Steps("select(P1:4)", "Alt+F9", "Right", "Right", "Alt+F9")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("With codes shown the caret enters the code and steps one character at a time; hiding the codes again leaves it after the field.")
            .Morph("TODO");

        yield return Probe("F-01g", "Right and Backspace at the start of a table of contents")
            .In("contents")
            .Steps("select(P1:0)", "Right", "Right", "Backspace")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the TOC is one atom: Right steps over it whole and Backspace selects it; (b) its entries are text to the caret and Backspace deletes a character of the entry.")
            .Morph("PARTIAL");

        yield return Probe("F-01h", "Delete inside a table of contents")
            .In("contents")
            .Steps("select(P2:0)", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the whole field is selected; (b) one character of the entry is deleted and the field is left dirty.")
            .Morph("PARTIAL");

        yield return Probe("F-02", "Delete at the start of a hyperlink's text, twice")
            .In("hyperlink")
            .Steps("select(P1:find(\"the site\"))", "Delete", "Delete")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("(a) one display character goes per press and the link shrinks; (b) the first Delete selects the whole link.")
            .Morph("PARTIAL");

        yield return Probe("F-02b", "Backspace from the end of a hyperlink through it and past its start")
            .In("hyperlink")
            .Steps("select(P1:find(\"site\"):end)", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace", "Backspace")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Notes("'the site' is eight characters; the ninth Backspace faces the space before the link.")
            .Hypotheses("(a) the link is deleted character by character and, once its text is empty, (a1) the empty field is removed with the next press or (a2) a collapsed field remains; (b) the whole link goes on one press.")
            .Morph("PARTIAL");

        yield return Probe("F-02c", "Enter inside a hyperlink's text, then Backspace")
            .In("hyperlink")
            .Steps("select(P1:find(\"site\"))", "Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Branch("backspace", S("Backspace"))
            .Hypotheses("(a) the link is split into two fields with the same address, one per paragraph, and Backspace joins them back into one; (b) the link ends at the break and 'site' is plain; (c) the whole link moves to the second paragraph.")
            .Morph("PARTIAL");

        yield return Probe("F-02d", "Shift+Enter inside a hyperlink's text")
            .In("hyperlink")
            .Steps("select(P1:find(\"site\"))", "Shift+Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("A line break is a character inside the link, which stays one field.")
            .Morph("PARTIAL");

        yield return Probe("F-02e", "Ctrl+Space over a hyperlink's text")
            .In("hyperlink")
            .Steps("select(P1:find(\"the site\")-P1:find(\"the site\"):end)", "Ctrl+Space")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("(a) the bold goes and the Hyperlink character style goes with it, the field remaining; (b) the Hyperlink style is kept and only the direct bold is removed.")
            .Morph("TODO");

        yield return Probe("F-02f", "Typing over a selection that starts inside a hyperlink and ends after it")
            .In("hyperlink")
            .Steps("select(P1:find(\"site\")-P1:find(\"today\"):end)", "type(\"x\")")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("(a) the typed character lands inside the link, which now reads 'the x'; (b) it lands outside and the link ends at 'the '.")
            .Morph("PARTIAL");

        yield return Probe("F-03", "Delete over the whole of a bookmark's text")
            .In("bookmark")
            .Steps("select(P1:10-P1:19)", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the bookmark goes with its text; (b) a collapsed bookmark remains at P1:10.")
            .Morph("PARTIAL");

        yield return Probe("F-03b", "Cut and paste a bookmark's text elsewhere")
            .In("bookmark")
            .Steps("select(P1:10-P1:19)", "Ctrl+X", "select(P2:0)", "Ctrl+V")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the bookmark travels with the cut text to P2; (b) it stays collapsed in P1 and the paste carries none; (c) it is lost.")
            .Morph("PARTIAL");

        yield return Probe("F-03c", "Copy and paste a bookmark's text elsewhere")
            .In("bookmark")
            .Steps("select(P1:10-P1:19)", "Ctrl+C", "select(P2:0)", "Ctrl+V")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the paste carries no bookmark, since a name cannot be duplicated; (b) the bookmark moves to the copy.")
            .Morph("PARTIAL");

        yield return Probe("F-03d", "Enter inside a bookmark")
            .In("bookmark")
            .Steps("select(P1:15)", "Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("The bookmark spans the paragraph mark and covers both halves.")
            .Morph("PARTIAL");

        yield return Probe("F-03e", "Typing at a bookmark's start and at its end")
            .In("bookmark")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:10", "P1:19")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("A character typed at the start lands outside the bookmark and one typed at the end lands inside it (the end moves, the start does not).")
            .Morph("PARTIAL");

        yield return Probe("F-03f", "Backspace that merges the paragraph after a bookmark into it")
            .In("bookmark")
            .Steps("select(P2:0)", "Backspace")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("The bookmark is unchanged by the merge: it does not grow to cover the joined text.")
            .Morph("PARTIAL");

        yield return Probe("F-04", "Right into and out of a rich text content control, one press at a time")
            .In("content-control")
            .Steps("select({from})", "Right", "Right", "Right")
            .Axis("from", "P1:5", "P1:18")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Notes("The rich text control covers offsets 6-18 of 'Rich: content here end.'; P1:5 is before the space ahead of it and P1:18 its last inside position.")
            .Hypotheses("(a) a control has a before-start and an inside-start position (and an inside-end and after-end), so one Right stays in place visually; (b) no extra positions.")
            .Morph("UNKNOWN");

        yield return Probe("F-04b", "Shift+Right into a content control, one press at a time")
            .In("content-control")
            .Steps("select(P1:5)", "Shift+Right", "Shift+Right", "Shift+Right")
            .Trace()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the selection grows one character at a time into the control; (b) the whole control is selected when the selection reaches its edge.")
            .Morph("UNKNOWN");

        yield return Probe("F-04c", "Delete or type over the whole of a content control's text")
            .In("content-control")
            .Steps("select(P1:6-P1:18)", "{key}")
            .Axis("key", "Delete", "type(\"x\")")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("Delete (a) empties the control and its placeholder text returns, (b) removes the control; typing replaces the text and the control is kept.")
            .Morph("UNKNOWN");

        yield return Probe("F-04d", "Backspace at a content control's inside end and after its end")
            .In("content-control")
            .Steps("select({from})", "Backspace")
            .Axis("from", "P1:18", "P1:find(\" end\")")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("Inside, the last character goes; after the end, (a) the control is selected first, (b) its last character goes, (c) the control is removed.")
            .Morph("UNKNOWN");

        yield return Probe("F-04e", "Tab and Enter inside a rich text content control")
            .In("content-control")
            .Steps("select(P1:10)", "{key}")
            .Axis("key", "Tab", "Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("Tab (a) inserts a tab character, (b) moves to the next control; Enter splits the paragraph (a) inside the control, which then spans two paragraphs, (b) and the control is turned into a block control.")
            .Morph("UNKNOWN");

        yield return Probe("F-04f", "Enter inside a plain text content control")
            .In("content-control")
            .Steps("select(P2:9)", "Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) refused, since a plain text control without multiline allows no paragraph; (b) a line break is inserted; (c) the paragraph is split.")
            .Morph("UNKNOWN");

        yield return Probe("F-04g", "Space on a checkbox content control")
            .In("content-control")
            .Steps("select(P3:7)", "Space")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the box toggles to checked and the symbol changes; (b) a space is typed after it.")
            .Morph("UNKNOWN");

        yield return Probe("F-04h", "Down on a dropdown content control")
            .In("content-control")
            .Steps("select(P4:8)", "Down")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the caret moves down to the end of the document as in text; (b) the list opens and Down picks an entry.")
            .Morph("UNKNOWN");

        yield return Probe("F-04i", "Ctrl+A with the caret inside a content control")
            .In("content-control")
            .Steps("select(P1:10)", "Ctrl+A")
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) selects the control's contents only, a second press the document; (b) selects the whole document at once.")
            .Morph("UNKNOWN");

        yield return Probe("F-04j", "Cut a paragraph holding a content control and paste it after another control")
            .In("content-control")
            .Steps("select(P1:0-P1:end)", "Ctrl+X", "select(P4:end)", "Ctrl+V")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("The control travels with its text and is pasted after the dropdown's end, outside it.")
            .Morph("UNKNOWN");

        yield return Probe("F-05", "Typing at a comment anchor's start and at its end")
            .In("comment")
            .Steps("select({at})", "type(\"x\")")
            .Axis("at", "P1:10", "P1:19")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) a character typed at the end lands outside the anchor and one at the start outside too; (b) the end grows to take it.")
            .Morph("PARTIAL");

        yield return Probe("F-05b", "Delete over the whole of a comment's anchor")
            .In("comment")
            .Steps("select(P1:10-P1:19)", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the comment goes with its anchor text; (b) the comment stays on a collapsed anchor.")
            .Morph("PARTIAL");

        yield return Probe("F-05c", "Enter inside a comment's anchor")
            .In("comment")
            .Steps("select(P1:12)", "Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("The anchor spans the new paragraph mark and the comment is kept once.")
            .Morph("PARTIAL");

        yield return Probe("F-06", "Backspace over the PAGE field at the end of a footer")
            .In("header-footer")
            .Steps("seek(footer)", "select(FTR:P1:end)", "Backspace", "Backspace")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Hypotheses("The field is selected by the first Backspace and removed by the second, as in the body.")
            .Morph("PARTIAL");

        yield return Probe("F-07", "The caret inside a table of contents entry")
            .In("contents")
            .Steps("select(P1:3)")
            .Observing(Observe.Default | Observe.Markup)
            .Notes("No key: the record is where COM put the caret and what Word reports for the selection inside the field.")
            .Morph("PARTIAL");

        yield return Probe("F-07b", "Typing inside a table of contents entry")
            .In("contents")
            .Steps("select(P1:3)", "type(\"x\")")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the character lands in the entry and the field result is edited in place; (b) the whole field is selected first and replaced.")
            .Morph("PARTIAL");

        yield return Probe("F-07c", "F9 on a table of contents")
            .In("contents")
            .Steps("select(P1:3)", "dialog(F9)")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Notes("Word asks whether to update page numbers only or the entire table; the dialog is recorded and dismissed.")
            .Morph("TODO");

        yield return Probe("F-08", "Right over hidden text with marks hidden, one press at a time")
            .In("special")
            .Steps("select(P1:find(\" hidden\"))", "Right", "Right", "Right", "Right", "Right", "Right", "Right", "Right")
            .Trace()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("(a) the six hidden characters are skipped and the second Right lands after them; (b) stepped through one at a time.")
            .Morph("UNKNOWN");

        yield return Probe("F-08b", "Delete right before hidden text with marks hidden")
            .In("special")
            .Steps("select(P1:find(\"hidden\"))", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("(a) one hidden character is removed invisibly; (b) the hidden run is skipped and the space after it goes.")
            .Morph("UNKNOWN");

        yield return Probe("F-08c", "Hide text with Ctrl+Shift+H, then Right over it")
            .In("special")
            .Steps("select(P1:0-P1:5)", "Ctrl+Shift+H", "Right", "Right")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("The five characters become hidden; Right collapses the selection to its end and the next press moves one visible character on.")
            .Morph("UNKNOWN");

        yield return Probe("F-09", "Inserting a field by keys: Ctrl+F9, the code, F9")
            .In("plain")
            .Steps("select(P1:4)", "Ctrl+F9", "type(\"DATE\")", "F9")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("Ctrl+F9 inserts empty field braces with the caret between them and codes shown for that field; F9 evaluates it and shows the result.")
            .Morph("TODO");

        yield return Probe("F-10", "Ctrl+K on a text selection")
            .In("plain")
            .Steps("select(P1:4-P1:12)", "dialog(Ctrl+K)")
            .Observing(Observe.Default | Observe.Markup)
            .Notes("The Insert Hyperlink dialog is recorded and dismissed; the document and the undo list are expected unchanged.")
            .Morph("TODO");

        yield return Probe("F-10b", "Ctrl+K inside an existing hyperlink")
            .In("hyperlink")
            .Steps("select(P1:find(\"site\"))", "dialog(Ctrl+K)")
            .Observing(Observe.Default | Observe.Markup)
            .Notes("The Edit Hyperlink dialog is recorded and dismissed; the selection afterwards shows whether Word selected the whole link to edit it.")
            .Morph("TODO");

        yield return Probe("F-11", "Enter inside a hyperlink to a bookmark, then Backspace, with a wait for the link to be followed")
            .In("hyperlink-internal")
            .Steps("select(P1:find(\"site\"))", "Enter", "wait(1500)")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Branch("backspace", S("Backspace", "wait(1500)"))
            .Notes("F-02c's Enter changed nothing in either mode, yet its tracked Backspace branch lost the foreground to the browser every time. The link here points at the bookmark on 'Target' in P2, so following it moves the caret to P2 instead of opening a browser, and the wait gives a delayed follow time to show.")
            .Hypotheses("(a) Enter inside the link follows it under Track Changes only, the caret landing at the bookmark in P2, and did so in F-02c too with the browser arriving after the case had ended; (b) Enter is a no-op and Backspace is what follows the link under tracking; (c) neither key follows an internal link and F-02c's foreground loss has another cause.")
            .Morph("PARTIAL");

        yield return Probe("F-11b", "Enter with the whole hyperlink field selected")
            .In("hyperlink-internal")
            .Steps("select(P1:find(\"the site\")-P1:find(\"the site\"):end)", "Enter", "wait(1500)")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("(a) Enter over the selected display text replaces it with a paragraph mark, as over any selection; (b) with the field selected Enter follows the link to the bookmark.")
            .Morph("PARTIAL");
    }
}
