using System.Text.RegularExpressions;

namespace Morph;

/// <summary>
/// Prepares Markdown for the on-screen result pane. The exporter embeds images as base64 data URIs —
/// for an image-heavy document that is megabytes of encoded payload drowning the actual text — so the
/// pane swaps each payload for a short size note. Display-only: the download keeps the full URIs.
/// </summary>
public static partial class MarkdownPreview
{
    /// <summary>
    /// Whether the Markdown carries a base64 payload large enough that <see cref="ElideImages"/> would
    /// replace it — i.e. whether the pane should caption that an image was omitted.
    /// </summary>
    public static bool HasElidableImages(string markdown) =>
        DataUriPayload().IsMatch(markdown);

    /// <summary>Replaces every sizeable base64 data-URI payload with a short "… KB elided …" note.</summary>
    public static string ElideImages(string markdown) =>
        DataUriPayload().Replace(
            markdown,
            match =>
            {
                // Base64 encodes 3 bytes per 4 characters.
                var kilobytes = match.Groups["payload"].Length * 3.0 / 4 / 1024;
                return $"{match.Groups["prefix"].Value}…{kilobytes:0.#} KB elided…";
            });

    // Payloads of 256+ characters (~192 bytes) elide; anything shorter is harmless to show inline.
    [GeneratedRegex(@"(?<prefix>data:[\w.+/-]+;base64,)(?<payload>[A-Za-z0-9+/=]{256,})")]
    private static partial Regex DataUriPayload();
}
