using static Define;

// L — lists: Enter at an item's start, middle and end, Tab and Shift+Tab, the Backspace cascade,
// the formatting keys on items, merging items, numbering continuation and AutoFormat. Every row that
// changes the document runs again with Track Changes on.
//
// The list number is not a text position, so a click on it cannot be aimed: L-08 clicks the left side
// of the first character instead, which lands on the text start. Pasting into another document is not
// expressible, so L-11b pastes within numbers-two-lists.
static class ListsFamily
{
    public static FamilyDefinition Definition { get; } = new("lists", "Lists",
        "Enter, Tab, Backspace and the formatting keys in bulleted and numbered lists, and merging items.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("L-01", "Enter at an item's start, middle and end, then typing")
            .In("{fixture}")
            .Steps("select({at})", "Enter", "type(\"x\")")
            .Axis("fixture", "numbers", "numbers-nested")
            .Axis("at", "P1:0", "P1:2", "P1:end", "P2:end", "P3:end")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("Enter at the start puts an empty numbered item above; in the middle splits the item, both numbered; at the end adds a numbered item at the same level, so P3:end in numbers-nested adds a level-2 item.")
            .Morph("PARTIAL");

        yield return Probe("L-02", "Enter twice at the last item's end")
            .In("{fixture}")
            .Steps("select(P3:end)", "Enter", "Enter", "type(\"x\")")
            .Axis("fixture", "numbers", "numbers-nested")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) the second Enter on the empty item takes it out of the list, leaving a Normal paragraph; (b) at level 2 it outdents to level 1 first, and a third Enter would leave the list.")
            .Morph("DONE");

        yield return Probe("L-03", "The Backspace cascade at an item's start")
            .In("{fixture}")
            .Steps("select(P2:0)", "Backspace", "Backspace", "Backspace", "Backspace")
            .Axis("fixture", "bullets-nested", "bullets")
            .Trace()
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("At level 2: outdent to level 1, then the bullet off with the indent kept, then the indent off, then merge with the paragraph before. At level 1: the bullet off, the indent off, merge.")
            .Morph("DONE");

        yield return Probe("L-04", "Tab and Shift+Tab on the first item and on a later one")
            .In("bullets")
            .Steps("select({at})", "Tab", "Tab", "Shift+Tab", "Shift+Tab", "Shift+Tab")
            .Axis("at", "P1:2", "P2:2")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("Tab in the first item moves the whole list right by 360 twips rather than demoting it; Tab in a later item demotes it a level. Shift+Tab at level 1 of a later item (a) does nothing; (b) takes the numbering off; (c) sets Normal.")
            .Morph("DONE");

        yield return Probe("L-04b", "Shift+Tab twice on a level-2 item")
            .In("bullets-nested")
            .Steps("select(P2:0)", "Shift+Tab", "Shift+Tab")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("The first press promotes Beta to level 1; the second (a) does nothing, (b) takes the bullet off.")
            .Morph("DONE");

        yield return Probe("L-05", "Alt+Shift+Right and Alt+Shift+Left on items")
            .In("bullets")
            .Steps("select({at})", "Alt+Shift+Right", "Alt+Shift+Left", "Alt+Shift+Left")
            .Axis("at", "P1:2", "P2:2")
            .Trace()
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("Alt+Shift+Right demotes the item a level even in the first item (unlike Tab, which moves the whole list); Alt+Shift+Left promotes; at level 1 it (a) does nothing, (b) takes the bullet off.")
            .Morph("TODO");

        yield return Probe("L-05b", "Alt+Shift+Left or Alt+Shift+Right alone on a first and a later item, flat, nested and numbered")
            .In("{fixture}")
            .Steps("select({at})", "{key}")
            .Axis("fixture", "bullets", "bullets-nested", "numbers")
            .Axis("at", "P1:1", "P2:1")
            .Axis("key", "Alt+Shift+Left", "Alt+Shift+Right")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Notes("L-05 and E-95c sent both keys in one case; here each case sends one. In bullets-nested P2 is the level-2 item.")
            .Hypotheses("(a) the outline keys ignore the list: Alt+Shift+Left makes the item Heading 1 (keeping or dropping its bullet) and Alt+Shift+Right leaves a body paragraph alone; (b) in a list they change the level like Tab and Shift+Tab, Alt+Shift+Right demoting even the first item.")
            .Morph("TODO");

