using System.Text.RegularExpressions;
using AngleSharp.Dom;

/// <summary>
/// Text pasted as HTML — from a browser, Google Docs, Word — read for what the editor keeps of it when
/// it merges it into a document, as Word's Merge Formatting does: its paragraphs and line breaks, its
/// headings (h1 to h6, and Word's own title and quote classes), its lists with their levels, its
/// alignment, its links, and its emphasis — bold, italic, underline, strikethrough, superscript and
/// subscript, by tag or by CSS. Fonts, sizes and colours are the document's, and are not read; nor are
/// pictures. A table's rows are paragraphs, their cells a tab apart.
/// </summary>
static partial class PastedHtml
{
    static readonly AngleSharp.Html.Parser.HtmlParser parser = new();

    public static IReadOnlyList<PastedParagraph> Read(string html)
    {
        var document = parser.ParseDocument(html);
        var reader = new Reader();
        if (document.Body is { } body)
        {
            reader.Children(body, default);
        }

        return reader.Finish();
    }

    // Elements whose content is not text the reader sees.
    static readonly HashSet<string> unread = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "title", "script", "style", "meta", "link", "template", "noscript", "svg", "math", "iframe",
        "object", "embed", "canvas", "video", "audio", "img", "picture", "input", "select", "textarea",
        "button"
    };

    static readonly HashSet<string> blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "nav", "aside", "blockquote",
        "figure", "figcaption", "address", "center", "pre", "dd", "dt", "dl", "h1", "h2", "h3", "h4",
        "h5", "h6", "caption", "details", "summary", "form", "fieldset", "legend", "hr"
    };

    // Word's paragraph classes for the styles it builds in, as its HTML names them.
    static readonly Dictionary<string, string> wordClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MsoTitle"] = "Title",
        ["MsoSubtitle"] = "Subtitle",
        ["MsoQuote"] = "Quote",
        ["MsoIntenseQuote"] = "IntenseQuote"
    };

    // What text takes from the elements it stands in.
    readonly record struct Inline(
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strike,
        DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues? Vertical,
        string? Link,
        bool Preformatted);

    // What a paragraph takes from the block it stands in. Item names the list item it is the first
    // paragraph of: a second paragraph in the same item is not numbered again.
    readonly record struct Block(string? Style, ListKind List, int Level, TextAlignment? Alignment, int Item);

    sealed class Reader
    {
        readonly List<PastedParagraph> paragraphs = [];
        readonly HashSet<int> numbered = [];
        readonly Stack<ListKind> lists = [];
        List<PastedRun> runs = [];
        readonly StringBuilder text = new();
        Inline current;
        Block block;
        bool explicitly;
        int cells;
        int items;

        public void Children(INode node, Inline inline)
        {
            foreach (var child in node.ChildNodes)
            {
                switch (child)
                {
                    case IText each:
                        Text(each.Data, inline);
                        break;
                    case IElement element:
                        Element(element, inline);
                        break;
                }
            }
        }

        void Element(IElement element, Inline inline)
        {
            var name = element.LocalName;
            var css = Declarations(element);
            if (unread.Contains(name) ||
                element.HasAttribute("hidden") ||
                css.GetValueOrDefault("display") == "none" ||
                css.GetValueOrDefault("visibility") == "hidden" ||
                css.GetValueOrDefault("mso-hide") == "all" ||
                css.GetValueOrDefault("mso-list") == "ignore")
            {
                return;
            }

            inline = Styled(element, name, css, inline);
            switch (name)
            {
                case "br":
                    Append('\n', inline);
                    explicitly = true;
                    return;
                case "ul":
                case "ol":
                    lists.Push(name == "ol" ? ListKind.Number : ListKind.Bullet);
                    Children(element, inline);
                    lists.Pop();
                    return;
                case "li":
                    Item(element, css, inline);
                    return;
                case "tr":
                    Row(element, inline);
                    return;
                case "td":
                case "th":
                    Children(element, inline);
                    return;
            }

            if (blocks.Contains(name))
            {
                Paragraph(element, name, css, inline);
                return;
            }

            Children(element, inline);
        }

        void Paragraph(IElement element, string name, Dictionary<string, string> css, Inline inline)
        {
            // In a table's cell, what would be a paragraph is part of the cell's text.
            if (cells > 0)
            {
                Separate(inline);
                Children(element, inline);
                Separate(inline);
                return;
            }

            Flush();
            var saved = block;
            block = block with
            {
                Style = StyleOf(element, name) ?? block.Style,
                Alignment = AlignmentOf(element, css) ?? block.Alignment
            };

            // Word writes a list's items as paragraphs, each saying its list and level in CSS, its
            // number or bullet in a span marked to be ignored.
            if (css.TryGetValue("mso-list", out var list) &&
                WordLevel.Match(list) is {Success: true} level)
            {
                block = block with
                {
                    List = WordListKind(element),
                    Level = Math.Clamp(int.Parse(level.Groups[1].Value, CultureInfo.InvariantCulture) - 1, 0, 8),
                    Item = ++items
                };
            }

            if (name == "pre")
            {
                inline = inline with
                {
                    Preformatted = true
                };
            }

            if (name != "hr")
            {
                Children(element, inline);
            }

            Flush();
            block = saved;
        }

        void Item(IElement element, Dictionary<string, string> css, Inline inline)
        {
            if (cells > 0)
            {
                Separate(inline);
                Children(element, inline);
                return;
            }

            Flush();
            var saved = block;
            var level = Math.Max(0, lists.Count - 1);

            // Google Docs writes nested items flat, each saying its level.
            if (int.TryParse(element.GetAttribute("aria-level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var aria))
            {
                level = aria - 1;
            }

            var kind = ListKind.Bullet;
            if (lists.Count > 0)
            {
                kind = lists.Peek();
            }

            block = block with
            {
                List = kind,
                Level = Math.Clamp(level, 0, 8),
                Alignment = AlignmentOf(element, css) ?? block.Alignment,
                Item = ++items
            };
            Children(element, inline);
            Flush();
            block = saved;
        }

        // A table's row is a paragraph, its cells a tab apart.
        void Row(IElement row, Inline inline)
        {
            if (cells > 0)
            {
                Separate(inline);
                Children(row, inline);
                return;
            }

            Flush();
            var first = true;
            foreach (var cell in row.Children)
            {
                if (cell.LocalName is not ("td" or "th"))
                {
                    continue;
                }

                if (!first)
                {
                    TrimEnd(lineBreak: false);
                    Append('\t', inline);
                }

                first = false;
                cells++;
                Children(cell, Styled(cell, cell.LocalName, Declarations(cell), inline));
                cells--;
            }

            Flush();
        }

        void Text(string data, Inline inline)
        {
            if (inline.Preformatted)
            {
                foreach (var character in data)
                {
                    switch (character)
                    {
                        case '\r':
                            break;
                        case '\n':
                            explicitly = true;
                            Flush();
                            break;
                        default:
                            Append(character, inline);
                            break;
                    }
                }

                return;
            }

            foreach (var character in data)
            {
                if (character is ' ' or '\t' or '\n' or '\r' or '\f')
                {
                    Separate(inline);
                    continue;
                }

                if (character == '\u00A0')
                {
                    explicitly = true;
                    Append(' ', inline);
                    continue;
                }

                if (character >= ' ' &&
                    character is not ('\uFFFE' or '\uFFFF'))
                {
                    Append(character, inline);
                }
            }
        }

        // One space between words, and none at a paragraph's start.
        void Separate(Inline inline)
        {
            if (Last() is not (null or ' ' or '\n' or '\t'))
            {
                Append(' ', inline);
            }
        }

        char? Last()
        {
            if (text.Length > 0)
            {
                return text[^1];
            }

            if (runs.Count > 0 &&
                runs[^1].Text.Length > 0)
            {
                return runs[^1].Text[^1];
            }

            return null;
        }

        void Append(char character, Inline inline)
        {
            if (text.Length > 0 &&
                inline != current)
            {
                Close();
            }

            current = inline;
            text.Append(character);
        }

        void Close()
        {
            if (text.Length == 0)
            {
                return;
            }

            var format = new RunFormat(
                Bold: current.Bold ? true : null,
                Italic: current.Italic ? true : null,
                // A link's own style underlines it.
                Underline: current.Underline && current.Link == null ? true : null,
                Strike: current.Strike ? true : null,
                Vertical: current.Vertical);
            // Text that looks as the run before it does is that run's.
            if (runs.Count > 0 &&
                runs[^1].Format == format &&
                runs[^1].Link == current.Link)
            {
                runs[^1] = runs[^1] with
                {
                    Text = runs[^1].Text + text
                };
            }
            else
            {
                runs.Add(new(text.ToString(), format, current.Link));
            }

            text.Clear();
        }

        // Spaces at the end of what has been read, and a break, when it ends a paragraph, show nothing.
        void TrimEnd(bool lineBreak)
        {
            Close();
            while (runs.Count > 0)
            {
                var last = runs[^1];
                var trimmed = last.Text.TrimEnd(' ');
                if (lineBreak &&
                    trimmed.EndsWith('\n'))
                {
                    trimmed = trimmed[..^1];
                }

                if (trimmed.Length > 0)
                {
                    runs[^1] = last with
                    {
                        Text = trimmed
                    };
                    break;
                }

                runs.RemoveAt(runs.Count - 1);
            }
        }

        void Flush()
        {
            TrimEnd(lineBreak: true);
            if (runs.Count == 0 &&
                !explicitly)
            {
                return;
            }

            var listed = block.List != ListKind.None && numbered.Add(block.Item);
            paragraphs.Add(
                new(runs)
                {
                    Style = block.Style,
                    List = listed ? block.List : ListKind.None,
                    Level = listed ? block.Level : 0,
                    Alignment = block.Alignment
                });
            runs = [];
            explicitly = false;
        }

        public IReadOnlyList<PastedParagraph> Finish()
        {
            Flush();
            var start = 0;
            while (start < paragraphs.Count &&
                   paragraphs[start].Runs.Count == 0)
            {
                start++;
            }

            var end = paragraphs.Count;
            while (end > start &&
                   paragraphs[end - 1].Runs.Count == 0)
            {
                end--;
            }

            return paragraphs.GetRange(start, end - start);
        }
    }

    // What an element and its CSS do to the text in it.
    static Inline Styled(IElement element, string name, Dictionary<string, string> css, Inline inline)
    {
        switch (name)
        {
            case "b":
            case "strong":
                inline = inline with
                {
                    Bold = true
                };
                break;
            case "i":
            case "em":
            case "cite":
            case "dfn":
            case "var":
                inline = inline with
                {
                    Italic = true
                };
                break;
            case "u":
            case "ins":
                inline = inline with
                {
                    Underline = true
                };
                break;
            case "s":
            case "strike":
            case "del":
                inline = inline with
                {
                    Strike = true
                };
                break;
            case "sup":
                inline = inline with
                {
                    Vertical = DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues.Superscript
                };
                break;
            case "sub":
                inline = inline with
                {
                    Vertical = DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues.Subscript
                };
                break;
            case "a":
                if (Address(element.GetAttribute("href")) is { } address)
                {
                    inline = inline with
                    {
                        Link = address
                    };
                }

                break;
        }

        if (css.TryGetValue("font-weight", out var weight))
        {
            inline = inline with
            {
                Bold = weight is "bold" or "bolder" ||
                       (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 600)
            };
        }

        if (css.TryGetValue("font-style", out var style))
        {
            inline = inline with
            {
                Italic = style is "italic" or "oblique"
            };
        }

        foreach (var property in (string[]) ["text-decoration", "text-decoration-line"])
        {
            if (css.TryGetValue(property, out var decoration))
            {
                inline = inline with
                {
                    Underline = decoration.Contains("underline"),
                    Strike = decoration.Contains("line-through")
                };
            }
        }

        if (css.TryGetValue("vertical-align", out var vertical))
        {
            inline = inline with
            {
                Vertical = vertical switch
                {
                    "super" => DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues.Superscript,
                    "sub" => DocumentFormat.OpenXml.Wordprocessing.VerticalPositionValues.Subscript,
                    _ => null
                }
            };
        }

        return inline;
    }

    // A link to the web or to mail is kept. One into the page it came from, or relative to it, has
    // nowhere to go in the document.
    static string? Address(string? href)
    {
        if (href == null ||
            !Uri.TryCreate(href.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto" or "ftp"))
        {
            return null;
        }

        return uri.OriginalString;
    }

    static string? StyleOf(IElement element, string name)
    {
        if (name.Length == 2 &&
            name[0] == 'h' &&
            name[1] is >= '1' and <= '6')
        {
            return $"Heading{name[1]}";
        }

        foreach (var each in element.ClassList)
        {
            if (wordClasses.TryGetValue(each, out var style))
            {
                return style;
            }
        }

        return null;
    }

    static TextAlignment? AlignmentOf(IElement element, Dictionary<string, string> css)
    {
        var value = css.GetValueOrDefault("text-align") ?? element.GetAttribute("align")?.Trim().ToLowerInvariant();
        return value switch
        {
            "center" => TextAlignment.Center,
            "right" or "end" => TextAlignment.Right,
            "justify" => TextAlignment.Justify,
            _ => null
        };
    }

    // Word's list item is numbered when the marker it writes is a number or a letter followed by a
    // stop or a bracket; any other marker is a bullet.
    static ListKind WordListKind(IElement paragraph)
    {
        foreach (var span in paragraph.QuerySelectorAll("span"))
        {
            if (Declarations(span).GetValueOrDefault("mso-list") != "ignore")
            {
                continue;
            }

            var marker = span.TextContent.Replace('\u00A0', ' ').Trim();
            if (NumberMarker.IsMatch(marker))
            {
                return ListKind.Number;
            }

            return ListKind.Bullet;
        }

        return ListKind.Bullet;
    }

    [GeneratedRegex(@"level(\d+)")]
    private static partial Regex WordLevel { get; }

    [GeneratedRegex(@"^\(?[0-9A-Za-z]{1,6}[.)]$")]
    private static partial Regex NumberMarker { get; }

    // An element's style attribute, by property, lower-cased.
    static Dictionary<string, string> Declarations(IElement element)
    {
        var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.GetAttribute("style") is not { } style)
        {
            return declarations;
        }

        foreach (var declaration in style.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var value = declaration[(colon + 1)..].Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();
            declarations[declaration[..colon].Trim()] = value;
        }

        return declarations;
    }
}
