// Usage: PngIdentical <previous-root> <tree-root>
//
// For every PNG under <previous-root> (a mirror of the tree's verified PNGs taken before a baseline
// regeneration), compares it with the file at the same relative path under <tree-root>. When both
// decode to the same size and the same RGBA bytes but the files differ, the previous bytes are copied
// back over the new file: the render did not change, only its encoding (a new encoder or zlib level),
// and promoting it would churn a baseline for nothing.

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PngIdentical <previous-root> <tree-root>");
    return 2;
}

var previousRoot = Path.GetFullPath(args[0]);
var treeRoot = Path.GetFullPath(args[1]);
if (!Directory.Exists(previousRoot))
{
    Console.Error.WriteLine($"No previous baselines at {previousRoot}; nothing to compare.");
    return 0;
}

var restored = 0;
var changed = 0;
var unchanged = 0;
foreach (var previousPath in Directory.EnumerateFiles(previousRoot, "*.png", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(previousRoot, previousPath);
    var currentPath = Path.Combine(treeRoot, relative);
    if (!File.Exists(currentPath))
    {
        continue;
    }

    var previousBytes = File.ReadAllBytes(previousPath);
    var currentBytes = File.ReadAllBytes(currentPath);
    if (previousBytes.AsSpan().SequenceEqual(currentBytes))
    {
        unchanged++;
        continue;
    }

    if (SamePixels(previousBytes, currentBytes))
    {
        File.WriteAllBytes(currentPath, previousBytes);
        restored++;
    }
    else
    {
        changed++;
    }
}

Console.WriteLine($">>> PNG baselines: {changed} changed, {restored} re-encoded only (restored), {unchanged} byte-identical");
return 0;

static bool SamePixels(byte[] previousBytes, byte[] currentBytes)
{
    PngImage previous;
    PngImage current;
    try
    {
        previous = PngDecoder.Decode(new MemoryStream(previousBytes));
        current = PngDecoder.Decode(new MemoryStream(currentBytes));
    }
    catch (Exception exception)
    {
        // Anything the decoder cannot read is kept as promoted: only a proven pixel match reverts.
        Console.Error.WriteLine($"    undecodable, kept as promoted: {exception.Message}");
        return false;
    }

    return previous.Width == current.Width &&
           previous.Height == current.Height &&
           previous.Rgba.AsSpan().SequenceEqual(current.Rgba);
}
