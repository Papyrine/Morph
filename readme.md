# <img src='/src/icon.png' height='30px'> Morph

[![NuGet Status](https://img.shields.io/nuget/v/Morph.svg?label=Morph)](https://www.nuget.org/packages/Morph/)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.Skia.svg?label=Morph.Skia)](https://www.nuget.org/packages/Morph.Skia/)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.ImageSharp.svg?label=Morph.ImageSharp)](https://www.nuget.org/packages/Morph.ImageSharp/)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.Pdf.svg?label=Morph.Pdf)](https://www.nuget.org/packages/Morph.Pdf/)

A .NET library that converts Microsoft Word DOCX documents or HTML content into **PNG images, PDF, semantic HTML, or Markdown**.

Either input can produce any of the four outputs, and a document is parsed once no matter how many formats it is exported to.

**[Try it live in the browser](https://morph.papyrine.org/)** — a Blazor WebAssembly app that converts a DOCX to PNG, PDF, Markdown or plain text client-side, built on `Morph.ImageSharp` and `Morph.Pdf`.


## Open Source Maintenance Fee

This project participates in the [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). The source code is freely available under the terms of the [license](license.txt). To support sustainable maintenance, use of the project's official binary releases in revenue-generating activities and all government agencies requires adherence to the [Open Source Maintenance Fee EULA](OsmfEula.txt). The fee is paid by [sponsoring Papyrine](https://github.com/sponsors/Papyrine).

This project uses [SponsorCheck](https://github.com/SimonCropp/SponsorCheck) to surface a build-time reminder in consuming projects that are not yet sponsoring.


## Requirements

- .NET 10.0 or later
- Cross-platform support: Windows, macOS, Linux


## NuGet packages

Which package is needed depends on the *output* format, not the input — DOCX and HTML are both handled by every package:

| Output | Package | Notes |
|--------|---------|-------|
| HTML | [`Morph`](https://nuget.org/packages/Morph/) | No rendering backend required |
| Markdown | [`Morph`](https://nuget.org/packages/Morph/) | No rendering backend required |
| PDF | [`Morph.Pdf`](https://nuget.org/packages/Morph.Pdf/) | Vector text via PdfSharp |
| PNG | [`Morph.Skia`](https://nuget.org/packages/Morph.Skia/) or [`Morph.ImageSharp`](https://nuget.org/packages/Morph.ImageSharp/) | Pick a [rendering backend](#rendering-backends) |

`Morph.Skia`, `Morph.ImageSharp` and `Morph.Pdf` all depend on `Morph`, so any of them also brings the HTML and Markdown exporters.


## Features


### Text Formatting

- Font families and sizes
- Bold, italic, underline, strikethrough
- Text colors and highlighting
- All caps, small caps
- Superscript, subscript
- Character spacing


### Paragraph Formatting

- Text alignment (left, right, center, justified)
- Indentation (first-line, hanging, left, right)
- Spacing (before, after, line spacing)
- Contextual spacing
- Paragraph borders


### Document Structure

- Multiple sections with different margins/orientation
- Page breaks (manual and automatic)
- Section breaks (continuous, next page, odd/even)
- Headers and footers
- Page numbering
- Line numbering


### Tables

- Complex table structures with merged cells
- Cell borders and shading
- Table styles
- Nested tables
- Column widths


### Lists

- Bullet lists
- Numbered lists
- Multi-level lists with various numbering styles
- Custom list formatting


### Graphics

- Embedded images (JPEG, PNG)
- Shapes (rectangles, circles, etc.)
- Drawing objects
- SVG content
- Ink/handwriting annotations


### Advanced Features

- Theme support (colors, fonts)
- Compatibility modes (Word 2007 and later)
- [Font resolution](docs/fonts.md) — name-table-driven, deterministic across platforms
- Hyphenation
- HTML content via AltChunk


### Export fidelity (HTML / Markdown / PDF)

The HTML, Markdown and PDF exporters run off the same parsed model as the PNG renderers above — the DOCX parser for a `.docx` source, the HTML parser for an HTML source — so the same content carries across, each within the limits of its format:

- **HTML** — semantic tags (`<strong>`, `<em>`, `<u>`, `<h1>`–`<h6>`, `<table>`, `<ul>`/`<ol>`) over one embedded stylesheet, with inline overrides only where a run or paragraph deviates from the document defaults. Theme colours (including `themeShade` / `themeTint`), per-run fonts (with generic fallbacks) and sizes, the page background, paragraph spacing / indentation / alignment / borders, and table cell widths / shading / borders / vertical alignment are preserved; background shapes, gradients and accent panels are emitted as inline SVG behind the text.
- **Markdown** — CommonMark with GFM pipe tables; adjacent runs are coalesced so emphasis stays well-formed and headings stay clean.
- **PDF** — vector text via PdfSharp, paginated to match the source page layout.

Export galleries — a visual index of every scenario's exporter output (PDF pages beside the Word reference render):

- [HTML](src/Tests/Inputs/compare-all-html.md)
- [Markdown](src/Tests/Inputs/compare-all-markdown.md)
- [PDF](src/Tests/Inputs/compare-all-pdf.md) — pages rendered by [Verify.PDFium](https://github.com/VerifyTests/Verify.PDFium)


## Rendering Backends

Morph supports two rendering backends:

| Backend | Package (DOCX + HTML) | Pros |
|---------|----------------------|------|
| **SkiaSharp** | `Morph.Skia` | Mature, includes SVG support |
| **ImageSharp** | `Morph.ImageSharp` | Fully managed (no native dependencies) |

[Rendering comparison gallery](src/Tests/Inputs/compare-all-images.md) — every scenario rendered by both backends side by side against the Microsoft Word reference image, with per-page error metrics.


## Usage


### DOCX to PNG

The examples below use the SkiaSharp backend. To use ImageSharp instead, replace `SkiaDocumentConverter` with `ImageSharpDocumentConverter`.


### Basic Usage - Save to Files

<!-- snippet: BasicUsage -->
<a id='snippet-BasicUsage'></a>
```cs
var converter = new SkiaDocumentConverter();

var result = converter.ConvertToImages(
    "document.docx",
    "output-folder");

Console.WriteLine($"Generated {result.PageCount} pages");
foreach (var path in result.ImagePaths)
{
    Console.WriteLine($"Created: {path}");
}
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L21-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-BasicUsage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### In-Memory Conversion

<!-- snippet: InMemoryConversion -->
<a id='snippet-InMemoryConversion'></a>
```cs
var converter = new SkiaDocumentConverter();

var imageData = converter.ConvertToImageData("document.docx");

foreach (var pngBytes in imageData)
{
    // Use the PNG byte array as needed
}
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L40-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-InMemoryConversion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Stream-Based Conversion

<!-- snippet: StreamBasedConversion -->
<a id='snippet-StreamBasedConversion'></a>
```cs
var converter = new SkiaDocumentConverter();

using var stream = File.OpenRead("document.docx");

// From stream to files
var result = converter.ConvertToImages(stream, "output-folder");

// Or from stream to memory
var imageData = converter.ConvertToImageData(stream);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L56-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-StreamBasedConversion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### With Custom Options

<!-- snippet: CustomOptions -->
<a id='snippet-CustomOptions'></a>
```cs
var converter = new SkiaDocumentConverter();

var options = new ImageExportOptions
{
    Dpi = 300,
    FontWidthScale = 1.08
};

var result = converter.ConvertToImages(
    "document.docx",
    "output-folder",
    options);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L73-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-CustomOptions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### DOCX to HTML / Markdown / PDF

Morph also serializes a DOCX directly to semantic HTML, Markdown, or a vector-text PDF — no backend choice needed for HTML / Markdown. Per-format options classes (`HtmlExportOptions`, `MarkdownExportOptions`, `PdfExportOptions`) carry the knobs relevant to each output.

The HTML output renders background shapes, gradients and accent panels from the source as inline SVG behind the text, so coloured backgrounds and decorative artwork survive the conversion.


#### Basic — HTML

<!-- snippet: ConvertToHtml -->
<a id='snippet-ConvertToHtml'></a>
```cs
var html = DocumentConverter.ConvertToHtml("document.docx");
File.WriteAllText("document.html", html);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L93-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-ConvertToHtml' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Basic — Markdown

<!-- snippet: ConvertToMarkdown -->
<a id='snippet-ConvertToMarkdown'></a>
```cs
var markdown = DocumentConverter.ConvertToMarkdown("document.docx");
File.WriteAllText("document.md", markdown);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L103-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-ConvertToMarkdown' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Basic — PDF

<!-- snippet: ConvertToPdf -->
<a id='snippet-ConvertToPdf'></a>
```cs
var outputPath = "document.pdf";
PdfDocumentConverter.ConvertToPdf("document.docx", outputPath);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L113-L118' title='Snippet source file'>snippet source</a> | <a href='#snippet-ConvertToPdf' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Parse once, export many

For multi-format export, `WordDocument` parses the source a single time and supports calling as many `ExportToXxx` methods as needed. The PDF extension method comes from `Morph.Pdf`; HTML and Markdown are built in.

<!-- snippet: ParseOnceExportMany -->
<a id='snippet-ParseOnceExportMany'></a>
```cs
// Parse once with WordDocument, then export to as many formats as you like — the source
// .docx is only opened and parsed a single time.
var document = new WordDocument("document.docx");

File.WriteAllText("document.html", document.ExportToHtml());
File.WriteAllText("document.md",   document.ExportToMarkdown());
// extension method from Morph.Pdf
document.ExportToPdf("document.pdf");
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L123-L134' title='Snippet source file'>snippet source</a> | <a href='#snippet-ParseOnceExportMany' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Custom image handling

By default Morph inlines images as base64 data URIs. Pass an `ImageHandler` to write images to disk, upload them to a CDN, or reference them however suits the calling pipeline.

<!-- snippet: HtmlExportWithImageHandler -->
<a id='snippet-HtmlExportWithImageHandler'></a>
```cs
// Write images to a media folder and reference them relatively, instead of base64-inlining.
Directory.CreateDirectory("media");
var html = DocumentConverter.ConvertToHtml(
    "document.docx",
    new()
    {
        ImageHandler = image =>
        {
            var extension = image.ContentType switch
            {
                "image/svg+xml" => "svg",
                "image/jpeg" => "jpg",
                _ => "png"
            };
            var path = $"media/image-{image.Index}.{extension}";
            File.WriteAllBytes(path, image.Data);
            return path;
        }
    });
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L139-L161' title='Snippet source file'>snippet source</a> | <a href='#snippet-HtmlExportWithImageHandler' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Warning callback

Some source features can't always be represented in the chosen output (ink strokes in HTML, foreground vector shapes in PDF, etc.). Pass an `OnWarning` callback to discover what was dropped.

<!-- snippet: WarningCallback -->
<a id='snippet-WarningCallback'></a>
```cs
// Discover features in the source that couldn't be fully represented in the output —
// unsupported elements (ink strokes, vector shapes), missing fonts, etc.
var warnings = new List<ExportWarning>();
var html = DocumentConverter.ConvertToHtml(
    "document.docx",
    new()
    {
        OnWarning = warnings.Add
    });

foreach (var warning in warnings)
{
    Console.WriteLine($"[{warning.Kind}] {warning.Message}");
}
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L166-L183' title='Snippet source file'>snippet source</a> | <a href='#snippet-WarningCallback' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### PDF page range

Render only specific pages — useful for previews / thumbnails.

<!-- snippet: PdfPageRange -->
<a id='snippet-PdfPageRange'></a>
```cs
// Render only the first three pages of the document.
var firstThreePages = PdfDocumentConverter.ConvertToPdf(
    "document.docx",
    new()
    {
        Pages = new(Start: 1, End: 3)
    });

File.WriteAllBytes("document-preview.pdf", firstThreePages);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L188-L200' title='Snippet source file'>snippet source</a> | <a href='#snippet-PdfPageRange' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### HTML to PNG

To use ImageSharp instead, replace `SkiaHtmlConverter` with `ImageSharpHtmlConverter`. HTML parsing is asynchronous (AngleSharp), so these APIs return a `Task`.


#### Basic Usage - Save to Files

<!-- snippet: HtmlToImages -->
<a id='snippet-HtmlToImages'></a>
```cs
var converter = new SkiaHtmlConverter();

var result = await converter.ConvertToImages(
    "<h1>Hello</h1><p>World</p>",
    "output-folder");

Console.WriteLine($"Generated {result.PageCount} pages");
foreach (var path in result.ImagePaths)
{
    Console.WriteLine($"Created: {path}");
}
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L205-L219' title='Snippet source file'>snippet source</a> | <a href='#snippet-HtmlToImages' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### In-Memory Conversion

<!-- snippet: HtmlToImageData -->
<a id='snippet-HtmlToImageData'></a>
```cs
var converter = new SkiaHtmlConverter();

var imageData = await converter.ConvertToImageData("<h1>Hello</h1><p>World</p>");

foreach (var pngBytes in imageData)
{
    // Use the PNG byte array as needed
}
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L224-L235' title='Snippet source file'>snippet source</a> | <a href='#snippet-HtmlToImageData' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### HTML to HTML / Markdown / PDF

The same exporters are available for an HTML source: `HtmlConverter.ConvertToHtml` normalizes the input into the same semantic HTML the DOCX path emits, `HtmlConverter.ConvertToMarkdown` converts it to Markdown, and `PdfHtmlConverter.ConvertToPdf` (from `Morph.Pdf`) paginates it into a vector-text PDF.


#### Basic — Markdown

<!-- snippet: HtmlToMarkdown -->
<a id='snippet-HtmlToMarkdown'></a>
```cs
var markdown = await HtmlConverter.ConvertToMarkdown("<h1>Hello</h1><p>World</p>");
await File.WriteAllTextAsync("page.md", markdown);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L240-L245' title='Snippet source file'>snippet source</a> | <a href='#snippet-HtmlToMarkdown' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Basic — PDF

<!-- snippet: HtmlToPdf -->
<a id='snippet-HtmlToPdf'></a>
```cs
var pdf = await PdfHtmlConverter.ConvertToPdf("<h1>Hello</h1><p>World</p>");
await File.WriteAllBytesAsync("page.pdf", pdf);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L250-L255' title='Snippet source file'>snippet source</a> | <a href='#snippet-HtmlToPdf' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Parse once, export many

`HtmlDocument` is the HTML-source counterpart to `WordDocument` — construct it with `LoadAsync`, then export as many times as needed off the single parse.

<!-- snippet: HtmlParseOnceExportMany -->
<a id='snippet-HtmlParseOnceExportMany'></a>
```cs
// Parse once with HtmlDocument, then export to as many formats as you like — the
// source HTML is only parsed a single time.
var document = await HtmlDocument.LoadAsync("<h1>Hello</h1><p>World</p>");

await File.WriteAllTextAsync("page.html", document.ExportToHtml());
await File.WriteAllTextAsync("page.md",   document.ExportToMarkdown());
// extension method from Morph.Pdf
document.ExportToPdf("page.pdf");
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L260-L271' title='Snippet source file'>snippet source</a> | <a href='#snippet-HtmlParseOnceExportMany' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Bookmark page numbers

A cross-reference — a PAGEREF field, or an entry in a table of contents — needs to know which page its target landed on. That is a product of pagination, which is why Word leaves those fields to compute when the document opens, and why a generated document typically ships placeholders and a prompt.

`GetBookmarkPages` paginates the document and reports where each bookmark ended up, so a generator can write the numbers into the file it produces instead:

<!-- snippet: GetBookmarkPages -->
<a id='snippet-GetBookmarkPages'></a>
```cs
// Which page each bookmark falls on — the number a PAGEREF field or a table-of-contents
// entry needs, and which only pagination can answer.
var pages = DocumentConverter.GetBookmarkPages("report.docx");

foreach (var (bookmark, page) in pages)
{
    Console.WriteLine($"{bookmark} is on page {page}");
}
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L312-L323' title='Snippet source file'>snippet source</a> | <a href='#snippet-GetBookmarkPages' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It costs a layout pass and nothing more — the answer is read off the layout engine's placed items, so no page is drawn and no rendering backend is involved. Bookmarks that cannot be placed, such as one sitting between paragraphs at body level (`ParagraphIndex == null`), are absent from the result rather than reported at a guessed page.

[Parchment](https://github.com/Papyrine/Parchment) consumes this through its `Parchment.Morph` package to resolve a generated document's table of contents as it is built.


## Shrinking a DOCX

A DOCX authored in Word carries parts that hold no rendering information. `DocumentCleaner` removes them. Applied to this repository's own 328-document test corpus it recovered 9.5 MB, 14% of the corpus on disk.

The biggest single contributor is the Explorer preview picture Word writes when "Save Thumbnails" is on: 46 documents carried one, totalling 7.1 MB, and in the worst case a 3.85 MB card template was 91% preview picture, leaving 350 KB once stripped.

| `DocumentParts` | Package location | What it is |
|--------|------|-------------|
| `Thumbnail` | `docProps/thumbnail.*` | The preview picture Explorer shows. Usually the largest part in a template. |
| `Glossary` | `word/glossary/` | Building blocks and Quick Parts, used only by Word's insert UI. |
| `CustomXml` | `customXml/` | Custom XML data islands and their properties. |
| `RevisionAuthors` | `word/people.xml` | Display names for tracked-change author IDs. |

None of these are reachable from `word/document.xml` content, so removing them cannot change what any of Morph's exporters produce. Parts that survive are copied across verbatim — only the package relationships and `[Content_Types].xml` are rewritten, and only to drop entries that would otherwise dangle.

<!-- snippet: ShrinkDocx -->
<a id='snippet-ShrinkDocx'></a>
```cs
// Strips every part that carries no rendering information. Returns what was
// actually removed, or DocumentParts.None if there was nothing to strip — in
// which case the file is left byte-for-byte untouched.
var removed = DocumentCleaner.Remove("document.docx");

Console.WriteLine($"Removed: {removed}");
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L276-L285' title='Snippet source file'>snippet source</a> | <a href='#snippet-ShrinkDocx' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`DocumentParts` is a `[Flags]` enum, so a subset can be selected, and `Find` reports what a package holds without touching it:

<!-- snippet: ShrinkDocxSelectively -->
<a id='snippet-ShrinkDocxSelectively'></a>
```cs
// Drop only the Explorer preview picture, keeping building blocks and custom XML.
DocumentCleaner.Remove("document.docx", DocumentParts.Thumbnail);

// Or report what a package is carrying without modifying it.
var present = DocumentCleaner.Find("document.docx");
if (present.HasFlag(DocumentParts.Thumbnail))
{
    Console.WriteLine("This document has a preview picture");
}

// Stream overloads write the cleaned package to a destination of your choosing.
using var source = File.OpenRead("document.docx");
using var target = File.Create("document-clean.docx");
DocumentCleaner.Remove(source, target, DocumentParts.Thumbnail | DocumentParts.Glossary);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L290-L307' title='Snippet source file'>snippet source</a> | <a href='#snippet-ShrinkDocxSelectively' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

One caveat on `CustomXml`: a content control can carry a `w:dataBinding` into a data island. The bound value is also cached inline in `word/document.xml` — which is what Morph, and Word until it refreshes, actually reads — but if the island and the cache have drifted apart, removing the island changes what Word eventually shows.


## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Dpi` | int | 150 | Image resolution in dots per inch |

Font-related options (`FontDirectory`, `FontFallback`, `DefaultFont`, `FontWidthScale`, `DeterministicRendering`) are documented in [docs/fonts.md](docs/fonts.md).


## Icon

[Impossible Star](https://thenounproject.com/icon/impossible-star-3612694/) designed by [Rflor](https://thenounproject.com/creator/rflor/) from [The Noun Project](https://thenounproject.com).
