/// <summary>How a cell looks in the grid: its font, its fill, its edges, how its text stands in it.</summary>
sealed record GridLook
{
    public static GridLook Plain { get; } = new();

    public string? FontFamily { get; init; }

    public double? FontSizePoints { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public bool Strikethrough { get; init; }

    public string? ColorHex { get; init; }

    public string? FillHex { get; init; }

    public TextAlignment? Horizontal { get; init; }

    public CellVerticalAlignment Vertical { get; init; } = CellVerticalAlignment.Bottom;

    public bool Wrap { get; init; }

    public int Indent { get; init; }

    public string? NumberFormat { get; init; }

    public CellBorders Borders { get; init; } = new();

    public bool IsPlain =>
        FillHex == null &&
        !Borders.HasAnyBorder;
}
