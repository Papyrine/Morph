namespace Morph;

/// <summary>
/// Wraps Morph's converters for the browser: an Office file arrives as an in-memory <c>byte[]</c> (a WASM
/// client has no filesystem) and every output is produced as bytes ready to download. Three inputs are
/// read — Word <c>.docx</c>, Excel <c>.xlsx</c> and PowerPoint <c>.pptx</c> — and each converts to every
/// <see cref="OutputFormat"/>, so the source only picks which of Morph's converter families to route
/// through. Rendering uses the <see cref="ImageSharpDocumentConverter"/> family because it is pure-managed
/// and runs in WebAssembly; the Skia backend would need a native wasm build that the NuGet assets don't
/// ship. The rendered formats (PNG, PDF) resolve fonts against a directory of bundled Aptos faces
/// (<see cref="FontStore"/>) with every other family mapped onto Aptos — so any file renders, its own
/// fonts substituted rather than failing. The text formats (HTML, Markdown, plain text) draw nothing
/// but still take the directory, because a workbook's column widths are measured off its body font —
/// see <see cref="ToHtml"/>.
/// </summary>
public static class ConversionService
{
    static IReadOnlyList<FormatInfo> AllFormats { get; } =
    [
        new(OutputFormat.Png, "PNG image", ".png", "image/png"),
        new(OutputFormat.Pdf, "PDF", ".pdf", "application/pdf"),
        new(OutputFormat.Html, "HTML", ".html", "text/html"),
        new(OutputFormat.Markdown, "Markdown", ".md", "text/markdown"),
        new(OutputFormat.Text, "Plain text", ".txt", "text/plain"),
    ];

    /// <summary>Everything a source file can be converted into.</summary>
    public static IReadOnlyList<FormatInfo> WritableFormats => AllFormats;

    /// <summary>The Office formats that can be read, in the order the upload panel offers them.</summary>
    public static IReadOnlyList<InputFormatInfo> ReadableFormats { get; } =
    [
        new(InputFormat.Docx, "Word document", ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "📄", "Word", "page"),
        new(InputFormat.Xlsx, "Excel workbook", ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "📊", "Excel", "page"),
        new(InputFormat.Pptx, "PowerPoint presentation", ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "📽️", "PowerPoint", "slide"),
    ];

    /// <summary>The <c>accept</c> list for the file input — every readable extension.</summary>
    public static string ReadableAccept { get; } = string.Join(",", ReadableFormats.Select(_ => _.Extension));

    /// <summary>Looks up the <see cref="FormatInfo"/> for an output format.</summary>
    public static FormatInfo? Find(OutputFormat format) =>
        AllFormats.FirstOrDefault(_ => _.Format == format);

    /// <summary>
    /// Looks up the <see cref="InputFormatInfo"/> for an input format. Non-null (unlike the
    /// <see cref="OutputFormat"/> overload): every member of the enum has a row, and a new one added
    /// without one should fail loudly rather than render a blank label.
    /// </summary>
    public static InputFormatInfo Find(InputFormat format) =>
        ReadableFormats.First(_ => _.Format == format);

