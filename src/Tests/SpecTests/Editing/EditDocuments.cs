using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;

/// <summary>
/// What the editing tests share: the people editing, short ways to say what a paragraph is to become,
/// and a notation for the runs an edit leaves behind — finer than <see cref="ReviewDocuments.Describe"/>,
/// which shows text and revisions but not where one run ends and the next begins:
/// <code>
/// [Hello ][b:world]            two runs, the second bold
/// [i:a][!b:b][u:c][s:d]        italic, bold turned off, underlined, struck through
/// {+[new]+}  {-[old]-}         an insertion and a deletion, by whoever
/// &lt;[link]&gt;                     a run in a hyperlink
/// (|[text]|)                   a run in a content control
/// [a] ¶ [b]                    two paragraphs; ¶+ and ¶- a mark inserted and deleted
/// </code>
/// </summary>
static class EditDocuments
{
    public static DateTimeOffset Now { get; } = new(2026, 9, 27, 14, 30, 45, TimeSpan.FromHours(10));

    public static EditOptions Plain { get; } = new("Ann", Now, Track: false);

    public static EditOptions Tracked { get; } = new("Ann", Now, Track: true);

    /// <summary>Text formatted as a unit of the paragraph is.</summary>
    public static NewItem T(int unit, string text, RunFormat format = default) =>
        new(unit, text, format);

    /// <summary>A unit that is not text, kept.</summary>
    public static NewItem K(int unit) =>
        new(unit, null);

    public static NewParagraph Reads(params NewItem[] items) =>
        new(items);

    public static byte[] Rewrite(byte[] docx, int paragraph, EditOptions options, params NewParagraph[] content) =>
        DocumentEditor.Rewrite(docx, paragraph, content, options).Document;

    /// <summary>The body's markup, without the namespace declaration each block would repeat.</summary>
    public static string Xml(byte[] docx) =>
        Bare(ReviewDocuments.BodyXml(docx));

    public static string Bare(string xml) =>
        xml.Replace(" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"", "");

    /// <summary>The body's runs in the notation above.</summary>
    public static string Shape(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = WordprocessingDocument.Open(stream, false);
        var builder = new StringBuilder();
        foreach (var paragraph in package.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>())
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            foreach (var child in paragraph.ChildElements)
            {
                Shape(child, builder);
            }

            if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }

            builder.Append('¶');
            foreach (var mark in paragraph.ParagraphProperties?.ParagraphMarkRunProperties?.ChildElements ?? [])
            {
                if (mark is Inserted)
                {
                    builder.Append('+');
                }
                else if (mark is Deleted)
                {
                    builder.Append('-');
                }
            }
        }

        return builder.ToString();
    }

    static void Shape(OpenXmlElement element, StringBuilder builder)
    {
        switch (element)
        {
            case OoxmlParagraphProperties:
                break;
            case OoxmlRun run:
                builder.Append('[');
                builder.Append(Marks(run.RunProperties));
                builder.Append(SourceRuns.Text(run).Replace("\n", "\\n").Replace("\t", "\\t"));
                foreach (var child in run.ChildElements)
                {
                    if (child is FieldChar mark)
                    {
                        builder.Append(FieldMark(mark));
                    }
                    else if (child is Drawing)
                    {
                        builder.Append('▣');
                    }
                }

                builder.Append(']');
                break;
            case InsertedRun:
                Wrap(element, builder, "{+", "+}");
                break;
            case DeletedRun:
                Wrap(element, builder, "{-", "-}");
                break;
            case Hyperlink:
                Wrap(element, builder, "<", ">");
                break;
            case SdtRun:
                Wrap(element, builder, "(|", "|)");
                break;
            default:
                foreach (var child in element.ChildElements)
                {
                    Shape(child, builder);
                }

                break;
        }
    }

    static string FieldMark(FieldChar mark)
    {
        var type = mark.FieldCharType?.Value;
        if (type == FieldCharValues.Begin)
        {
            return "«";
        }

        if (type == FieldCharValues.End)
        {
            return "»";
        }

        return "|";
    }

    static void Wrap(OpenXmlElement element, StringBuilder builder, string open, string close)
    {
        builder.Append(open);
        foreach (var child in element.ChildElements)
        {
            Shape(child, builder);
        }

        builder.Append(close);
    }

    static string Marks(OoxmlRunProperties? properties)
    {
        if (properties == null)
        {
            return "";
        }

        var marks = new StringBuilder();
        Toggle(properties.Bold, "b");
        Toggle(properties.Italic, "i");
        Toggle(properties.Strike, "s");
        if (properties.Underline?.Val?.Value is { } underline)
        {
            if (underline == UnderlineValues.None)
            {
                marks.Append('!');
            }

            marks.Append('u');
        }

        if (properties.GetFirstChild<RunPropertiesChange>() != null)
        {
            marks.Append('~');
        }

        if (marks.Length > 0)
        {
            marks.Append(':');
        }

        return marks.ToString();

        void Toggle(OnOffType? toggle, string name)
        {
            if (toggle == null)
            {
                return;
            }

            if (toggle.Val?.Value == false)
            {
                marks.Append('!');
            }

            marks.Append(name);
        }
    }
}
