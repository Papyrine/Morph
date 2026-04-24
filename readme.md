# <img src='/src/icon.png' height='30px'> Morph

[![Build status](https://img.shields.io/appveyor/build/SimonCropp/morph)](https://ci.appveyor.com/project/SimonCropp/morph)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.OpenXml.Skia.svg?label=Morph.OpenXml.Skia)](https://www.nuget.org/packages/Morph.OpenXml.Skia/)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.OpenXml.ImageSharp.svg?label=Morph.OpenXml.ImageSharp)](https://www.nuget.org/packages/Morph.OpenXml.ImageSharp/)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.Html.Skia.svg?label=Morph.Html.Skia)](https://www.nuget.org/packages/Morph.Html.Skia/)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.Html.ImageSharp.svg?label=Morph.Html.ImageSharp)](https://www.nuget.org/packages/Morph.Html.ImageSharp/)

A .NET library that converts Microsoft Word DOCX documents or HTML content into PNG images.


## Requirements

- .NET 10.0 or later
- Cross-platform support: Windows, macOS, Linux


## NuGet packages

### DOCX to PNG

For converting Word documents to images:

https://nuget.org/packages/Morph.OpenXml.Skia/

https://nuget.org/packages/Morph.OpenXml.ImageSharp/

### HTML to PNG

For converting HTML content to images (no Microsoft Word / OpenXml dependency):

https://nuget.org/packages/Morph.Html.Skia/

https://nuget.org/packages/Morph.Html.ImageSharp/


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
- Custom fonts with multi-level fallback
- Font width scaling for Word rendering accuracy
- Hyphenation
- HTML content via AltChunk


## Rendering Backends

Morph supports two rendering backends:

| Backend | DOCX Package | HTML Package | Pros |
|---------|-------------|-------------|------|
| **SkiaSharp** | `Morph.OpenXml.Skia` | `Morph.Html.Skia` | Mature, includes SVG support |
| **ImageSharp** | `Morph.OpenXml.ImageSharp` | `Morph.Html.ImageSharp` | Fully managed (no native dependencies) |


## Usage


### DOCX to PNG

The examples below use the SkiaSharp backend. To use ImageSharp instead, replace `WordRender.Skia.DocumentConverter` with `WordRender.ImageSharp.DocumentConverter`.


### Basic Usage - Save to Files

<!-- snippet: BasicUsage -->
<a id='snippet-BasicUsage'></a>
```cs
var converter = new WordRender.Skia.ImageSharpDocumentConverter();

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
var converter = new WordRender.Skia.ImageSharpDocumentConverter();

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
var converter = new WordRender.Skia.ImageSharpDocumentConverter();

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
var converter = new WordRender.Skia.ImageSharpDocumentConverter();

var options = new ConversionOptions
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
| `FontWidthScale` | double | 1.0 | Font width adjustment factor (1.07 recommended for Word matching) |


## Icon

[Impossible Star](https://thenounproject.com/icon/impossible-star-3612694/) designed by [Rflor](https://thenounproject.com/creator/rflor/) from [The Noun Project](https://thenounproject.com).
