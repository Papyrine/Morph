/// <summary>
/// What a formatting command changes in the cells it is given: each property set here is set, each left
/// null is left as each cell has it. A colour is six hex digits; an empty fill takes the fill away.
/// </summary>
sealed record CellFormatChange
{
    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public bool? Underline { get; init; }

    public bool? Strikethrough { get; init; }

    public string? FontFamily { get; init; }

    public double? FontSizePoints { get; init; }

    public string? ColorHex { get; init; }

    /// <summary>The fill's colour, or an empty string for no fill.</summary>
    public string? FillHex { get; init; }

    /// <summary><c>left</c>, <c>center</c>, <c>right</c>, or <c>general</c> to follow the value.</summary>
    public string? Horizontal { get; init; }

    /// <summary><c>top</c>, <c>center</c> or <c>bottom</c>.</summary>
    public string? Vertical { get; init; }

    public bool? Wrap { get; init; }

    /// <summary>A number format's code, <c>General</c> to take the format away.</summary>
    public string? NumberFormat { get; init; }

    /// <summary>The cells' outside edges (<c>outside</c>), every edge (<c>all</c>), or none (<c>none</c>).</summary>
    public string? Borders { get; init; }
}
