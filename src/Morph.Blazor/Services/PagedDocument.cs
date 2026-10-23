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

    // Where the placed text came from, read off the whole layout on the first text layer asked for: a
    // paragraph that runs over a page break is followed from the page it starts on.
    SourceIndex? sources;
    bool sourcesBuilt;

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
    ///
    /// <paramref name="traceSources"/> has a Word document's text layers say where in the file their text
    /// came from (<see cref="PageTextLayer.Sources"/>), which ties the viewer's comments and tracked
    /// changes to the page. It changes nothing that is drawn, and a preview has no use for it.
    /// </summary>
    public static PagedDocument Open(byte[] bytes, InputFormat source, string fontDirectory, bool traceSources = false)
    {
        // The DPI is irrelevant to parsing and layout; each render supplies its own.
        var options = ConversionService.ImageOptions(96, fontDirectory);

        // What the public render entry points do before parsing: it locks the process-wide default font.
        DefaultFontSettings.MarkRenderOccurred();

        // A file the editor has just written is read from the package its edit still holds open, whose
        // tree is that file's: reading the body's XML again was half of the parse.
        if (source != InputFormat.Docx ||
            !DocumentReading.Borrow(bytes, _ => DocumentConverter.Parse(_, options.DefaultFont, options.UseLetterPageSize, traceSources), out var document))
        {
            using var stream = new MemoryStream(bytes);
            document = source switch
            {
                InputFormat.Docx => DocumentConverter.Parse(stream, options.DefaultFont, options.UseLetterPageSize, traceSources),
                InputFormat.Xlsx => ExcelConverter.Parse(stream, options),
                InputFormat.Pptx => PowerPointConverter.Parse(stream, options.DefaultFont),
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown input format.")
            };
        }

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
            return textLayers[index] ??= TextLayerBuilder.Build(laidOut.Pages[index], SourcesLocked());
        }
    }

    /// <summary>
    /// Where the pages' text came from in the file; null for a workbook or a deck, and for a document
    /// with nothing on its pages to trace.
    /// </summary>
    public SourceIndex? Sources
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return SourcesLocked();
            }
        }
    }

    /// <summary>
    /// Where the document's paragraphs are on the pages, for editing them in place.
    /// <paramref name="outline"/> is the same file read for editing.
    /// </summary>
    public EditMap Edits(DocumentOutline outline)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // A paragraph stands between the margins when it is the body's, a header's, a footer's or a
            // note's, rather than a cell's or a text box's.
            var body = document.Elements
                .OfType<ParagraphElement>()
                .Select(_ => _.Source)
                .OfType<int>()
                .ToHashSet();
            foreach (var paragraph in outline.Paragraphs)
            {
                if (paragraph.Part != ReviewPart.Body &&
                    !paragraph.InTable)
                {
                    body.Add(paragraph.Ordinal);
                }
            }
            return EditMap.Build(laidOut, SourcesLocked(), _ => outline.ParagraphOf(_)?.Ordinal, body);
        }
    }

    /// <summary>
    /// The body's floating pictures on a page, for the editor to select and size. The layout keeps no
    /// note of where a picture came from, and must not grow one for the viewer's sake; a picture is
    /// found instead by its image's bytes — the very array the parser read, which the layout draws
    /// (the raster fallback, for an SVG) — and the parser's note on the picture of where its drawing
    /// is (<see cref="FloatingImageElement.Source"/>). An SVG with no raster fallback, a picture in a
    /// header, a footer or a rotated group, and two pictures sharing one array, are not offered.
    /// </summary>
    public IReadOnlyList<PagePicture> Pictures(int pageIndex, DocumentOutline outline)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var sources = new Dictionary<byte[], RunSource?>(ReferenceEqualityComparer.Instance);
            Collect(document.Elements);
            var found = new List<PagePicture>();
            Walk(laidOut.Pages[pageIndex].Items);
            return found;

            void Collect(IEnumerable<DocumentElement> elements)
            {
                foreach (var element in elements)
                {
                    if (element is FloatingImageElement image)
                    {
                        foreach (var data in new[] {image.ImageData, image.RasterFallbackData}.OfType<byte[]>())
                        {
                            // An array two pictures share cannot say which is which.
                            if (!sources.TryAdd(data, image.Source))
                            {
                                sources[data] = null;
                            }
                        }
                    }
                    else if (element is TableElement table)
                    {
                        foreach (var cell in table.Rows.SelectMany(_ => _.Cells))
                        {
                            Collect(cell.Content);
                        }
                    }
                }
            }

            void Walk(IReadOnlyList<PlacedItem> items)
            {
                foreach (var item in items)
                {
                    switch (item)
                    {
                        case PlacedImage
                        {
                            Data: { } data
                        } placed when sources.TryGetValue(data, out var source) &&
                                      source is { } from:
                            if (outline.ParagraphOf(from.Run) is { } paragraph &&
                                paragraph.OffsetOf(new(from.Run, from.Start)) is { } offset)
                            {
                                found.Add(new(placed.X, placed.Y, placed.Width, placed.Height, paragraph.Ordinal, offset));
                            }

                            break;
                        case PlacedTableRow row:
                            foreach (var cell in row.Cells)
                            {
                                Walk(cell.Floats);
                                Walk(cell.Content);
                            }

                            break;
                    }
                }
            }
        }
    }

    SourceIndex? SourcesLocked()
    {
        if (!sourcesBuilt)
        {
            sources = SourceIndex.Build(laidOut);
            sourcesBuilt = true;
        }

        return sources;
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

    /// <summary>
    /// The page (zero-based <paramref name="index"/>) as SVG for the browser to draw (<see cref="SvgPainter"/>),
    /// its text in <paramref name="fontFamily"/> — the face morph-text.js registers, which is the Aptos every
    /// family maps to here. <paramref name="dpi"/> only sets what the SVG still carries as pixels (warped
    /// WordArt). A page the SVG cannot carry whole (<see cref="SvgPage.Complete"/>) is painted with
    /// <see cref="RenderPage"/> instead.
    /// </summary>
    public SvgPage RenderSvg(int index, int dpi, string fontFamily = "Morph Text")
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var context = ContextFor(dpi);
            try
            {
                return SvgPainter.PaintPage(laidOut.Pages[index], context, fontFamily);
            }
            finally
            {
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
