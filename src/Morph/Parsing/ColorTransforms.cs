/// <summary>
/// Color transform parameters for theme colors.
/// Shade and tint use the Word/OpenXML 0-255 scale.
/// </summary>
sealed record ColorTransforms
{
    /// <summary>Shade value (0-255, darkens the color).</summary>
    public byte? Shade { get; init; }

    /// <summary>Tint value (0-255, lightens the color).</summary>
    public byte? Tint { get; init; }

    /// <summary>Luminance modulation percentage (e.g., 75 = 75% brightness).</summary>
    public double? LumMod { get; init; }

    /// <summary>Luminance offset percentage points.</summary>
    public double? LumOff { get; init; }

    /// <summary>Saturation modulation percentage (e.g., 50 = 50% saturation).</summary>
    public double? SatMod { get; init; }

    /// <summary>Saturation offset percentage points.</summary>
    public double? SatOff { get; init; }

    /// <summary>Returns true if any transform is specified.</summary>
    public bool HasTransforms =>
        Shade.HasValue ||
        Tint.HasValue ||
        LumMod.HasValue ||
        LumOff.HasValue ||
        SatMod.HasValue ||
        SatOff.HasValue;
}
