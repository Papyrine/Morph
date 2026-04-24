/// <summary>
/// Document-level hyphenation settings.
/// </summary>
sealed record HyphenationSettings
{
    /// <summary>
    /// When true, automatic hyphenation is enabled for the document.
    /// </summary>
    public bool AutoHyphenation { get; init; }

    /// <summary>
    /// The hyphenation zone in points. Words within this distance from the right margin
    /// may be hyphenated. Default is 18 points (0.25 inch).
    /// </summary>
    public double HyphenationZonePoints { get; init; } = 18;

    /// <summary>
    /// Maximum number of consecutive lines that can end with a hyphen.
    /// 0 means unlimited. Default is 0.
    /// </summary>
    public int ConsecutiveHyphenLimit { get; init; }

    /// <summary>
    /// When true, words in all capital letters will not be hyphenated.
    /// </summary>
    public bool DoNotHyphenateCaps { get; init; }
}