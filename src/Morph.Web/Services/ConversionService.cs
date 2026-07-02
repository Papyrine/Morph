namespace Morph.Web.Services;

/// <summary>
/// Wraps Morph's converters for the browser: a DOCX arrives as an in-memory <c>byte[]</c> (a WASM client
/// has no filesystem) and every output is produced as bytes ready to download. Rendering uses the
/// <see cref="ImageSharpDocumentConverter"/> because it is pure-managed and runs in WebAssembly; the Skia
/// backend would need a native wasm build that the NuGet assets don't ship. The rendered formats (PNG,
/// PDF) resolve fonts against a directory of bundled Aptos faces (<see cref="FontStore"/>) with every
/// other family mapped onto Aptos — so any document renders, its own fonts substituted rather than
/// failing. The text formats (Markdown, plain text) need no fonts.
/// </summary>
public static class ConversionService
{
    static IReadOnlyList<FormatInfo> AllFormats { get; } =
    [
        new(OutputFormat.Png, "PNG image", ".png", "image/png"),
        new(OutputFormat.Pdf, "PDF", ".pdf", "application/pdf"),
        new(OutputFormat.Markdown, "Markdown", ".md", "text/markdown"),
        new(OutputFormat.Text, "Plain text", ".txt", "text/plain"),
    ];

    /// <summary>Everything the app can convert a DOCX into.</summary>
    public static IReadOnlyList<FormatInfo> WritableFormats => AllFormats;

    /// <summary>The single input format — Word's OOXML .docx.</summary>
    public const string ReadableAccept = ".docx";

    /// <summary>Looks up the <see cref="FormatInfo"/> for an output format.</summary>
    public static FormatInfo? Find(OutputFormat format) =>
        AllFormats.FirstOrDefault(_ => _.Format == format);

    /// <summary>True when the uploaded file name looks like a DOCX this app can read.</summary>
    public static bool CanRead(string fileName) =>
        fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    // Any font the document names that the bundled Aptos faces don't cover is mapped to Aptos, so a doc
    // using Calibri, Times New Roman, "Aptos Light", etc. still renders (substituted, not failed).
    const string fallbackFont = "Aptos";

    /// <summary>
    /// Renders every page of the DOCX to a PNG — one <c>byte[]</c> per page. Drives both the on-screen
    /// preview and the PNG download. <paramref name="fontDirectory"/> pins font resolution to the bundled
    /// Aptos faces (see <see cref="FontStore"/>): without it the renderer walks its OS-font fallback chain
    /// and throws in the browser / on a clean CI runner.
    /// </summary>
    public static IReadOnlyList<byte[]> RenderPngPages(byte[] docx, ImageSettings settings, string fontDirectory)
    {
        using var stream = new MemoryStream(docx);
        var converter = new ImageSharpDocumentConverter();
        return converter.ConvertToImageData(
            stream,
            new()
            {
                Dpi = settings.Dpi,
                FontDirectory = fontDirectory,
                FontFallback = _ => fallbackFont,
            });
    }

    /// <summary>Exports the DOCX as Markdown.</summary>
    public static string ToMarkdown(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocumentConverter.ConvertToMarkdown(stream);
    }

    /// <summary>
    /// Exports the DOCX as plain text. Morph has no text exporter, so this renders the semantic HTML
    /// fragment (no document wrapper, image references dropped) and flattens it via <see cref="TextExtraction"/>.
    /// </summary>
    public static string ToText(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        var html = DocumentConverter.ConvertToHtml(
            stream,
            new()
            {
                EmitDocument = false,
                EmbedImagesAsBase64 = false,
            });
        return TextExtraction.FromHtml(html);
    }

    /// <summary>
    /// Exports the DOCX as a vector-text PDF. <paramref name="fontDirectory"/> supplies the fonts to
    /// PdfSharp — in the browser this is the in-memory directory <see cref="FontStore"/> populates, since
    /// the PDF backend can't reach Morph's embedded fonts.
    /// </summary>
    public static byte[] ToPdf(byte[] docx, string fontDirectory)
    {
        using var stream = new MemoryStream(docx);
        return PdfDocumentConverter.ConvertToPdf(
            stream,
            new()
            {
                FontDirectory = fontDirectory,
                FontFallback = _ => fallbackFont,
            });
    }

    /// <summary>
    /// Produces the downloadable payload for the chosen <paramref name="format"/>. PNG is a single
    /// <c>.png</c> for a one-page document, or a <c>.zip</c> of <c>page_0001.png</c>… when it has several —
    /// the extension and content type therefore travel with the bytes rather than being read off
    /// <see cref="FormatInfo"/>, which can't know the page count up front. <paramref name="fontDirectory"/>
    /// pins font resolution for the rendered formats (PNG, PDF); the text formats ignore it.
    /// </summary>
    public static DownloadPayload BuildDownload(byte[] docx, OutputFormat format, ImageSettings image, string fontDirectory) =>
        format switch
        {
            OutputFormat.Png => PngDownload(RenderPngPages(docx, image, fontDirectory)),
            OutputFormat.Pdf => new(ToPdf(docx, fontDirectory), ".pdf", "application/pdf"),
            OutputFormat.Markdown => new(Encoding.UTF8.GetBytes(ToMarkdown(docx)), ".md", "text/markdown"),
            OutputFormat.Text => new(Encoding.UTF8.GetBytes(ToText(docx)), ".txt", "text/plain"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown output format."),
        };

    static DownloadPayload PngDownload(IReadOnlyList<byte[]> pages) =>
        pages.Count == 1
            ? new(pages[0], ".png", "image/png")
            : new(ZipPages(pages), ".zip", "application/zip");

    static byte[] ZipPages(IReadOnlyList<byte[]> pages)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < pages.Count; i++)
            {
                var entry = archive.CreateEntry($"page_{i + 1:D4}.png", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(pages[i], 0, pages[i].Length);
            }
        }

        return memory.ToArray();
    }
}

/// <summary>A downloadable conversion result: the bytes plus the file extension and MIME type to serve them with.</summary>
public sealed record DownloadPayload(byte[] Bytes, string Extension, string ContentType);
