using P = DocumentFormat.OpenXml.Presentation;

/// <summary>
/// Parses a PPTX package into the shared <see cref="ParsedDocument"/> model, so slides reach every
/// output — PNG, PDF, HTML and Markdown — through the same layout engine and painters as DOCX.
///
/// The mapping is deliberately thin, because a slide already IS what the engine calls a page of body
/// floats:
/// <list type="bullet">
/// <item><c>p:sldSz</c> becomes the <see cref="PageSettings"/> page box, with zero margins — a slide
/// has no margin concept, and zero margins also pin the flow cursor at the page top, which the float
/// anchoring in <see cref="SlideShapeParser"/> depends on.</item>
/// <item>Slides are separated by a <see cref="PageBreakElement"/>. It is not decoration: a slide
/// contributes no flow items, so <c>Fragmenter.FinishPage</c> would discard the page as a natural
/// overflow blank unless an explicit break marks it deliberate. One break BETWEEN each pair of
/// slides — a trailing one would add a blank page.</item>
/// <item>Shape order in <c>p:spTree</c> is z-order, and is preserved as emission order.</item>
/// </list>
/// Slide size is a presentation-level property, so no section breaks are needed: every page in a deck
/// shares one geometry.
/// </summary>
sealed class PresentationParser(string defaultFont)
{
    /// <summary>Default 4:3 slide, used when a package declares no <c>p:sldSz</c>.</summary>
    const long defaultSlideWidthEmu = 9144000;
    const long defaultSlideHeightEmu = 6858000;

    readonly Dictionary<OpenXmlPart, byte[]> partBytes = [];

    public PresentationParser()
        : this(DefaultFontSettings.DefaultFont)
    {
    }

    public ParsedDocument Parse(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Parse(stream);
    }

    public ParsedDocument Parse(Stream stream)
    {
        var normalized = StrictToTransitional.Normalize(stream);
        try
        {
            using var document = PresentationDocument.Open(normalized, false);
            return ParsePresentation(document);
        }
        finally
        {
            if (!ReferenceEquals(normalized, stream))
            {
                normalized.Dispose();
            }
        }
    }

    ParsedDocument ParsePresentation(PresentationDocument document)
    {
        var presentationPart = document.PresentationPart ??
                               throw new InvalidOperationException("The package has no presentation part.");
        var presentation = presentationPart.Presentation ??
                           throw new InvalidOperationException("The presentation part has no presentation element.");

        partBytes.Clear();

        var size = presentation.SlideSize;
        var pageSettings = new PageSettings
        {
            WidthPoints = (size?.Cx?.Value ?? defaultSlideWidthEmu) / OoxmlUnits.EmusPerPoint,
            HeightPoints = (size?.Cy?.Value ?? defaultSlideHeightEmu) / OoxmlUnits.EmusPerPoint,
            MarginTop = 0,
            MarginBottom = 0,
            MarginLeft = 0,
            MarginRight = 0,
            HeaderDistance = 0,
            FooterDistance = 0
        };

        // The theme hangs off the slide master, not the presentation part. Every corpus deck has
        // exactly one master, so the first one is authoritative.
        var themePart = presentationPart.SlideMasterParts.FirstOrDefault()?.ThemePart;
        var themeColors = ThemeParser.ExtractThemeColors(themePart);
        var themeFonts = ThemeParser.ExtractThemeFonts(themePart);

        var shapeParser = new SlideShapeParser(
            themeColors,
            themeFonts,
            defaultFont,
            GetPartBytes,
            pageSettings.WidthPoints,
            pageSettings.HeightPoints,
            presentationPart.TableStylesPart);

        var elements = new List<DocumentElement>();
        var slideParts = OrderedSlideParts(presentationPart).ToArray();
        for (var index = 0; index < slideParts.Length; index++)
        {
            if (index > 0)
            {
                elements.Add(new PageBreakElement());
            }

            var slidePart = slideParts[index];
            elements.AddRange(
                shapeParser.ParseSlide(
                    slidePart,
                    SlidePlaceholders.For(slidePart),
                    presentation.DefaultTextStyle));
        }

        return new()
        {
            PageSettings = pageSettings,
            Elements = elements,
            ThemeColors = themeColors,
            ThemeFonts = themeFonts,

            // Each slide's a:fld caches that slide's own number, so the text exporters keep the
            // cached values — a DOCX header's single cached value repeated per page is the case
            // the single-page evaluation exists for, and it does not apply here.
            PageFieldsPreEvaluated = true
        };
    }

    /// <summary>
    /// Slides in presentation order. <c>p:sldIdLst</c> is authoritative — the part names
    /// (<c>slide13.xml</c>, <c>slide22.xml</c>) do not sort into display order, and
    /// <c>PresentationPart.SlideParts</c> is relationship order rather than deck order.
    /// </summary>
    static IEnumerable<SlidePart> OrderedSlideParts(PresentationPart presentationPart)
    {
        var list = presentationPart.Presentation?.SlideIdList;
        if (list == null)
        {
            yield break;
        }

        foreach (var slideId in list.Elements<P.SlideId>())
        {
            var relationshipId = slideId.RelationshipId?.Value;
            if (relationshipId == null)
            {
                continue;
            }

            if (presentationPart.GetPartById(relationshipId) is SlidePart slidePart)
            {
                yield return slidePart;
            }
        }
    }

    // One buffer per image part per parse, so a logo repeated across slides shares a single array —
    // the render-side image caches key on byte-array reference identity.
    byte[] GetPartBytes(OpenXmlPart part)
    {
        if (partBytes.TryGetValue(part, out var cached))
        {
            return cached;
        }

        using var stream = part.GetStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        partBytes[part] = bytes;
        return bytes;
    }
}
