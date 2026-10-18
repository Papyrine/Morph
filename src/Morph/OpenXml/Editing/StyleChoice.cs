/// <summary>
/// A paragraph style an editor can offer: its id, the name Word shows for it, whether it belongs in
/// the style gallery, whether the document defines it yet (<see cref="BuiltInStyles"/> adds one it
/// does not), and enough of how its text looks to draw a sample of it.
/// </summary>
sealed record StyleChoice(string Id, string Name, bool Gallery, bool Defined)
{
    public double SizePoints { get; init; } = 11;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    /// <summary>Its text's colour as six hex digits, or null for the automatic colour.</summary>
    public string? ColorHex { get; init; }
}
