using AngleSharp.Dom;

/// <summary>
/// Parses HTML content embedded in DOCX via AltChunk.
/// </summary>
internal sealed class HtmlParser
{
    public static List<DocumentElement> Parse(string html)
    {
        var elements = new List<DocumentElement>();
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var document = parser.ParseDocument(html);

        var body = document.Body;
        if (body == null)
        {
            return elements;
        }

        ParseNodes(body.ChildNodes, elements);
        return elements;
    }

    static void ParseNodes(INodeList nodes, List<DocumentElement> elements)
    {
        foreach (var node in nodes)
        {
            ParseNode(node, elements);
        }
    }

    static void ParseNode(INode node, List<DocumentElement> elements)
    {
        switch (node)
        {
            case IText textNode:
                var text = textNode.TextContent.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    elements.Add(new ParagraphElement
                    {
                        Runs = [new() { Text = text, Properties = new() }]
                    });
                }
                break;

            case IElement element:
                ParseElement(element, elements);
                break;
        }
    }

    static void ParseElement(IElement element, List<DocumentElement> elements)
    {
        switch (element.TagName.ToLowerInvariant())
        {
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                var level = int.Parse(element.TagName[1..]);
                var headingPara = CreateParagraph(element, GetHeadingFontSize(level), true);
                elements.Add(headingPara);
                break;

            case "p":
                var style = ParseInlineStyle(element);
                var para = CreateParagraph(element, 11, false, style);
                elements.Add(para);
                break;

            case "ul":
                ParseList(element, elements, "\u2022 ");
                break;

            case "ol":
                ParseOrderedList(element, elements);
                break;

            case "table":
                var table = ParseTable(element);
                if (table != null)
                {
                    elements.Add(table);
                }
                break;

            case "br":
                elements.Add(new ParagraphElement
                {
                    Runs = [new() { Text = "", Properties = new() }],
                    Properties = new() { SpacingAfterPoints = 0 }
                });
                break;

            case "div":
            case "section":
            case "article":
            case "main":
            case "header":
            case "footer":
            case "nav":
            case "aside":
                // Container elements - process children
                ParseNodes(element.ChildNodes, elements);
                break;

            default:
                // For other elements, try to extract content
                var text = element.TextContent.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var defaultPara = CreateParagraph(element, 11, false);
                    elements.Add(defaultPara);
                }
                break;
        }
    }

    static ParagraphElement CreateParagraph(IElement element, double fontSize, bool bold, InlineStyle? style = null)
    {
        var runs = ParseInlineElements(element, new RunProperties
        {
            FontSizePoints = fontSize,
            Bold = bold,
            ColorHex = style?.Color
        });

        return new()
        {
            Runs = runs.Count > 0 ? runs : [new() { Text = "", Properties = new() { FontSizePoints = fontSize } }],
            Properties = new()
            {
                Alignment = style?.Alignment ?? TextAlignment.Left,
                SpacingAfterPoints = fontSize > 14 ? 12 : 8
            }
        };
    }

    static List<Run> ParseInlineElements(IElement element, RunProperties baseProps)
    {
        var runs = new List<Run>();
        ParseInlineNodes(element.ChildNodes, runs, baseProps);
        return runs;
    }

    static void ParseInlineNodes(INodeList nodes, List<Run> runs, RunProperties props)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case IText textNode:
                    var text = textNode.TextContent;
                    if (!string.IsNullOrEmpty(text))
                    {
                        runs.Add(new() { Text = text, Properties = props });
                    }
                    break;

                case IElement element:
                    ParseInlineElement(element, runs, props);
                    break;
            }
        }
    }

    static void ParseInlineElement(IElement element, List<Run> runs, RunProperties props)
    {
        switch (element.TagName.ToLowerInvariant())
        {
            case "b":
            case "strong":
                ParseInlineNodes(element.ChildNodes, runs, props with { Bold = true });
                break;

            case "i":
            case "em":
                ParseInlineNodes(element.ChildNodes, runs, props with { Italic = true });
                break;

            case "u":
                ParseInlineNodes(element.ChildNodes, runs, props with { Underline = true });
                break;

            case "s":
            case "strike":
            case "del":
                ParseInlineNodes(element.ChildNodes, runs, props with { Strikethrough = true });
                break;

            case "font":
                var fontProps = ParseFontElement(element, props);
                ParseInlineNodes(element.ChildNodes, runs, fontProps);
                break;

            case "span":
                var spanProps = ParseSpanStyle(element, props);
                ParseInlineNodes(element.ChildNodes, runs, spanProps);
                break;

            case "a":
                // Render links as blue underlined text
                ParseInlineNodes(element.ChildNodes, runs, props with { ColorHex = "0000FF", Underline = true });
                break;

            case "br":
                runs.Add(new() { Text = "\n", Properties = props });
                break;

            case "sub":
            case "sup":
                // Render sub/sup as smaller text
                ParseInlineNodes(element.ChildNodes, runs, props with { FontSizePoints = props.FontSizePoints * 0.7 });
                break;

            default:
                // Process children for unknown inline elements
                ParseInlineNodes(element.ChildNodes, runs, props);
                break;
        }
    }

    static RunProperties ParseFontElement(IElement element, RunProperties baseProps)
    {
        var props = baseProps;

        var face = element.GetAttribute("face");
        if (!string.IsNullOrEmpty(face))
        {
            props = props with { FontFamily = face };
        }

        var color = element.GetAttribute("color");
        if (!string.IsNullOrEmpty(color))
        {
            props = props with { ColorHex = NormalizeColor(color) };
        }

        var size = element.GetAttribute("size");
        if (!string.IsNullOrEmpty(size) && int.TryParse(size, out var sizeValue))
        {
            double[] fontSizes = [8, 10, 12, 14, 18, 24, 36];
            var idx = Math.Clamp(sizeValue - 1, 0, 6);
            props = props with { FontSizePoints = fontSizes[idx] };
        }

        return props;
    }

    static RunProperties ParseSpanStyle(IElement element, RunProperties baseProps)
    {
        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style))
        {
            return baseProps;
        }

        return ApplyStyleToRunProps(style, baseProps);
    }

    static RunProperties ApplyStyleToRunProps(string style, RunProperties props)
    {
        var styles = ParseStyleAttribute(style);

        if (styles.TryGetValue("color", out var color))
        {
            props = props with { ColorHex = NormalizeColor(color) };
        }

        if (styles.TryGetValue("font-family", out var fontFamily))
        {
            props = props with { FontFamily = fontFamily.Trim('\'', '"') };
        }

        if (styles.TryGetValue("font-size", out var fontSize))
        {
            if (double.TryParse(fontSize.Replace("px", "").Replace("pt", ""), out var size))
            {
                props = props with { FontSizePoints = size };
            }
        }

        if (styles.TryGetValue("font-weight", out var fontWeight))
        {
            if (fontWeight.Contains("bold", StringComparison.OrdinalIgnoreCase) || fontWeight == "700")
            {
                props = props with { Bold = true };
            }
        }

        if (styles.TryGetValue("font-style", out var fontStyle))
        {
            if (fontStyle.Contains("italic", StringComparison.OrdinalIgnoreCase))
            {
                props = props with { Italic = true };
            }
        }

        if (styles.TryGetValue("text-decoration", out var textDecoration))
        {
            if (textDecoration.Contains("underline", StringComparison.OrdinalIgnoreCase))
            {
                props = props with { Underline = true };
            }
            if (textDecoration.Contains("line-through", StringComparison.OrdinalIgnoreCase))
            {
                props = props with { Strikethrough = true };
            }
        }

        return props;
    }

    static InlineStyle? ParseInlineStyle(IElement element)
    {
        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style))
        {
            return null;
        }

        var styles = ParseStyleAttribute(style);
        var result = new InlineStyle();

        if (styles.TryGetValue("text-align", out var textAlign))
        {
            result.Alignment = textAlign.ToLowerInvariant() switch
            {
                "center" => TextAlignment.Center,
                "right" => TextAlignment.Right,
                "justify" => TextAlignment.Justify,
                _ => TextAlignment.Left
            };
        }

        if (styles.TryGetValue("color", out var color))
        {
            result.Color = NormalizeColor(color);
        }

        return result;
    }

    static Dictionary<string, string> ParseStyleAttribute(string style)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var declarations = style.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var declaration in declarations)
        {
            var colonIndex = declaration.IndexOf(':');
            if (colonIndex > 0)
            {
                var property = declaration[..colonIndex].Trim();
                var value = declaration[(colonIndex + 1)..].Trim();
                result[property] = value;
            }
        }

        return result;
    }

    static void ParseList(IElement listElement, List<DocumentElement> elements, string bulletPrefix, int level = 0)
    {
        foreach (var child in listElement.Children)
        {
            if (!child.TagName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var textContent = new List<string>();
            IElement? nestedList = null;

            foreach (var node in child.ChildNodes)
            {
                if (node is IElement el && (el.TagName.Equals("ul", StringComparison.OrdinalIgnoreCase) ||
                                            el.TagName.Equals("ol", StringComparison.OrdinalIgnoreCase)))
                {
                    nestedList = el;
                }
                else
                {
                    var text = node.TextContent.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        textContent.Add(text);
                    }
                }
            }

            var itemText = string.Join(" ", textContent);
            if (!string.IsNullOrEmpty(itemText))
            {
                var indent = 18 * (level + 1);
                elements.Add(new ParagraphElement
                {
                    Runs = [new() { Text = bulletPrefix + itemText, Properties = new() }],
                    Properties = new() { LeftIndentPoints = indent, SpacingAfterPoints = 4 }
                });
            }

            if (nestedList != null)
            {
                if (nestedList.TagName.Equals("ul", StringComparison.OrdinalIgnoreCase))
                {
                    ParseList(nestedList, elements, "\u25E6 ", level + 1);
                }
                else
                {
                    ParseOrderedList(nestedList, elements, level + 1);
                }
            }
        }
    }

    static void ParseOrderedList(IElement listElement, List<DocumentElement> elements, int level = 0)
    {
        var num = 1;
        foreach (var child in listElement.Children)
        {
            if (!child.TagName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = child.TextContent.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var indent = 18 * (level + 1);
                elements.Add(new ParagraphElement
                {
                    Runs = [new() { Text = $"{num}. {text}", Properties = new() }],
                    Properties = new() { LeftIndentPoints = indent, SpacingAfterPoints = 4 }
                });
                num++;
            }
        }
    }

    static TableElement? ParseTable(IElement tableElement)
    {
        var rows = new List<TableRow>();

        // Parse table-level cellpadding
        CellSpacing? defaultCellPadding = null;
        var cellpadding = tableElement.GetAttribute("cellpadding");
        if (!string.IsNullOrEmpty(cellpadding) && double.TryParse(cellpadding, out var padding))
        {
            defaultCellPadding = new(padding);
        }

        // Parse table-level style for padding
        var tableStyle = tableElement.GetAttribute("style");
        if (!string.IsNullOrEmpty(tableStyle))
        {
            var tablePadding = ParseCssSpacing(tableStyle, "padding");
            if (tablePadding != null)
            {
                defaultCellPadding = tablePadding;
            }
        }

        foreach (var tr in tableElement.QuerySelectorAll("tr"))
        {
            var cells = new List<TableCell>();

            foreach (var cell in tr.Children)
            {
                if (!cell.TagName.Equals("td", StringComparison.OrdinalIgnoreCase) &&
                    !cell.TagName.Equals("th", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isHeader = cell.TagName.Equals("th", StringComparison.OrdinalIgnoreCase);
                var text = cell.TextContent.Trim();

                CellSpacing? cellPadding = null;
                CellSpacing? cellMargin = null;
                var cellStyle = cell.GetAttribute("style");
                if (!string.IsNullOrEmpty(cellStyle))
                {
                    cellPadding = ParseCssSpacing(cellStyle, "padding");
                    cellMargin = ParseCssSpacing(cellStyle, "margin");
                }

                var cellElements = new List<DocumentElement>();
                if (!string.IsNullOrEmpty(text))
                {
                    cellElements.Add(new ParagraphElement
                    {
                        Runs = [new() { Text = text, Properties = new() { Bold = isHeader } }]
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
                rows.Add(new() { Cells = cells });
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
        var styles = ParseStyleAttribute(style);

        // Try shorthand property
        if (styles.TryGetValue(property, out var all))
        {
            if (double.TryParse(all.Replace("px", "").Replace("pt", ""), out var value))
            {
                return new(value);
            }
        }

        // Try individual properties
        double? top = null, right = null, bottom = null, left = null;

        if (styles.TryGetValue($"{property}-top", out var topStr) &&
            double.TryParse(topStr.Replace("px", "").Replace("pt", ""), out var t))
        {
            top = t;
        }

        if (styles.TryGetValue($"{property}-right", out var rightStr) &&
            double.TryParse(rightStr.Replace("px", "").Replace("pt", ""), out var r))
        {
            right = r;
        }

        if (styles.TryGetValue($"{property}-bottom", out var bottomStr) &&
            double.TryParse(bottomStr.Replace("px", "").Replace("pt", ""), out var b))
        {
            bottom = b;
        }

        if (styles.TryGetValue($"{property}-left", out var leftStr) &&
            double.TryParse(leftStr.Replace("px", "").Replace("pt", ""), out var l))
        {
            left = l;
        }

        if (top.HasValue || right.HasValue || bottom.HasValue || left.HasValue)
        {
            return new(top ?? 0, right ?? 0, bottom ?? 0, left ?? 0);
        }

        return null;
    }

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
        if (color.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
        {
            var values = color[4..^1].Split(',');
            if (values.Length == 3 &&
                int.TryParse(values[0].Trim(), out var r) &&
                int.TryParse(values[1].Trim(), out var g) &&
                int.TryParse(values[2].Trim(), out var b))
            {
                return $"{r:X2}{g:X2}{b:X2}";
            }
        }

        return null;
    }

    class InlineStyle
    {
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public string? Color { get; set; }
    }
}
