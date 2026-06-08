# <img src='/src/icon.png' height='30px'> Morph

[![Build status](https://img.shields.io/appveyor/build/SimonCropp/morph)](https://ci.appveyor.com/project/SimonCropp/morph)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.svg?label=Morph)](https://www.nuget.org/packages/Morph/)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.Skia.svg?label=Morph.Skia)](https://www.nuget.org/packages/Morph.Skia/)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.ImageSharp.svg?label=Morph.ImageSharp)](https://www.nuget.org/packages/Morph.ImageSharp/)

A .NET library that converts Microsoft Word DOCX documents or HTML content into PNG images.


## Open Source Maintenance Fee

This project participates in the [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). The source code is freely available under the terms of the [license](license.txt). To support sustainable maintenance, use of the project's official binary releases in revenue-generating activities and all government agencies requires adherence to the [Open Source Maintenance Fee EULA](OsmfEula.txt). The fee is paid by [sponsoring Papyrine](https://github.com/sponsors/Papyrine).

This project uses [SponsorCheck](https://github.com/SimonCropp/SponsorCheck) to surface a build-time reminder in consuming projects that are not yet sponsoring.


## Requirements

- .NET 10.0 or later
- Cross-platform support: Windows, macOS, Linux


## NuGet packages

### DOCX or HTML to PNG

A single package per backend converts both Word documents and HTML content to images:

https://nuget.org/packages/Morph.Skia/

https://nuget.org/packages/Morph.ImageSharp/


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

The HTML, Markdown and PDF exporters share the DOCX parser above, so the same content carries across — each within the limits of its format:

- **HTML** — semantic tags (`<strong>`, `<em>`, `<u>`, `<h1>`–`<h6>`, `<table>`, `<ul>`/`<ol>`) over one embedded stylesheet, with inline overrides only where a run or paragraph deviates from the document defaults. Theme colours (including `themeShade` / `themeTint`), per-run fonts (with generic fallbacks) and sizes, the page background, paragraph spacing / indentation / alignment / borders, and table cell widths / shading / borders / vertical alignment are preserved; background shapes, gradients and accent panels are emitted as inline SVG behind the text.
- **Markdown** — Pandoc-flavoured CommonMark with GFM pipe tables; adjacent runs are coalesced so emphasis stays well-formed and headings stay clean.
- **PDF** — vector text via PdfSharp, paginated to match the source page layout.


## Rendering Backends

Morph supports two rendering backends:

| Backend | Package (DOCX + HTML) | Pros |
|---------|----------------------|------|
| **SkiaSharp** | `Morph.Skia` | Mature, includes SVG support |
| **ImageSharp** | `Morph.ImageSharp` | Fully managed (no native dependencies) |


## Usage


### DOCX to PNG

The examples below use the SkiaSharp backend. To use ImageSharp instead, replace `WordRender.Skia.DocumentConverter` with `WordRender.ImageSharp.DocumentConverter`.


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
<sup><a href='/src/Tests/ReadmeSamples.cs#L18-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-BasicUsage' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/src/Tests/ReadmeSamples.cs#L37-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-InMemoryConversion' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/src/Tests/ReadmeSamples.cs#L53-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-StreamBasedConversion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### With Custom Options

<!-- snippet: CustomOptions -->
<a id='snippet-CustomOptions'></a>
```cs
var converter = new SkiaDocumentConverter();

var options = new ImageExportOptions
{
    Dpi = 300,
    FontWidthScale = 1.07
};

var result = converter.ConvertToImages(
    "document.docx",
    "output-folder",
    options);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L70-L85' title='Snippet source file'>snippet source</a> | <a href='#snippet-CustomOptions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### DOCX to HTML / Markdown / PDF

Morph also serializes a DOCX directly to semantic HTML, Pandoc-flavoured Markdown, or a vector-text PDF — no backend choice needed for HTML / Markdown. Per-format options classes (`HtmlExportOptions`, `MarkdownExportOptions`, `PdfExportOptions`) carry the knobs relevant to each output.

The HTML output renders background shapes, gradients and accent panels from the source as inline SVG behind the text, so coloured backgrounds and decorative artwork survive the conversion.


#### Basic — HTML

<!-- snippet: ConvertToHtml -->
<a id='snippet-ConvertToHtml'></a>
```cs
var html = DocumentConverter.ConvertToHtml("document.docx");
File.WriteAllText("document.html", html);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L90-L95' title='Snippet source file'>snippet source</a> | <a href='#snippet-ConvertToHtml' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Basic — Markdown

<!-- snippet: ConvertToMarkdown -->
<a id='snippet-ConvertToMarkdown'></a>
```cs
var markdown = DocumentConverter.ConvertToMarkdown("document.docx");
File.WriteAllText("document.md", markdown);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L100-L105' title='Snippet source file'>snippet source</a> | <a href='#snippet-ConvertToMarkdown' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


#### Basic — PDF

<!-- snippet: ConvertToPdf -->
<a id='snippet-ConvertToPdf'></a>
```cs
var outputPath = "document.pdf";
PdfDocumentConverter.ConvertToPdf("document.docx", outputPath);
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L110-L115' title='Snippet source file'>snippet source</a> | <a href='#snippet-ConvertToPdf' title='Start of snippet'>anchor</a></sup>
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
document.ExportToPdf("document.pdf");   // extension method from Morph.Pdf
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L120-L130' title='Snippet source file'>snippet source</a> | <a href='#snippet-ParseOnceExportMany' title='Start of snippet'>anchor</a></sup>
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
                "image/jpeg"    => "jpg",
                _               => "png"
            };
            var path = $"media/image-{image.Index}.{extension}";
            File.WriteAllBytes(path, image.Data);
            return path;
        }
    });
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L135-L157' title='Snippet source file'>snippet source</a> | <a href='#snippet-HtmlExportWithImageHandler' title='Start of snippet'>anchor</a></sup>
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
        OnWarning = warning => warnings.Add(warning)
    });

