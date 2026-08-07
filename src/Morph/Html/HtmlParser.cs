/// <summary>
/// Parses HTML content embedded in DOCX via AltChunk.
/// </summary>
sealed class HtmlParser
{
    readonly string defaultFontFamily;
    readonly string containerFontFamily;

    HtmlParser(string defaultFontFamily, string containerFontFamily)
    {
        this.defaultFontFamily = defaultFontFamily;
        this.containerFontFamily = containerFontFamily;
    }

    RunProperties DefaultRunProps() =>
        new()
        {
            FontFamily = defaultFontFamily
        };

    // Table cells and list items take the HOST document's default font, not the browser-default
    // serif that body paragraphs use. Probed against Word (one AltChunk carrying the same word in a
    // paragraph, a cell, a ul, an ol and a heading): the paragraph and heading render Times New
    // Roman while the cell and both list items render the destination document's Normal font (Aptos
    // where the package declares no styles part). For standalone HTML input there is no host
    // document, so the container font defaults to the body font and nothing changes.
    RunProperties ContainerRunProps() =>
        new()
        {
            FontFamily = containerFontFamily
        };

    public static List<DocumentElement> Parse(string html) =>
        Parse(html, "Times New Roman");

    // AngleSharp's parser is reusable (each ParseDocument call builds its own context);
    // constructing one per Parse call re-created its options and factories every time.
    static readonly AngleSharp.Html.Parser.HtmlParser angleSharpParser = new();

    // CSS pixels to points, for the HTML attributes that count pixels (img width/height,
    // cellpadding, border).
    const double pixelsToPoints = 0.75;

    // The grey Word rules a legacy border="n" table with. Measured off a Word probe at 150 DPI:
    // each rule lays 120 units of ink over two anti-aliased pixel rows, which at 0.75pt is a solid
    // value of ~B2. Not black — that is a browser's rendering of the attribute, not Word's.
    const string htmlTableBorderColor = "B2B2B2";

    public static List<DocumentElement> Parse(string html, string defaultFontFamily) =>
        Parse(html, defaultFontFamily, defaultFontFamily);

