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
        var bytes = Mutations(Original(scenario)).Single(_ => _.Name == mutation).Bytes();
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

    // Each mutation builds its bytes only when asked: the bombs take a second or two to deflate, and every
    // case would otherwise build every other case's package.
    static IEnumerable<(string Name, Func<byte[]> Bytes)> Mutations(byte[] original)
    {
        foreach (var fraction in new[] { 0.1, 0.5, 0.9, 0.99 })
        {
            yield return ($"truncated at {fraction}", () => original[..(int) (original.Length * fraction)]);
        }

        var parts = Read(original);
        foreach (var part in parts.Keys.Order())
        {
            yield return ($"without {part}", () => Write(parts.Where(_ => _.Key != part)));
        }

        var document = parts["word/document.xml"];
        foreach (var fraction in new[] { 0.3, 0.6, 0.95 })
        {
            yield return ($"body cut at {fraction}", () => Write(parts.Select(_ => _.Key == "word/document.xml" ? new(_.Key, document[..(int) (document.Length * fraction)]) : _)));
        }

        var random = new Random(1234);
        for (var round = 0; round < 6; round++)
        {
            var scrambled = (byte[]) document.Clone();
            for (var flip = 0; flip < 8; flip++)
            {
                scrambled[random.Next(scrambled.Length)] = (byte) random.Next(256);
            }

            yield return ($"body scrambled #{round}", () => Write(parts.Select(_ => _.Key == "word/document.xml" ? new(_.Key, scrambled) : _)));
        }

        var text = Encoding.UTF8.GetString(document);
        foreach (var hostile in new[] { "999999999", "-999999999", "0" })
        {
            var mutated = System.Text.RegularExpressions.Regex.Replace(text, "(w:(?:val|w|h|left|right|top|bottom|hanging|firstLine|before|after|line|gridSpan|sz))=\"-?\\d+\"", $"$1=\"{hostile}\"");
            yield return ($"numbers all {hostile}", () => Write(parts.Select(_ => _.Key == "word/document.xml" ? new(_.Key, Encoding.UTF8.GetBytes(mutated)) : _)));
        }

        // Styles that inherit from each other in a ring, and one from itself.
        var styles = Encoding.UTF8.GetString(parts["word/styles.xml"]);
        var cyclicStyles = styles.Replace(
            "</w:styles>",
            """<w:style w:type="paragraph" w:styleId="CycleA"><w:name w:val="CycleA"/><w:basedOn w:val="CycleB"/></w:style><w:style w:type="paragraph" w:styleId="CycleB"><w:name w:val="CycleB"/><w:basedOn w:val="CycleA"/></w:style><w:style w:type="paragraph" w:styleId="Self"><w:name w:val="Self"/><w:basedOn w:val="Self"/></w:style></w:styles>""");
        yield return ("styles based on each other in a ring", () => WithBody(parts, """<w:p><w:pPr><w:pStyle w:val="CycleA"/></w:pPr><w:r><w:t>ring</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val="Self"/></w:pPr><w:r><w:t>self</w:t></w:r></w:p>""", ("word/styles.xml", cyclicStyles)));

        // A list whose definition links to a numbering style, whose own properties name the list again —
        // the w:numStyleLink / w:styleLink indirection followed round in a circle.
        var numbering = Encoding.UTF8.GetString(parts["word/numbering.xml"]);
        var firstAbstract = numbering.IndexOf("<w:abstractNum ", StringComparison.Ordinal);
        var cyclicNumbering = (numbering[..firstAbstract] + """<w:abstractNum w:abstractNumId="9077"><w:numStyleLink w:val="ListCycle"/></w:abstractNum>""" + numbering[firstAbstract..])
            .Replace("</w:numbering>", """<w:num w:numId="9077"><w:abstractNumId w:val="9077"/></w:num></w:numbering>""");
        var listStyles = styles.Replace(
            "</w:styles>",
            """<w:style w:type="numbering" w:styleId="ListCycle"><w:name w:val="ListCycle"/><w:pPr><w:numPr><w:numId w:val="9077"/></w:numPr></w:pPr></w:style></w:styles>""");
        yield return ("a list linked to a style that names it", () => WithBody(parts, """<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="9077"/></w:numPr></w:pPr><w:r><w:t>item</w:t></w:r></w:p>""", ("word/numbering.xml", cyclicNumbering), ("word/styles.xml", listStyles)));

        // Tables nested three hundred deep.
        const int depth = 300;
        var nested = string.Concat(Enumerable.Repeat("<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc>", depth)) +
                     "<w:p><w:r><w:t>deep</w:t></w:r></w:p>" +
                     string.Concat(Enumerable.Repeat("</w:tc></w:tr></w:tbl><w:p/>", depth));
        yield return ($"tables nested {depth} deep", () => WithBody(parts, nested));

        // A gigabyte of zeros, deflated to a megabyte: once in a part nothing refers to, and once as the
        // bytes of a picture the document draws.
        yield return ("an unreferenced part of a gigabyte", () => Write(parts, ("customXml/bomb.bin", 1L << 30)));
        if (parts.ContainsKey("word/media/image1.png"))
        {
            yield return ("a picture of a quarter gigabyte", () => Write(parts.Where(_ => _.Key != "word/media/image1.png"), ("word/media/image1.png", 1L << 28)));
        }
    }

    // The package with its body opened by the given paragraphs, and any parts replaced.
    static byte[] WithBody(Dictionary<string, byte[]> parts, string paragraphs, params (string Name, string Xml)[] replaced)
    {
        var document = Encoding.UTF8.GetString(parts["word/document.xml"]);
        var body = document.IndexOf("<w:body>", StringComparison.Ordinal) + "<w:body>".Length;
        var changed = new Dictionary<string, byte[]>(parts)
        {
            ["word/document.xml"] = Encoding.UTF8.GetBytes(document[..body] + paragraphs + document[body..])
        };
        foreach (var (name, xml) in replaced)
        {
            changed[name] = Encoding.UTF8.GetBytes(xml);
        }

        return Write(changed);
    }

    // Writes the parts, then a part of the given number of zero bytes, streamed so the test never holds it.
    static byte[] Write(IEnumerable<KeyValuePair<string, byte[]>> parts, (string Name, long Length) zeros)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in parts)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }

            using var bomb = zip.CreateEntry(zeros.Name, CompressionLevel.SmallestSize).Open();
            var chunk = new byte[1 << 20];
            for (var written = 0L; written < zeros.Length; written += chunk.Length)
            {
                bomb.Write(chunk);
            }
        }

        return buffer.ToArray();
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
