extern alias Skia;
using SkiaRenderContext = Skia::RenderContext;
using SkiaPageRenderer = Skia::PageRenderer;

/// <summary>
/// Rendering tests for HTML-parsed elements through the Skia pipeline.
/// Verifies that each element type produces correct visual output.
/// </summary>
public class HtmlElementRenderTests
{
    static byte[] RenderElements(params DocumentElement[] elements)
    {
        var doc = new ParsedDocument
        {
            PageSettings = new()
            {
                WidthPoints = 300,
                HeightPoints = 200,
                MarginTop = 20,
                MarginBottom = 20,
                MarginLeft = 20,
                MarginRight = 20
            },
            Elements = elements
        };

        using var context = new SkiaRenderContext(doc.PageSettings, 96, fontDirectory: ProjectFonts.Directory);
        using var renderer = new SkiaPageRenderer(context);

        byte[]? result = null;
        renderer.RenderDocument(doc, writePng =>
        {
            using var ms = new MemoryStream();
            writePng(ms);
            result ??= ms.ToArray();
        });

        return result!;
    }

    static Task VerifyRendered(params DocumentElement[] elements) =>
        Verify(new Target("png", new MemoryStream(RenderElements(elements))));

    // HorizontalRuleElement

    [Test]
    public Task HorizontalRule() =>
        VerifyRendered(new HorizontalRuleElement());

    [Test]
    public Task HorizontalRule_BetweenParagraphs() =>
        VerifyRendered(
            new ParagraphElement
            {
                Runs = [new() { Text = "Above", Properties = new() }],
                Properties = new() { SpacingAfterPoints = 4 }
            },
            new HorizontalRuleElement(),
            new ParagraphElement
            {
                Runs = [new() { Text = "Below", Properties = new() }]
            });

    // Blockquote (paragraph with left indent)

    [Test]
    public Task Blockquote_LeftIndent() =>
        VerifyRendered(new ParagraphElement
        {
            Runs = [new() { Text = "Indented quote", Properties = new() }],
            Properties = new() { LeftIndentPoints = 36, SpacingAfterPoints = 8 }
        });

    // Pre (monospace font)

    [Test]
    public Task Pre_MonospaceFont() =>
        VerifyRendered(new ParagraphElement
        {
            Runs = [new() { Text = "  code\n  here", Properties = new() { FontFamily = "Courier New" } }],
            Properties = new() { SpacingAfterPoints = 8 }
        });

    // Definition list (bold dt + indented dd)

    [Test]
    public Task DefinitionList_BoldTerm_IndentedDef() =>
        VerifyRendered(
            new ParagraphElement
            {
                Runs = [new() { Text = "Term", Properties = new() { Bold = true } }],
                Properties = new() { SpacingAfterPoints = 8 }
            },
            new ParagraphElement
            {
                Runs = [new() { Text = "Definition", Properties = new() }],
                Properties = new() { LeftIndentPoints = 36, SpacingAfterPoints = 8 }
            });

    // Figcaption (italic)

    [Test]
    public Task Figcaption_Italic() =>
        VerifyRendered(new ParagraphElement
        {
            Runs = [new() { Text = "Figure caption", Properties = new() { Italic = true } }],
            Properties = new() { SpacingAfterPoints = 8 }
        });

    // Mark (background highlight)

    [Test]
    public Task Mark_BackgroundHighlight() =>
        VerifyRendered(new ParagraphElement
        {
            Runs =
            [
                new() { Text = "normal ", Properties = new() },
                new() { Text = "highlighted", Properties = new() { BackgroundColorHex = "FFFF00" } },
                new() { Text = " text", Properties = new() }
            ],
            Properties = new() { SpacingAfterPoints = 8 }
        });

    // Small (reduced font)

    [Test]
    public Task Small_ReducedFont() =>
        VerifyRendered(new ParagraphElement
        {
            Runs =
            [
                new() { Text = "normal ", Properties = new() },
                new() { Text = "small", Properties = new() { FontSizePoints = 11 * 0.8 } }
            ],
            Properties = new() { SpacingAfterPoints = 8 }
        });

    // InlineCode (Courier New)

    [Test]
    public Task InlineCode_Monospace() =>
        VerifyRendered(new ParagraphElement
        {
            Runs =
            [
                new() { Text = "text ", Properties = new() },
                new() { Text = "code()", Properties = new() { FontFamily = "Courier New" } }
            ],
            Properties = new() { SpacingAfterPoints = 8 }
        });

    // ImageElement (block image from data URI)

    [Test]
    public Task BlockImage() =>
        VerifyRendered(CreateSmallImage());

    // Inline image in a run

