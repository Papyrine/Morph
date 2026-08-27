/// <summary>
/// Tests for HtmlParser which converts HTML content (from AltChunk) to DocumentElements.
/// </summary>
public class HtmlParserTests
{
    [Test]
    public async Task EmptyHtml_ReturnsEmpty()
    {
        var result = HtmlParser.Parse("");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task BodyOnly_ReturnsEmpty()
    {
        var result = HtmlParser.Parse("<html><body></body></html>");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Table_EmptyTable_ReturnsEmpty()
    {
        var result = HtmlParser.Parse("<table></table>");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task UnknownElement_EmptyText_Skipped()
    {
        var result = HtmlParser.Parse("<custom></custom>");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public Task PlainText() =>
        Verify(HtmlParser.Parse("Hello world"));

    [Test]
    [Arguments("h1")]
    [Arguments("h2")]
    [Arguments("h3")]
    [Arguments("h4")]
    [Arguments("h5")]
    [Arguments("h6")]
    public Task Headings(string tag) =>
        Verify(HtmlParser.Parse($"<{tag}>Title</{tag}>"));

    [Test]
    public Task Paragraph() =>
        Verify(HtmlParser.Parse("<p>Text</p>"));

    [Test]
    public Task EmptyParagraph() =>
        Verify(HtmlParser.Parse("<p></p>"))
            .Snapshot(
                """
                []
                """);

    [Test]
    public Task Bold_B() =>
        Verify(HtmlParser.Parse("<p><b>bold</b></p>"));

    [Test]
    public Task Bold_Strong() =>
        Verify(HtmlParser.Parse("<p><strong>bold</strong></p>"));

    [Test]
    public Task Italic_I() =>
        Verify(HtmlParser.Parse("<p><i>italic</i></p>"));

    [Test]
    public Task Italic_Em() =>
        Verify(HtmlParser.Parse("<p><em>italic</em></p>"));

    [Test]
    public Task Underline() =>
        Verify(HtmlParser.Parse("<p><u>underline</u></p>"));

    [Test]
    [Arguments("s")]
    [Arguments("strike")]
    [Arguments("del")]
    public Task Strikethrough(string tag) =>
        Verify(HtmlParser.Parse($"<p><{tag}>struck</{tag}></p>"));

    [Test]
    public Task NestedFormatting_BoldItalic() =>
        Verify(HtmlParser.Parse("<p><b><i>both</i></b></p>"));

    [Test]
    public Task Link() =>
        Verify(HtmlParser.Parse("<p><a href=\"https://example.com\">link</a></p>"));

    [Test]
    public Task InlineBr() =>
        Verify(HtmlParser.Parse("<p>line1<br>line2</p>"));

    [Test]
    public Task Subscript() =>
        Verify(HtmlParser.Parse("<p><sub>subscript</sub></p>"));

    [Test]
    public Task Superscript() =>
        Verify(HtmlParser.Parse("<p><sup>superscript</sup></p>"));

    [Test]
    public Task FontTag_Face() =>
        Verify(HtmlParser.Parse("<p><font face=\"Arial\">text</font></p>"));

    [Test]
    public async Task DefaultFontFamily_AppliedToRunsWithoutExplicitFont()
    {
        var result = HtmlParser.Parse("<p><font size=\"7\">large</font></p>", "Times New Roman");
        var para = (ParagraphElement) result[0];
        await Assert.That(para.Runs[0].Properties.FontFamily).IsEqualTo("Times New Roman");
    }

    [Test]
    public async Task DefaultFontFamily_NotOverriddenWhenFontFaceSet()
    {
        var result = HtmlParser.Parse("<p><font face=\"Arial\">text</font></p>", "Times New Roman");
        var para = (ParagraphElement) result[0];
        await Assert.That(para.Runs[0].Properties.FontFamily).IsEqualTo("Arial");
    }

    [Test]
    public Task FontTag_Color() =>
        Verify(HtmlParser.Parse("<p><font color=\"red\">text</font></p>"));

    [Test]
    [Arguments("1")]
    [Arguments("2")]
    [Arguments("3")]
    [Arguments("4")]
    [Arguments("5")]
    [Arguments("6")]
    [Arguments("7")]
    public Task FontTag_Size(string size) =>
        Verify(HtmlParser.Parse($"<p><font size=\"{size}\">text</font></p>"));

    [Test]
    public Task FontTag_SizeClamped_Below1() =>
        Verify(HtmlParser.Parse("<p><font size=\"0\">text</font></p>"));

    [Test]
    public Task FontTag_SizeClamped_Above7() =>
        Verify(HtmlParser.Parse("<p><font size=\"99\">text</font></p>"));

    [Test]
    public Task Span_Color() =>
        Verify(HtmlParser.Parse("<p><span style=\"color: #FF0000\">text</span></p>"));

    [Test]
    public Task Span_FontFamily() =>
        Verify(HtmlParser.Parse("<p><span style=\"font-family: 'Courier New'\">text</span></p>"));

    [Test]
    public Task Span_FontSize() =>
        Verify(HtmlParser.Parse("<p><span style=\"font-size: 16pt\">text</span></p>"));

    [Test]
    public Task Span_FontWeightBold() =>
        Verify(HtmlParser.Parse("<p><span style=\"font-weight: bold\">text</span></p>"));

    [Test]
    public Task Span_FontWeight700() =>
        Verify(HtmlParser.Parse("<p><span style=\"font-weight: 700\">text</span></p>"));

    [Test]
    public Task Span_FontStyleItalic() =>
        Verify(HtmlParser.Parse("<p><span style=\"font-style: italic\">text</span></p>"));

    [Test]
    public Task Span_TextDecorationUnderline() =>
        Verify(HtmlParser.Parse("<p><span style=\"text-decoration: underline\">text</span></p>"));

    [Test]
    public Task Span_TextDecorationLineThrough() =>
        Verify(HtmlParser.Parse("<p><span style=\"text-decoration: line-through\">text</span></p>"));

    [Test]
    public Task Span_NoStyle() =>
        Verify(HtmlParser.Parse("<p><span>text</span></p>"));

    [Test]
    [Arguments("center")]
    [Arguments("right")]
    [Arguments("justify")]
    [Arguments("left")]
    public Task ParagraphAlignment(string align) =>
        Verify(HtmlParser.Parse($"<p style=\"text-align: {align}\">text</p>"));

    [Test]
    public Task ParagraphStyle_Color() =>
        Verify(HtmlParser.Parse("<p style=\"color: blue\">text</p>"));

    [Test]
    [Arguments("red")]
    [Arguments("green")]
    [Arguments("blue")]
    [Arguments("black")]
    [Arguments("white")]
    [Arguments("yellow")]
    [Arguments("orange")]
    [Arguments("purple")]
    [Arguments("gray")]
    [Arguments("grey")]
    public Task NormalizeColor_Named(string name) =>
        Verify(HtmlParser.Parse($"<p><font color=\"{name}\">text</font></p>"));

    [Test]
    public Task NormalizeColor_Hex6() =>
        Verify(HtmlParser.Parse("<p><font color=\"#abcdef\">text</font></p>"));

    [Test]
    public Task NormalizeColor_Hex3() =>
        Verify(HtmlParser.Parse("<p><font color=\"#abc\">text</font></p>"));

    [Test]
    public Task NormalizeColor_Rgb() =>
        Verify(HtmlParser.Parse("<p><font color=\"rgb(255, 128, 0)\">text</font></p>"));

    [Test]
    public Task NormalizeColor_Unknown() =>
        Verify(HtmlParser.Parse("<p><font color=\"notacolor\">text</font></p>"));

    [Test]
    public Task Br() =>
        Verify(HtmlParser.Parse("<br>"));

    [Test]
    public Task ContainerElement_Div() =>
        Verify(HtmlParser.Parse("<div><p>inner</p></div>"));

    [Test]
    public Task UnorderedList() =>
        Verify(HtmlParser.Parse("<ul><li>item1</li><li>item2</li></ul>"));

    [Test]
    public Task OrderedList() =>
        Verify(HtmlParser.Parse("<ol><li>first</li><li>second</li></ol>"));

    [Test]
    public Task NestedList() =>
        Verify(HtmlParser.Parse("<ul><li>outer<ul><li>inner</li></ul></li></ul>"));

    [Test]
    public Task NestedOrderedList() =>
        Verify(HtmlParser.Parse("<ol><li>one<ol><li>one-a</li></ol></li><li>two</li></ol>"));

    [Test]
    public Task OrderedList_StartAttribute() =>
        Verify(HtmlParser.Parse("<ol start=\"5\"><li>five</li><li>six</li></ol>"));

    [Test]
    public Task List_SkipsNonLiChildren() =>
        Verify(HtmlParser.Parse("<ul><div>skip</div><li>item</li></ul>"));

    [Test]
    public Task Table_Basic() =>
        Verify(HtmlParser.Parse("<table><tr><td>cell1</td><td>cell2</td></tr></table>"));

    [Test]
    public Task Table_ThCells() =>
        Verify(HtmlParser.Parse("<table><tr><th>header</th></tr></table>"));

    [Test]
    public Task Table_EmptyCell() =>
        Verify(HtmlParser.Parse("<table><tr><td></td></tr></table>"));

    [Test]
    public Task Table_Cellpadding() =>
        Verify(HtmlParser.Parse("<table cellpadding=\"5\"><tr><td>cell</td></tr></table>"));

    [Test]
    public Task Table_StylePadding_OverridesCellpadding() =>
        Verify(HtmlParser.Parse("<table cellpadding=\"5\" style=\"padding: 10px\"><tr><td>cell</td></tr></table>"));

    [Test]
    public Task Table_CellStylePadding() =>
        Verify(HtmlParser.Parse("<table><tr><td style=\"padding: 8px\">cell</td></tr></table>"));

    [Test]
    public Task Table_CellStyleMargin() =>
        Verify(HtmlParser.Parse("<table><tr><td style=\"margin-top: 3px; margin-left: 5px\">cell</td></tr></table>"));

    [Test]
    public Task Table_CssSpacing_IndividualProperties() =>
        Verify(HtmlParser.Parse("<table><tr><td style=\"padding-top: 1px; padding-right: 2px; padding-bottom: 3px; padding-left: 4px\">cell</td></tr></table>"));

    [Test]
    public Task Table_CssSpacing_PtUnits() =>
        Verify(HtmlParser.Parse("<table><tr><td style=\"padding: 12pt\">cell</td></tr></table>"));

    [Test]
    public Task MultipleElements() =>
        Verify(HtmlParser.Parse("<h1>Title</h1><p>Body</p><ul><li>Item</li></ul>"));

    [Test]
    public Task MixedInlineContent() =>
        Verify(HtmlParser.Parse("<p>normal <b>bold</b> <i>italic</i></p>"));

    [Test]
    public Task UnknownElement() =>
        Verify(HtmlParser.Parse("<custom>content</custom>"));

    // Block elements

    [Test]
    public Task Blockquote() =>
        Verify(HtmlParser.Parse("<blockquote>quoted text</blockquote>"));

    [Test]
    public Task Blockquote_WithParagraph() =>
        Verify(HtmlParser.Parse("<blockquote><p>quoted paragraph</p></blockquote>"));

    [Test]
    public Task Blockquote_Nested() =>
        Verify(HtmlParser.Parse("<blockquote><blockquote>deeply quoted</blockquote></blockquote>"));

    [Test]
    public Task Pre() =>
        Verify(HtmlParser.Parse("<pre>  code\n  here  </pre>"));

    [Test]
    public Task Hr() =>
        Verify(HtmlParser.Parse("<hr>"))
            .Snapshot(
                """
                [
                  {}
                ]
                """);

    [Test]
    public Task Hr_BetweenParagraphs() =>
        Verify(HtmlParser.Parse("<p>before</p><hr><p>after</p>"));

    [Test]
    public Task DefinitionList() =>
        Verify(HtmlParser.Parse("<dl><dt>Term</dt><dd>Definition</dd></dl>"));

    [Test]
    public Task DefinitionList_MultipleDd() =>
        Verify(HtmlParser.Parse("<dl><dt>Term</dt><dd>Def1</dd><dd>Def2</dd></dl>"));

    [Test]
    public Task Figure_WithCaption() =>
        Verify(HtmlParser.Parse("<figure><p>content</p><figcaption>caption</figcaption></figure>"));

    [Test]
    public Task Figcaption_Standalone() =>
        Verify(HtmlParser.Parse("<figcaption>standalone caption</figcaption>"));

    // Inline elements

    [Test]
    public Task Mark() =>
        Verify(HtmlParser.Parse("<p><mark>highlighted</mark></p>"));

    [Test]
    public Task Small() =>
        Verify(HtmlParser.Parse("<p><small>small text</small></p>"));

    [Test]
    public Task InlineCode() =>
        Verify(HtmlParser.Parse("<p><code>monospace</code></p>"));

    // Images

    [Test]
    public Task Img_DataUri() =>
        Verify(HtmlParser.Parse("<img src=\"data:image/png;base64,iVBORw0KGgo=\">"))
            .Snapshot(
                """
                [
                  {
                    "ImageData": "iVBORw0KGgo=",
                    "WidthPoints": 100.0,
                    "HeightPoints": 100.0,
                    "ContentType": "image/png",
                    "FlipHorizontal": false,
                    "FlipVertical": false
                  }
                ]
                """);

    [Test]
    public Task Img_WithDimensions() =>
        Verify(HtmlParser.Parse("<img src=\"data:image/png;base64,iVBORw0KGgo=\" width=\"200\" height=\"150\">"))
            .Snapshot(
                """
                [
                  {
                    "ImageData": "iVBORw0KGgo=",
                    "WidthPoints": 150.0,
                    "HeightPoints": 112.5,
                    "ContentType": "image/png",
                    "FlipHorizontal": false,
                    "FlipVertical": false
                  }
                ]
                """);

    [Test]
    public async Task Img_NoSrc_Skipped()
    {
        var result = HtmlParser.Parse("<img>");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Img_HttpSrc_Skipped()
    {
        var result = HtmlParser.Parse("<img src=\"https://example.com/image.png\">");
        await Assert.That(result).IsEmpty();
    }

    // CSS

    [Test]
    public Task Span_BackgroundColor() =>
        Verify(HtmlParser.Parse("<p><span style=\"background-color: #FFFF00\">highlighted</span></p>"));

    [Test]
    public Task Paragraph_TextIndent() =>
        Verify(HtmlParser.Parse("<p style=\"text-indent: 36pt\">indented</p>"));

    [Test]
    public Task Paragraph_LineHeight() =>
        Verify(HtmlParser.Parse("<p style=\"line-height: 2.0\">double spaced</p>"));

    // Tables

    [Test]
    public Task Table_CellBackgroundColor() =>
        Verify(HtmlParser.Parse("<table><tr><td style=\"background-color: yellow\">cell</td></tr></table>"));

    [Test]
    public Task Table_BorderAttribute() =>
        Verify(HtmlParser.Parse("<table border=\"2\"><tr><td>cell</td></tr></table>"));

    [Test]
    public Task Table_BorderZero() =>
        Verify(HtmlParser.Parse("<table border=\"0\"><tr><td>cell</td></tr></table>"));

    [Test]
    public Task Table_BorderCss() =>
        Verify(HtmlParser.Parse("<table style=\"border: 2px solid red\"><tr><td>cell</td></tr></table>"));

    [Test]
    public Task Table_Colspan() =>
        Verify(HtmlParser.Parse("<table><tr><td colspan=\"2\">wide</td></tr><tr><td>a</td><td>b</td></tr></table>"));

    [Test]
    public Task Table_Rowspan() =>
        Verify(HtmlParser.Parse("<table><tr><td rowspan=\"2\">tall</td><td>right1</td></tr><tr><td>right2</td></tr></table>"));

    [Test]
    public Task Table_TheadTbodyTfoot() =>
        Verify(HtmlParser.Parse("<table><thead><tr><th>H</th></tr></thead><tbody><tr><td>B</td></tr></tbody><tfoot><tr><td>F</td></tr></tfoot></table>"));
}
