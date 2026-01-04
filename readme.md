# <img src='/src/icon.png' height='30px'> Morph

[![Build status](https://img.shields.io/appveyor/build/SimonCropp/morph)](https://ci.appveyor.com/project/SimonCropp/morph)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.svg?label=PackageShader)](https://www.nuget.org/packages/Morph/)

A .NET library that converts Microsoft Word DOCX documents into PNG images.


## Overview

Morph parses OpenXML-based Word documents and renders them to images that closely match the appearance of the original documents as they would appear in Microsoft Word. It uses a two-stage pipeline architecture:

1. **Parsing Stage** - Converts DOCX to an intermediate representation using DocumentFormat.OpenXml
2. **Rendering Stage** - Renders the intermediate representation to PNG images using SkiaSharp


## Requirements

- .NET 10.0 or later
- Cross-platform support: Windows, macOS, Linux


## Dependencies

- **DocumentFormat.OpenXml** - DOCX file parsing
- **SkiaSharp** - Cross-platform graphics rendering
- **Svg.Skia** - SVG rendering support
- **AngleSharp** - HTML content parsing (for AltChunk support)


## NuGet package

https://nuget.org/packages/Morph/


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


## Usage


### Basic Usage - Save to Files

<!-- snippet: BasicUsage -->
<a id='snippet-BasicUsage'></a>
```cs
var converter = new DocumentConverter();

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
var converter = new DocumentConverter();

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
var converter = new DocumentConverter();

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
var converter = new DocumentConverter();

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


## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Dpi` | int | 150 | Image resolution in dots per inch |
| `FontWidthScale` | double | 1.0 | Font width adjustment factor (1.07 recommended for Word matching) |


## Icon

[Impossible Star](https://thenounproject.com/icon/impossible-star-3612694/) designed by [Rflor](https://thenounproject.com/creator/rflor/) from [The Noun Project](https://thenounproject.com).
