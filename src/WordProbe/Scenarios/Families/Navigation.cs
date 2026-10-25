using static Define;

// N — caret navigation: where Left/Right/Up/Down/Home/End and their Ctrl forms stop at every kind of
// boundary. Every row is a caret placed over COM, a key, and the selection read back.
static class NavigationFamily
{
    public static FamilyDefinition Definition { get; } = new("navigation", "Caret navigation",
        "Where the arrow keys, Home/End and their Ctrl forms put the caret at every kind of boundary: paragraph marks, cells, " +
        "inline pictures, fields, hyperlinks, tabs, line breaks, breaks, notes and tracked text.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("N-01", "Left and Right over text and every boundary")
            .In("{fixture}")
            .Steps("select({from})", "{key}")
            .Axis("fixture", "three-paragraphs", "tabs-breaks", "image-inline", "field-page", "hyperlink", "footnote", "special", "page-break", "section-break", "table-2x2")
            .Axis("from", "P1:start", "P1:end", "P2:start", "DOC:end")
            .Axis("key", "Right", "Left")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("Right at a paragraph's end lands at the next paragraph's start; Left at a start lands before the previous mark; the document ends do not move; a cell end steps into the next cell.");

        yield return Probe("N-01b", "Left and Right across the specials inside a paragraph")
            .In("{fixture}")
            .Steps("select({from})", "{key}")
            .Axis("fixture", "tabs-breaks")
            .Axis("from", "P1:4", "P1:5", "P2:8", "P2:9", "P3:3", "P3:4")
            .Axis("key", "Right", "Left")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Notes("P1:4 is before the tab, P2:8 before the line break, P3:3 between the two spaces.");

        yield return Probe("N-01c", "Left and Right across a picture, a field, a link and a note reference")
            .In("{fixture}")
            .Steps("select({from})", "{key}")
            .Axis("fixture", "image-inline", "field-page", "hyperlink", "footnote")
            .Axis("from", "P1:find(\" \"):end", "P1:word(2)", "P1:wordend(2)", "P1:word(3)")
            .Axis("key", "Right", "Left")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs);

        yield return Probe("N-02", "Left and Right over a field: one step or one per result character")
            .In("field-page")
            .Steps("select(P1:5)", "Right", "Right", "Right")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Branch("back", S("Left", "Left"))
            .Branch("codes", S("Alt+F9", "Right", "Right"), Node("codes-off", S("Alt+F9")))
            .Hypotheses("(a) the whole field is one position for Left/Right; (b) each result character is a position; (c) per character only with codes shown.");

