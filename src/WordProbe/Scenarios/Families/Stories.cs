using static Define;

// H — the other stories: entering and leaving headers and footers, their edges, the note reference as a
// character, keys inside a note, and comments.
//
// Rows of the catalogue with no scenario here, and why:
//   H-10  a linked header in section 2 — section-break carries no header text.
//   H-13 and H-14 are covered by other families.
static class StoriesFamily
{
    public static FamilyDefinition Definition { get; } = new("stories", "Headers, footers, notes, comments and text boxes",
        "Entering and leaving the other stories, their edges, and the note reference as a character.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("H-01", "Double-click into the header or footer, then Esc or a double-click on the body")
            .In("header-footer")
            .Steps("select(P1:5)", "dblclick({area}:P1:0)")
            .Axis("area", "HDR", "FTR")
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Branch("esc", S("Esc"))
            .Branch("body", S("dblclick(P1:5)"))
            .Hypotheses("The double-click opens the header or footer story with the caret at its start; (a) Esc closes header editing and returns the caret to the body; (b) Esc does nothing and only the double-click on the body leaves.")
            .Morph("TODO");

        yield return Probe("H-02", "Typing, Enter and Tab in the header")
            .In("header-footer")
            .Steps("seek(header)", "select(HDR:P1:end)", "type(\" x\")", "Enter", "Tab", "Tab")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Formatting)
            .Tracked()
            .Hypotheses("The header edits like the body; the new paragraph keeps the Header style, whose centre and right tab stops take the two Tabs.")
            .Morph("DONE");

        yield return Probe("H-03", "Story edges in the header")
            .In("header-footer")
            .Steps("seek(header)", "select(HDR:P1:0)", "{key}")
            .Axis("key", "Up", "Down", "Ctrl+Home", "Ctrl+End", "PgDn")
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Hypotheses("(a) every key stays in the header story, Down on its last line going to its end; (b) Down enters the footer; PgDn (a) goes to the next page's header (b) stays.")
            .Morph("UNKNOWN");

        yield return Probe("H-04", "A single click on the header area from the body")
            .In("header-footer")
            .Steps("select(P1:5)", "click(HDR:P1:0)")
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Hypotheses("The caret stays in the body at P1:5; the header needs a double-click.")
            .Morph("UNKNOWN");

        yield return Probe("H-05", "Right and Left over a footnote reference")
            .In("footnote")
            .Steps("select(P1:4)", "Right", "Left")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Notes("The reference follows 'Text' at P1:4.")
            .Hypotheses("The reference is one position to the caret.")
            .Morph("PARTIAL");

        yield return Probe("H-05b", "Shift+Right over a footnote reference")
            .In("footnote")
            .Steps("select(P1:4)", "Shift+Right")
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Hypotheses("The selection takes the reference alone, Start 4 and End 5.")
            .Morph("PARTIAL");

        yield return Probe("H-05c", "Backspace and Delete at a footnote and an endnote reference, twice")
            .In("footnote")
            .Steps("select({from})", "{key}", "{key}")
            .Axis("from", "P1:5", "P1:4", "P2:end")
            .Axis("key", "Backspace", "Delete")
            .Trace()
            .Observing(Observe.Default | Observe.Story)
            .Tracked()
            .Notes("P1:5 is after the footnote reference, P1:4 before it; P2:end is after the endnote reference.")
            .Hypotheses("(a) one press deletes the reference and its note; (b) the first press selects the reference and the second deletes it. Delete at P2:end takes the paragraph mark, not the reference.")
            .Morph("PARTIAL");

        yield return Probe("H-06", "Double-click the footnote reference, then the note's own reference")
            .In("footnote")
            .Steps("dblclick(P1:4)")
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Branch("back", S("dblclick(FN1:P1:0)"))
            .Hypotheses("The first double-click opens the footnote story at the note's text; the second, on the note's reference mark, returns to the body after the reference.")
            .Morph("TODO");

        yield return Probe("H-07", "Keys at the end of a footnote's text")
            .In("footnote")
            .Steps("seek(footnotes)", "select(FN1:P1:end)", "{key}")
            .Axis("key", "Enter", "Up", "Down", "Ctrl+A", "Ctrl+Home")
            .Observing(Observe.Default | Observe.Story)
            .Tracked()
            .Hypotheses("Enter adds a paragraph to the note; Up and Down stay in the footnote story; Ctrl+A selects (a) the note's text (b) every footnote; Ctrl+Home goes to (a) the note's start (b) the first footnote.")
            .Morph("PARTIAL");

