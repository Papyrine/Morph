/// <summary>
/// Parses HTML content embedded in DOCX via AltChunk.
/// </summary>
sealed class HtmlParser
{
    readonly string defaultFontFamily;

    HtmlParser(string defaultFontFamily) =>
        this.defaultFontFamily = defaultFontFamily;

    RunProperties DefaultRunProps() =>
        new()
        {
            FontFamily = defaultFontFamily
        };

    public static List<DocumentElement> Parse(string html) =>
        Parse(html, "Times New Roman");

    // AngleSharp's parser is reusable (each ParseDocument call builds its own context);
    // constructing one per Parse call re-created its options and factories every time.
    static readonly AngleSharp.Html.Parser.HtmlParser angleSharpParser = new();

    public static List<DocumentElement> Parse(string html, string defaultFontFamily)
    {
        var instance = new HtmlParser(defaultFontFamily);
        var elements = new List<DocumentElement>();
        var document = angleSharpParser.ParseDocument(html);

        var body = document.Body;
        if (body == null)
        {
            return elements;
        }

        instance.ParseNodes(body.ChildNodes, elements);
        return elements;
    }

    /// <summary>
    /// Async version of Parse. Currently delegates to the sync implementation,
    /// but will support async image fetching in the future.
    /// </summary>
    public static Task<List<DocumentElement>> Parse(string html, Cancel cancel) =>
        Parse(html, "Times New Roman", cancel);

    public static Task<List<DocumentElement>> Parse(string html, string defaultFontFamily, Cancel cancel)
    {
        cancel.ThrowIfCancellationRequested();
        return Task.FromResult(Parse(html, defaultFontFamily));
    }

    void ParseNodes(INodeList nodes, List<DocumentElement> elements)
    {
        foreach (var node in nodes)
        {
            ParseNode(node, elements);
        }
    }

    void ParseNode(INode node, List<DocumentElement> elements)
    {
        switch (node)
        {
            case IText textNode:
                if (textNode.TextContent.TryTrim(out var text))
                {
                    elements.Add(
                        new ParagraphElement
                        {
                            Runs =
                            [
                                new()
                                {
                                    Text = text,
                                    Properties = DefaultRunProps()
                                }
                            ]
                        });
                }

                break;

            case IElement element:
                ParseElement(element, elements);
                break;
        }
    }

