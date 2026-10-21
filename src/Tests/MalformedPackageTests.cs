using System.IO.Compression;
using System.Text;

/// <summary>
/// A damaged package renders or fails cleanly — never hangs, never surfaces a crash from inside the
/// pipeline. Each case damages a corpus document one way: truncated, a part dropped, the body cut
/// mid-tag, bytes of it scrambled, or its numbers made hostile. The outcome is a render, or one of the
/// exceptions that mean "this is not a readable document": the zip, XML and package readers' own, a
/// schema-invalid value the SDK rejects (<see cref="FormatException"/>), or a part the document cannot
/// do without (<see cref="InvalidOperationException"/>).
///
/// Two of these found bugs when they were written (2026-10-21): a package without its relationships part
/// threw from every unguarded relationship lookup (<c>OpenXmlExtensions.PartOrNull</c> now), and margins
/// wider than their page left no content box and hung the fragmenter (<c>DocumentParser.FitMargins</c>).
/// </summary>
public class MalformedPackageTests
{
    static readonly string fonts = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static byte[] Original(string scenario) =>
        File.ReadAllBytes(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", scenario, "input.docx"));

    public static IEnumerable<(string Scenario, string Mutation)> Cases()
    {
        foreach (var scenario in new[] { "complex_tables", "resumes/05" })
        {
            foreach (var mutation in Mutations(Original(scenario)).Select(_ => _.Name))
            {
                yield return (scenario, mutation);
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task A_damaged_package_renders_or_fails_cleanly(string scenario, string mutation)
    {
        var bytes = Mutations(Original(scenario)).Single(_ => _.Name == mutation).Bytes;
        var render = Task.Run(() => new SkiaDocumentConverter().ConvertToImageData(new MemoryStream(bytes), new() { Dpi = 24, FontDirectory = fonts }));

        Exception? failure = null;
        try
        {
            await render.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var clean = failure is null
            or InvalidDataException
            or System.Xml.XmlException
            or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException
            or FileFormatException
            or FormatException
            or InvalidOperationException;
        await Assert.That(clean).IsTrue().Because($"{failure?.GetType().Name}: {failure?.Message}\n{failure?.StackTrace}");
    }

    // Margins far wider than the page (which is itself clamped) left a content box of no size and hung
    // the fragmenter. They now shrink to leave the page an inch of content.
    [Test]
    public async Task Margins_wider_than_the_page_still_leave_room_to_lay_out()
    {
        var parts = Read(Original("complex_tables"));
        var document = Encoding.UTF8.GetString(parts["word/document.xml"]);
        var hostile = System.Text.RegularExpressions.Regex.Replace(document, "(w:(?:w|h|left|right|top|bottom))=\"-?\\d+\"", "$1=\"999999999\"");
        var bytes = Write(parts.Select(_ => _.Key == "word/document.xml" ? new(_.Key, Encoding.UTF8.GetBytes(hostile)) : _));

        var pages = await Task.Run(() => new SkiaDocumentConverter().ConvertToImageData(new MemoryStream(bytes), new() { Dpi = 12, FontDirectory = fonts }))
            .WaitAsync(TimeSpan.FromSeconds(60));

        await Assert.That(pages.Count).IsGreaterThan(0);
    }

    static IEnumerable<(string Name, byte[] Bytes)> Mutations(byte[] original)
    {
        foreach (var fraction in new[] { 0.1, 0.5, 0.9, 0.99 })
        {
            yield return ($"truncated at {fraction}", original[..(int) (original.Length * fraction)]);
        }

        var parts = Read(original);
        foreach (var part in parts.Keys.Order())
        {
            yield return ($"without {part}", Write(parts.Where(_ => _.Key != part)));
        }

        var document = parts["word/document.xml"];
        foreach (var fraction in new[] { 0.3, 0.6, 0.95 })
        {
            yield return ($"body cut at {fraction}", Write(parts.Select(_ => _.Key == "word/document.xml" ? new(_.Key, document[..(int) (document.Length * fraction)]) : _)));
        }

        var random = new Random(1234);
        for (var round = 0; round < 6; round++)
        {
            var scrambled = (byte[]) document.Clone();
            for (var flip = 0; flip < 8; flip++)
            {
                scrambled[random.Next(scrambled.Length)] = (byte) random.Next(256);
            }

            yield return ($"body scrambled #{round}", Write(parts.Select(_ => _.Key == "word/document.xml" ? new(_.Key, scrambled) : _)));
        }

        var text = Encoding.UTF8.GetString(document);
        foreach (var hostile in new[] { "999999999", "-999999999", "0" })
        {
            var mutated = System.Text.RegularExpressions.Regex.Replace(text, "(w:(?:val|w|h|left|right|top|bottom|hanging|firstLine|before|after|line|gridSpan|sz))=\"-?\\d+\"", $"$1=\"{hostile}\"");
            yield return ($"numbers all {hostile}", Write(parts.Select(_ => _.Key == "word/document.xml" ? new(_.Key, Encoding.UTF8.GetBytes(mutated)) : _)));
        }
    }

    static Dictionary<string, byte[]> Read(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package));
        return zip.Entries.ToDictionary(
            _ => _.FullName,
            _ =>
            {
                using var stream = _.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            });
    }

    static byte[] Write(IEnumerable<KeyValuePair<string, byte[]>> parts)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in parts)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }
}