    // containerFontFamily is the HOST document's default, applied to table cells and list items;
    // AltChunk passes it so imported tables and lists match Word's destination-document font while
    // body text keeps the browser-default serif. Standalone callers omit it and get body = container.
    public static List<DocumentElement> Parse(string html, string defaultFontFamily, string containerFontFamily)
    {
        var instance = new HtmlParser(defaultFontFamily, containerFontFamily);
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
                    elements.Add(new ParagraphElement
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
                var headingStyle = ParseInlineStyle(element);
                var headingPara = CreateParagraph(element, GetHeadingFontSize(level), true, headingStyle, styleId: $"Heading{level}");
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
                elements.Add(new ParagraphElement
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
                        elements.Add(new ParagraphElement
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
                elements.Add(new ParagraphElement
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
                        elements.Add(new ParagraphElement
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
                elements.Add(new ParagraphElement
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
                ParseContainer(element, elements);
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

    void ParseContainer(IElement element, List<DocumentElement> elements)
    {
        var background = ContainerBackgroundColor(element);
        if (background == null)
        {
            ParseNodes(element.ChildNodes, elements);
            return;
        }

        // A container carrying its own background-color (e.g. <div style="background-color:#E0E0E0">)
        // is paragraph shading in Word — a full-width band behind everything it wraps. Morph has no
        // block-box model, so approximate by pushing the fill onto each child paragraph that doesn't
        // already declare one of its own.
        var children = new List<DocumentElement>();
        ParseNodes(element.ChildNodes, children);
        foreach (var child in children)
        {
            if (child is ParagraphElement { Properties.BackgroundColorHex: null } para)
            {
                elements.Add(new ParagraphElement
                {
                    Runs = para.Runs,
                    Properties = para.Properties with
                    {
                        BackgroundColorHex = background
                    },
                    IsAnchorOnlyMark = para.IsAnchorOnlyMark,
                    IsCollapsedCellMark = para.IsCollapsedCellMark
                });
            }
            else
            {
                elements.Add(child);
            }
        }
    }

    static string? ContainerBackgroundColor(IElement element)
    {
        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style))
        {
            return null;
        }

        return ParseStyleAttribute(style).TryGetValue("background-color", out var background)
            ? NormalizeColor(background)
            : null;
    }

    ParagraphElement CreateParagraph(IElement element, double fontSize, bool bold, InlineStyle? style = null, string? styleId = null)
    {
        // Base run props: the heading size/bold, then the block element's OWN inline character
        // styles (font-size, font-family, font-weight/style, text-decoration, color) applied on
        // top — <p style="font-size:18pt"> is a block element, so those declarations never reach
        // the character path that only fires for <span>/<font>. Its background-color becomes
        // full-width paragraph shading below, so it is cleared here to avoid a redundant
        // glyph-tight highlight painted on top of the band.
        var baseProps = ParseSpanStyle(
            element,
            DefaultRunProps() with
            {
                FontSizePoints = fontSize,
                Bold = bold
            }) with
        {
            BackgroundColorHex = null
        };

        var runs = ParseInlineElements(element, baseProps);

        return new()
        {
            Runs = runs.Count > 0
                ? runs
                : [new() { Text = "", Properties = baseProps }],
            Properties = new()
            {
                Alignment = style?.Alignment ?? TextAlignment.Left,
                // Word's AltChunk HTML import spaces block paragraphs ~14pt apart (measured
                // <p> pitch ≈ 57px at 150 DPI vs a ~29px line box); 8pt packed them ~6pt too
                // tight, so every band/line drifted up the page. Headings keep their own value.
                SpacingAfterPoints = baseProps.FontSizePoints > 14 ? 12 : 14,
                FirstLineIndentPoints = style?.TextIndent ?? 0,
                LineSpacingMultiplier = style?.LineHeight ?? 1.08,
                BackgroundColorHex = style?.BackgroundColor,
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
        switch (element.LocalName)
        {
            case "b":
            case "strong":
                ParseInlineNodes(element.ChildNodes, runs, props with
                {
                    Bold = true
                });
                break;

            case "i":
            case "em":
                ParseInlineNodes(element.ChildNodes, runs, props with
                {
                    Italic = true
                });
                break;

            case "u":
                ParseInlineNodes(element.ChildNodes, runs, props with
                {
                    Underline = true
                });
                break;

            case "s":
            case "strike":
            case "del":
                ParseInlineNodes(element.ChildNodes, runs, props with
                {
                    Strikethrough = true
                });
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
                ParseInlineNodes(element.ChildNodes, runs, props with
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
                    element.ChildNodes,
                    runs,
                    props with
                    {
                        FontSizePoints = props.FontSizePoints * 0.7
                    });
                break;

            case "mark":
                ParseInlineNodes(
                    element.ChildNodes,
                    runs,
                    props with
                    {
                        BackgroundColorHex = "FFFF00"
                    });
                break;

            case "small":
                ParseInlineNodes(
                    element.ChildNodes,
                    runs,
                    props with
                    {
                        FontSizePoints = props.FontSizePoints * 0.8
                    });
                break;

            case "code":
                ParseInlineNodes(
                    element.ChildNodes,
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

    // CSS font-family is a comma-separated fallback list whose names may be quoted, e.g.
    // 'Times New Roman', serif. Take the first family and strip the quotes — handing the whole
    // list to the font loader throws (it treats "Times New Roman', serif" as one missing name).
    static string FirstFontFamily(string value) =>
        value.Split(',')[0].Trim().Trim('\'', '"').Trim();

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
                FontFamily = FirstFontFamily(fontFamily)
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

        // A block element's background-color (e.g. <p style="background-color:#FFFFCC">) is
        // Word's paragraph shading (w:shd) — a full-width band behind the whole paragraph, not
        // the glyph-tight run highlight a <span> background produces.
        if (styles.TryGetValue("background-color", out var backgroundColor))
        {
            result.BackgroundColor = NormalizeColor(backgroundColor);
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
                // Word's own HTML import cycles three bullet glyphs by depth \u2014 filled round, open
                // round, filled square \u2014 and repeats from there. Stopping at the open bullet drew
                // html_nested_lists' third level (Item 1.2.1 / 1.2.2) as hollow circles where Word
                // shows small filled squares.
                elements.Add(ListItemParagraph(itemText, BulletForLevel(level), level));
            }

            if (nestedList != null)
            {
                ParseNestedList(nestedList, elements, level + 1);
            }
        }
    }

    // •  U+2022 filled round, ◦ U+25E6 open round, ▪ U+25AA filled square — Word's HTML-import
    // bullet cycle, repeating every three levels.
    static string BulletForLevel(int level) => (level % 3) switch
    {
        0 => "•",
        1 => "◦",
        _ => "▪"
    };

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
                    Properties = ContainerRunProps()
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

        // Parse table-level cellpadding. The attribute counts CSS PIXELS, like the img width and
        // height attributes, so it converts at 0.75 the same way — read as points it inset cell
        // text by a third too much (cellpadding=15 measured 33px against Word's 27 at 150 DPI).
        // Applied below, once it is known whether the table fills its container.
        CellSpacing? defaultCellPadding = null;
        double? cellPaddingPixels = null;
        var cellpadding = tableElement.GetAttribute("cellpadding");
        if (!string.IsNullOrEmpty(cellpadding) &&
            double.TryParse(cellpadding, out var padding))
        {
            cellPaddingPixels = padding;
            defaultCellPadding = new(padding);
        }

        // No border attribute means NO rules — an HTML table is borderless unless it asks for
        // borders, and Word honours that. Probed with a filled table carrying no `border`: the cell
        // fills render with the usual cellspacing gaps between them and not one rule is drawn,
        // grey or otherwise. Defaulting to CellBorders.All drew an outer box around every
        // borderless HTML table (`html_table`).
        var defaultBorders = new CellBorders();
        var borderWidthPoints = 0.0;
        var borderAttribute = tableElement.GetAttribute("border");
        if (!string.IsNullOrEmpty(borderAttribute) &&
            double.TryParse(borderAttribute, out var borderWidth))
        {
            if (borderWidth > 0)
            {
                var borderPt = borderWidth * pixelsToPoints;
                borderWidthPoints = borderPt;
                var edge = new BorderEdge
                {
                    IsVisible = true,
                    WidthPoints = borderPt,
                    ColorHex = htmlTableBorderColor
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

        // Parse table-level style for padding, border and width CSS
        double? preferredWidthPoints = null;
        var fillContainer = false;
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
                    borderWidthPoints = parsed.Left.WidthPoints;
                }
            }

            // width: 100% fills the container (drives the autofit scale-up); a px/pt width
            // becomes the preferred width. Fractional percentages have no table-level model
            // slot, so any near-full percentage maps to fill-the-container.
            if (tableStyles.TryGetValue("width", out var cssWidth))
            {
                var widthValue = cssWidth.Trim();
                if (widthValue.EndsWith('%'))
                {
                    if (double.TryParse(widthValue[..^1], out var percent) && percent >= 99)
                    {
                        fillContainer = true;
                    }
                }
                else if (TryParseCssLengthToPoints(widthValue, out var widthPoints))
                {
                    preferredWidthPoints = widthPoints;
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

            // Row-level style cascades onto the row's cells: <tr style="background-color: ...;
            // color: ..."> is how HTML tables paint header fills and zebra striping.
            string? rowBackgroundColor = null;
            string? rowTextColor = null;
            var rowStyleAttribute = tr.GetAttribute("style");
            if (!string.IsNullOrEmpty(rowStyleAttribute))
            {
                var rowStyles = ParseStyleAttribute(rowStyleAttribute);
                if (rowStyles.TryGetValue("background-color", out var rowBg))
                {
                    rowBackgroundColor = NormalizeColor(rowBg);
                }

                if (rowStyles.TryGetValue("color", out var rowFg))
                {
                    rowTextColor = NormalizeColor(rowFg);
                }
            }

            foreach (var cell in tr.Children)
            {
                if (!cell.TagName.Equals("td", StringComparison.OrdinalIgnoreCase) &&
                    !cell.TagName.Equals("th", StringComparison.OrdinalIgnoreCase))
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

                var isHeader = cell.TagName.Equals("th", StringComparison.OrdinalIgnoreCase);

                CellSpacing? cellPadding = null;
                CellSpacing? cellMargin = null;
                var cellBgColor = rowBackgroundColor;
                var cellTextColor = rowTextColor;
                TextAlignment? cellAlignment = null;
                double? cellWidthPoints = null;
                double? cellWidthFraction = null;
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

                    if (cellStyles.TryGetValue("color", out var fg))
                    {
                        cellTextColor = NormalizeColor(fg);
                    }

                    if (cellStyles.TryGetValue("text-align", out var textAlign))
                    {
                        cellAlignment = textAlign.ToLowerInvariant() switch
                        {
                            "center" => TextAlignment.Center,
                            "right" => TextAlignment.Right,
                            "justify" => TextAlignment.Justify,
                            _ => TextAlignment.Left
                        };
                    }

                    if (cellStyles.TryGetValue("width", out var cssWidth))
                    {
                        var widthValue = cssWidth.Trim();
                        if (widthValue.EndsWith('%'))
                        {
                            if (double.TryParse(widthValue[..^1], out var percent) && percent > 0)
                            {
                                cellWidthFraction = percent / 100;
                            }
                        }
                        else if (TryParseCssLengthToPoints(widthValue, out var widthPoints))
                        {
                            cellWidthPoints = widthPoints;
                        }
                    }
                }

                // Legacy <td width="..."> attribute: a bare number is pixels.
                if (cellWidthPoints == null && cellWidthFraction == null &&
                    cell.GetAttribute("width") is {Length: > 0} widthAttribute)
                {
                    var attributeValue = widthAttribute.Trim();
                    if (attributeValue.EndsWith('%'))
                    {
                        if (double.TryParse(attributeValue[..^1], out var percent) && percent > 0)
                        {
                            cellWidthFraction = percent / 100;
                        }
                    }
                    else if (double.TryParse(attributeValue, out var widthPx) && widthPx > 0)
                    {
                        cellWidthPoints = widthPx * 0.75;
                    }
                }

                // Handle colspan
                var gridSpan = 1;
                var colspanAttribute = cell.GetAttribute("colspan");
                if (!string.IsNullOrEmpty(colspanAttribute) && int.TryParse(colspanAttribute, out var cs) && cs > 1)
                {
                    gridSpan = cs;
                }

                // Handle rowspan
                var verticalMerge = VerticalMergeType.None;
                var rowspanAttribute = cell.GetAttribute("rowspan");
                if (!string.IsNullOrEmpty(rowspanAttribute) && int.TryParse(rowspanAttribute, out var rs) && rs > 1)
                {
                    verticalMerge = VerticalMergeType.Restart;
                    newRowspans[colIndex] = rs - 1;
                }

                var cellElements = new List<DocumentElement>();
                if (cell.TextContent.TryTrim(out var text))
                {
                    var cellRunProperties = ContainerRunProps() with
                    {
                        Bold = isHeader
                    };
                    if (cellTextColor != null)
                    {
                        cellRunProperties = cellRunProperties with
                        {
                            ColorHex = cellTextColor
                        };
                    }

                    cellElements.Add(new ParagraphElement
                    {
                        Properties = cellAlignment is { } alignment
                            ? new()
                            {
                                Alignment = alignment
                            }
                            : new(),
                        Runs =
                        [
                            new()
                            {
                                Text = text,
                                Properties = cellRunProperties
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
                        VerticalMerge = verticalMerge,
                        WidthPoints = cellWidthPoints,
                        WidthFraction = cellWidthFraction
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

        // Word aligns an imported table's CELL TEXT with the text margin, not its frame, so the
        // table is outdented by everything sitting left of that text: the cell padding and the
        // border. Probed against Word at 150 DPI (three tables in one document, differing only in
        // these values): the first glyph lands at x=152 for cellpadding 0, cellpadding 15 and
        // border=3 alike, while the frame moves to 144, 125 and 137 to suit. Anchoring the frame
        // at the margin instead — what Morph did — pushed cell text up to 30px right of Word's.
        // A table that fills its container is deliberately left on the old footing, in BOTH the
        // pixel conversion and the outdent. Word widens a full-width table by the inset at each end
        // so its cell text still spans the text column exactly, while Morph's fill-container path
        // resolves columns against the container width alone — and with a fixed total width the
        // padding drives the COLUMN distribution, so correcting it alone moves every column away
        // from Word. html_complex is the whole of that evidence: correct 6pt padding measured
        // +0.015 AE per backend against the too-large 8pt. The pixel rule is right and the column
        // distribution is the compensating error; they have to land together.
        if (cellPaddingPixels is { } pixels && !fillContainer)
        {
            defaultCellPadding = new(pixels * pixelsToPoints);
        }

        var leftInset = fillContainer
            ? 0
            : (defaultCellPadding?.Left ?? 0) + borderWidthPoints;

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                DefaultBorders = defaultBorders,
                DefaultCellPadding = defaultCellPadding ?? new CellSpacing(),
                IndentPoints = -leftInset,
                PreferredWidthPoints = preferredWidthPoints,
                FillContainer = fillContainer
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

    // The full CSS named-colour set (CSS Color Level 4). Only ten were present before, so any
    // other name (darkblue, lightgray, teal, …) fell through NormalizeColor to null and was
    // dropped — text rendered black, background bands vanished. "transparent" is deliberately
    // absent: returning null there yields no fill, which is the correct outcome for a background.
    static readonly Dictionary<string, string> namedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"] = "F0F8FF",
        ["antiquewhite"] = "FAEBD7",
        ["aqua"] = "00FFFF",
        ["aquamarine"] = "7FFFD4",
        ["azure"] = "F0FFFF",
        ["beige"] = "F5F5DC",
        ["bisque"] = "FFE4C4",
        ["black"] = "000000",
        ["blanchedalmond"] = "FFEBCD",
        ["blue"] = "0000FF",
        ["blueviolet"] = "8A2BE2",
        ["brown"] = "A52A2A",
        ["burlywood"] = "DEB887",
        ["cadetblue"] = "5F9EA0",
        ["chartreuse"] = "7FFF00",
        ["chocolate"] = "D2691E",
        ["coral"] = "FF7F50",
        ["cornflowerblue"] = "6495ED",
        ["cornsilk"] = "FFF8DC",
        ["crimson"] = "DC143C",
        ["cyan"] = "00FFFF",
        ["darkblue"] = "00008B",
        ["darkcyan"] = "008B8B",
        ["darkgoldenrod"] = "B8860B",
        ["darkgray"] = "A9A9A9",
        ["darkgreen"] = "006400",
        ["darkgrey"] = "A9A9A9",
        ["darkkhaki"] = "BDB76B",
        ["darkmagenta"] = "8B008B",
        ["darkolivegreen"] = "556B2F",
        ["darkorange"] = "FF8C00",
        ["darkorchid"] = "9932CC",
        ["darkred"] = "8B0000",
        ["darksalmon"] = "E9967A",
        ["darkseagreen"] = "8FBC8F",
        ["darkslateblue"] = "483D8B",
        ["darkslategray"] = "2F4F4F",
        ["darkslategrey"] = "2F4F4F",
        ["darkturquoise"] = "00CED1",
        ["darkviolet"] = "9400D3",
        ["deeppink"] = "FF1493",
        ["deepskyblue"] = "00BFFF",
        ["dimgray"] = "696969",
        ["dimgrey"] = "696969",
        ["dodgerblue"] = "1E90FF",
        ["firebrick"] = "B22222",
        ["floralwhite"] = "FFFAF0",
        ["forestgreen"] = "228B22",
        ["fuchsia"] = "FF00FF",
        ["gainsboro"] = "DCDCDC",
        ["ghostwhite"] = "F8F8FF",
        ["gold"] = "FFD700",
        ["goldenrod"] = "DAA520",
        ["gray"] = "808080",
        ["green"] = "008000",
        ["greenyellow"] = "ADFF2F",
        ["grey"] = "808080",
        ["honeydew"] = "F0FFF0",
        ["hotpink"] = "FF69B4",
        ["indianred"] = "CD5C5C",
        ["indigo"] = "4B0082",
        ["ivory"] = "FFFFF0",
        ["khaki"] = "F0E68C",
        ["lavender"] = "E6E6FA",
        ["lavenderblush"] = "FFF0F5",
        ["lawngreen"] = "7CFC00",
        ["lemonchiffon"] = "FFFACD",
        ["lightblue"] = "ADD8E6",
        ["lightcoral"] = "F08080",
        ["lightcyan"] = "E0FFFF",
        ["lightgoldenrodyellow"] = "FAFAD2",
        ["lightgray"] = "D3D3D3",
        ["lightgreen"] = "90EE90",
        ["lightgrey"] = "D3D3D3",
        ["lightpink"] = "FFB6C1",
        ["lightsalmon"] = "FFA07A",
        ["lightseagreen"] = "20B2AA",
        ["lightskyblue"] = "87CEFA",
        ["lightslategray"] = "778899",
        ["lightslategrey"] = "778899",
        ["lightsteelblue"] = "B0C4DE",
        ["lightyellow"] = "FFFFE0",
        ["lime"] = "00FF00",
        ["limegreen"] = "32CD32",
        ["linen"] = "FAF0E6",
        ["magenta"] = "FF00FF",
        ["maroon"] = "800000",
        ["mediumaquamarine"] = "66CDAA",
        ["mediumblue"] = "0000CD",
        ["mediumorchid"] = "BA55D3",
        ["mediumpurple"] = "9370DB",
        ["mediumseagreen"] = "3CB371",
        ["mediumslateblue"] = "7B68EE",
        ["mediumspringgreen"] = "00FA9A",
        ["mediumturquoise"] = "48D1CC",
        ["mediumvioletred"] = "C71585",
        ["midnightblue"] = "191970",
        ["mintcream"] = "F5FFFA",
        ["mistyrose"] = "FFE4E1",
        ["moccasin"] = "FFE4B5",
        ["navajowhite"] = "FFDEAD",
        ["navy"] = "000080",
        ["oldlace"] = "FDF5E6",
        ["olive"] = "808000",
        ["olivedrab"] = "6B8E23",
        ["orange"] = "FFA500",
        ["orangered"] = "FF4500",
        ["orchid"] = "DA70D6",
        ["palegoldenrod"] = "EEE8AA",
        ["palegreen"] = "98FB98",
        ["paleturquoise"] = "AFEEEE",
        ["palevioletred"] = "DB7093",
        ["papayawhip"] = "FFEFD5",
        ["peachpuff"] = "FFDAB9",
        ["peru"] = "CD853F",
        ["pink"] = "FFC0CB",
        ["plum"] = "DDA0DD",
        ["powderblue"] = "B0E0E6",
        ["purple"] = "800080",
        ["rebeccapurple"] = "663399",
        ["red"] = "FF0000",
        ["rosybrown"] = "BC8F8F",
        ["royalblue"] = "4169E1",
        ["saddlebrown"] = "8B4513",
        ["salmon"] = "FA8072",
        ["sandybrown"] = "F4A460",
        ["seagreen"] = "2E8B57",
        ["seashell"] = "FFF5EE",
        ["sienna"] = "A0522D",
        ["silver"] = "C0C0C0",
        ["skyblue"] = "87CEEB",
        ["slateblue"] = "6A5ACD",
        ["slategray"] = "708090",
        ["slategrey"] = "708090",
        ["snow"] = "FFFAFA",
        ["springgreen"] = "00FF7F",
        ["steelblue"] = "4682B4",
        ["tan"] = "D2B48C",
        ["teal"] = "008080",
        ["thistle"] = "D8BFD8",
        ["tomato"] = "FF6347",
        ["turquoise"] = "40E0D0",
        ["violet"] = "EE82EE",
        ["wheat"] = "F5DEB3",
        ["white"] = "FFFFFF",
        ["whitesmoke"] = "F5F5F5",
        ["yellow"] = "FFFF00",
        ["yellowgreen"] = "9ACD32"
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
            // HTML <img> width/height are CSS pixels; convert to points (1px = 0.75pt) so the
            // image renders at Word's size. Treating px as pt drew every image ~33% oversized,
            // which also pushed later content down and across page breaks.
            return result * 0.75;
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

    // CSS length to POINTS, honouring the unit: a px value is 0.75pt (the same 96→72 DPI ratio the
    // image and cellpadding attributes use), a pt value passes through, a bare number is treated as
    // px. Word sizes a `width: 400px` table at 300pt — measured at 623px against 625 predicted at
    // 150 DPI. Unlike TryParseCssDimension (which reads px as pt), so use this only where the value
    // is a genuine CSS length whose px→pt scaling has been Word-confirmed.
    static bool TryParseCssLengthToPoints(ReadOnlySpan<char> value, out double points)
    {
        var span = value.Trim();
        var isPoints = span.EndsWith("pt", StringComparison.OrdinalIgnoreCase);
        if (isPoints ||
            span.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            span = span[..^2].TrimEnd();
        }

        if (!double.TryParse(span, out var raw))
        {
            points = 0;
            return false;
        }

        points = isPoints ? raw : raw * pixelsToPoints;
        return true;
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
}