        yield return Probe("L-06", "Ctrl+Shift+L twice, on a numbered item and on a Normal paragraph")
            .In("{fixture}")
            .Steps("select(P2:1)", "Ctrl+Shift+L", "Ctrl+Shift+L")
            .Axis("fixture", "numbers", "three-paragraphs")
            .Trace()
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("Ctrl+Shift+L applies the List Bullet style: a numbered item becomes a bulleted one, the second press (a) is a no-op, (b) toggles back to Normal; on a Normal paragraph the first press bullets it.")
            .Morph("DONE");

        yield return Probe("L-07", "AutoFormat of a typed '1. ' item on Enter, then Backspace, undo or another Enter")
            .In("empty-paragraphs")
            .Steps("select(P2:0)", "type(\"1. item\")", "Enter")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Branch("backspace", S("Backspace"))
            .Branch("undo", S("undo"), Node("undo", S("undo")))
            .Branch("enter", S("Enter"))
            .Tracked()
            .Hypotheses("Enter turns '1. item' into a numbered item and starts item 2; Backspace on the new empty item (a) takes its number off, (b) cascades as L-03; the first undo reverts the AutoFormat and keeps the typed text, the second removes the paragraph; another Enter ends the list.")
            .Morph("TODO");

        yield return Probe("L-08", "A click on the number's side of an item, then Ctrl+B or typing")
            .In("numbers-two-lists")
            .Steps("click(P1:0, side=left)")
            .Observing(Observe.Default | Observe.Formatting | Observe.Lists)
            .Branch("bold", S("Ctrl+B"))
            .Branch("type", S("type(\"x\")"))
            .Tracked()
            .Hypotheses("A click on a list number selects every number of the list, so Ctrl+B bolds the numbers and typing is refused; the harness's click lands on the text start, so the record is expected to show an IP before 'One' instead.")
            .Notes("The number is not a text position; click(P1:0, side=left) is the nearest aim the grammar has and is recorded as such.")
            .Morph("TODO");

        yield return Probe("L-09", "Delete a middle item whole")
            .In("numbers")
            .Steps("select(P2:0-P3:0)", "Delete")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("Two goes and Three becomes item 2.")
            .Morph("UNKNOWN");

        yield return Probe("L-10", "Delete at an item's end merges it with what follows: the survivor's level and list")
            .In("{fixture}")
            .Steps("select({at})", "Delete")
            .Axis("fixture", "numbers", "numbers-nested", "numbers-two-lists")
            .Axis("at", "P1:end", "P2:end", "P3:end")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("The merged paragraph keeps the first paragraph's properties: a level-1 item swallowing a level-2 one stays at level 1; an item swallowing 'After the list.' stays an item; 'Between the lists.' swallowing Alpha stays Normal, and Beta then continues the first list's numbering or restarts.")
            .Morph("UNKNOWN");

