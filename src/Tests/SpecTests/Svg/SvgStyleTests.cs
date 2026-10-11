using static SvgTestSupport;

/// <summary>
/// How a shape comes by its fill and stroke: presentation attributes, the <c>style</c> attribute,
/// class rules from a <c>style</c> element, and inheritance — the cascade
/// <see cref="SvgReader"/> and <see cref="SvgStyleSheet"/> resolve between them.
/// </summary>
public class SvgStyleTests
{
    const string square = "d=\"M0 0H10V10H0Z\"";

    static SvgPath Only(string body) =>
        DrawBody(body).Paths.Single();

    [Test]
    public async Task A_shape_with_nothing_said_is_filled_black_and_not_stroked()
    {
        var path = Only($"<path {square}/>");

        await Assert.That(path.FillColorHex).IsEqualTo("000000");
        await Assert.That(path.FillAlpha).IsEqualTo(1d);
        await Assert.That(path.Stroke).IsNull();
        await Assert.That(path.EvenOdd).IsFalse();
    }

    [Test]
    public async Task Colours_read_as_hex_short_hex_rgb_and_names()
    {
        await Assert.That(Only($"<path {square} fill=\"#4285F4\"/>").FillColorHex).IsEqualTo("4285F4");
        await Assert.That(Only($"<path {square} fill=\"#1af\"/>").FillColorHex).IsEqualTo("11AAFF");
        await Assert.That(Only($"<path {square} fill=\"rgb(234, 67, 53)\"/>").FillColorHex).IsEqualTo("EA4335");
        await Assert.That(Only($"<path {square} fill=\"rgb(100%, 0%, 50%)\"/>").FillColorHex).IsEqualTo("FF0080");
        await Assert.That(Only($"<path {square} fill=\"SeaGreen\"/>").FillColorHex).IsEqualTo("2E8B57");

        var translucent = Only($"<path {square} fill=\"rgba(0, 0, 255, 0.25)\"/>");
        await Assert.That(translucent.FillColorHex).IsEqualTo("0000FF");
        await Assert.That(translucent.FillAlpha).IsEqualTo(0.25).Within(1e-9);
    }

    [Test]
    public async Task A_class_rule_outranks_the_attribute_it_repeats()
    {
        // Office's theme classes: the rule is the colour after the theme was applied, the attribute
        // the one before. Office's own PNG of to-do-list-for-projects' icon is the rule's navy.
        var path = Only(
            $$"""
              <style>
              .MsftOfcThm_Accent1_Fill_v2 {
               fill:#003780;
              }
              </style>
              <path {{square}} class="MsftOfcThm_Accent1_Fill_v2" fill="#FAD5D5"/>
              """);

        await Assert.That(path.FillColorHex).IsEqualTo("003780");
    }

    [Test]
    public async Task A_class_rule_supplies_what_the_attributes_leave_out()
    {
        var path = Only(
            $$"""
              <style>.outline { stroke: #C6BFAA; stroke-width: 2 } .hollow { fill: none }</style>
              <path {{square}} class="outline hollow"/>
              """);

        await Assert.That(path.FillColorHex).IsNull();
        await Assert.That(path.Stroke!.ColorHex).IsEqualTo("C6BFAA");
        await Assert.That(path.Stroke.Width).IsEqualTo(2d);
    }

    [Test]
    public async Task The_more_specific_rule_wins_and_between_equals_the_later()
    {
        const string sheet =
            """
            <style>
            /* an id over a class over an element name, wherever each stands */
            #one { fill: #111111 }
            path.accent { fill: #222222 }
            .accent { fill: #333333 }
            path, rect { fill: #444444 }
            * { fill: #555555 }
            .late { fill: #666666 }
            .late { fill: #777777 }
            </style>
            """;

        await Assert.That(Only($"{sheet}<path {square} id=\"one\" class=\"accent\"/>").FillColorHex).IsEqualTo("111111");
        await Assert.That(Only($"{sheet}<path {square} class=\"accent\"/>").FillColorHex).IsEqualTo("222222");
        await Assert.That(Only($"{sheet}<rect width=\"5\" height=\"5\" class=\"accent\"/>").FillColorHex).IsEqualTo("333333");
        await Assert.That(Only($"{sheet}<rect width=\"5\" height=\"5\"/>").FillColorHex).IsEqualTo("444444");
        await Assert.That(Only($"{sheet}<circle r=\"5\"/>").FillColorHex).IsEqualTo("555555");
        await Assert.That(Only($"{sheet}<circle r=\"5\" class=\"late\"/>").FillColorHex).IsEqualTo("777777");
    }

    [Test]
    public async Task A_style_attribute_outranks_both()
    {
        var path = Only($"<style>.a {{ fill: #111111 }}</style><path {square} class=\"a\" fill=\"#222222\" style=\"fill: #333333; stroke:#444444\"/>");

        await Assert.That(path.FillColorHex).IsEqualTo("333333");
        await Assert.That(path.Stroke!.ColorHex).IsEqualTo("444444");
    }

