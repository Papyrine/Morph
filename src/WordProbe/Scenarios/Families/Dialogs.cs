using static Define;

// K — keys that open something rather than edit: a dialog, a pane, Backstage. Each is sent through
// dialog(...), which records the window class and title of what opened and presses Esc; the observation
// is the selection after Esc, the text and the undo list, all of which should be as they were.
static class DialogsFamily
{
    public static FamilyDefinition Definition { get; } = new("dialogs", "Keys that open a dialog, pane or view",
        "Each key that opens something: what opened, that the document and undo list did not change, and that Esc put the selection back.",
        Scenarios);

    static IEnumerable<ScenarioBuilder> Scenarios()
    {
        yield return Probe("K-01", "Keys that open a dialog")
            .In("plain")
            .Steps("select(P1:5)", "dialog({key})")
            .Axis("key",
                "Ctrl+F", "Ctrl+H", "Ctrl+G", "F5", "Ctrl+D", "Ctrl+Shift+F", "Ctrl+Shift+P", "Ctrl+K", "Ctrl+Alt+V",
                "Ctrl+Shift+G", "Alt+F3", "Alt+Shift+I", "Alt+Shift+X", "Alt+Shift+O", "Ctrl+Shift+F5", "F7", "Shift+F7")
            .Observing(Observe.Selection | Observe.Text | Observe.Undo)
            .Notes("Ctrl+F opens the Navigation pane in current Word rather than a dialog, so its record may show the main window; Ctrl+Alt+V (Paste Special) needs something on the clipboard and may refuse with nothing there; F7 and Shift+F7 may open the Editor pane instead of a dialog.")
            .Hypotheses("Every key leaves the text and the undo list untouched and Esc returns the caret to P1:5. The record names the dialog each key opened.")
            .Morph("PARTIAL");

        yield return Probe("K-02", "Keys that open a file dialog or Backstage")
            .In("plain")
            .Steps("select(P1:5)", "dialog({key})")
            .Axis("key", "Ctrl+O", "Ctrl+F12", "F12", "Ctrl+S", "Ctrl+P", "Ctrl+F2")
            .Observing(Observe.Selection | Observe.Text | Observe.Undo)
            .Notes("Ctrl+O, Ctrl+S, Ctrl+P and Ctrl+F2 open Backstage, a full-window view rather than a dialog, so the record may show Word's own window with a changed title; Ctrl+F12 and F12 open the classic Open and Save As dialogs. The fixture is unsaved, so Ctrl+S behaves as Save As.")
            .Hypotheses("Esc leaves Backstage and the classic dialogs alike with the document unchanged and the caret at P1:5.")
            .Morph("TODO");

        yield return Probe("K-03", "Keys that open a pane or the ribbon")
            .In("plain")
            .Steps("select(P1:5)", "dialog({key})")
            .Axis("key", "Ctrl+Shift+S", "Ctrl+Alt+Shift+S", "Alt+F8", "Shift+F10", "F1", "Ctrl+F1")
            .Observing(Observe.Selection | Observe.Text | Observe.Undo)
            .Notes("A pane keeps the main window in front, so the harness may record no dialog for Ctrl+Shift+S (Apply Styles), Ctrl+Alt+Shift+S (Styles) and F1 (Help); Alt+F8 is the Macros dialog; Shift+F10 opens the context menu; Ctrl+F1 collapses or expands the ribbon and Esc does not undo that, so the ribbon state after the run is noted.")
            .Hypotheses("None changes the document or the undo list; the caret is at P1:5 after Esc; Ctrl+F1 leaves the ribbon collapsed.")
            .Morph("TODO");

        yield return Probe("K-04", "Keys that move the selection without opening anything")
            .In("three-pages")
            .Steps("select(P1:3)", "{key}", "{key}")
            .Axis("key", "Ctrl+Alt+Y", "Ctrl+PgDn", "Ctrl+PgUp")
            .Trace()
            .Observing(Observe.Selection | Observe.Text | Observe.Undo)
            .Notes("Ctrl+Alt+Y repeats the last find; there has been none in the fixture's session. Ctrl+PgDn and Ctrl+PgUp browse by the last browse object, pages by default.")
            .Hypotheses("Ctrl+Alt+Y with no prior find does nothing, or opens Find; Ctrl+PgDn goes to the top of page 2 then page 3; Ctrl+PgUp from P1:3 goes to the document start and stays.")
            .Morph("TODO");
    }
}