        yield return Probe("N-03", "Left and Right over a hyperlink, one step at a time")
            .In("hyperlink")
            .Steps("select({from})", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}")
            .Axis("from", "P1:3", "P1:end")
            .Axis("key", "Right", "Left")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Formatting)
            .Notes("P1:3 is after 'See'; the link's hidden HYPERLINK code occupies positions before its display text, so the trace shows whether the caret visits them.");

        yield return Probe("N-04", "Left and Right over another author's tracked deletion, per markup view")
            .In("tracked")
            .Steps("view({view})", "select(P1:5)", "{key}", "{key}")
            .Axis("view", "all", "simple", "none")
            .Axis("key", "Right", "Left", "Ctrl+Right", "Shift+Right")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Revisions)
            .Hypotheses("(a) the collapsed views skip deleted text and Start jumps past it; (b) deleted text is stepped through in every view.");

        yield return Probe("N-05", "Left and Right over hidden text with marks hidden")
            .In("special")
            .Steps("select(P1:find(\" hidden\"))", "Right", "Right", "Right", "Right", "Right", "Right", "Right", "Right", "Left", "Left", "Left")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("Hidden text is skipped when marks are off (the harness's view), stepped through when shown.");

        yield return Probe("N-06", "Left and Right at content control and bookmark edges")
            .In("{fixture}")
            .Steps("select({from})", "{key}")
            .Axis("fixture", "content-control", "bookmark")
            .Axis("from", "P1:5", "P1:6", "P1:find(\" end\")", "P1:find(\" end\"):end")
            .Axis("key", "Right", "Left")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("(a) a content control has an inside-start and a before-start position, so one Right stays in place visually; (b) no extra positions. Bookmark edges add none.");

        yield return Probe("N-07", "Up and Down keep the remembered x across short, empty and larger lines")
            .In("{fixture}")
            .Steps("select({from})", "Down", "Down")
            .Axis("fixture", "long-paragraph", "empty-paragraphs", "bullets", "kinds")
            .Axis("from", "P1:5", "P1:end")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Branch("up", S("Up", "Up"))
            .Branch("reset-by-left", S("Left", "Down"))
            .Hypotheses("The x of the first caret is kept over a shorter line and re-applied on the next long one; Left/Right/Home/End/typing reset it.");

        yield return Probe("N-08", "Up and Down into, through and out of a table")
            .In("{fixture}")
            .Steps("select({from})", "Down", "Down", "Down", "Down")
            .Axis("fixture", "table-3x3", "table-merged", "table-at-end", "table-multi")
            .Axis("from", "P1:3", "P1:end")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Tables)
            .Branch("back-up", S("Up", "Up", "Up", "Up", "Up"))
            .Hypotheses("Down enters the cell under the caret's x, keeps the column row by row, exits to the paragraph after; a two-line cell takes two presses; a vertically merged cell is one stop.");

        yield return Probe("N-09", "Up on the first line and Down on the last line of a document")
            .In("plain")
            .Steps("select(P1:5)", "{key}")
            .Axis("key", "Up", "Down")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("Up on the only line goes to the paragraph start; Down to its end.");

        yield return Probe("N-10", "Home and End on wrapped, tabbed, broken, empty and trailing-space lines")
            .In("{fixture}")
            .Steps("select({from})", "{key}")
            .Axis("fixture", "wrap", "tabs-breaks", "empty-paragraphs", "table-2x2")
            .Axis("from", "P1:3", "P1:30", "P2:3", "C(1,1):1")
            .Axis("key", "End", "Home")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("End on a wrapped line lands (a) before the wrap space (b) after it; End on the last line lands before the mark; in a cell before the cell mark.");

        yield return Probe("N-11", "Ctrl+Home and Ctrl+End from body, cell, header and text box")
            .In("{fixture}")
            .Steps("{setup}", "{key}")
            .Axis("fixture", "table-2x2", "header-footer", "text-box", "footnote")
            .Axis("setup", "select(P1:3)", "select(C(1,1):1)", "seek(header)", "select(TB1:P1:2)", "seek(footnotes)")
            .Axis("key", "Ctrl+End", "Ctrl+Home")
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Notes("Setups that do not apply to a fixture (a cell in a document without a table) fail and are recorded as such.");

        yield return Probe("N-12", "Ctrl+Right and Ctrl+Left word stops, one press at a time")
            .In("{fixture}")
            .Steps("select({from})", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}", "{key}")
            .Axis("fixture", "words", "image-inline", "hyperlink", "field-page", "tabs-breaks", "table-2x2")
            .Axis("from", "P1:start", "DOC:end")
            .Axis("key", "Ctrl+Right", "Ctrl+Left")
            .Trace()
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("A stop is the start of the next word after its trailing spaces; punctuation is its own word; a hyphen splits, an apostrophe does not; 3.14 and 1,000 are single words; the last word of a paragraph stops before the mark, then at the next paragraph's start; Ctrl+Left at a paragraph start lands at the previous paragraph's end.");

        yield return Probe("N-13", "Ctrl+Up and Ctrl+Down between paragraphs, in cells and over a table")
            .In("{fixture}")
            .Steps("select({from})", "{key}", "{key}")
            .Axis("fixture", "three-paragraphs", "table-2x2", "table-multi")
            .Axis("from", "P2:4", "P1:end", "C(1,1):1")
            .Axis("key", "Ctrl+Up", "Ctrl+Down")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("Ctrl+Up goes to the paragraph start, then the previous start; Ctrl+Down to the next paragraph start; in a cell (a) the next paragraph of the cell, then the next cell (b) straight to the next cell.");

        yield return Probe("N-14", "PgUp, PgDn, Ctrl+PgUp and Ctrl+PgDn over three pages")
            .In("three-pages")
            .Steps("select({from})", "{key}", "{key}")
            .Axis("from", "P1:3", "P15:3")
            .Axis("key", "PgDn", "PgUp", "Ctrl+PgDn", "Ctrl+PgUp", "Ctrl+Alt+PgDn", "Ctrl+Alt+PgUp")
            .Trace()
            .Observing(Observe.Selection | Observe.Text);

        yield return Probe("N-15", "Alt+Home, Alt+End, Alt+PgUp and Alt+PgDn inside tables")
            .In("{fixture}")
            .Steps("select(C(2,2):1)", "{key}")
            .Axis("fixture", "table-3x3", "table-merged", "table-nested")
            .Axis("key", "Alt+Home", "Alt+End", "Alt+PgUp", "Alt+PgDn")
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("First/last cell in the row, top/bottom cell in the column, with the cell contents selected.");

        yield return Probe("N-16", "Tab and Shift+Tab between cells, including empty and merged cells")
            .In("{fixture}")
            .Steps("select({from})", "{key}", "{key}", "{key}", "{key}")
            .Axis("fixture", "table-2x2", "table-2x2-empty", "table-merged")
            .Axis("from", "C(1,1):0", "C(2,2):0")
            .Axis("key", "Tab", "Shift+Tab")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Tables)
            .Hypotheses("Tab selects the next cell's contents (an IP in an empty cell); Shift+Tab at (1,1) (a) does nothing (b) leaves the table.");

        yield return Probe("N-17", "Shift+F5 and Ctrl+Alt+Z cycle the last edit positions")
            .In("three-paragraphs")
            .Steps("select(P1:2)", "type(\"a\")", "select(P3:2)", "type(\"b\")", "select(P2:2)", "type(\"c\")", "select(P1:end)", "{key}", "{key}", "{key}", "{key}", "{key}")
            .Axis("key", "Shift+F5", "Ctrl+Alt+Z")
            .Trace()
            .Observing(Observe.Selection | Observe.Text);

        yield return Probe("N-18", "Arrows with a floating picture selected nudge it")
            .In("image-floating")
            .Steps("click(SHP1)", "{key}")
            .Axis("key", "Right", "Right x3", "Left", "Down", "Up", "Ctrl+Right", "Shift+Right")
            .Observing(Observe.Selection | Observe.Shapes | Observe.Undo)
            .Hypotheses("(a) one grid step (0.13 inch) per press, Ctrl a fine step, the caret unchanged; (b) 1pt per press. Shift+Right (a) nothing (b) grows the width.");

        yield return Probe("N-19", "Arrows with an inline picture selected")
            .In("image-inline")
            .Steps("click(IMG1)", "{key}")
            .Axis("key", "Right", "Left", "Down", "Up")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("Right leaves an IP after the picture, Left before it, Down the line below.");

        yield return Probe("N-20", "An arrow with a text selection collapses it: to the end, or one step further")
            .In("words")
            .Steps("select(P1:6-12)", "{key}")
            .Axis("key", "Right", "Left", "Down", "Up", "Home", "End")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("(a) Right collapses to the selection end without moving, Left to its start; (b) collapses and moves one step.");

        yield return Probe("N-21", "Esc with a selection")
            .In("words")
            .Steps("select(P1:6-12)", "Esc")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("The selection is kept.");

        yield return Probe("N-22", "Left and Right at a page break and a section break")
            .In("{fixture}")
            .Steps("select({from})", "{key}")
            .Axis("fixture", "page-break", "section-break")
            .Axis("from", "P1:end", "P2:start", "P2:1")
            .Axis("key", "Right", "Left")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs);

        yield return Probe("N-23", "Down at the bottom of column 1 of a two-column section")
            .In("columns")
            .Steps("select(P3:end)", "Up x3", "Down x4")
            .Observing(Observe.Selection | Observe.Text);

        yield return Probe("N-24", "A click seeds the remembered x for Up and Down")
            .In("long-paragraph")
            .Steps("click(P2:40)", "Up", "Down", "Down")
            .Observing(Observe.Selection | Observe.Text);

        yield return Probe("N-25", "Smart cursoring: a caret scrolled off screen moves to the viewed page")
            .In("three-pages")
            .Steps("select(P1:5)", "PgDn", "PgDn", "Right")
            .Toggle("Options.SmartCursoring", "true", "false")
            .Observing(Observe.Selection | Observe.Text)
            .Hypotheses("With smart cursoring the caret jumps to the viewed page on the first key; without it moves one character at P1.");
    }
}