        yield return Probe("H-07b", "Backspace and Delete at the start of a footnote and over its whole text")
            .In("footnote")
            .Steps("seek(footnotes)", "select({from})", "{key}")
            .Axis("from", "FN1:P1:0", "FN1:P1:0-FN1:P1:end")
            .Axis("key", "Backspace", "Delete")
            .Observing(Observe.Default | Observe.Story)
            .Tracked()
            .Hypotheses("Backspace at the note's start, after its reference mark, (a) does nothing (b) deletes the mark; deleting the whole text leaves an empty note whose mark and reference stay.")
            .Morph("PARTIAL");

        yield return Probe("H-08", "Typing, Enter and Esc in a comment")
            .In("comment")
            .Steps("select(CMT1:P1:0)", "type(\"x\")", "Enter", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Hypotheses("The comment edits like a paragraph; Esc (a) returns the caret to the body at the comment's anchor; (b) stays in the comment.")
            .Morph("PARTIAL");

        yield return Probe("H-08b", "The commented text deleted")
            .In("comment")
            .Steps("select(P1:10-P1:19)", "Delete")
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Notes("P1:10-19 is 'brown fox', the comment's whole anchor.")
            .Hypotheses("(a) the comment survives on a collapsed anchor at 10; (b) the comment is deleted with its text. Under tracking the text is struck and the comment stays.")
            .Morph("PARTIAL");

        yield return Probe("H-09", "Backspace and Delete at the header's edges")
            .In("header-footer")
            .Steps("seek(header)", "select({from})", "{key}")
            .Axis("from", "HDR:P1:0", "HDR:P1:end")
            .Axis("key", "Backspace", "Delete")
            .Observing(Observe.Default | Observe.Story)
            .Tracked()
            .Hypotheses("Backspace at the story's start and Delete before its last mark do nothing; the other two take one character.")
            .Morph("UNKNOWN");

        yield return Probe("H-09b", "Ctrl+A then Delete in the header")
            .In("header-footer")
            .Steps("seek(header)", "select(HDR:P1:0)", "Ctrl+A", "Delete")
            .Observing(Observe.Default | Observe.Story)
            .Tracked()
            .Hypotheses("Ctrl+A selects the header story only; Delete leaves one empty paragraph in it and the body untouched.")
            .Morph("UNKNOWN");

        yield return Probe("H-11", "Ctrl+Alt+M in a header")
            .In("header-footer")
            .Steps("seek(header)", "select(HDR:P1:2)", "Ctrl+Alt+M")
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Hypotheses("A comment is added on the header text and the caret moves into the new comment's story.")
            .Morph("UNKNOWN");

        yield return Probe("H-11b", "Ctrl+Alt+M in a footnote")
            .In("footnote")
            .Steps("seek(footnotes)", "select(FN1:P1:2)", "Ctrl+Alt+M")
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) a comment is added on the note's text; (b) the key is refused in a note.")
            .Morph("UNKNOWN");

        yield return Probe("H-12", "Typing in a comment with Track Changes on")
            .In("comment")
            .Steps("track(on)", "select(CMT1:P1:0)", "type(\"x\")")
            .Observing(Observe.Default | Observe.Revisions | Observe.Story)
            .Hypotheses("(a) the typed letter is a tracked insertion inside the comment; (b) comment text is never tracked.")
            .Morph("PARTIAL");

        yield return Probe("H-15", "Typing at the end of the footer, after the PAGE field")
            .In("header-footer")
            .Steps("seek(footer)", "select(FTR:P1:end)", "type(\" x\")")
            .Observing(Observe.Default | Observe.Story | Observe.Formatting)
            .Tracked()
            .Hypotheses("The text lands after the field result, outside the field.")
            .Morph("PARTIAL");

