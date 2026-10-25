using static Define;

// X — everything else: Click-and-Type, Overtype, Esc in each context, Ctrl+A then a key, section and
// page breaks, column breaks, the final paragraph of a list, smart cursoring with typing.
//
// Three catalogue rows have no scenario here because the harness cannot stage them:
//   X-13  window focus and the selection — the harness cannot switch windows and come back;
//   X-14  zoom and click targeting — the harness pins the zoom, so a click at another zoom cannot be sent;
//   X-15  out of scope by the catalogue's own note.
static class MiscFamily
{
    public static FamilyDefinition Definition { get; } = new("misc", "Everything else",
        "Click-and-Type, Overtype, Esc per context, Ctrl+A then Delete, section and page breaks, smart cursoring. " +
        "X-13 (window focus), X-14 (zoom) and X-15 (out of scope) have no scenario: the harness cannot switch windows or change the zoom.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("X-01", "Click-and-Type: a double click in the blank area under the text, per zone")
            .In("plain")
            .Steps("dblclick(below P1, side={side})")
            .Axis("side", "left", "centre", "right")
            .Toggle("Options.AllowClickAndTypeMouse", "true", "false")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("(a) with the option on, empty paragraphs are inserted down to the click and the last one takes the zone's alignment (left, centred, right); with it off, (b) the last word of the nearest line is selected, as a double click on text would.")
            .Morph("TODO");

        yield return Probe("X-02", "Overtype: typing replaces the character under the caret, except at a paragraph's end")
            .In("words")
            .Steps("option(Options.Overtype=true)", "select(P1:6)", "type(\"xy\")", "select(P1:end)", "type(\"z\")")
            .Tracked()
            .Observing(Observe.Default)
            .Hypotheses("'xy' replaces 'be'; 'z' at the end is inserted before the mark since there is nothing to overtype. Under tracking the replaced characters are marked deleted.")
            .Morph("TODO");

        yield return Probe("X-03", "Esc with a text selection")
            .In("words")
            .Steps("select(P1:6-P1:10)", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story)
            .Hypotheses("(a) the selection is kept: Esc has nothing to leave outside a mode; (b) it collapses.")
            .Morph("PARTIAL");

        yield return Probe("X-03b", "Esc leaves Extend mode (F8) and keeps what it selected")
            .In("words")
            .Steps("select(P1:6)", "F8", "F8", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story)
            .Hypotheses("Two F8 presses select the word; Esc ends the mode and the word stays selected.")
            .Morph("TODO");

        yield return Probe("X-03c", "Esc leaves column selection mode (Ctrl+Shift+F8)")
            .In("words")
            .Steps("select(P1:6)", "Ctrl+Shift+F8", "Right", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story)
            .Hypotheses("Right extends a one-line column selection; Esc ends the mode and (a) keeps the selection (b) collapses it.")
            .Morph("TODO");

        yield return Probe("X-03d", "Esc with an inline picture selected")
            .In("image-inline")
            .Steps("click(IMG1)", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Shapes)
            .Hypotheses("(a) the picture stays selected; (b) the selection collapses to an insertion point beside it.")
            .Morph("PARTIAL");

        yield return Probe("X-03e", "Esc with a floating picture selected")
            .In("image-floating")
            .Steps("click(SHP1)", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Shapes)
            .Hypotheses("Esc deselects the shape and the caret returns to the text where it was before the click.")
            .Morph("PARTIAL");

        yield return Probe("X-03f", "Esc inside a text box, twice")
            .In("text-box")
            .Steps("click(TB1:P1:2)", "Esc", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Shapes)
            .Hypotheses("The first Esc selects the text box as a shape; the second deselects it and puts the caret back in the body.")
            .Morph("TODO");

        yield return Probe("X-03g", "Esc inside a header")
            .In("header-footer")
            .Steps("dblclick(HDR:P1:0)", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story)
            .Hypotheses("Esc leaves the header and returns to the body at the position before the double click.")
            .Morph("TODO");

        yield return Probe("X-03h", "Esc inside a comment")
            .In("comment")
            .Steps("select(CMT1:P1:0)", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story)
            .Hypotheses("Esc leaves the comment and returns to the commented text in the body.")
            .Morph("TODO");

        yield return Probe("X-07", "Ctrl+A then Delete or typing over paragraphs of every kind")
            .In("kinds")
            .Steps("Ctrl+A")
            .Branch("delete", S("Delete"))
            .Branch("type", S("type(\"x\")"))
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Shapes | Observe.Markup)
            .Hypotheses("One paragraph is left; it carries (a) the first paragraph's properties (b) the last's. Typing after Ctrl+A puts 'x' in that paragraph with the first run's formatting.")
            .Morph("TODO");

        yield return Probe("X-07b", "Ctrl+A then Delete with a floating picture in the document")
            .In("image-floating")
            .Steps("Ctrl+A", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Shapes | Observe.Markup)
            .Hypotheses("The anchor goes with its paragraph's text, so the picture is deleted too.")
            .Morph("TODO");

