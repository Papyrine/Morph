/// <summary>
/// Provides region-based default page sizes matching Microsoft Word behavior.
/// Letter (8.5" x 11") in North America, A4 (210 x 297mm) elsewhere.
/// </summary>
static class DefaultPageSize
{
    // Letter: 8.5" x 11" = 612 x 792 points
    const double letterWidthPoints = 612.0;
    const double letterHeightPoints = 792.0;

    // A4: 210mm x 297mm = 595.28 x 841.89 points
    const double a4WidthPoints = 595.28;
    const double a4HeightPoints = 841.89;

    static HashSet<string> letterRegions =
    [
        with(StringComparer.OrdinalIgnoreCase),
        // United States
        "US",
        // Canada
        "CA",
        // Mexico
        "MX",
        // Philippines
        "PH",
        // Chile
        "CL",
        // Colombia
        "CO",
        // Venezuela
        "VE",
        // Guatemala
        "GT",
        // Costa Rica
        "CR",
        // Panama
        "PA"
    ];

    static bool? useLetterSize;

    /// <summary>
    /// Gets or sets whether to use Letter size (true) or A4 size (false).
    /// When null, automatically determined from system region.
    /// </summary>
    public static bool UseLetterSize
    {
        get => useLetterSize ?? IsLetterRegion();
        set => useLetterSize = value;
    }

    /// <summary>
    /// Resets to automatic region-based detection.
    /// </summary>
    public static void ResetToAutoDetect() => useLetterSize = null;

    /// <summary>Default page width in points.</summary>
    public static double WidthPoints => UseLetterSize ? letterWidthPoints : a4WidthPoints;

    /// <summary>Default page height in points.</summary>
    public static double HeightPoints => UseLetterSize ? letterHeightPoints : a4HeightPoints;

    static bool IsLetterRegion()
    {
        var region = RegionInfo.CurrentRegion;
        return letterRegions.Contains(region.TwoLetterISORegionName);
    }
}