        yield return Probe("H-15b", "Backspace and Delete around the footer's PAGE field, twice")
            .In("header-footer")
            .Steps("seek(footer)", "select({from})", "{key}", "{key}")
            .Axis("from", "FTR:P1:5", "FTR:P1:end")
            .Axis("key", "Backspace", "Delete")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Formatting)
            .Tracked()
            .Notes("FTR:P1:5 is between 'Page ' and the field; FTR:P1:end is after the field.")
            .Hypotheses("(a) one press at the field's edge selects the whole field and the second deletes it; (b) one press deletes it. The press on the other side takes the space or does nothing.")
            .Morph("PARTIAL");

        // ---- The comment card, driven on purpose -----------------------------------------------------------
        // E-104 and H-08 sent their letter 40 ms after Ctrl+Alt+M and it landed in the body before the card had
        // focus (the card is a pane control that opens asynchronously; the document's Selection does not move
        // into it). These rows wait for the card, type into it, and leave it by each of the keys a user has.

        yield return Probe("H-16", "Ctrl+Alt+M, a wait for the card, typing into it, then Ctrl+Enter, Esc or Enter")
            .In("plain")
            .Steps("select({range})", "Ctrl+Alt+M", "wait(1500)", "type(\"Note\")", "wait(300)")
            .Axis("range", "P1:6", "P1:4-P1:9", "P1:end")
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Branch("post", S("Ctrl+Enter", "wait(500)"), Node("esc", S("Esc")), Node("type", S("type(\"z\")")))
            .Branch("esc", S("Esc"))
            .Branch("enter", S("Enter", "type(\"more\")", "Ctrl+Enter", "wait(500)"))
            .Tracked()
            .Notes("The root case leaves the draft card open with 'Note' typed and nothing posted. P1:6 is inside 'quick', P1:4-P1:9 is 'quick' selected, P1:end is after the full stop. Not traced: a traced first run showed that reading the selection over COM between steps takes focus off the card, so Ctrl+Enter never posted and Enter split the body paragraph; H-17, untraced, posted.")
            .Hypotheses("The typed text goes into the card and the body is unchanged; the comment does not exist for COM until it is posted, so the root case records no comment. Ctrl+Enter posts it: the scope is the word at an insertion point, the selection over a selection, and at a paragraph's end (a) the word before (b) an empty scope. After posting, Esc (a) returns the caret to the body at the scope's end (b) selects the scope; typing after posting (a) goes into the card as an edit (b) lands in the body. Esc on the draft (a) discards it (b) leaves the draft open with focus in the body. Enter inside the card (a) starts a second paragraph of the comment (b) posts it.")
            .Morph("PARTIAL");

        yield return Probe("H-17", "A posted comment, then its anchor deleted, backspaced into or typed into")
            .In("plain")
            .Steps("select(P1:4-P1:9)", "Ctrl+Alt+M", "wait(1500)", "type(\"Note\")", "wait(300)", "Ctrl+Enter", "wait(500)", "Esc")
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Branch("delete-anchor", S("select(P1:4-P1:9)", "Delete"))
            .Branch("backspace-end", S("select(P1:9)", "Backspace"))
            .Branch("type-inside", S("select(P1:6)", "type(\"x\")"))
            .Branch("type-after", S("select(P1:9)", "type(\"x\")"))
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses("Deleting the whole anchor deletes the comment (H-08b, F-05b found so for a fixture comment); Backspace at the scope's end shrinks the scope by one character and keeps the comment; typing inside grows the scope; typing right after the scope's end lands outside it (F-05); undo right after posting (a) removes the comment (b) removes the typed text first. Tracked, the anchor's deletion is struck and the comment stays.")
            .Morph("PARTIAL");

        yield return Probe("H-18", "Reaching an existing comment's card by keyboard: Review Next, F6, or a second comment on its scope")
            .In("comment")
            .Steps("select(P1:12)")
            .Observing(Observe.Default | Observe.Story | Observe.Markup)
            .Branch("next-type", S("Alt+R", "N", "wait(800)", "type(\"x\")", "wait(300)", "Ctrl+Enter", "wait(500)", "Esc"))
            .Branch("f6-type", S("F6", "wait(500)", "type(\"x\")", "wait(300)", "Esc"))
            .Branch("second", S("Ctrl+Alt+M", "wait(1500)", "type(\"Two\")", "Ctrl+Enter", "wait(500)", "Esc"))
            .Tracked()
            .Notes("P1:12 is inside the comment's scope ('brown fox'). Alt+R then N is Review › Next comment by key tips; F6 cycles the window's panes. Not traced, for the reason H-16 gives: a traced first run typed every letter into the body.")
            .Hypotheses("Review › Next moves focus to the card: typing then (a) starts a reply, posted by Ctrl+Enter (b) edits the comment's text (c) lands in the body. F6 (a) reaches the Comments pane and typing lands in the card (b) reaches the ribbon or status bar and the typed letter is lost. Ctrl+Alt+M inside an existing scope adds a second comment whose scope is the word under the caret, overlapping the first.")
            .Morph("TODO");
    }
}
