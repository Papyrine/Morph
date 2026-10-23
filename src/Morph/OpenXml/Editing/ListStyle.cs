/// <summary>
/// An item of Word's Bullets or Numbering library (<see cref="ListTemplates.Bullets"/>,
/// <see cref="ListTemplates.Numbers"/>): how a list's first level is numbered — its format, its text
/// (<c>%1.</c>, or a bullet's character in <see cref="Font"/>), whether the number stands right of its
/// place, and how far it hangs, in twips. <see cref="Shown"/> is how a menu shows it, the bullet's
/// character as the browser has it rather than the symbol font's.
/// </summary>
sealed record ListStyle(string Name, ListKind Kind, NumberFormatValues Format, string Text, string? Font, bool Right, int Hanging, string Shown)
{
    /// <summary>Whether the bullet's font is named for complex scripts too, as Word names Courier New.</summary>
    public bool ComplexScript { get; init; }
}
