namespace WordRender;

/// <summary>
/// Result of a document conversion.
/// </summary>
/// <param name="ImagePaths">Paths to the generated PNG images.</param>
/// <param name="PageCount">Number of pages in the document.</param>
public sealed record ConversionResult(IReadOnlyList<string> ImagePaths, int PageCount);
