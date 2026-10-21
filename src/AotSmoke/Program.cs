using Morph;

// Converts one document every way the libraries offer and fails loudly when any conversion comes back
// empty: run from a trimmed publish, that is what shows trimming left the code each one needs. Usage:
// AotSmoke <input.docx> <font directory>
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: AotSmoke <input.docx> <font directory>");
    return 2;
}

var (input, fonts) = (args[0], args[1]);
var failures = 0;

void Check(string name, int size)
{
    Console.WriteLine($"{name}: {size}");
    if (size == 0)
    {
        failures++;
    }
}

Check("skia png pages", new SkiaDocumentConverter().ConvertToImageData(input, new() { FontDirectory = fonts, Dpi = 48 }).Count);
Check("imagesharp png pages", new ImageSharpDocumentConverter().ConvertToImageData(input, new() { FontDirectory = fonts, Dpi = 48 }).Count);
Check("pdf bytes", PdfDocumentConverter.ConvertToPdf(input, new() { FontDirectory = fonts }).Length);
Check("html chars", DocumentConverter.ConvertToHtml(input, new() { FontDirectory = fonts }).Length);
Check("markdown chars", DocumentConverter.ConvertToMarkdown(input, new() { FontDirectory = fonts }).Length);

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} conversion(s) came back empty");
    return 1;
}

return 0;
