/// <summary>
/// Represents borders for all four edges of a cell.
/// Diagonals (<c>w:tl2br</c> / <c>w:tr2bl</c>) are tracked separately on
/// <see cref="TableCellProperties.Diagonals"/> so they don't interfere with the
/// cell→table 4-side border cascade.
/// </summary>
sealed record CellBorders
{
    public BorderEdge Top { get; init; } = BorderEdge.None;
    public BorderEdge Right { get; init; } = BorderEdge.None;
    public BorderEdge Bottom { get; init; } = BorderEdge.None;
    public BorderEdge Left { get; init; } = BorderEdge.None;

    /// <summary>
    /// Which sides this record actually states. A cell's <c>w:tcBorders</c> (and a table style's
    /// conditional <c>w:tcBorders</c>) may name only some of the four: the ones it leaves out are
    /// NOT switched off, they keep whatever the table-level cascade gives that position. Word-read on
    /// business-plans/10, whose header cells declare only a 1.5pt <c>w:bottom</c> and whose first body
    /// row only a <c>w:top</c> — Word draws the table-level grid around them, so the outer box and
    /// every vertical rule survive. Reading the record as a whole box instead dropped those three
    /// sides AND, because a missing side then read as an explicit <c>nil</c>, suppressed the shared
    /// inside rules of the neighbouring cells too. Paragraph, page and run borders declare all four.
    /// </summary>
    public BorderSides Declared { get; init; } = BorderSides.All;

    /// <summary>Returns true if any border edge is visible.</summary>
    public bool HasAnyBorder => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;

    /// <summary>True when the given side is declared here AND declared invisible (<c>nil</c>/<c>none</c>).</summary>
    public bool DeclaresInvisible(BorderSides side) =>
        (Declared & side) != 0 && !Edge(side).IsVisible;

    public BorderEdge Edge(BorderSides side) => side switch
    {
        BorderSides.Top => Top,
        BorderSides.Right => Right,
        BorderSides.Bottom => Bottom,
        _ => Left
    };

    /// <summary>
    /// This record layered over <paramref name="under"/>: every side declared here wins, every other
    /// side comes from <paramref name="under"/>, and the result declares the union. A null
    /// <paramref name="under"/> leaves the undeclared sides as they are.
    /// </summary>
    public CellBorders Over(CellBorders? under)
    {
        if (under == null || Declared == BorderSides.All)
        {
            return this;
        }

        return new()
        {
            Top = (Declared & BorderSides.Top) != 0 ? Top : under.Top,
            Right = (Declared & BorderSides.Right) != 0 ? Right : under.Right,
            Bottom = (Declared & BorderSides.Bottom) != 0 ? Bottom : under.Bottom,
            Left = (Declared & BorderSides.Left) != 0 ? Left : under.Left,
            Declared = Declared | under.Declared
        };
    }

    public static CellBorders All => new()
    {
        Top = BorderEdge.Default,
        Right = BorderEdge.Default,
        Bottom = BorderEdge.Default,
        Left = BorderEdge.Default
    };

    /// <summary>
    /// The same edge on all four sides — a run border (<c>w:bdr</c>), which OOXML declares once and
    /// Word draws as a box around the run. Lets the run path reuse the cell/paragraph edge painter
    /// rather than growing its own stroke code in each backend.
    /// </summary>
    public static CellBorders Uniform(BorderEdge edge) => new()
    {
        Top = edge,
        Right = edge,
        Bottom = edge,
        Left = edge
    };
}

/// <summary>The sides a <see cref="CellBorders"/> record states — see <see cref="CellBorders.Declared"/>.</summary>
[Flags]
enum BorderSides
{
    None = 0,
    Top = 1,
    Right = 2,
    Bottom = 4,
    Left = 8,
    All = Top | Right | Bottom | Left
}