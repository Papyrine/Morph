using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;

/// <summary>
/// The small documents the editing tests type into, written as the markup under test, and the reading
/// of an edited document back as its paragraphs: their text, a run per bracket, the way
/// <c>[plain][b:bold]</c>, with an insertion and a deletion as <c>{+…+}</c> and <c>{-…-}</c>.
/// </summary>
static class EditFixtures
{
    const string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Three paragraphs: a plain one, a centred one with a bold word, and a plain one.</summary>
    public static byte[] Paragraphs { get; } = Build(
        P("", R("First paragraph here.")) +
        P("<w:jc w:val=\"center\"/>", R("Second "), R("bold", "<w:b/>"), R(" one.")) +
        P("", R("Third and last.")));

    public static string P(string properties, params string[] content)
    {
        var formatting = properties.Length > 0 ? $"<w:pPr>{properties}</w:pPr>" : "";
        return $"<w:p>{formatting}{string.Concat(content)}</w:p>";
    }

    public static string R(string text, string properties = "")
    {
        var formatting = properties.Length > 0 ? $"<w:rPr>{properties}</w:rPr>" : "";
        return $"<w:r>{formatting}<w:t xml:space=\"preserve\">{text}</w:t></w:r>";
    }

    public static byte[] Build(string body, string? settings = null)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new($"<w:document xmlns:w=\"{wordNamespace}\"><w:body>{body}</w:body></w:document>");
            if (settings != null)
            {
                main.AddNewPart<DocumentSettingsPart>().Settings = new($"<w:settings xmlns:w=\"{wordNamespace}\">{settings}</w:settings>");
            }
        }

        return stream.ToArray();
    }

    /// <summary>The body's paragraphs, each in the notation above.</summary>
    public static IReadOnlyList<string> Read(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = WordprocessingDocument.Open(stream, false);
        var paragraphs = new List<string>();
        foreach (var paragraph in package.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>())
        {
            var builder = new StringBuilder();
            foreach (var child in paragraph.ChildElements)
            {
                Read(child, builder);
            }

            var alignment = paragraph.ParagraphProperties?.Justification?.Val?.InnerText;
            if (alignment != null)
            {
                builder.Append($" ({alignment})");
            }

            paragraphs.Add(builder.ToString());
        }

        return paragraphs;
    }

    static void Read(OpenXmlElement element, StringBuilder builder)
    {
        switch (element)
        {
            case OoxmlParagraphProperties:
                break;
            case OoxmlRun run:
                builder.Append('[');
                builder.Append(Marks(run.RunProperties));
                builder.Append(SourceRuns.Text(run).Replace("\n", "\\n").Replace("\t", "\\t"));
                builder.Append(']');
                break;
            case InsertedRun:
                Wrap(element, builder, "{+", "+}");
                break;
            case DeletedRun:
                Wrap(element, builder, "{-", "-}");
                break;
            default:
                foreach (var child in element.ChildElements)
                {
                    Read(child, builder);
                }

                break;
        }
    }

    static void Wrap(OpenXmlElement element, StringBuilder builder, string open, string close)
    {
        builder.Append(open);
        foreach (var child in element.ChildElements)
        {
            Read(child, builder);
        }

        builder.Append(close);
    }

    static string Marks(OoxmlRunProperties? properties)
    {
        var marks = new StringBuilder();
        Toggle(properties?.Bold, 'b');
        Toggle(properties?.Italic, 'i');
        Toggle(properties?.Strike, 's');
        if (properties?.Underline?.Val?.Value is { } underline &&
            underline != UnderlineValues.None)
        {
            marks.Append('u');
        }

        if (marks.Length > 0)
        {
            marks.Append(':');
        }

        return marks.ToString();

        void Toggle(OnOffType? toggle, char name)
        {
            if (toggle != null &&
                toggle.Val?.Value != false)
            {
                marks.Append(name);
            }
        }
    }
}
