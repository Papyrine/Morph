namespace Morph;

/// <summary>
/// Turns Morph's HTML export into a plain-text (.txt) rendition. Morph ships only HTML and Markdown
/// exporters, so "plain text" is derived by walking the exported HTML fragment: block elements become
/// line breaks, list items get a "- " bullet, table cells are tab-separated, and inline runs collapse
/// their whitespace. AngleSharp does the parsing — it's already on the dependency graph via Morph.
/// </summary>
public static class TextExtraction
{
    // Elements that force a line break around their content but carry no bullet / cell semantics.
    static readonly HashSet<string> blockTags =
    [
        with(StringComparer.Ordinal),
        "div", "section", "article", "header", "footer", "main", "aside", "nav", "figure",
        "figcaption", "ul", "ol", "table", "thead", "tbody", "tfoot", "caption", "dl", "dt", "dd"
    ];

    /// <summary>Flattens an HTML fragment or document into plain text.</summary>
    public static string FromHtml(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument($"<!doctype html><html><body>{html}</body></html>");
        var builder = new StringBuilder();
        WalkChildren(document.Body!, builder);
        return Normalize(builder.ToString());
    }

    static void WalkChildren(INode node, StringBuilder builder)
    {
        foreach (var child in node.ChildNodes)
        {
            Walk(child, builder);
        }
    }

    static void Walk(INode node, StringBuilder builder)
    {
        if (node is IText text)
        {
            AppendInline(builder, text.Text);
            return;
        }

        if (node is not IElement element)
        {
            return;
        }

        switch (element.LocalName)
        {
            case "br":
                builder.Append('\n');
                return;
            case "hr":
                NewLine(builder);
                builder.Append("----------\n");
                return;
            case "li":
                NewLine(builder);
                builder.Append("- ");
                WalkChildren(element, builder);
                builder.Append('\n');
                return;
            case "td":
            case "th":
                WalkChildren(element, builder);
                builder.Append('\t');
                return;
            case "tr":
                NewLine(builder);
                WalkChildren(element, builder);
                builder.Append('\n');
                return;
            case "p":
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
            case "blockquote":
            case "pre":
                // Paragraphs and headings get a trailing blank line so they read as separate blocks.
                NewLine(builder);
                WalkChildren(element, builder);
                builder.Append("\n\n");
                return;
        }

        if (blockTags.Contains(element.LocalName))
        {
            NewLine(builder);
            WalkChildren(element, builder);
            NewLine(builder);
            return;
        }

        // Any other element (span, a, strong, em, …) is inline: emit its text in place.
        WalkChildren(element, builder);
    }

    // Appends text with every run of whitespace (spaces, tabs, newlines in the source markup) collapsed
    // to a single space, so re-flowed HTML source doesn't leak its indentation into the plain text.
    static void AppendInline(StringBuilder builder, string text)
    {
        var inWhitespace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!inWhitespace)
                {
                    builder.Append(' ');
                    inWhitespace = true;
                }
            }
            else
            {
                builder.Append(ch);
                inWhitespace = false;
            }
        }
    }

    static void NewLine(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '\n')
        {
            builder.Append('\n');
        }
    }

    // Trims each line, drops leading/trailing blank lines, and collapses any run of blank lines to one —
    // so the output is tidy regardless of how the walker peppered newlines around nested blocks.
    static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(_ => _.Trim());
        var output = new List<string>();
        var pendingBlank = false;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                pendingBlank = output.Count > 0;
                continue;
            }

            if (pendingBlank)
            {
                output.Add(string.Empty);
                pendingBlank = false;
            }

            output.Add(line);
        }

        return output.Count == 0 ? string.Empty : string.Join('\n', output) + '\n';
    }
}