        yield return Probe("L-10b", "Backspace twice at a level-2 item's start")
            .In("numbers-nested")
            .Steps("select(P3:0)", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("The cascade runs first: level 1, then the number off; the item is not merged with Two until a later press.")
            .Morph("DONE");

        yield return Probe("L-11", "Cut an item and paste it at another item's end")
            .In("numbers")
            .Steps("select(P2:0-P3:0)", "Ctrl+X", "select(P2:end)", "Ctrl+V")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("After the cut P2 is Three; pasting a whole item at its end puts Two after it as item 3, the mark carried with it.")
            .Morph("UNKNOWN");

        yield return Probe("L-11b", "Cut two items and paste them into the Normal paragraph between two lists")
            .In("numbers-two-lists")
            .Steps("select(P1:0-P2:0)", "Ctrl+X", "select(P1:3)", "Ctrl+V")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("After the cut 'Between the lists.' is P1; the pasted items (a) keep their numbering and split the paragraph, (b) take the Normal paragraph's formatting.")
            .Notes("Positions are read after each step, so the paste target is P1:3, where the Normal paragraph stands once P1 and P2 are cut.")
            .Morph("UNKNOWN");

        yield return Probe("L-12", "Shift+Enter inside an item, then typing")
            .In("numbers")
            .Steps("select(P2:2)", "Shift+Enter", "type(\"x\")")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("A line break inside item 2, no new number; the typed text is on the second line of the same item.")
            .Morph("UNKNOWN");

        yield return Probe("L-13", "Backspace over a selection that spans list items and the paragraph after")
            .In("numbers-nested")
            .Steps("select({selection})", "Backspace")
            .Axis("selection", "P3:2-P4:3", "P2:1-P4:3")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("The survivor keeps the first paragraph's properties: a level-2 item in the first case, a level-1 item in the second.")
            .Morph("UNKNOWN");

        yield return Probe("L-14", "Enter at a numbered heading's end and middle")
            .In("numbered-heading")
            .Steps("select({at})", "Enter", "type(\"x\")")
            .Axis("at", "P1:end", "P1:4")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("At the end the new paragraph takes the heading's next style (Normal) and no number; in the middle both halves are numbered Heading 1.")
            .Morph("UNKNOWN");

        yield return Probe("L-15", "A typed number after a list: continuation or restart")
            .In("numbers-two-lists")
            .Steps("select(P3:end)", "Enter", "type(\"{text}\")", "Enter")
            .Axis("text", "3. next", "1. next")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) AutoFormat joins the item to the list above and numbers it 3 whatever was typed; (b) it numbers as typed, so '1. next' starts a new list; (c) it joins the list below instead.")
            .Morph("TODO");

        yield return Probe("L-16", "Tab and Shift+Tab in a list item inside a cell")
            .In("list-in-cell")
            .Steps("select(C(1,1):2)", "{key}")
            .Axis("key", "Tab", "Shift+Tab")
            .Observing(Observe.Default | Observe.Lists | Observe.Tables)
            .Tracked()
            .Hypotheses("The compact form of T-02: Tab leaves for the next cell rather than demoting the item.")
            .Morph("TODO");

        yield return Probe("L-17", "Enter at the first item's start")
            .In("numbers")
            .Steps("select(P1:0)", "Enter")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("An empty item 1 above, One renumbered 2, the caret staying with One.")
            .Morph("PARTIAL");

        yield return Probe("L-18", "Home, Left, Shift+Home and Shift+Right around the number")
            .In("numbers")
            .Steps("select({from})", "{key}", "{key}")
            .Axis("from", "P2:0", "P1:end")
            .Axis("key", "Home", "Left", "Shift+Home", "Shift+Right")
            .Trace()
            .Observing(Observe.Default | Observe.Lists)
            .Hypotheses("The number is not a position: Home at P2:0 stays, Left goes to P1's end, Shift+Home at P2:0 selects nothing; Shift+Right from P1:end takes the mark and the selection then shows the whole of One, then into Two.")
            .Morph("UNKNOWN");

        yield return Probe("L-19", "Ctrl+M twice then Ctrl+Shift+M twice on items")
            .In("numbers")
            .Steps("select({at})", "Ctrl+M", "Ctrl+M", "Ctrl+Shift+M", "Ctrl+Shift+M")
            .Axis("at", "P1:1", "P2:1")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("(a) Ctrl+M indents the item by a default tab stop without changing its level; (b) in a list it demotes a level like Tab; on the first item (c) the whole list moves.")
            .Morph("TODO");