    [Test]
    public Task InlineImage() =>
        VerifyRendered(new ParagraphElement
        {
            Runs =
            [
                new() { Text = "before ", Properties = new() },
                new()
                {
                    Text = "",
                    Properties = new(),
                    InlineImageData = CreateRedPixelPng(),
                    InlineImageWidthPoints = 20,
                    InlineImageHeightPoints = 20,
                    InlineImageContentType = "image/png"
                },
                new() { Text = " after", Properties = new() }
            ],
            Properties = new() { SpacingAfterPoints = 8 }
        });

    // CSS: background-color on span

    [Test]
    public Task Span_BackgroundColor() =>
        VerifyRendered(new ParagraphElement
        {
            Runs = [new() { Text = "highlighted", Properties = new() { BackgroundColorHex = "FFFF00" } }],
            Properties = new() { SpacingAfterPoints = 8 }
        });

    // CSS: text-indent

    [Test]
    public Task Paragraph_TextIndent() =>
        VerifyRendered(new ParagraphElement
        {
            Runs = [new() { Text = "Indented first line of text in this paragraph", Properties = new() }],
            Properties = new() { FirstLineIndentPoints = 36, SpacingAfterPoints = 8 }
        });

    // CSS: line-height

    [Test]
    public Task Paragraph_LineHeight() =>
        VerifyRendered(new ParagraphElement
        {
            Runs = [new() { Text = "Double spaced paragraph with enough text to wrap onto multiple lines in this small page", Properties = new() }],
            Properties = new() { LineSpacingMultiplier = 2.0, SpacingAfterPoints = 8 }
        });

    // Table: cell background color

    [Test]
    public Task Table_CellBackgroundColor() =>
        VerifyRendered(new TableElement
        {
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "colored", Properties = new() }] }],
                            Properties = new() { BackgroundColorHex = "FFFF00" }
                        }
                    ]
                }
            ],
            Properties = new() { DefaultBorders = CellBorders.All }
        });

    // Table: custom border width

    [Test]
    public Task Table_CustomBorderWidth() =>
        VerifyRendered(new TableElement
        {
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "thick", Properties = new() }] }],
                            Properties = new()
                        }
                    ]
                }
            ],
            Properties = new()
            {
                DefaultBorders = new()
                {
                    Top = new() { IsVisible = true, WidthPoints = 2, ColorHex = "FF0000" },
                    Right = new() { IsVisible = true, WidthPoints = 2, ColorHex = "FF0000" },
                    Bottom = new() { IsVisible = true, WidthPoints = 2, ColorHex = "FF0000" },
                    Left = new() { IsVisible = true, WidthPoints = 2, ColorHex = "FF0000" }
                }
            }
        });

    // Table: no borders (border="0")

    [Test]
    public Task Table_NoBorders() =>
        VerifyRendered(new TableElement
        {
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "borderless", Properties = new() }] }],
                            Properties = new()
                        }
                    ]
                }
            ],
            Properties = new() { DefaultBorders = new() }
        });

    // Table: colspan

    [Test]
    public Task Table_Colspan() =>
        VerifyRendered(new TableElement
        {
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "spans 2", Properties = new() }] }],
                            Properties = new() { GridSpan = 2 }
                        }
                    ]
                },
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "a", Properties = new() }] }],
                            Properties = new()
                        },
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "b", Properties = new() }] }],
                            Properties = new()
                        }
                    ]
                }
            ],
            Properties = new() { DefaultBorders = CellBorders.All }
        });

    // Table: rowspan

    [Test]
    public Task Table_Rowspan() =>
        VerifyRendered(new TableElement
        {
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "tall", Properties = new() }] }],
                            Properties = new() { VerticalMerge = VerticalMergeType.Restart }
                        },
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "r1", Properties = new() }] }],
                            Properties = new()
                        }
                    ]
                },
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [],
                            Properties = new() { VerticalMerge = VerticalMergeType.Continue }
                        },
                        new()
                        {
                            Content = [new ParagraphElement { Runs = [new() { Text = "r2", Properties = new() }] }],
                            Properties = new()
                        }
                    ]
                }
            ],
            Properties = new() { DefaultBorders = CellBorders.All }
        });

    // Helpers

    static ImageElement CreateSmallImage() =>
        new()
        {
            ImageData = CreateRedPixelPng(),
            WidthPoints = 30,
            HeightPoints = 30,
            ContentType = "image/png"
        };

    static byte[] CreateRedPixelPng()
    {
        // Minimal valid 1x1 red PNG
        using var bmp = new SkiaSharp.SKBitmap(1, 1);
        bmp.SetPixel(0, 0, new(255, 0, 0));
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
