# Editor backlog

Open findings from the 2026-11-02 review of `MorphEditor` (browser session plus code reviews of the
typing box, the viewer script, the ribbon and command table, and the .NET edit flow). A finding is
deleted when it lands. Paths are under `src/Morph.Blazor/` unless they say otherwise.

Every bug of that review, and the in-reach missing features, landed on 2026-11-02. What is left is
what those fixes stopped short of.

## Residues

- **Undo/Redo stay enabled while a paragraph is open**, whether or not anything was typed in it:
  `EditorState.CanUndo`/`CanRedo` count an open session as undoable, because the box's own stack is
  known only to the script and reading it would cross to .NET per keystroke. Undo with nothing typed
  falls through to the file's history.
- **Insert > Table's greying inside the open paragraph is CSS only** (`markClipboard` marks
  `.editor-caret-only` with `data-selected`), and a button on a freshly shown Insert tab is only marked
  at the next selection change.
- **Escape in the ribbon's Font and Size boxes does not put the shown value back**; they apply on Enter
  or on leaving the box.
- **A click in F8 extend mode ends the mode.** Word may extend the selection to the click instead;
  settle it with a Word probe.
- **A submenu opened from a menu that scrolls** (`placeMenu` caps a tall menu's height) is clipped by
  it.
- **A drag that would reorder a paragraph's fields, pictures or notes is undone** with a status note:
  `Rewrite` requires them to keep their order.
- Touch pinch-zoom is untested on a real touch device.

## Missing features

Absent from the core: shapes, text boxes, equations, cover page, captions, cross-references, column
breaks, hyphenation, line numbers, picture position, sort, text effects, citations, spelling (browser
spellcheck only), show-markup options, compare, read mode, web layout, split and one/two-page views.
