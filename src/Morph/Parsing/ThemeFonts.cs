/// <summary>
/// Theme font definitions from the document theme.
/// </summary>
sealed class ThemeFonts
{
    /// <summary>Major font for headings (e.g., "Calibri Light").</summary>
    public string MajorFont { get; init; } = "Calibri Light";

    /// <summary>Minor font for body text (e.g., "Calibri").</summary>
    public string MinorFont { get; init; } = "Calibri";

    /// <summary>
    /// Resolves a theme font reference to the actual font name.
    /// </summary>
    /// <param name="themeFontName">Theme font reference (e.g., "majorHAnsi", "minorHAnsi")</param>
    /// <returns>The resolved font name, or null if not a recognized theme font reference.</returns>
    public string? ResolveFont(string themeFontName) =>
        // OpenXML ThemeFontValues stores raw XML values: majorHAnsi, minorHAnsi, etc.
        themeFontName.ToLowerInvariant() switch
        {
            "majorhansi" or "majorascii" or "majorbidi" or "majoreastasia" => MajorFont,
            "minorhansi" or "minorascii" or "minorbidi" or "minoreastasia" => MinorFont,
            _ => null
        };
}