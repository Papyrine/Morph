/// <summary>
/// One line of one of the editor's menus — a ribbon button's dropdown or the right-click menu: one of the
/// editor's commands, or one of the choices a menu offers; a heading over a group of them, or a line
/// between groups; or, with lines of its own, a submenu. A menu is a list of these, made where what it
/// offers is known, and one fragment draws every menu from its lines (MorphEditor.Menu) — so the
/// right-click menu offers the ribbon's own commands, greyed out by the same rules.
/// </summary>
sealed record MenuLine(string Label)
{
    public MenuLineKind Kind { get; init; }

    /// <summary>The command the line runs, whose rule decides whether it is greyed out.</summary>
    public EditorCommand? Command { get; init; }

    /// <summary>What choosing the line does, for one that is not a command.</summary>
    public Func<Task>? Choice { get; init; }

    /// <summary>Whether choosing it leaves the menu open, as a border's width chosen before the border.</summary>
    public bool StaysOpen { get; init; }

    /// <summary>Whether it is the one chosen of a set, or on; null for a line that is neither.</summary>
    public bool? Checked { get; init; }

    /// <summary>On or off on its own, rather than one of a set.</summary>
    public bool Toggle { get; init; }

    /// <summary>A choice that cannot be made where the reader is. A command's own rule decides its line.</summary>
    public bool Disabled { get; init; }

    /// <summary>What is shown after the label, as a margin preset's sizes.</summary>
    public string? Detail { get; init; }

    /// <summary>The lines of the submenu it opens.</summary>
    public IReadOnlyList<MenuLine>? Lines { get; init; }

    public static MenuLine Separator { get; } = new("") {Kind = MenuLineKind.Separator};

    public static MenuLine Heading(string label) =>
        new(label) {Kind = MenuLineKind.Heading};

    public static MenuLine Of(EditorCommand command, string? label = null) =>
        new(label ?? command.Label) {Command = command};
}
