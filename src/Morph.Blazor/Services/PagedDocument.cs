using Morph;

/// <summary>
/// An Office file parsed once and laid out once, from which any page can be painted at any resolution
/// and whose text geometry can be read back for the selectable text layer.
///
/// The public converters parse and paginate on every call, which is right for a one-shot export and wrong
/// for a viewer: zooming re-renders the visible pages at a higher DPI, and scrolling renders pages one at a
/// time as they come into view, so re-parsing a long document per page would cost seconds on the
/// single-threaded WebAssembly runtime. Layout does not depend on DPI — the tree is in points — so it runs
/// once here and every render paints a one-page slice of it.
///
/// Rendering goes through the same internals the public ImageSharp converters use (the parse seams, the
/// canonical layout recipe, <c>ImageSharpPainter</c>), so a page painted here is byte-identical to the
/// same page from <see cref="ConversionService.RenderPngPages"/>.
/// </summary>
sealed class PagedDocument : IDisposable
{
    // A context bakes its DPI into its scale and its pixel-keyed caches, so each resolution gets its own.
    // Three covers what a viewer holds at once — the zoomed pages, the sidebar thumbnails, and a print run.
    const int maxContexts = 3;

    readonly ParsedDocument document;
    readonly LaidOutDocument laidOut;
    readonly ImageExportOptions options;
    readonly PageTextLayer?[] textLayers;

    // Most recently used last.
    readonly List<(int Dpi, ImageSharpRenderContext Context)> contexts = [];

    // Uncontended in the browser (one thread), but the host test runner renders in parallel.
    readonly Lock gate = new();
    bool disposed;

    PagedDocument(ParsedDocument document, LaidOutDocument laidOut, ImageExportOptions options)
    {
        this.document = document;
        this.laidOut = laidOut;
        this.options = options;
        textLayers = new PageTextLayer?[laidOut.Pages.Count];
    }

    /// <summary>
    /// Parses and paginates <paramref name="bytes"/>, resolving fonts against
    /// <paramref name="fontDirectory"/> with every unknown family mapped to Aptos — the same options every
    /// other conversion in this package uses (<see cref="ConversionService.ImageOptions"/>).
    /// </summary>
    public static PagedDocument Open(byte[] bytes, InputFormat source, string fontDirectory)
    {
        // The DPI is irrelevant to parsing and layout; each render supplies its own.
        var options = ConversionService.ImageOptions(96, fontDirectory);

        // What the public render entry points do before parsing: it locks the process-wide default font.
        DefaultFontSettings.MarkRenderOccurred();

        using var stream = new MemoryStream(bytes);
        var document = source switch
        {
            InputFormat.Docx => DocumentConverter.Parse(stream, options.DefaultFont, options.UseLetterPageSize),
            InputFormat.Xlsx => ExcelConverter.Parse(stream, options),
            InputFormat.Pptx => PowerPointConverter.Parse(stream, options.DefaultFont),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown input format.")
        };

        return new(document, ImageSharpDocumentConverter.Layout(document, options), options);
    }

    public int PageCount => laidOut.Pages.Count;

    /// <summary>Page width in points. Pages can differ: a section break or a sheet can change the paper.</summary>
    public double WidthPoints(int index) => laidOut.Pages[index].Settings.WidthPoints;

    public double HeightPoints(int index) => laidOut.Pages[index].Settings.HeightPoints;

    /// <summary>The page's selectable text, built on first request and cached.</summary>
    public PageTextLayer TextLayer(int index)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return textLayers[index] ??= TextLayerBuilder.Build(laidOut.Pages[index]);
        }
    }

    /// <summary>Paints one page (zero-based <paramref name="index"/>) at <paramref name="dpi"/> to a PNG.</summary>
    public byte[] RenderPage(int index, int dpi)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var context = ContextFor(dpi);
            try
            {
                // Restricting the tree to one page is safe once pagination is resolved: page-number
                // fields are already baked per page (LaidOutDocument.Restrict relies on the same fact).
                var onePage = new LaidOutDocument([laidOut.Pages[index]]);
                return PageSink.ToMemory(_ => ImageSharpPainter.Paint(onePage, context, PageCrop.FullPage, _))[0];
            }
            finally
            {
                // The context outlives the page; its decoded pictures must not.
                context.ReleaseImages();
            }
        }
    }

    ImageSharpRenderContext ContextFor(int dpi)
    {
        var index = contexts.FindIndex(_ => _.Dpi == dpi);
        if (index >= 0)
        {
            var entry = contexts[index];
            contexts.RemoveAt(index);
            contexts.Add(entry);
            return entry.Context;
        }

        if (contexts.Count == maxContexts)
        {
            contexts[0].Context.Dispose();
            contexts.RemoveAt(0);
        }

        var context = ImageSharpDocumentConverter.CreateContext(
            document,
            options with
            {
                Dpi = dpi
            });
        contexts.Add((dpi, context));
        return context;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var (_, context) in contexts)
            {
                context.Dispose();
            }

            contexts.Clear();
        }
    }
}
