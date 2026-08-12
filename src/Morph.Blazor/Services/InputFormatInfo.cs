namespace Morph;

/// <summary>
/// Browser-facing metadata for an <see cref="InputFormat"/>: how to label it, which file names and MIME
/// type identify it, and where its bundled sample lives.
/// </summary>
/// <param name="Format">The format this describes.</param>
/// <param name="DisplayName">Human name for the source kind, e.g. "Word document".</param>
/// <param name="Extension">The file extension, including the dot.</param>
/// <param name="ContentType">MIME type, used when the file is handed back to the browser.</param>
/// <param name="Icon">Emoji shown beside the format in the upload panel and sample buttons.</param>
/// <param name="ShortName">One-word label for the sample button, e.g. "Word".</param>
/// <param name="PageNoun">What one rendered page is called — a deck renders slides, not pages.</param>
public record InputFormatInfo(
    InputFormat Format,
    string DisplayName,
    string Extension,
    string ContentType,
    string Icon,
    string ShortName,
    string PageNoun)
{
    /// <summary>
    /// Path of the bundled sample file, relative to the app base. It ships in this package's
    /// <see cref="MorphAssets.ContentRoot">static web assets</see> and is fetched on demand, so it
    /// only downloads when a user actually asks for the sample.
    /// </summary>
    public string SampleAsset => $"{MorphAssets.ContentRoot}/sample/{SampleFileName}";

    /// <summary>File name the sample is presented under once loaded.</summary>
    public string SampleFileName => $"sample{Extension}";

    /// <summary>Pluralises <see cref="PageNoun"/> for a count, e.g. "1 page", "3 slides".</summary>
    public string PageLabel(int count) =>
        $"{count} {PageNoun}{(count == 1 ? "" : "s")}";
}
