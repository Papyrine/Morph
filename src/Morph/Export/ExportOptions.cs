namespace Morph;

/// <summary>
/// Shared base for the per-format export options records (<see cref="HtmlExportOptions"/>,
/// <see cref="MarkdownExportOptions"/>, <see cref="PdfExportOptions"/>, <see cref="ImageExportOptions"/>).
/// Properties here apply to every output format; format-specific knobs live on the derived records.
/// </summary>
public abstract record ExportOptions
{
    /// <summary>
    /// Optional path to a directory containing the font files to use for measurement / embedding.
    /// When set, only fonts from this directory (searched recursively) are used; system/user/Office
    /// font caches and OS-level fallbacks are ignored. Use this to make rendering deterministic
    /// across machines.
    /// </summary>
    public string? FontDirectory { get; init; }

    /// <summary>
    /// Overrides the fallback font family used when the source document does not declare a default
    /// run font. When <c>null</c>, <see cref="DefaultFontSettings.DefaultFont"/> is used.
    /// </summary>
    public string? DefaultFont { get; init; }

    /// <summary>
    /// Invoked for every feature the source document contained that couldn't be fully represented
    /// in the chosen output format — unsupported elements, missing fonts, inline images that
    /// failed to decode, etc. Null disables warning emission entirely.
    /// </summary>
    public Action<ExportWarning>? OnWarning { get; init; }
}