    /// <summary>
    /// Identifies an uploaded file by extension, or null when it isn't one Morph reads. Extension is
    /// the only signal available — the browser's reported MIME type varies by OS and is often empty.
    /// </summary>
    public static InputFormatInfo? Detect(string fileName) =>
        ReadableFormats.FirstOrDefault(_ => fileName.EndsWith(_.Extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the uploaded file name looks like something Morph can read.</summary>
    public static bool CanRead(string fileName) =>
        Detect(fileName) is not null;

    // Any font the source names that the bundled Aptos faces don't cover is mapped to Aptos, so a file
    // using Calibri, Times New Roman, "Aptos Light", etc. still renders (substituted, not failed).
    const string fallbackFont = "Aptos";

    /// <summary>
    /// Renders every page of the source to a PNG — one <c>byte[]</c> per page (per slide, for a deck).
    /// Drives both the on-screen preview and the PNG download. <paramref name="fontDirectory"/> pins font
    /// resolution to the bundled Aptos faces (see <see cref="FontStore"/>): without it the renderer walks
    /// its OS-font fallback chain and throws in the browser / on a clean CI runner.
    /// </summary>
    public static IReadOnlyList<byte[]> RenderPngPages(byte[] bytes, InputFormat source, ImageSettings settings, string fontDirectory)
    {
        using var stream = new MemoryStream(bytes);
        ImageExportOptions options = new()
        {
            Dpi = settings.Dpi,
            Crop = settings.Crop,
            FontDirectory = fontDirectory,
            FontFallback = _ => fallbackFont,
        };
        return source switch
        {
            InputFormat.Docx => new ImageSharpDocumentConverter().ConvertToImageData(stream, options),
            InputFormat.Xlsx => new ImageSharpExcelConverter().ConvertToImageData(stream, options),
            InputFormat.Pptx => new ImageSharpPowerPointConverter().ConvertToImageData(stream, options),
            _ => throw UnknownSource(source),
        };
    }

    /// <summary>
    /// Exports the source as Markdown. <paramref name="fontDirectory"/> pins font resolution for the
    /// same reason the HTML export needs it — see <see cref="ToHtml"/>.
    /// </summary>
    public static string ToMarkdown(byte[] bytes, InputFormat source, string fontDirectory)
    {
        using var stream = new MemoryStream(bytes);
        MarkdownExportOptions options = new()
        {
            FontDirectory = fontDirectory,
        };
        return source switch
        {
            InputFormat.Docx => DocumentConverter.ConvertToMarkdown(stream, options),
            InputFormat.Xlsx => ExcelConverter.ConvertToMarkdown(stream, options),
            InputFormat.Pptx => PowerPointConverter.ConvertToMarkdown(stream, options),
            _ => throw UnknownSource(source),
        };
    }

    /// <summary>
    /// Exports the source as a self-contained HTML document — the exporter's defaults emit the full
    /// document wrapper with styles inline and images embedded as data URIs, so the single file views
    /// anywhere with no companion assets.
    ///
    /// <paramref name="fontDirectory"/> pins font resolution even though nothing is drawn: Excel's
    /// column-width unit is the widest digit of the workbook's body font, so a sheet's <c>td</c> widths
    /// are whatever face resolves. Left to the OS they differ per machine — the sample invoice's Arial
    /// resolves on Windows and falls through to the bundled Aptos on a clean Linux runner, moving every
    /// column by 4%.
    /// </summary>
    public static string ToHtml(byte[] bytes, InputFormat source, string fontDirectory) =>
        ExportHtml(
            bytes,
            source,
            new()
            {
                FontDirectory = fontDirectory,
            });

    /// <summary>
    /// Exports the source as plain text. Morph has no text exporter, so this renders the semantic HTML
    /// fragment (no document wrapper, image references dropped) and flattens it via <see cref="TextExtraction"/>.
    /// </summary>
    public static string ToText(byte[] bytes, InputFormat source, string fontDirectory)
    {
        var html = ExportHtml(
            bytes,
            source,
            new()
            {
                FontDirectory = fontDirectory,
                EmitDocument = false,
                EmbedImagesAsBase64 = false,
            });
        return TextExtraction.FromHtml(html);
    }

    static string ExportHtml(byte[] bytes, InputFormat source, HtmlExportOptions options)
    {
        using var stream = new MemoryStream(bytes);
        return source switch
        {
            InputFormat.Docx => DocumentConverter.ConvertToHtml(stream, options),
            InputFormat.Xlsx => ExcelConverter.ConvertToHtml(stream, options),
            InputFormat.Pptx => PowerPointConverter.ConvertToHtml(stream, options),
            _ => throw UnknownSource(source),
        };
    }

    /// <summary>
    /// Exports the source as a vector-text PDF. <paramref name="fontDirectory"/> supplies the fonts to
    /// PdfSharp — in the browser this is the in-memory directory <see cref="FontStore"/> populates, since
    /// the PDF backend can't reach Morph's embedded fonts.
    /// </summary>
    public static byte[] ToPdf(byte[] bytes, InputFormat source, string fontDirectory)
    {
        using var stream = new MemoryStream(bytes);
        PdfExportOptions options = new()
        {
            FontDirectory = fontDirectory,
            FontFallback = _ => fallbackFont,
        };
        return source switch
        {
            InputFormat.Docx => PdfDocumentConverter.ConvertToPdf(stream, options),
            InputFormat.Xlsx => PdfExcelConverter.ConvertToPdf(stream, options),
            InputFormat.Pptx => PdfPowerPointConverter.ConvertToPdf(stream, options),
            _ => throw UnknownSource(source),
        };
    }

    /// <summary>
    /// Produces the downloadable payload for the chosen <paramref name="format"/>. PNG is a single
    /// <c>.png</c> for a one-page source, or a <c>.zip</c> of <c>page_0001.png</c>… when it has several —
    /// the extension and content type therefore travel with the bytes rather than being read off
    /// <see cref="FormatInfo"/>, which can't know the page count up front. <paramref name="fontDirectory"/>
    /// pins font resolution for every format — the text ones draw nothing, but a workbook's column widths
    /// are measured off its body font (see <see cref="ToHtml"/>).
    /// </summary>
    public static DownloadPayload BuildDownload(byte[] bytes, InputFormat source, OutputFormat format, ImageSettings image, string fontDirectory) =>
        format switch
        {
            OutputFormat.Png => PngDownload(RenderPngPages(bytes, source, image, fontDirectory)),
            OutputFormat.Pdf => new(ToPdf(bytes, source, fontDirectory), ".pdf", "application/pdf"),
            OutputFormat.Html => new(Encoding.UTF8.GetBytes(ToHtml(bytes, source, fontDirectory)), ".html", "text/html"),
            OutputFormat.Markdown => new(Encoding.UTF8.GetBytes(ToMarkdown(bytes, source, fontDirectory)), ".md", "text/markdown"),
            OutputFormat.Text => new(Encoding.UTF8.GetBytes(ToText(bytes, source, fontDirectory)), ".txt", "text/plain"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown output format."),
        };

    static ArgumentOutOfRangeException UnknownSource(InputFormat source) =>
        new(nameof(source), source, "Unknown input format.");

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
