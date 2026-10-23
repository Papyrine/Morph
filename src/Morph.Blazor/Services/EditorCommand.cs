using Morph;

/// <summary>
/// One of the editor's commands: what it is called and drawn with, where on the ribbon it is, Word's key
/// for it, when it can be used, whether it is on, and what it does. The ribbon's buttons, its menus' items
/// and Word's keys all run a command (MorphEditor.RunCommandAsync), and the script names the one a key or
/// a button it handles is for, so a command is enabled, shown and checked by one rule however it is
/// reached — the rule asked of the one <see cref="EditorState"/>.
///
/// As on Office's ribbon, a command that cannot be used is greyed out rather than taken away, so the
/// ribbon never moves under the pointer: only a contextual tab comes and goes.
/// </summary>
sealed class EditorCommand(string name, string label, Func<MorphEditor, Task> run)
{
    public EditorCommand(string name, string label, Action<MorphEditor> run) :
        this(
            name,
            label,
            _ =>
            {
                run(_);
                return Task.CompletedTask;
            })
    {
    }

    /// <summary>What the script calls it: the name a key, or a button the script handles, hands in.</summary>
    public string Name => name;

    /// <summary>A large or small button's text, an icon's accessible name, a menu item's text.</summary>
    public string Label => label;

    /// <summary>What hovering says, where that is more than the label.</summary>
    public string? Tip { get; init; }

    /// <summary>
    /// Word's key for it, as Word's tooltips write it ("Ctrl+B"). The script binds the key (wordKey in
    /// morph-edit.js), since a key's default has to be stopped while it is still being pressed.
    /// </summary>
    public string? Key { get; init; }

    public Icon? Icon { get; init; }

    /// <summary>Where it is on the ribbon, "Home › Font"; null for one only a key runs.</summary>
    public string? Place { get; init; }

    /// <summary>The menu its button opens, by name.</summary>
    public string? Menu { get; init; }

    /// <summary>What its press opens, for <c>aria-haspopup</c>: a menu, a listbox or a dialog.</summary>
    public string? Opens { get; init; }

    public CommandHandler Handler { get; init; }

    public Func<EditorState, bool> Enabled { get; init; } = _ => _.Editable;

    /// <summary>Whether it is on the ribbon at all: only what the host or the document rules out is not.</summary>
    public Func<EditorState, bool> Visible { get; init; } = _ => true;

    /// <summary>
    /// Whether it is on, for a command that is on or off; null for one that is not, and for those whose
    /// pressed state the script keeps as the caret moves (bold, the alignments).
    /// </summary>
    public Func<EditorState, bool?> Checked { get; init; } = _ => null;

    public Func<MorphEditor, Task> Run => run;

    /// <summary>The tooltip, its key after it as Word writes it: "Bold (Ctrl+B)".</summary>
    public string Title
    {
        get
        {
            var tip = Tip ?? Label;
            if (Key is null)
            {
                return tip;
            }

            return $"{tip} ({Key})";
        }
    }

    /// <summary>The key as <c>aria-keyshortcuts</c> names it: "Control+B".</summary>
    public string? KeyShortcuts => Key?.Replace("Ctrl+", "Control+", StringComparison.Ordinal);
}
