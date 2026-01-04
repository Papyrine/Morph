namespace WordRender;

/// <summary>
/// Parses HTML content embedded in DOCX via AltChunk.
/// </summary>
internal sealed partial class HtmlParser
{
    public static List<DocumentElement> Parse(string html)
    {
        var elements = new List<DocumentElement>();

        // Extract body content if present
        var bodyMatch = BodyRegex().Match(html);
        var content = bodyMatch.Success ? bodyMatch.Groups[1].Value : html;

        // Parse block-level elements
        ParseContent(content, elements);

        return elements;
    }

    static void ParseContent(string content, List<DocumentElement> elements)
    {
        // Process block elements: headings, paragraphs, lists, tables
        var pos = 0;
        while (pos < content.Length)
        {
            // Skip whitespace
            while (pos < content.Length && char.IsWhiteSpace(content[pos]))
            {
                pos++;
            }

            if (pos >= content.Length)
            {
                break;
            }

            // Try to match block elements
            var remaining = content[pos..];

            // Headings h1-h6
            var headingMatch = HeadingRegex().Match(remaining);
            if (headingMatch is {Success: true, Index: 0})
            {
                var level = int.Parse(headingMatch.Groups[1].Value);
                var innerHtml = headingMatch.Groups[2].Value;
                var para = CreateParagraphFromInlineHtml(innerHtml, GetHeadingFontSize(level), true);
                elements.Add(para);
                pos += headingMatch.Length;
                continue;
            }

            // Paragraph
            var paraMatch = ParagraphRegex().Match(remaining);
            if (paraMatch is {Success: true, Index: 0})
            {
                var style = paraMatch.Groups[1].Value;
                var innerHtml = paraMatch.Groups[2].Value;
                var para = CreateParagraphFromInlineHtml(innerHtml, 11, false, ParseInlineStyle(style));
                elements.Add(para);
                pos += paraMatch.Length;
                continue;
            }

            // Unordered list
            var ulMatch = UlRegex().Match(remaining);
            if (ulMatch is {Success: true, Index: 0})
            {
                ParseList(ulMatch.Groups[1].Value, elements, "\u2022 "); // bullet
                pos += ulMatch.Length;
                continue;
            }

            // Ordered list
            var olMatch = OlRegex().Match(remaining);
            if (olMatch is {Success: true, Index: 0})
            {
                ParseOrderedList(olMatch.Groups[1].Value, elements);
                pos += olMatch.Length;
                continue;
            }

            // Table
            var tableMatch = TableRegex().Match(remaining);
            if (tableMatch is {Success: true, Index: 0})
            {
                var table = ParseTable(tableMatch.Value);
                if (table != null)
                {
                    elements.Add(table);
                }

                pos += tableMatch.Length;
                continue;
            }

            // Line break
            if (remaining.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
            {
                var brMatch = BrRegex().Match(remaining);
                if (brMatch.Success)
                {
                    elements.Add(new ParagraphElement
                    {
                        Runs = new List<Run> { new() { Text = "", Properties = new() } },
                        Properties = new()
                            { SpacingAfterPoints = 0 }
                    });
                    pos += brMatch.Length;
                    continue;
                }
            }

            // Any other tag - try to extract text content
            var anyTagMatch = AnyTagRegex().Match(remaining);
            if (anyTagMatch is {Success: true, Index: 0})
            {
                var innerHtml = anyTagMatch.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(StripTags(innerHtml)))
                {
                    var para = CreateParagraphFromInlineHtml(innerHtml, 11, false);
                    elements.Add(para);
                }
                pos += anyTagMatch.Length;
                continue;
            }

            // Plain text until next tag
            var nextTag = remaining.IndexOf('<');
            if (nextTag == -1)
            {
                // Rest is plain text
                var text = HttpUtility.HtmlDecode(remaining.Trim());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    elements.Add(new ParagraphElement
                    {
                        Runs = new List<Run> { new() { Text = text, Properties = new() } }
                    });
                }
                break;
            }
            else if (nextTag > 0)
            {
                var text = HttpUtility.HtmlDecode(remaining[..nextTag].Trim());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    elements.Add(new ParagraphElement
                    {
                        Runs = new List<Run> { new() { Text = text, Properties = new() } }
                    });
                }
                pos += nextTag;
            }
            else
            {
                pos++; // Skip unrecognized character
            }
        }
    }

    static ParagraphElement CreateParagraphFromInlineHtml(string html, double fontSize, bool bold, InlineStyle? style = null)
    {
        var runs = ParseInlineElements(html, new()
        {
            FontSizePoints = fontSize,
            Bold = bold,
            ColorHex = style?.Color
        });

        return new()
        {
            Runs = runs.Count > 0 ? runs :
            [
                new()
                {
                    Text = "",
                    Properties = new()
                    {
                        FontSizePoints = fontSize
                    }
                }
            ],
            Properties = new()
            {
                Alignment = style?.Alignment ?? TextAlignment.Left,
                SpacingAfterPoints = fontSize > 14 ? 12 : 8
            }
        };
    }

    static List<Run> ParseInlineElements(string html, RunProperties baseProps)
    {
        var runs = new List<Run>();
        ParseInlineContent(html, runs, baseProps);
        return runs;
    }

    static void ParseInlineContent(string html, List<Run> runs, RunProperties props)
    {
        var pos = 0;
        while (pos < html.Length)
        {
            var tagStart = html.IndexOf('<', pos);
            if (tagStart == -1)
            {
                // Remaining is plain text
                var text = HttpUtility.HtmlDecode(html[pos..]);
                if (!string.IsNullOrEmpty(text))
                {
                    runs.Add(new()
                        { Text = text, Properties = props });
                }

                break;
            }

            // Text before tag
            if (tagStart > pos)
            {
                var text = HttpUtility.HtmlDecode(html[pos..tagStart]);
                if (!string.IsNullOrEmpty(text))
                {
                    runs.Add(new()
                        { Text = text, Properties = props });
                }
            }

            // Find tag end
            var tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd == -1)
            {
                break;
            }

            var tagContent = html[(tagStart + 1)..tagEnd];
            var isClosing = tagContent.StartsWith('/');
            if (isClosing)
            {
                pos = tagEnd + 1;
                continue;
            }

            // Self-closing or opening tag
            var tagName = tagContent.Split(' ', '/')[0].ToLowerInvariant();

            // Handle inline formatting tags
            switch (tagName)
            {
                case "b":
                case "strong":
                    var boldEnd = FindClosingTag(html, tagEnd + 1, tagName);
                    if (boldEnd > tagEnd)
                    {
                        var innerHtml = html[(tagEnd + 1)..boldEnd];
                        ParseInlineContent(innerHtml, runs, props with { Bold = true });
                        pos = html.IndexOf('>', boldEnd) + 1;
                    }
                    else
                    {
                        pos = tagEnd + 1;
                    }

                    continue;

                case "i":
                case "em":
                    var italicEnd = FindClosingTag(html, tagEnd + 1, tagName);
                    if (italicEnd > tagEnd)
                    {
                        var innerHtml = html[(tagEnd + 1)..italicEnd];
                        ParseInlineContent(innerHtml, runs, props with { Italic = true });
                        pos = html.IndexOf('>', italicEnd) + 1;
                    }
                    else
                    {
                        pos = tagEnd + 1;
                    }

                    continue;

                case "u":
                    var underlineEnd = FindClosingTag(html, tagEnd + 1, tagName);
                    if (underlineEnd > tagEnd)
                    {
                        var innerHtml = html[(tagEnd + 1)..underlineEnd];
                        ParseInlineContent(innerHtml, runs, props with { Underline = true });
                        pos = html.IndexOf('>', underlineEnd) + 1;
                    }
                    else
                    {
                        pos = tagEnd + 1;
                    }

                    continue;

                case "s":
                case "strike":
                case "del":
                    var strikeEnd = FindClosingTag(html, tagEnd + 1, tagName);
                    if (strikeEnd > tagEnd)
                    {
                        var innerHtml = html[(tagEnd + 1)..strikeEnd];
                        ParseInlineContent(innerHtml, runs, props with { Strikethrough = true });
                        pos = html.IndexOf('>', strikeEnd) + 1;
                    }
                    else
                    {
                        pos = tagEnd + 1;
                    }

                    continue;

                case "font":
                    var fontEnd = FindClosingTag(html, tagEnd + 1, "font");
                    if (fontEnd > tagEnd)
                    {
                        var fontProps = ParseFontTag(tagContent, props);
                        var innerHtml = html[(tagEnd + 1)..fontEnd];
                        ParseInlineContent(innerHtml, runs, fontProps);
                        pos = html.IndexOf('>', fontEnd) + 1;
                    }
                    else
                    {
                        pos = tagEnd + 1;
                    }

                    continue;

                case "span":
                    var spanEnd = FindClosingTag(html, tagEnd + 1, "span");
                    if (spanEnd > tagEnd)
                    {
                        var spanProps = ParseSpanStyle(tagContent, props);
                        var innerHtml = html[(tagEnd + 1)..spanEnd];
                        ParseInlineContent(innerHtml, runs, spanProps);
                        pos = html.IndexOf('>', spanEnd) + 1;
                    }
                    else
                    {
                        pos = tagEnd + 1;
                    }

                    continue;

                case "a":
                    var linkEnd = FindClosingTag(html, tagEnd + 1, "a");
                    if (linkEnd > tagEnd)
                    {
                        var innerHtml = html[(tagEnd + 1)..linkEnd];
                        // Render links as blue underlined text
                        ParseInlineContent(innerHtml, runs, props with { ColorHex = "0000FF", Underline = true });
                        pos = html.IndexOf('>', linkEnd) + 1;
                    }
                    else
                    {
                        pos = tagEnd + 1;
                    }

                    continue;

                case "br":
                    runs.Add(new()
                        { Text = "\n", Properties = props });
                    pos = tagEnd + 1;
                    continue;

                case "sub":
                case "sup":
                    var scriptEnd = FindClosingTag(html, tagEnd + 1, tagName);
                    if (scriptEnd > tagEnd)
                    {
                        var innerHtml = html[(tagEnd + 1)..scriptEnd];
                        // Render sub/sup as smaller text (approximation)
                        ParseInlineContent(innerHtml, runs, props with { FontSizePoints = props.FontSizePoints * 0.7 });
                        pos = html.IndexOf('>', scriptEnd) + 1;
                    }
                    else
                    {
                        pos = tagEnd + 1;
                    }

                    continue;
            }

            pos = tagEnd + 1;
        }
    }

    static int FindClosingTag(string html, int startPos, string tagName)
    {
        var pattern = $"</{tagName}>";
        var index = html.IndexOf(pattern, startPos, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? index : -1;
    }

    static RunProperties ParseFontTag(string tagContent, RunProperties baseProps)
    {
        var props = baseProps;

        // Parse face attribute
        var faceMatch = Regex.Match(tagContent, @"face\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (faceMatch.Success)
        {
            props = props with { FontFamily = faceMatch.Groups[1].Value };
        }

        // Parse color attribute
        var colorMatch = Regex.Match(tagContent, @"color\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (colorMatch.Success)
        {
            props = props with { ColorHex = NormalizeColor(colorMatch.Groups[1].Value) };
        }

        // Parse size attribute (1-7, where 3 is normal ~11pt)
        var sizeMatch = Regex.Match(tagContent, @"size\s*=\s*[""'](\d+)[""']", RegexOptions.IgnoreCase);
        if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var size))
        {
            double[] fontSizes = [8, 10, 12, 14, 18, 24, 36];
            var idx = Math.Clamp(size - 1, 0, 6);
            props = props with { FontSizePoints = fontSizes[idx] };
        }

        return props;
    }

    static RunProperties ParseSpanStyle(string tagContent, RunProperties baseProps)
    {
        var styleMatch = Regex.Match(tagContent, @"style\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!styleMatch.Success)
        {
            return baseProps;
        }

        var style = styleMatch.Groups[1].Value;
        var props = baseProps;

        // color
        var colorMatch = Regex.Match(style, @"color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (colorMatch.Success)
        {
            props = props with { ColorHex = NormalizeColor(colorMatch.Groups[1].Value.Trim()) };
        }

        // font-family
        var fontMatch = Regex.Match(style, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (fontMatch.Success)
        {
            props = props with { FontFamily = fontMatch.Groups[1].Value.Trim().Trim('\'', '"') };
        }

        // font-size
        var sizeMatch = Regex.Match(style, @"font-size\s*:\s*(\d+)", RegexOptions.IgnoreCase);
        if (sizeMatch.Success && double.TryParse(sizeMatch.Groups[1].Value, out var size))
        {
            props = props with { FontSizePoints = size };
        }

        // font-weight
        if (style.Contains("font-weight") && (style.Contains("bold") || style.Contains("700")))
        {
            props = props with { Bold = true };
        }

        // font-style
        if (style.Contains("font-style") && style.Contains("italic"))
        {
            props = props with { Italic = true };
        }

        // text-decoration
        if (style.Contains("text-decoration") && style.Contains("underline"))
        {
            props = props with { Underline = true };
        }

        if (style.Contains("text-decoration") && style.Contains("line-through"))
        {
            props = props with { Strikethrough = true };
        }

        return props;
    }

    static InlineStyle? ParseInlineStyle(string style)
    {
        if (string.IsNullOrEmpty(style))
        {
            return null;
        }

        var styleMatch = Regex.Match(style, @"style\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!styleMatch.Success)
        {
            return null;
        }

        var styleValue = styleMatch.Groups[1].Value;
        var result = new InlineStyle();

        // text-align
        var alignMatch = Regex.Match(styleValue, @"text-align\s*:\s*(\w+)", RegexOptions.IgnoreCase);
        if (alignMatch.Success)
        {
            result.Alignment = alignMatch.Groups[1].Value.ToLowerInvariant() switch
            {
                "center" => TextAlignment.Center,
                "right" => TextAlignment.Right,
                "justify" => TextAlignment.Justify,
                _ => TextAlignment.Left
            };
        }

        // color
        var colorMatch = Regex.Match(styleValue, @"(?<!background-)color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (colorMatch.Success)
        {
            result.Color = NormalizeColor(colorMatch.Groups[1].Value.Trim());
        }

        return result;
    }

    static void ParseList(string listContent, List<DocumentElement> elements, string bulletPrefix, int level = 0)
    {
        var matches = LiRegex().Matches(listContent);
        foreach (Match match in matches)
        {
            var itemContent = match.Groups[1].Value;

            // Check for nested lists
            var nestedUl = UlRegex().Match(itemContent);
            var nestedOl = OlRegex().Match(itemContent);

            // Get text before nested list
            var textEnd = nestedUl.Success ? nestedUl.Index : nestedOl.Success ? nestedOl.Index : itemContent.Length;
            var text = StripTags(itemContent[..textEnd]).Trim();

            if (!string.IsNullOrEmpty(text))
            {
                var indent = 18 * (level + 1);
                var runs = new List<Run> { new() { Text = bulletPrefix + text, Properties = new() } };
                elements.Add(new ParagraphElement
                {
                    Runs = runs,
                    Properties = new()
                        { LeftIndentPoints = indent, SpacingAfterPoints = 4 }
                });
            }

            // Process nested lists
            if (nestedUl.Success)
            {
                ParseList(nestedUl.Groups[1].Value, elements, "\u25E6 ", level + 1); // hollow bullet
            }

            if (nestedOl.Success)
            {
                ParseOrderedList(nestedOl.Groups[1].Value, elements, level + 1);
            }
        }
    }

    static void ParseOrderedList(string listContent, List<DocumentElement> elements, int level = 0)
    {
        var matches = LiRegex().Matches(listContent);
        var num = 1;
        foreach (Match match in matches)
        {
            var itemContent = match.Groups[1].Value;
            var text = StripTags(itemContent).Trim();

            if (!string.IsNullOrEmpty(text))
            {
                var indent = 18 * (level + 1);
                var runs = new List<Run> { new() { Text = $"{num}. {text}", Properties = new() } };
                elements.Add(new ParagraphElement
                {
                    Runs = runs,
                    Properties = new()
                        { LeftIndentPoints = indent, SpacingAfterPoints = 4 }
                });
                num++;
            }
        }
    }

    static TableElement? ParseTable(string tableHtml)
    {
        var rows = new List<TableRow>();

        // Parse table-level cellpadding attribute
        CellSpacing? defaultCellPadding = null;
        var cellpaddingMatch = Regex.Match(tableHtml, @"<table[^>]*cellpadding\s*=\s*[""']?(\d+)[""']?", RegexOptions.IgnoreCase);
        if (cellpaddingMatch.Success && double.TryParse(cellpaddingMatch.Groups[1].Value, out var padding))
        {
            defaultCellPadding = new(padding);
        }

        // Parse table-level style for padding
        var tableStyleMatch = Regex.Match(tableHtml, @"<table[^>]*style\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (tableStyleMatch.Success)
        {
            var tablePadding = ParseCssSpacing(tableStyleMatch.Groups[1].Value, "padding");
            if (tablePadding != null)
            {
                defaultCellPadding = tablePadding;
            }
        }

        var trMatches = TrRegex().Matches(tableHtml);

        foreach (Match trMatch in trMatches)
        {
            var rowContent = trMatch.Groups[1].Value;
            var cells = new List<TableCell>();

            // Match td and th with their full opening tag
            var cellMatches = TdThFullRegex().Matches(rowContent);
            foreach (Match cellMatch in cellMatches)
            {
                var tagName = cellMatch.Groups[1].Value;
                var attrs = cellMatch.Groups[2].Value;
                var cellContent = cellMatch.Groups[3].Value;
                var isHeader = tagName.Equals("th", StringComparison.OrdinalIgnoreCase);
                var text = StripTags(cellContent).Trim();

                // Parse cell-level padding and margin from style
                CellSpacing? cellPadding = null;
                CellSpacing? cellMargin = null;
                var styleMatch = Regex.Match(attrs, @"style\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (styleMatch.Success)
                {
                    var styleValue = styleMatch.Groups[1].Value;
                    cellPadding = ParseCssSpacing(styleValue, "padding");
                    cellMargin = ParseCssSpacing(styleValue, "margin");
                }

                var cellElements = new List<DocumentElement>();
                if (!string.IsNullOrEmpty(text))
                {
                    cellElements.Add(new ParagraphElement
                    {
                        Runs = new List<Run>
                        {
                            new()
                            {
                                Text = text,
                                Properties = new()
                                    { Bold = isHeader }
                            }
                        }
                    });
                }

                cells.Add(new()
                {
                    Content = cellElements,
                    Properties = new()
                    {
                        Padding = cellPadding,
                        Margin = cellMargin
                    }
                });
            }

            if (cells.Count > 0)
            {
                rows.Add(new()
                    { Cells = cells });
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                DefaultBorders = CellBorders.All,
                DefaultCellPadding = defaultCellPadding ?? new CellSpacing()
            }
        };
    }

    static CellSpacing? ParseCssSpacing(string style, string property)
    {
        // Try padding: X or margin: X (shorthand for all sides)
        var allMatch = Regex.Match(style, $@"{property}\s*:\s*(\d+)(?:px|pt)?(?:\s|;|$)", RegexOptions.IgnoreCase);
        if (allMatch.Success && double.TryParse(allMatch.Groups[1].Value, out var all))
        {
            return new(all);
        }

        // Try individual properties
        double? top = null, right = null, bottom = null, left = null;

        var topMatch = Regex.Match(style, $@"{property}-top\s*:\s*(\d+)(?:px|pt)?", RegexOptions.IgnoreCase);
        if (topMatch.Success && double.TryParse(topMatch.Groups[1].Value, out var t))
        {
            top = t;
        }

        var rightMatch = Regex.Match(style, $@"{property}-right\s*:\s*(\d+)(?:px|pt)?", RegexOptions.IgnoreCase);
        if (rightMatch.Success && double.TryParse(rightMatch.Groups[1].Value, out var r))
        {
            right = r;
        }

        var bottomMatch = Regex.Match(style, $@"{property}-bottom\s*:\s*(\d+)(?:px|pt)?", RegexOptions.IgnoreCase);
        if (bottomMatch.Success && double.TryParse(bottomMatch.Groups[1].Value, out var b))
        {
            bottom = b;
        }

        var leftMatch = Regex.Match(style, $@"{property}-left\s*:\s*(\d+)(?:px|pt)?", RegexOptions.IgnoreCase);
        if (leftMatch.Success && double.TryParse(leftMatch.Groups[1].Value, out var l))
        {
            left = l;
        }

        if (top.HasValue || right.HasValue || bottom.HasValue || left.HasValue)
        {
            return new(
                top ?? 0,
                right ?? 0,
                bottom ?? 0,
                left ?? 0
            );
        }

        return null;
    }

    static string StripTags(string html) =>
        HttpUtility.HtmlDecode(TagRegex().Replace(html, ""));

    static double GetHeadingFontSize(int level) => level switch
    {
        1 => 24,
        2 => 18,
        3 => 14,
        4 => 12,
        5 => 11,
        6 => 10,
        _ => 11
    };

    static string? NormalizeColor(string color)
    {
        if (string.IsNullOrEmpty(color))
        {
            return null;
        }

        color = color.Trim();

        // Named colors
        var namedColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["red"] = "FF0000",
            ["green"] = "008000",
            ["blue"] = "0000FF",
            ["black"] = "000000",
            ["white"] = "FFFFFF",
            ["yellow"] = "FFFF00",
            ["orange"] = "FFA500",
            ["purple"] = "800080",
            ["gray"] = "808080",
            ["grey"] = "808080"
        };

        if (namedColors.TryGetValue(color, out var hex))
        {
            return hex;
        }

        // #RGB or #RRGGBB
        if (color.StartsWith('#'))
        {
            var hexValue = color[1..];
            if (hexValue.Length == 3)
            {
                return $"{hexValue[0]}{hexValue[0]}{hexValue[1]}{hexValue[1]}{hexValue[2]}{hexValue[2]}";
            }

            if (hexValue.Length == 6)
            {
                return hexValue.ToUpperInvariant();
            }
        }

        // rgb(r, g, b)
        var rgbMatch = Regex.Match(color, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
        if (rgbMatch.Success)
        {
            var r = int.Parse(rgbMatch.Groups[1].Value);
            var g = int.Parse(rgbMatch.Groups[2].Value);
            var b = int.Parse(rgbMatch.Groups[3].Value);
            return $"{r:X2}{g:X2}{b:X2}";
        }

        return null;
    }

    class InlineStyle
    {
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public string? Color { get; set; }
    }

    [GeneratedRegex("<body[^>]*>(.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BodyRegex();

    [GeneratedRegex(@"<h([1-6])[^>]*>(.*?)</h\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("<p([^>]*)>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex("<ul[^>]*>(.*?)</ul>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UlRegex();

    [GeneratedRegex("<ol[^>]*>(.*?)</ol>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OlRegex();

    [GeneratedRegex("<li[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LiRegex();

    [GeneratedRegex("<table[^>]*>.*?</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableRegex();

    [GeneratedRegex("<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TrRegex();

    [GeneratedRegex(@"<(td|th)[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TdThRegex();

    [GeneratedRegex(@"<(td|th)([^>]*)>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TdThFullRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();

    [GeneratedRegex(@"<(\w+)[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}
