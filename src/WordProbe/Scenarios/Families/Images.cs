using static Define;

// I — pictures, shapes and text boxes: how an inline picture is selected and deleted, what keys do while
// it or a floating shape is selected, nudging, anchors, text boxes and the Esc levels out of them.
//
// Rows of the catalogue with no scenario here, and why:
//   I-07  a picture inside a link, a list or a heading — no fixture carries one.
//   I-08  resize handles — a handle is not a position the step grammar can name.
//   I-20  wrap kinds and clicking — the fixtures carry one wrap kind (square) only.
//   I-28  grouped shapes — no fixture carries a group.
// I-04 and I-19 have no table variant: table-2x2 carries neither a picture nor a shape.
static class ImagesFamily
{
    public static FamilyDefinition Definition { get; } = new("images", "Pictures, shapes and text boxes",
        "Inline and floating pictures: selection, deletion, keys while selected, nudging, anchors, text boxes and Esc.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("I-01", "An inline picture clicked")
            .In("image-inline")
            .Steps("click(IMG1)")
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Notes("The picture sits at P1:12, between 'before ' and ' after'.")
            .Hypotheses("The click selects the picture alone, selection Type InlineShape, Start 12 and End 13.")
            .Morph("DONE");

        yield return Probe("I-01b", "An inline picture selected by keyboard, then one character more")
            .In("image-inline")
            .Steps("select(P1:12)", "Shift+Right", "Shift+Right")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("(a) the picture alone by keyboard is Type InlineShape, Normal once text joins it; (b) Normal from the first press.")
            .Morph("DONE");

        yield return Probe("I-02", "Backspace after an inline picture, twice")
            .In("image-inline")
            .Steps("select(P1:13)", "Backspace", "Backspace")
            .Trace()
            .Observing(Observe.Default | Observe.Shapes)
            .Tracked()
            .Hypotheses("(a) the first press deletes the picture and the second the space before it; (b) the first press selects the picture and the second deletes it.")
            .Morph("DONE");

        yield return Probe("I-02b", "Delete before an inline picture")
            .In("image-inline")
            .Steps("select(P1:12)", "Delete")
            .Observing(Observe.Default | Observe.Shapes)
            .Tracked()
            .Hypotheses("(a) one press deletes the picture; (b) one press selects it.")
            .Morph("DONE");

        yield return Probe("I-02c", "Delete with the inline picture selected, and the undo")
            .In("image-inline")
            .Steps("click(IMG1)", "Delete")
            .Observing(Observe.Default | Observe.Shapes)
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses("The picture goes and the caret stays at 12; undo brings it back selected.")
            .Morph("DONE");

        yield return Probe("I-03", "Keys with the inline picture selected")
            .In("image-inline")
            .Steps("click(IMG1)")
            .Observing(Observe.Default | Observe.Formatting | Observe.Shapes)
            .Branch("type", S("type(\"x\")"), Node("undo", S("undo")))
            .Branch("enter", S("Enter"), Node("undo", S("undo")))
            .Branch("tab", S("Tab"), Node("undo", S("undo")))
            .Branch("line-break", S("Shift+Enter"), Node("undo", S("undo")))
            .Branch("bold", S("Ctrl+B", "type(\"y\")"), Node("undo", S("undo", "undo")))
            .Tracked()
            .Hypotheses("Typing replaces the picture; Enter (a) deletes the picture and splits the paragraph (b) splits around it; Tab and Shift+Enter replace it like a character; Ctrl+B on a picture is ignored, so 'y' follows the picture's run formatting.")
            .Morph("PARTIAL");

        yield return Probe("I-04", "Cut an inline picture and paste it into the next paragraph")
            .In("image-inline")
            .Steps("click(IMG1)", "Ctrl+X", "select(P2:3)", "{paste}")
            .Axis("paste", "Ctrl+V", "Ctrl+Shift+V")
            .Observing(Observe.Default | Observe.Shapes)
            .Tracked()
            .Notes("Ctrl+Shift+V is Word's paste text only; a picture has no text form.")
            .Hypotheses("Ctrl+V puts the picture at P2:3 inline; Ctrl+Shift+V pastes nothing.")
            .Morph("TODO");

        yield return Probe("I-05", "Drag an inline picture to another place")
            .In("image-inline")
            .Steps("{drag}")
            .Axis("drag", "drag(IMG1→P2:3)", "ctrl+drag(IMG1→P2:3)", "drag(IMG1→P1:0)")
            .Observing(Observe.Default | Observe.Shapes)
            .Tracked()
            .Hypotheses("A plain drag moves the picture inline to the drop point; Ctrl+drag copies it; a drop at P1:0 puts it at the paragraph start.")
            .Morph("TODO");

        yield return Probe("I-06", "Enter, Backspace and Delete at a picture that starts or ends a paragraph")
            .In("image-edges")
            .Steps("select({from})", "{key}")
            .Axis("from", "P1:0", "P1:1", "P2:20", "P2:end")
            .Axis("key", "Enter", "Backspace", "Delete")
            .Observing(Observe.Default | Observe.Shapes)
            .Tracked()
            .Notes("P1 starts with the picture (P1:0 before it, P1:1 after); P2 ends with one (P2:20 before it, P2:end after).")
            .Hypotheses("The picture is one character to each key: Delete before it or Backspace after it removes it in one press; Enter before the first picture leaves an empty paragraph above it.")
            .Morph("UNKNOWN");

        yield return Probe("I-06b", "Home and End next to an edge picture, then Left and Right")
            .In("image-edges")
            .Steps("select({from})", "{key}", "Left", "Right")
            .Axis("from", "P1:5", "P2:5")
            .Axis("key", "Home", "End")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("Home lands before the leading picture, End after the trailing one; the arrows step over a picture as one position.")
            .Morph("UNKNOWN");

        yield return Probe("I-10", "A floating picture, a rectangle and a text box selected")
            .In("{fixture}")
            .Steps("{how}")
            .Axis("fixture", "image-floating", "shape", "text-box")
            .Axis("how", "click(SHP1)", "select(SHP1)")
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes)
            .Notes("select(SHP1) is the COM selection of the same shape, for comparison with the click.")
            .Hypotheses("Both give selection Type Shape with the shape in ShapeRange; the text box needs its border clicked, since a click inside it enters its text.")
            .Morph("DONE");