        yield return Probe("X-08", "A double click on a word joined by a non-breaking space")
            .In("special")
            .Steps("dblclick(P1:0)")
            .Tracked()
            .Observing(Observe.Default)
            .Notes("P1:1 is the non-breaking space between 'a' and 'b'.")
            .Hypotheses("(a) the non-breaking space is a word boundary like a space, so 'a' alone is selected (with the nbsp as its trailing space); (b) 'a b' is one word.")
            .Morph("TODO");

        yield return Probe("X-08b", "Ctrl+Right across a non-breaking space")
            .In("special")
            .Steps("select(P1:0)", "Ctrl+Right", "Ctrl+Right")
            .Trace()
            .Observing(Observe.Default)
            .Hypotheses("(a) the first stop is after the nbsp, at 'b'; (b) the nbsp is not a word break and the first stop is after 'b '.")
            .Morph("TODO");

        yield return Probe("X-08c", "Delete on a non-breaking space")
            .In("special")
            .Steps("select(P1:1)", "Delete")
            .Tracked()
            .Observing(Observe.Default)
            .Hypotheses("One character goes; 'ab' remains.")
            .Morph("DONE");

        yield return Probe("X-09", "Backspace at the start of the paragraph after a section break")
            .In("section-break")
            .Steps("select(P2:0)", "Backspace")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Notes("P2 begins with the next-page section break; section 2 is landscape.")
            .Hypotheses("(a) the break is removed and the merged section keeps section 2's properties (landscape), since a section's properties live in its last paragraph's mark; (b) section 1's survive.")
            .Morph("UNKNOWN");

        yield return Probe("X-09b", "Delete at the end of the paragraph before a section break")
            .In("section-break")
            .Steps("select(P1:end)", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("(a) P1's mark goes and P1's text joins P2 under section 2's properties; (b) the break goes first and both paragraphs stay.")
            .Morph("UNKNOWN");

        yield return Probe("X-09c", "Delete with the section break selected")
            .In("section-break")
            .Steps("select(P1:end-P2:0)", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("The break alone goes; one section remains with (a) section 2's (b) section 1's properties.")
            .Morph("UNKNOWN");

        yield return Probe("X-09d", "Enter before a section break")
            .In("section-break")
            .Steps("select(P1:end)", "Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Formatting | Observe.Markup)
            .Hypotheses("A new empty paragraph in section 1; the break stays at P3's start.")
            .Morph("UNKNOWN");

        yield return Probe("X-10", "Delete at the end of the paragraph before a page break")
            .In("page-break")
            .Steps("select(P1:end)", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Notes("P2 starts with the page break character, then 'After the break.'")
            .Hypotheses("P1's mark goes and the break joins P1's text, so 'Before the break.' is followed by the break in one paragraph.")
            .Morph("UNKNOWN");

        yield return Probe("X-10b", "Backspace after a page break")
            .In("page-break")
            .Steps("select(P2:1)", "Backspace")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("The break character alone is deleted; P2 stays a separate paragraph.")
            .Morph("UNKNOWN");

        yield return Probe("X-10c", "Delete with the page break selected")
            .In("page-break")
            .Steps("select(P2:0-P2:1)", "Delete")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("The same result as Backspace after it.")
            .Morph("UNKNOWN");

        yield return Probe("X-10d", "Typing after a page break")
            .In("page-break")
            .Steps("select(P2:1)", "type(\"x\")")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("'x' lands at the top of page 2 in the same paragraph as the break.")
            .Morph("UNKNOWN");

        yield return Probe("X-11", "A column break in a one-column section")
            .In("three-paragraphs")
            .Steps("select(P2:4)", "Ctrl+Shift+Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("(a) the break is written as w:br type=column and lays out as a page break; (b) it is written and has no visible effect.")
            .Morph("TODO");

        yield return Probe("X-12", "Enter at the end of a numbered list, twice")
            .In("numbers")
            .Steps("select(DOC:end)", "Enter", "Enter")
            .Trace()
            .Tracked()
            .Observing(Observe.Default | Observe.Lists)
            .Hypotheses("The first Enter adds an empty numbered item; the second removes the numbering from it and leaves a Normal paragraph.")
            .Morph("PARTIAL");

        yield return Probe("X-16", "Smart cursoring with typing: text goes to the viewed page or to the old caret")
            .In("three-pages")
            .Steps("select(P1:3)", "PgDn", "PgDn", "type(\"x\")")
            .Toggle("Options.SmartCursoring", "true", "false")
            .Tracked()
            .Observing(Observe.Default)
            .Hypotheses("With smart cursoring the typed character lands on the page scrolled to; without it at P1:3 and the view scrolls back.")
            .Morph("TODO");

        yield return Probe("X-17", "A column break in a two-column section")
            .In("columns")
            .Steps("select(P1:end)", "Ctrl+Shift+Enter")
            .Tracked()
            .Observing(Observe.Default | Observe.Markup)
            .Hypotheses("P2 starts at the top of the second column; the break is written as w:br type=column at P1's end with the mark after it.")
            .Morph("TODO");
    }
}