        yield return Probe("L-19b", "Ctrl+T twice then Ctrl+Shift+T twice on items")
            .In("numbers")
            .Steps("select({at})", "Ctrl+T", "Ctrl+T", "Ctrl+Shift+T", "Ctrl+Shift+T")
            .Axis("at", "P1:1", "P2:1")
            .Trace()
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+T grows the hanging indent, so the number stays and the text moves right; Ctrl+Shift+T shrinks it back.")
            .Morph("TODO");

        yield return Probe("L-20", "Delete at item 1's end when item 2 is at level 2")
            .In("numbers-nested")
            .Steps("select(P2:end)", "Delete")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("(a) the merged item keeps level 1; (b) it takes level 2 from the swallowed item.")
            .Morph("UNKNOWN");

        yield return Probe("L-21", "Shift+Tab twice on a later level-1 item")
            .In("bullets")
            .Steps("select(P2:1)", "Shift+Tab", "Shift+Tab")
            .Trace()
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("(a) nothing on either press; (b) the first takes the bullet off; (c) Normal.")
            .Morph("DONE");

        yield return Probe("L-22", "Tab with a selection across two items")
            .In("bullets")
            .Steps("select(P1:2-P2:2)", "Tab")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("Both items are demoted a level and the selection is kept; or, the first item being selected, the whole list moves right.")
            .Morph("UNKNOWN");

        yield return Probe("L-23", "Ctrl+Q and Ctrl+Shift+N on an item")
            .In("numbers")
            .Steps("select(P2:1)", "{key}")
            .Axis("key", "Ctrl+Q", "Ctrl+Shift+N")
            .Observing(Observe.Default | Observe.Lists | Observe.Markup)
            .Tracked()
            .Hypotheses("Ctrl+Q resets the paragraph formatting to the style's and (a) keeps the number, (b) takes it off; Ctrl+Shift+N applies Normal and takes the number off, the others renumbering.")
            .Morph("UNKNOWN");

        yield return Probe("L-24", "Alt+Shift+Down and Alt+Shift+Up move an item")
            .In("numbers")
            .Steps("select(P2:1)", "Alt+Shift+Down", "Alt+Shift+Up", "Alt+Shift+Up")
            .Trace()
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("Two swaps with Three, then back, then with One; the numbers stay in order and the caret moves with the text.")
            .Morph("TODO");

        yield return Probe("L-25", "Nested AutoFormat: Tab on a new item then a typed 'a. '")
            .In("numbers")
            .Steps("select(P1:end)", "Enter", "Tab", "type(\"a. x\")", "Enter")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("Tab demotes the empty item to level 2 (numbered a.); typing 'a. x' then (a) is left as text under the level-2 number, (b) AutoFormat sees the duplicate and strips the typed 'a. '.")
            .Morph("TODO");

        yield return Probe("L-26", "Enter with a selection spanning two items")
            .In("numbers")
            .Steps("select(P1:2-P2:2)", "Enter")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("The selection is deleted and a paragraph break put in its place: 'On' as item 1 and 'o' as item 2.")
            .Morph("UNKNOWN");

        yield return Probe("L-27", "A list made in a text box, then Tab")
            .In("text-box")
            .Steps("select(TB1:P1:2)", "Ctrl+Shift+L", "Tab")
            .Observing(Observe.Default | Observe.Story | Observe.Lists)
            .Tracked()
            .Hypotheses("Ctrl+Shift+L bullets the box's first paragraph; Tab in the first item moves the list, as in the body.")
            .Morph("UNKNOWN");

        yield return Probe("L-28", "Backspace three times at the first item's start")
            .In("numbers")
            .Steps("select(P1:0)", "Backspace", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("The number off, the indent off, then nothing at the document start; Two becomes item 1.")
            .Morph("DONE");

        yield return Probe("L-29", "Delete at the end of the Normal paragraph before a list")
            .In("numbers-two-lists")
            .Steps("select(P3:end)", "Delete")
            .Observing(Observe.Default | Observe.Lists)
            .Tracked()
            .Hypotheses("Alpha is merged into the Normal paragraph and loses its number; Beta becomes the second list's first item.")
            .Morph("UNKNOWN");
    }
}
