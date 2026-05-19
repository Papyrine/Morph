/// <summary>
/// Stroke around glyph outlines (w14:textOutline). Currently captures the most
/// commonly used parameters (width and solid colour); cap/dash/compound style
/// from the OOXML are not modelled.
/// </summary>
sealed record TextOutline
{
    /// <summary>Stroke colour as 6-digit hex (e.g. "000000").</summary>
    public required string ColorHex { get; init; }

    /// <summary>Stroke width in points.</summary>
    public required double WidthPoints { get; init; }
}
