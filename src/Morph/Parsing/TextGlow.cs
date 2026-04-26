/// <summary>
/// Soft halo around text (w14:glow). Captures the radius and colour;
/// scheme-colour transforms (lumMod/lumOff) from OOXML are not modelled.
/// </summary>
sealed record TextGlow
{
    /// <summary>Glow colour as 6-digit hex.</summary>
    public required string ColorHex { get; init; }

    /// <summary>Radius in points.</summary>
    public required double RadiusPoints { get; init; }

    /// <summary>Alpha 0–100 (100 = fully opaque).</summary>
    public required int AlphaPercent { get; init; }
}
