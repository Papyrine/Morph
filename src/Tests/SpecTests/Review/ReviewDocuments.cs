using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OoxmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;

/// <summary>
/// Builds the small documents the review tests edit, from the markup under test, and reads an edited
/// document back in a notation short enough to assert on in one line:
/// <code>
/// Hello {+inserted +}world {-removed.-}¶     text, an insertion, a deletion, the paragraph's end
/// ¶+  ¶-  ¶&gt;  ¶&lt;                             a mark inserted, deleted, moved to, moved from
/// [1:commented:1](1)                          comment 1's range and its reference mark
/// {&gt;moved&gt;}  {&lt;moved&lt;}                        a move's destination and its source
/// ~bold~                                      a run carrying a formatting change
/// &lt;a|b+/c&gt;                                   a table: rows by /, cells by |, a revised row marked
/// </code>
/// </summary>
static class ReviewDocuments
{
    const string namespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" " +
        "xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\"";

    /// <summary>A revision's attributes, for an element written inline in a test.</summary>
    public static string By(string author, int id = 1, string date = "2025-04-25T10:00:00Z") =>
        $"w:id=\"{id}\" w:author=\"{author}\" w:date=\"{date}\"";

    /// <summary>A paragraph of the given content: runs, revisions, and <see cref="Mark"/> first if any.</summary>
    public static string P(params string[] content) =>
        $"<w:p>{string.Concat(content)}</w:p>";

    /// <summary>A run of text, optionally bold.</summary>
    public static string R(string text, bool bold = false)
    {
        var properties = "";
        if (bold)
        {
            properties = "<w:rPr><w:b/></w:rPr>";
        }

        return $"<w:r>{properties}<w:t xml:space=\"preserve\">{text}</w:t></w:r>";
    }

    public static string Ins(string author, int id, params string[] content) =>
        $"<w:ins {By(author, id)}>{string.Concat(content)}</w:ins>";

    /// <summary>A deletion of the given text.</summary>
    public static string Del(string author, int id, string text) =>
        $"<w:del {By(author, id)}><w:r><w:delText xml:space=\"preserve\">{text}</w:delText></w:r></w:del>";

    /// <summary>The paragraph properties that mark a paragraph's own mark as revised: <c>ins</c> or <c>del</c>.</summary>
    public static string Mark(string kind, string author, int id, string properties = "") =>
        $"<w:pPr>{properties}<w:rPr><w:{kind} {By(author, id)}/></w:rPr></w:pPr>";

    public static byte[] Build(string body, string? comments = null, string? commentsExtended = null, string? settings = null, string? styles = null, string? header = null)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = [with($"<w:document {namespaces}><w:body>{body}</w:body></w:document>")];
            if (comments != null)
            {
                main.AddNewPart<WordprocessingCommentsPart>().Comments = [with($"<w:comments {namespaces}>{comments}</w:comments>")];
            }

            if (commentsExtended != null)
            {
                main.AddNewPart<WordprocessingCommentsExPart>().CommentsEx = [with($"<w15:commentsEx {namespaces}>{commentsExtended}</w15:commentsEx>")];
            }

            if (settings != null)
            {
                main.AddNewPart<DocumentSettingsPart>().Settings = [with($"<w:settings {namespaces}>{settings}</w:settings>")];
            }

            if (styles != null)
            {
                main.AddNewPart<StyleDefinitionsPart>().Styles = [with($"<w:styles {namespaces}>{styles}</w:styles>")];
            }