    [Test]
    public async Task Fill_and_stroke_are_inherited_and_a_shape_can_take_them_back()
    {
        var paths = DrawBody(
            $"""
             <g fill="#AA0000" stroke="#0000AA" stroke-width="3" stroke-linecap="round" stroke-linejoin="bevel" stroke-miterlimit="8" fill-rule="evenodd">
               <path {square}/>
               <path {square} fill="none" stroke-width="1"/>
               <path {square} stroke="none"/>
             </g>
             """).Paths;

        await Assert.That(paths[0].FillColorHex).IsEqualTo("AA0000");
        await Assert.That(paths[0].EvenOdd).IsTrue();
        await Assert.That(paths[0].Stroke).IsEqualTo(new SvgStroke("0000AA", 1, 3, SvgLineCap.Round, SvgLineJoin.Bevel, 8));
        await Assert.That(paths[1].FillColorHex).IsNull();
        await Assert.That(paths[1].Stroke!.Width).IsEqualTo(1d);
        await Assert.That(paths[2].FillColorHex).IsEqualTo("AA0000");
        await Assert.That(paths[2].Stroke).IsNull();
    }

    [Test]
    public async Task CurrentColor_is_the_colour_property_at_that_element()
    {
        var paths = DrawBody($"<g color=\"#123456\"><path {square} fill=\"currentColor\"/><path {square} color=\"#654321\" fill=\"currentColor\"/></g>").Paths;

        await Assert.That(paths[0].FillColorHex).IsEqualTo("123456");
        await Assert.That(paths[1].FillColorHex).IsEqualTo("654321");
    }

    [Test]
    public async Task Opacities_multiply_into_the_alpha_of_what_they_fade()
    {
        var path = Only($"<g fill-opacity=\"0.5\"><path {square} opacity=\"0.5\"/></g>");
        await Assert.That(path.FillAlpha).IsEqualTo(0.25).Within(1e-9);

        var stroked = Only($"<path {square} fill=\"none\" stroke=\"#000\" stroke-opacity=\"50%\"/>");
        await Assert.That(stroked.Stroke!.Alpha).IsEqualTo(0.5).Within(1e-9);
    }

    [Test]
    public async Task What_paints_nothing_is_not_emitted()
    {
        await Assert.That(DrawBody($"<path {square} fill=\"none\"/>").Paths.Count).IsEqualTo(0);
        await Assert.That(DrawBody($"<path {square} fill=\"transparent\"/>").Paths.Count).IsEqualTo(0);
        await Assert.That(DrawBody($"<path {square} opacity=\"0\"/>").Paths.Count).IsEqualTo(0);
        await Assert.That(DrawBody($"<path {square} fill=\"none\" stroke=\"#000\" stroke-width=\"0\"/>").Paths.Count).IsEqualTo(0);
        await Assert.That(DrawBody($"<g display=\"none\"><path {square}/></g><path {square} visibility=\"hidden\"/>").Paths.Count).IsEqualTo(0);
        await Assert.That(DrawBody($"<defs><path {square}/></defs>").Paths.Count).IsEqualTo(0);

        // A visible child of a hidden group shows, as visibility is inherited rather than applied.
        await Assert.That(DrawBody($"<g visibility=\"hidden\"><path {square} visibility=\"visible\"/></g>").Paths.Count).IsEqualTo(1);
    }

    [Test]
    public async Task The_empty_dash_array_Office_writes_is_a_solid_stroke()
    {
        var path = Only($"<path {square} fill=\"none\" stroke=\"#231F20\" stroke-dasharray=\"\" stroke-dashoffset=\"1\"/>");

        await Assert.That(path.Stroke!.ColorHex).IsEqualTo("231F20");
    }

    [Test]
    public async Task A_selector_beyond_a_name_an_id_and_classes_gives_the_document_up()
    {
        await Assert.That(Unsupported($"<style>g path {{ fill: red }}</style><path {square}/>")).IsEqualTo("the style selector 'g path'");
        await Assert.That(Unsupported($"<style>path:hover {{ fill: red }}</style><path {square}/>")).IsEqualTo("the style selector 'path:hover'");
        await Assert.That(Unsupported($"<style>@media print {{ path {{ fill: red }} }}</style><path {square}/>")).IsEqualTo("an at-rule in a style sheet");
        await Assert.That(Unsupported($"<style>path {{ fill: red !important }}</style><path {square}/>")).IsEqualTo("!important in a style sheet");
        await Assert.That(Unsupported($"<style>path {{ transform: scale(2) }}</style><path {square}/>")).IsEqualTo("transform set as a style property");
    }
}