        yield return Probe("I-11", "Esc with a floating picture selected, twice")
            .In("image-floating")
            .Steps("select(P3:4)", "click(SHP1)", "Esc", "Esc")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes)
            .Hypotheses("(a) the first Esc puts the caret at the anchor paragraph; (b) at P3:4, where it was before the click; (c) the shape stays selected.")
            .Morph("TODO");

        yield return Probe("I-12", "Arrow keys nudge a selected floating picture")
            .In("image-floating")
            .Steps("click(SHP1)", "Right", "Right", "Right", "Down", "Ctrl+Right", "Up")
            .Trace()
            .Toggle("Options.SnapToGrid", "true", "false")
            .Observing(Observe.Selection | Observe.Shapes | Observe.Undo)
            .Tracked()
            .Notes("N-18 takes each key once from a fresh selection; this row follows one shape through a sequence, with the grid on and off.")
            .Hypotheses("(a) one grid step per press with the grid on and Ctrl a fine step, 1pt per press with it off; (b) 1pt per press either way. The anchor (a) follows the shape across paragraphs; (b) stays on its paragraph.")
            .Morph("TODO");

        yield return Probe("I-13", "Delete and Backspace with a shape selected, and the undo")
            .In("{fixture}")
            .Steps("click(SHP1)", "{key}")
            .Axis("fixture", "image-floating", "text-box", "shape")
            .Axis("key", "Delete", "Backspace")
            .Observing(Observe.Default | Observe.Shapes)
            .Branch("undo", S("undo"))
            .Tracked()
            .Hypotheses("Either key removes the shape with its text; the caret lands at the anchor; undo restores it selected.")
            .Morph("TODO");

        yield return Probe("I-14", "Typing and Enter with a shape selected")
            .In("{fixture}")
            .Steps("click(SHP1)")
            .Axis("fixture", "image-floating", "shape", "text-box")
            .Observing(Observe.Default | Observe.Shapes | Observe.Story)
            .Branch("type", S("type(\"x\")"), Node("undo", S("undo")))
            .Branch("enter", S("Enter"), Node("undo", S("undo")))
            .Tracked()
            .Hypotheses("Picture: typing (a) replaces it at the anchor (b) is ignored. Rectangle: the text is typed into it, which opens its story. Text box selected by its border: the text goes into the box's story (a) at its start (b) replacing its text.")
            .Morph("TODO");

        yield return Probe("I-15", "Drag, Ctrl+drag and Shift+drag a floating picture")
            .In("image-floating")
            .Steps("{drag}")
            .Axis("drag", "drag(SHP1→P3:2)", "ctrl+drag(SHP1→P3:2)", "shift+drag(SHP1→P3:2)")
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes | Observe.Undo)
            .Tracked()
            .Hypotheses("A plain drag moves the shape and re-anchors it to the paragraph under the drop; Ctrl+drag leaves a copy; Shift+drag constrains the move to one axis.")
            .Morph("TODO");

        yield return Probe("I-16", "Tab and Shift+Tab with a shape selected")
            .In("{fixture}")
            .Steps("click(SHP1)", "Tab", "Shift+Tab")
            .Axis("fixture", "image-floating", "text-box")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Shapes | Observe.Undo)
            .Notes("Each fixture carries one shape, so a cycle has one stop.")
            .Hypotheses("(a) Tab cycles the selection through the shapes and stays on the only one, nothing typed; (b) Tab inserts a tab at the anchor.")
            .Morph("TODO");

        yield return Probe("I-17", "The anchor paragraph deleted or merged")
            .In("image-floating")
            .Steps("select({from})", "{key}")
            .Axis("from", "P1:0-P2:0", "P1:0-P1:end", "P1:end", "P2:0")
            .Axis("key", "Delete", "Backspace")
            .Observing(Observe.Default | Observe.Shapes)
            .Tracked()
            .Notes("The anchor is at the end of P1. P1:0-P2:0 takes the whole paragraph and its mark; P1:0-P1:end its text only; P1:end Delete and P2:0 Backspace merge P1 and P2.")
            .Hypotheses("A selection that spans the anchor deletes the shape; deleting the text alone keeps it on the emptied paragraph; a merge keeps it on the merged paragraph. Under tracking the shape is struck rather than removed.")
            .Morph("UNKNOWN");

        yield return Probe("I-18", "Ctrl+A with a floating picture, then Delete or copy and paste")
            .In("image-floating")
            .Steps("select(P1:0)", "Ctrl+A")
            .Observing(Observe.Default | Observe.Shapes)
            .Branch("delete", S("Delete"))
            .Branch("copy-paste", S("Ctrl+C", "select(P3:end)", "Enter", "Ctrl+V"))
            .Tracked()
            .Hypotheses("Ctrl+A takes the shape with its anchor paragraph; Delete removes it with the text; the paste brings a second copy anchored in the pasted text.")
            .Morph("UNKNOWN");

        yield return Probe("I-19", "Cut a floating picture and paste it into the third paragraph")
            .In("image-floating")
            .Steps("click(SHP1)", "Ctrl+X", "select(P3:0)", "Ctrl+V")
            .Observing(Observe.Default | Observe.Shapes)
            .Tracked()
            .Hypotheses("The pasted shape is anchored to P3, keeps its size and wrap, and is selected afterwards.")
            .Morph("TODO");

        yield return Probe("I-23", "Esc out of a text box's text, twice")
            .In("text-box")
            .Steps("click(TB1:P1:2)", "Esc", "Esc")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Story | Observe.Shapes)
            .Hypotheses("(a) three levels: the text, then the box selected as a shape, then the body at the anchor; (b) the first Esc goes straight to the body.")
            .Morph("TODO");

        yield return Probe("I-24", "Caret keys inside a text box's two lines")
            .In("text-box")
            .Steps("select({from})", "{key}")
            .Axis("from", "TB1:P1:2", "TB1:P2:3")
            .Axis("key", "Up", "Down", "Ctrl+Home", "Ctrl+End", "Ctrl+A")
            .Observing(Observe.Selection | Observe.Text | Observe.Story)
            .Hypotheses("Up on the first line and Down on the last stay in the box; Ctrl+Home, Ctrl+End and Ctrl+A act on the box's story and never reach the body.")
            .Morph("TODO");

        yield return Probe("I-24b", "Tab and Enter inside a text box")
            .In("text-box")
            .Steps("select(TB1:P1:2)", "{key}")
            .Axis("key", "Tab", "Enter")
            .Observing(Observe.Default | Observe.Story)
            .Tracked()
            .Hypotheses("Both edit the box's story as in the body: a tab character, a paragraph split; the box grows if its text needs the room.")
            .Morph("TODO");

        yield return Probe("I-25", "Typing into a selected empty rectangle, then Esc twice")
            .In("shape")
            .Steps("click(SHP1)", "type(\"x\")", "Esc", "Esc")
            .Trace()
            .Observing(Observe.Default | Observe.Story | Observe.Shapes)
            .Tracked()
            .Hypotheses("The letter opens the rectangle's text frame and lands in it; the first Esc selects the shape, the second returns to the body.")
            .Morph("TODO");

        yield return Probe("I-26", "Shift+click from a text box into the body")
            .In("text-box")
            .Steps("click(TB1:P1:2)", "shift+click(P2:2)")
            .Observing(Observe.Selection | Observe.Text | Observe.Story | Observe.Undo)
            .Hypotheses("(a) the selection cannot cross stories, so the click leaves the box and places the caret at P2:2; (b) it is ignored.")
            .Morph("TODO");

        yield return Probe("I-26b", "Drag text out of a text box into the body")
            .In("text-box")
            .Steps("drag(TB1:P1:0-TB1:P1:4→P2:3)")
            .Observing(Observe.Default | Observe.Story | Observe.Shapes)
            .Tracked()
            .Hypotheses("The dragged text moves into P2 and leaves the box's first line shorter; the box itself stays.")
            .Morph("TODO");

        yield return Probe("I-27", "Double-click a floating picture")
            .In("image-floating")
            .Steps("dblclick(SHP1)")
            .Observing(Observe.Selection | Observe.Shapes | Observe.Undo)
            .Hypotheses("The shape stays selected, the Picture Format tab opens, and the document and undo list do not change.")
            .Morph("UNKNOWN");

        yield return Probe("I-29", "Arrow keys with an inline picture selected, twice")
            .In("image-inline")
            .Steps("click(IMG1)", "{key}", "{key}")
            .Axis("key", "Right", "Left", "Down")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Notes("N-19 takes each key once; the second press shows whether the first collapsed the selection without moving.")
            .Hypotheses("Right collapses to after the picture, Left to before it, then the second press moves one character; Down goes to the line below.")
            .Morph("UNKNOWN");

        yield return Probe("I-30", "A selection growing over an inline picture")
            .In("image-inline")
            .Steps("select(P1:11)", "Shift+Right", "Shift+Right", "Shift+Right")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Paragraphs)
            .Hypotheses("(a) Type stays Normal throughout, since text is in the selection before the picture joins it; (b) Type turns InlineShape when the picture joins.")
            .Morph("UNKNOWN");
    }
}