    void ParseElement(IElement element, List<DocumentElement> elements)
    {
        switch (element.LocalName)
        {
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                var level = int.Parse(element.TagName[1..]);
                var headingPara = CreateParagraph(element, GetHeadingFontSize(level), true, styleId: $"Heading{level}");
                elements.Add(headingPara);
                break;

            case "p":
                var style = ParseInlineStyle(element);
                var para = CreateParagraph(element, 11, false, style);
                elements.Add(para);
                break;

            case "ul":
                ParseList(element, elements);
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
                elements.Add(
                    new ParagraphElement
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = "",
                                Properties = DefaultRunProps()
                            }
                        ],
                        Properties = new()
                        {
                            SpacingAfterPoints = 0
                        }
                    });
                break;

            case "blockquote":
                var bqElements = new List<DocumentElement>();
                ParseNodes(element.ChildNodes, bqElements);
                foreach (var el in bqElements)
                {
                    if (el is ParagraphElement p)
                    {
                        elements.Add(
                            new ParagraphElement
                            {
                                Runs = p.Runs,
                                Properties = p.Properties with
                                {
                                    LeftIndentPoints = p.Properties.LeftIndentPoints + 36
                                }
                            });
                    }
                    else
                    {
                        elements.Add(el);
                    }
                }

                break;

            case "pre":
                elements.Add(
                    new ParagraphElement
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = element.TextContent,
                                Properties = new()
                                {
                                    FontFamily = "Courier New"
                                }
                            }
                        ],
                        Properties = new()
                        {
                            SpacingAfterPoints = 8
                        }
                    });
                break;

            case "hr":
                elements.Add(new HorizontalRuleElement());
                break;

            case "dl":
                ParseDefinitionList(element, elements);
                break;

            case "figure":
                foreach (var child in element.ChildNodes)
                {
                    if (child is IElement childEl &&
                        childEl.TagName.Equals("figcaption", StringComparison.OrdinalIgnoreCase))
                    {
                        var captionRuns = ParseInlineElements(
                            childEl,
                            DefaultRunProps() with
                            {
                                FontSizePoints = 11,
                                Italic = true
                            });
                        elements.Add(
                            new ParagraphElement
                            {
                                Runs = captionRuns.Count > 0
                                    ? captionRuns
                                    :
                                    [
                                        new()
                                        {
                                            Text = "",
                                            Properties = DefaultRunProps() with
                                            {
                                                FontSizePoints = 11,
                                                Italic = true
                                            }
                                        }
                                    ],
                                Properties = new()
                                {
                                    SpacingAfterPoints = 8
                                }
                            });
                    }
                    else
                    {
                        ParseNode(child, elements);
                    }
                }

                break;

            case "figcaption":
                var figRuns = ParseInlineElements(
                    element,
                    DefaultRunProps() with
                    {
                        FontSizePoints = 11,
                        Italic = true
                    });
                elements.Add(
                    new ParagraphElement
                    {
                        Runs = figRuns.Count > 0
                            ? figRuns
                            :
                            [
                                new()
                                {
                                    Text = "",
                                    Properties = DefaultRunProps() with
                                    {
                                        FontSizePoints = 11,
                                        Italic = true
                                    }
                                }
                            ],
                        Properties = new()
                        {
                            SpacingAfterPoints = 8
                        }
                    });
                break;

            case "img":
                var imgElement = ParseImgElement(element);
                if (imgElement != null)
                {
                    elements.Add(imgElement);
                }

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
                if (!string.IsNullOrWhiteSpace(element.TextContent))
                {
                    var defaultPara = CreateParagraph(element, 11, false);
                    elements.Add(defaultPara);
                }

                break;
        }
    }

    ParagraphElement CreateParagraph(IElement element, double fontSize, bool bold, InlineStyle? style = null, string? styleId = null)
    {
        var runs = ParseInlineElements(
            element,
            DefaultRunProps() with
            {
                FontSizePoints = fontSize,
                Bold = bold,
                ColorHex = style?.Color
            });

        return new()
        {
            Runs = runs.Count > 0
                ? runs
                :
                [
                    new()
                    {
                        Text = "",
                        Properties = DefaultRunProps() with
                        {
                            FontSizePoints = fontSize
                        }
                    }
                ],
            Properties = new()
            {
                Alignment = style?.Alignment ?? TextAlignment.Left,
                SpacingAfterPoints = fontSize > 14 ? 12 : 8,
                FirstLineIndentPoints = style?.TextIndent ?? 0,
                LineSpacingMultiplier = style?.LineHeight ?? 1.08,
                StyleId = styleId
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
                        runs.Add(
                            new()
                            {
                                Text = text,
                                Properties = props
                            });
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
        var childNodes = element.ChildNodes;
        switch (element.LocalName)
        {
            case "b":
            case "strong":
                ParseInlineNodes(childNodes, runs, props with
                {
                    Bold = true
                });
                break;

            case "i":
            case "em":
                ParseInlineNodes(childNodes, runs, props with
                {
                    Italic = true
                });
                break;

            case "u":
                ParseInlineNodes(childNodes, runs, props with
                {
                    Underline = true
                });
                break;

            case "s":
            case "strike":
            case "del":
                ParseInlineNodes(childNodes, runs, props with
                {
                    Strikethrough = true
                });
                break;

            case "font":
                var fontProps = ParseFontElement(element, props);
                ParseInlineNodes(childNodes, runs, fontProps);
                break;

            case "span":
                var spanProps = ParseSpanStyle(element, props);
                ParseInlineNodes(childNodes, runs, spanProps);
                break;

            case "a":
                // Render links as blue underlined text
                ParseInlineNodes(childNodes, runs, props with
                {
                    ColorHex = "0000FF",
                    Underline = true
                });
                break;

            case "br":
                runs.Add(
                    new()
                    {
                        Text = "\n",
                        Properties = props
                    });
                break;

            case "sub":
            case "sup":
                // Render sub/sup as smaller text
                ParseInlineNodes(
                    childNodes,
                    runs,
                    props with
                    {
                        FontSizePoints = props.FontSizePoints * 0.7
                    });
                break;

            case "mark":
                ParseInlineNodes(
                    childNodes,
                    runs,
                    props with
                    {
                        BackgroundColorHex = "FFFF00"
                    });
                break;

            case "small":
                ParseInlineNodes(
                    childNodes,
                    runs,
                    props with
                    {
                        FontSizePoints = props.FontSizePoints * 0.8
                    });
                break;

            case "code":
                ParseInlineNodes(
                    childNodes,
                    runs,
                    props with
                    {
                        FontFamily = "Courier New"
                    });
                break;

            case "img":
                var (imgData, imgContentType) = ParseDataUri(element.GetAttribute("src") ?? "");
                if (imgData != null)
                {
                    var imgWidth = ParseDimensionAttribute(element, "width") ?? 100;
                    var imgHeight = ParseDimensionAttribute(element, "height") ?? 100;
                    runs.Add(
                        new()
                        {
                            Text = "",
                            Properties = props,
                            InlineImageData = imgData,
                            InlineImageWidthPoints = imgWidth,
                            InlineImageHeightPoints = imgHeight,
                            InlineImageContentType = imgContentType
                        });
                }

                break;

            default:
                // Process children for unknown inline elements
                ParseInlineNodes(childNodes, runs, props);
                break;
        }
    }

    static RunProperties ParseFontElement(IElement element, RunProperties baseProps)
    {
        var props = baseProps;

        var face = element.GetAttribute("face");
        if (!string.IsNullOrEmpty(face))
        {
            props = props with
            {
                FontFamily = face
            };
        }

        var color = element.GetAttribute("color");
        if (!string.IsNullOrEmpty(color))
        {
            props = props with
            {
                ColorHex = NormalizeColor(color)
            };
        }

        var size = element.GetAttribute("size");
        if (!string.IsNullOrEmpty(size) &&
            int.TryParse(size, out var sizeValue))
        {
            double[] fontSizes = [8, 10, 12, 14, 18, 24, 36];
            var idx = Math.Clamp(sizeValue - 1, 0, 6);
            props = props with
            {
                FontSizePoints = fontSizes[idx]
            };
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
            props = props with
            {
                ColorHex = NormalizeColor(color)
            };
        }

        if (styles.TryGetValue("font-family", out var fontFamily))
        {
            props = props with
            {
                FontFamily = fontFamily.Trim('\'', '"')
            };
        }

        if (styles.TryGetValue("font-size", out var fontSize))
        {
            if (TryParseCssDimension(fontSize, out var size))
            {
                props = props with
                {
                    FontSizePoints = size
                };
            }
        }

        if (styles.TryGetValue("font-weight", out var fontWeight))
        {
            if (fontWeight.Contains("bold", StringComparison.OrdinalIgnoreCase) || fontWeight == "700")
            {
                props = props with
                {
                    Bold = true
                };
            }
        }

        if (styles.TryGetValue("font-style", out var fontStyle))
        {
            if (fontStyle.Contains("italic", StringComparison.OrdinalIgnoreCase))
            {
                props = props with
                {
                    Italic = true
                };
            }
        }

        if (styles.TryGetValue("text-decoration", out var textDecoration))
        {
            if (textDecoration.Contains("underline", StringComparison.OrdinalIgnoreCase))
            {
                props = props with
                {
                    Underline = true
                };
            }

            if (textDecoration.Contains("line-through", StringComparison.OrdinalIgnoreCase))
            {
                props = props with
                {
                    Strikethrough = true
                };
            }
        }

        if (styles.TryGetValue("background-color", out var bgColor))
        {
            props = props with
            {
                BackgroundColorHex = NormalizeColor(bgColor)
            };
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

        if (styles.TryGetValue("text-indent", out var textIndent))
        {
            if (TryParseCssDimension(textIndent, out var indentValue))
            {
                result.TextIndent = indentValue;
            }
        }

        if (styles.TryGetValue("line-height", out var lineHeight))
        {
            if (TryParseCssDimension(lineHeight, out var lhValue))
            {
                result.LineHeight = lhValue;
            }
        }

        return result;
    }

    static Dictionary<string, string> ParseStyleAttribute(string style)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var remaining = style.AsSpan();

        while (!remaining.IsEmpty)
        {
            ReadOnlySpan<char> declaration;
            var semicolon = remaining.IndexOf(';');
            if (semicolon < 0)
            {
                declaration = remaining;
                remaining = default;
            }
            else
            {
                declaration = remaining[..semicolon];
                remaining = remaining[(semicolon + 1)..];
            }

            var colonIndex = declaration.IndexOf(':');
            if (colonIndex > 0)
            {
                var property = declaration[..colonIndex].Trim();
                var value = declaration[(colonIndex + 1)..].Trim();
                result[property.ToString()] = value.ToString();
            }
        }

        return result;
    }

    void ParseList(IElement listElement, List<DocumentElement> elements, int level = 0)
    {
        foreach (var child in listElement.Children)
        {
            if (!child.TagName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (itemText, nestedList) = SplitListItem(child);
            if (!string.IsNullOrEmpty(itemText))
            {
                // Word's own HTML import uses \u2022 at the first level and an open bullet deeper.
                elements.Add(ListItemParagraph(itemText, level == 0 ? "\u2022" : "\u25E6", level));
            }

            if (nestedList != null)
            {
                ParseNestedList(nestedList, elements, level + 1);
            }
        }
    }

    void ParseOrderedList(IElement listElement, List<DocumentElement> elements, int level = 0)
    {
        // <ol start="5"> shifts the first ordinal; absent or invalid values start at 1.
        var number = 1;
        if (int.TryParse(listElement.GetAttribute("start"), out var start))
        {
            number = start;
        }

        foreach (var child in listElement.Children)
        {
            if (!child.TagName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (itemText, nestedList) = SplitListItem(child);
            if (!string.IsNullOrEmpty(itemText))
            {
                elements.Add(ListItemParagraph(itemText, $"{number}.", level));
                number++;
            }

            if (nestedList != null)
            {
                ParseNestedList(nestedList, elements, level + 1);
            }
        }
    }

    void ParseNestedList(IElement nestedList, List<DocumentElement> elements, int level)
    {
        if (nestedList.TagName.Equals("ul", StringComparison.OrdinalIgnoreCase))
        {
            ParseList(nestedList, elements, level);
        }
        else
        {
            ParseOrderedList(nestedList, elements, level);
        }
    }

    /// <summary>
    /// Splits an <c>&lt;li&gt;</c> into its own text and the trailing nested list element (if
    /// any), so nested <c>&lt;ul&gt;</c>/<c>&lt;ol&gt;</c> content becomes child items instead
    /// of being flattened into the parent item's text.
    /// </summary>
    static (string Text, IElement? NestedList) SplitListItem(IElement listItem)
    {
        var textContent = new List<string>();
        IElement? nestedList = null;

        foreach (var node in listItem.ChildNodes)
        {
            if (node is IElement childElement &&
                (childElement.TagName.Equals("ul", StringComparison.OrdinalIgnoreCase) ||
                 childElement.TagName.Equals("ol", StringComparison.OrdinalIgnoreCase)))
            {
                nestedList = childElement;
            }
            else
            {
                if (node.TextContent.TryTrim(out var text))
                {
                    textContent.Add(text);
                }
            }
        }

        return (string.Join(' ', textContent), nestedList);
    }

    /// <summary>
    /// A list-item paragraph with real <see cref="NumberingInfo"/> \u2014 marker text, w:ilvl-style
    /// level and Word's standard list geometry (text at 36pt per level, marker hanging 18pt to
    /// its left). Modelling the marker as numbering rather than baking it into the run text lets
    /// the HTML/Markdown exporters reconstruct genuine lists and matches how Word itself imports
    /// HTML list content.
    /// </summary>
    ParagraphElement ListItemParagraph(string text, string marker, int level)
    {
        var textIndent = 36 * (level + 1);
        const double hangingIndent = 18;
        return new()
        {
            Runs =
            [
                new()
                {
                    Text = text,
                    Properties = DefaultRunProps()
                }
            ],
            Properties = new()
            {
                LeftIndentPoints = textIndent,
                HangingIndentPoints = hangingIndent,
                SpacingAfterPoints = 4,
                Numbering = new()
                {
                    Text = marker,
                    Level = level,
                    IndentPoints = textIndent,
                    HangingIndentPoints = hangingIndent
                }
            }
        };
    }

    TableElement? ParseTable(IElement tableElement)
    {
        var rows = new List<TableRow>();

        // Parse table-level cellpadding
        CellSpacing? defaultCellPadding = null;
        var cellpadding = tableElement.GetAttribute("cellpadding");
        if (!string.IsNullOrEmpty(cellpadding) &&
            double.TryParse(cellpadding, out var padding))
        {
            defaultCellPadding = new(padding);
        }

        // Parse borders from border attribute
        var defaultBorders = CellBorders.All;
        var borderAttribute = tableElement.GetAttribute("border");
        if (!string.IsNullOrEmpty(borderAttribute) &&
            double.TryParse(borderAttribute, out var borderWidth))
        {
            if (borderWidth > 0)
            {
                var borderPt = borderWidth * 0.75;
                var edge = new BorderEdge
                {
                    IsVisible = true,
                    WidthPoints = borderPt,
                    ColorHex = "000000"
                };
                defaultBorders = new()
                {
                    Top = edge,
                    Right = edge,
                    Bottom = edge,
                    Left = edge
                };
            }
            else
            {
                defaultBorders = new();
            }
        }

        // Parse table-level style for padding and border CSS
        var tableStyle = tableElement.GetAttribute("style");
        if (!string.IsNullOrEmpty(tableStyle))
        {
            var tableStyles = ParseStyleAttribute(tableStyle);
            var tablePadding = ParseCssSpacing(tableStyles, "padding");
            if (tablePadding != null)
            {
                defaultCellPadding = tablePadding;
            }

            if (tableStyles.TryGetValue("border", out var cssBorder))
            {
                var parsed = ParseCssBorderShorthand(cssBorder);
                if (parsed != null)
                {
                    defaultBorders = parsed;
                }
            }
        }

        // Track active rowspans: column index -> remaining rows
        var activeRowspans = new Dictionary<int, int>();

        foreach (var tr in DirectRows(tableElement))
        {
            var cells = new List<TableCell>();
            var newRowspans = new Dictionary<int, int>();
            var colIndex = 0;

            foreach (var cell in tr.Children)
            {
                var tag = cell.TagName;
                if (!tag.Equals("td", StringComparison.OrdinalIgnoreCase) &&
                    !tag.Equals("th", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Insert Continue cells for active rowspans
                while (activeRowspans.ContainsKey(colIndex))
                {
                    cells.Add(
                        new()
                        {
                            Content = [],
                            Properties = new()
                            {
                                VerticalMerge = VerticalMergeType.Continue
                            }
                        });
                    colIndex++;
                }

                var isHeader = tag.Equals("th", StringComparison.OrdinalIgnoreCase);

                CellSpacing? cellPadding = null;
                CellSpacing? cellMargin = null;
                string? cellBgColor = null;
                var cellStyle = cell.GetAttribute("style");
                if (!string.IsNullOrEmpty(cellStyle))
                {
                    var cellStyles = ParseStyleAttribute(cellStyle);
                    cellPadding = ParseCssSpacing(cellStyles, "padding");
                    cellMargin = ParseCssSpacing(cellStyles, "margin");
                    if (cellStyles.TryGetValue("background-color", out var bg))
                    {
                        cellBgColor = NormalizeColor(bg);
                    }
                }

                // Handle colspan
                var gridSpan = 1;
                var colspanAttribute = cell.GetAttribute("colspan");
                if (!string.IsNullOrEmpty(colspanAttribute) &&
                    int.TryParse(colspanAttribute, out var cs) &&
                    cs > 1)
                {
                    gridSpan = cs;
                }

                // Handle rowspan
                var verticalMerge = VerticalMergeType.None;
                var rowspanAttribute = cell.GetAttribute("rowspan");
                if (!string.IsNullOrEmpty(rowspanAttribute) &&
                    int.TryParse(rowspanAttribute, out var rs) &&
                    rs > 1)
                {
                    verticalMerge = VerticalMergeType.Restart;
                    newRowspans[colIndex] = rs - 1;
                }

                var cellElements = new List<DocumentElement>();
                if (cell.TextContent.TryTrim(out var text))
                {
                    cellElements.Add(
                        new ParagraphElement
                        {
                            Runs =
                            [
                                new()
                                {
                                    Text = text,
                                    Properties = DefaultRunProps() with
                                    {
                                        Bold = isHeader
                                    }
                                }
                            ]
                        });
                }

                cells.Add(
                    new()
                    {
                        Content = cellElements,
                        Properties = new()
                        {
                            Padding = cellPadding,
                            Margin = cellMargin,
                            BackgroundColorHex = cellBgColor,
                            GridSpan = gridSpan,
                            VerticalMerge = verticalMerge
                        }
                    });

                colIndex += gridSpan;
            }

            // Insert trailing Continue cells for active rowspans
            while (activeRowspans.ContainsKey(colIndex))
            {
                cells.Add(
                    new()
                    {
                        Content = [],
                        Properties = new()
                        {
                            VerticalMerge = VerticalMergeType.Continue
                        }
                    });
                colIndex++;
            }

            // Update active rowspans for next row
            var nextRowspans = new Dictionary<int, int>();
            foreach (var kvp in activeRowspans)
            {
                var remaining = kvp.Value - 1;
                if (remaining > 0)
                {
                    nextRowspans[kvp.Key] = remaining;
                }
            }

            foreach (var kvp in newRowspans)
            {
                nextRowspans[kvp.Key] = kvp.Value;
            }

            activeRowspans = nextRowspans;

            if (cells.Count > 0)
            {
                rows.Add(
                    new()
                    {
                        Cells = cells
                    });
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
                DefaultBorders = defaultBorders,
                DefaultCellPadding = defaultCellPadding ?? new CellSpacing()
            }
        };
    }

    // Direct structural rows only: QuerySelectorAll("tr") also matched the rows of NESTED
    // tables, duplicating their content into the outer table. Rows sit either directly under
    // the table element or under its thead/tbody/tfoot sections.
    static IEnumerable<IElement> DirectRows(IElement tableElement)
    {
        foreach (var child in tableElement.Children)
        {
            if (child.LocalName == "tr")
            {
                yield return child;
            }
            else if (child.LocalName is "thead" or "tbody" or "tfoot")
            {
                foreach (var row in child.Children)
                {
                    if (row.LocalName == "tr")
                    {
                        yield return row;
                    }
                }
            }
        }
    }

    static CellSpacing? ParseCssSpacing(string style, string property) =>
        ParseCssSpacing(ParseStyleAttribute(style), property);

    // Overload for callers that already parsed the style attribute — a table cell's style
    // used to be tokenized three times (padding, margin, then the general lookup).
    static CellSpacing? ParseCssSpacing(Dictionary<string, string> styles, string property)
    {
        // Try shorthand property
        if (styles.TryGetValue(property, out var all))
        {
            if (TryParseCssDimension(all, out var value))
            {
                return new(value);
            }
        }

        // Try individual properties
        double? top = null, right = null, bottom = null, left = null;

        if (styles.TryGetValue($"{property}-top", out var topStr) &&
            TryParseCssDimension(topStr, out var topValue))
        {
            top = topValue;
        }

        if (styles.TryGetValue($"{property}-right", out var rightStr) &&
            TryParseCssDimension(rightStr, out var rightValue))
        {
            right = rightValue;
        }

        if (styles.TryGetValue($"{property}-bottom", out var bottomStr) &&
            TryParseCssDimension(bottomStr, out var bottomValue))
        {
            bottom = bottomValue;
        }

        if (styles.TryGetValue($"{property}-left", out var leftStr) &&
            TryParseCssDimension(leftStr, out var leftValue))
        {
            left = leftValue;
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

    static readonly Dictionary<string, string> namedColors = new(StringComparer.OrdinalIgnoreCase)
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

    static string? NormalizeColor(string color)
    {
        if (string.IsNullOrEmpty(color))
        {
            return null;
        }

        color = color.Trim();

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
            var inner = color.AsSpan()[4..^1];
            Span<Range> ranges = stackalloc Range[4];
            if (inner.Split(ranges, ',') == 3 &&
                int.TryParse(inner[ranges[0]].Trim(), out var r) &&
                int.TryParse(inner[ranges[1]].Trim(), out var g) &&
                int.TryParse(inner[ranges[2]].Trim(), out var b))
            {
                return $"{r:X2}{g:X2}{b:X2}";
            }
        }

        return null;
    }

    void ParseDefinitionList(IElement element, List<DocumentElement> elements)
    {
        foreach (var child in element.Children)
        {
            switch (child.LocalName)
            {
                case "dt":
                    elements.Add(CreateParagraph(child, 11, true));
                    break;
                case "dd":
                    var ddPara = CreateParagraph(child, 11, false);
                    elements.Add(new ParagraphElement
                    {
                        Runs = ddPara.Runs,
                        Properties = ddPara.Properties with
                        {
                            LeftIndentPoints = 36
                        }
                    });
                    break;
            }
        }
    }

    static ImageElement? ParseImgElement(IElement element)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrEmpty(src) ||
            !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var (data, contentType) = ParseDataUri(src);
        if (data == null)
        {
            return null;
        }

        var width = ParseDimensionAttribute(element, "width") ?? 100;
        var height = ParseDimensionAttribute(element, "height") ?? 100;

        return new()
        {
            ImageData = data,
            WidthPoints = width,
            HeightPoints = height,
            ContentType = contentType
        };
    }

    static (byte[]? Data, string? ContentType) ParseDataUri(string src)
    {
        if (string.IsNullOrEmpty(src) ||
            !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var commaIndex = src.IndexOf(',');
        if (commaIndex < 0)
        {
            return (null, null);
        }

        var meta = src[5..commaIndex];
        string? contentType;
        var semiIndex = meta.IndexOf(';');
        if (semiIndex >= 0)
        {
            contentType = meta[..semiIndex];
        }
        else
        {
            contentType = meta;
        }

        try
        {
            var data = Convert.FromBase64String(src[(commaIndex + 1)..]);
            return (data, contentType);
        }
        catch
        {
            return (null, null);
        }
    }

    static double? ParseDimensionAttribute(IElement element, string attribute)
    {
        var value = element.GetAttribute(attribute);
        if (!string.IsNullOrEmpty(value) &&
            TryParseCssDimension(value, out var result))
        {
            return result;
        }

        return null;
    }

    static bool TryParseCssDimension(ReadOnlySpan<char> value, out double result)
    {
        var span = value.Trim();
        if (span.EndsWith("px", StringComparison.OrdinalIgnoreCase) ||
            span.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            span = span[..^2].TrimEnd();
        }

        return double.TryParse(span, out result);
    }

    static CellBorders? ParseCssBorderShorthand(string value)
    {
        var trimmed = value.AsSpan().Trim();
        if (trimmed.IsEmpty)
        {
            return null;
        }

        var widthPt = 0.75;
        var color = "000000";

        foreach (var partRange in trimmed.Split(' '))
        {
            var part = trimmed[partRange];
            if (part.IsEmpty)
            {
                continue;
            }

            if (part.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseCssDimension(part, out var px))
                {
                    widthPt = px * 0.75;
                }
            }
            else if (part.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseCssDimension(part, out var pt))
                {
                    widthPt = pt;
                }
            }
            else if (part is "solid" || part is "dashed" ||
                     part is "dotted" || part is "double" ||
                     part is "groove" || part is "ridge" ||
                     part is "inset" || part is "outset")
            {
                // Style token — skip (we treat all visible styles the same)
            }
            else if (part is "none")
            {
                return new();
            }
            else
            {
                var normalized = NormalizeColor(part.ToString());
                if (normalized != null)
                {
                    color = normalized;
                }
            }
        }

        var edge = new BorderEdge
        {
            IsVisible = true,
            WidthPoints = widthPt,
            ColorHex = color
        };
        return new()
        {
            Top = edge,
            Right = edge,
            Bottom = edge,
            Left = edge
        };
    }

    class InlineStyle
    {
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public string? Color { get; set; }
        public double? TextIndent { get; set; }
        public double? LineHeight { get; set; }
    }
}
