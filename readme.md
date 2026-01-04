# <img src='/src/icon.png' height='30px'> Morph

[![Build status](https://img.shields.io/appveyor/build/morph)](https://ci.appveyor.com/project/SimonCropp/morph)
[![NuGet Status](https://img.shields.io/nuget/v/Morph.svg?label=PackageShader)](https://www.nuget.org/packages/Morph/)

A .NET library that converts Microsoft Word DOCX documents into PNG images with pixel-perfect accuracy.


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



## NuGet package

https://nuget.org/packages/Naiad/


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

```csharp
using WordRender;

// Convert DOCX to PNG files
var result = DocumentConverter.ConvertToImages(
    "document.docx",
    "output-folder"
);

Console.WriteLine($"Generated {result.PageCount} pages");
foreach (var path in result.ImagePaths)
{
    Console.WriteLine($"Created: {path}");
}
```


### In-Memory Conversion

```csharp
using WordRender;

// Convert DOCX to byte arrays (no files written)
var result = DocumentConverter.ConvertToImageData("document.docx");

foreach (var imageData in result.ImageData)
{
    // Use the PNG byte array as needed
    // e.g., save to cloud storage, send via API, etc.
}
```


### Stream-Based Conversion

```csharp
using WordRender;

using var stream = File.OpenRead("document.docx");

// From stream to files
var result = DocumentConverter.ConvertToImages(stream, "output-folder");

// Or from stream to memory
var memoryResult = DocumentConverter.ConvertToImageData(stream);
```


### With Custom Options

```csharp
using WordRender;

var options = new ConversionOptions
{
    Dpi = 300,           // Higher resolution (default: 150)
    FontWidthScale = 1.07 // Adjust font width to match Word rendering
};

var result = DocumentConverter.ConvertToImages(
    "document.docx",
    "output-folder",
    options
);
```


## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Dpi` | int | 150 | Image resolution in dots per inch |
| `FontWidthScale` | double | 1.0 | Font width adjustment factor (1.07 recommended for Word matching) |