foreach (var warning in warnings)
{
    Console.WriteLine($"[{warning.Kind}] {warning.Message}");
}
```
<sup><a href='/src/Tests/ReadmeSamples.cs#L162-L179' title='Snippet source file'>snippet source</a> | <a href='#snippet-WarningCallback' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/src/Tests/ReadmeSamples.cs#L184-L196' title='Snippet source file'>snippet source</a> | <a href='#snippet-PdfPageRange' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### HTML to PNG

To use ImageSharp instead, replace `HtmlRender.Skia.HtmlConverter` with `HtmlRender.ImageSharp.HtmlConverter`.


#### Basic Usage - Save to Files

```cs
var converter = new HtmlRender.Skia.HtmlConverter();

var result = await converter.ConvertToImages(
    "<h1>Hello</h1><p>World</p>",
    "output-folder");

Console.WriteLine($"Generated {result.PageCount} pages");
foreach (var path in result.ImagePaths)
{
    Console.WriteLine($"Created: {path}");
}
```


#### In-Memory Conversion

```cs
var converter = new HtmlRender.Skia.HtmlConverter();

var imageData = await converter.ConvertToImageData("<h1>Hello</h1><p>World</p>");

foreach (var pngBytes in imageData)
{
    // Use the PNG byte array as needed
}
```


## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Dpi` | int | 150 | Image resolution in dots per inch |

Font-related options (`FontDirectory`, `FontFallback`, `DefaultFont`, `FontWidthScale`, `DeterministicRendering`) are documented in [docs/fonts.md](docs/fonts.md).


## Icon

[Impossible Star](https://thenounproject.com/icon/impossible-star-3612694/) designed by [Rflor](https://thenounproject.com/creator/rflor/) from [The Noun Project](https://thenounproject.com).