            if (header != null)
            {
                main.AddNewPart<HeaderPart>().Header = [with($"<w:hdr {namespaces}>{header}</w:hdr>")];
            }
        }

        return stream.ToArray();
    }

    public static byte[] Corpus(params string[] path) =>
        File.ReadAllBytes(Path.Combine([ProjectFiles.ProjectDirectory, "Inputs", "word", .. path, "input.docx"]));

    /// <summary>The body in the notation above.</summary>
    public static string Describe(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = WordprocessingDocument.Open(stream, false);
        var builder = new StringBuilder();
        foreach (var child in package.MainDocumentPart!.Document!.Body!.ChildElements)
        {
            Describe(child, builder);
        }

        return builder.ToString();
    }

    /// <summary>The comments part as <c>id author: text</c>, a thread's replies indented under it.</summary>
    public static string DescribeComments(byte[] docx)
    {
        var builder = new StringBuilder();
        foreach (var comment in DocumentReview.Read(docx).Comments)
        {
            Append(builder, comment, "");
            foreach (var reply in comment.Replies)
            {
                Append(builder, reply, "  ");
            }
        }

        return builder.ToString().TrimEnd('\n');

        static void Append(StringBuilder builder, ReviewComment comment, string indent)
        {
            builder.Append(indent);
            builder.Append(comment.Id);
            builder.Append(' ');
            builder.Append(comment.Author);
            builder.Append(": ");
            builder.Append(comment.Text.Replace("\n", "\\n"));
            if (comment.Resolved)
            {
                builder.Append(" (resolved)");
            }

            builder.Append('\n');
        }
    }

    /// <summary>The raw markup of the body, for the assertions the notation is too coarse for.</summary>
    public static string BodyXml(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = WordprocessingDocument.Open(stream, false);
        return package.MainDocumentPart!.Document!.Body!.InnerXml;
    }

    public static T Read<T>(byte[] docx, Func<WordprocessingDocument, T> read)
    {
        using var stream = new MemoryStream(docx);
        using var package = WordprocessingDocument.Open(stream, false);
        return read(package);
    }

    static void Describe(OpenXmlElement element, StringBuilder builder)
    {
        switch (element)
        {
            case Paragraph paragraph:
                foreach (var child in paragraph.ChildElements)
                {
                    if (child is not OoxmlParagraphProperties)
                    {
                        Describe(child, builder);
                    }
                }

                builder.Append('¶');
                foreach (var mark in paragraph.ParagraphProperties?.ParagraphMarkRunProperties?.ChildElements ?? [])
                {
                    builder.Append(
                        mark switch
                        {
                            Inserted => "+",
                            Deleted => "-",
                            MoveTo => ">",
                            MoveFrom => "<",
                            _ => ""
                        });
                }

                break;

            case InsertedRun:
                Wrap(element, builder, "{+", "+}");
                break;

            case DeletedRun:
                Wrap(element, builder, "{-", "-}");
                break;

            case MoveToRun:
                Wrap(element, builder, "{>", ">}");
                break;

            case MoveFromRun:
                Wrap(element, builder, "{<", "<}");
                break;

            case OoxmlRun run:
                var changed = run.RunProperties?.GetFirstChild<RunPropertiesChange>() != null;
                if (changed)
                {
                    builder.Append('~');
                }

                builder.Append(SourceRuns.Text(run));
                if (run.GetFirstChild<CommentReference>() is { } reference)
                {
                    builder.Append($"({reference.Id?.Value})");
                }

                if (changed)
                {
                    builder.Append('~');
                }

                break;

            case CommentRangeStart start:
                builder.Append($"[{start.Id?.Value}:");
                break;

            case CommentRangeEnd end:
                builder.Append($":{end.Id?.Value}]");
                break;

            case Table table:
                builder.Append('<');
                builder.AppendJoin('/', table.Elements<OoxmlTableRow>().Select(Row));
                builder.Append('>');
                break;

            default:
                foreach (var child in element.ChildElements)
                {
                    Describe(child, builder);
                }

                break;
        }
    }

    static string Row(OoxmlTableRow row)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var cell in row.Elements<OoxmlTableCell>())
        {
            if (!first)
            {
                builder.Append('|');
            }

            first = false;
            foreach (var child in cell.ChildElements)
            {
                if (child is not OoxmlTableCellProperties)
                {
                    Describe(child, builder);
                }
            }

            // Every cell ends in a paragraph; its mark says nothing a test wants to read.
            if (builder.Length > 0 && builder[^1] == '¶')
            {
                builder.Length--;
            }
        }

        foreach (var mark in row.TableRowProperties?.ChildElements ?? [])
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

        return builder.ToString();
    }

    static void Wrap(OpenXmlElement element, StringBuilder builder, string open, string close)
    {
        builder.Append(open);
        foreach (var child in element.ChildElements)
        {
            Describe(child, builder);
        }

        builder.Append(close);
    }
}
