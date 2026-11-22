using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Slide rules read off PowerPoint's own exports of the corpus decks, or probed in PowerPoint 16
/// (2026-11-22). Each test names the deck and slide whose reference shows the rule.
/// </summary>
public class SlideFidelityTests
{
    static List<DocumentElement> Slide(string deck, int number)
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "powerpoint", deck, "input.pptx"));
        var elements = PowerPointConverter.Parse(stream, null).Elements;
        var slides = new List<List<DocumentElement>> { new() };
        foreach (var element in elements)
        {
            if (element is PageBreakElement)
            {
                slides.Add([]);
            }
            else
            {
                slides[^1].Add(element);
            }
        }

        return slides[number - 1];
    }

    static FloatingTextBoxElement TextBox(List<DocumentElement> slide, string text) =>
        slide.OfType<FloatingTextBoxElement>()
            .First(_ => _.Content.OfType<ParagraphElement>().Any(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text)).Contains(text)));

    static ParagraphElement Paragraph(FloatingTextBoxElement box, string text) =>
        box.Content.OfType<ParagraphElement>().First(_ => string.Concat(_.Runs.Select(run => run.Text)).Contains(text));

    // leaf's master maps bg1 to dk1 and tx1 to lt1: its slides are dark red with white text.
    [Test]
    public async Task A_masters_colour_map_swaps_text_and_background()
    {
        var slide = Slide("leaf", 1);
        var background = (FloatingShapeElement) slide[0];
        var title = TextBox(slide, "Basic presentation");

        await Assert.That(background.FillColorHex).IsEqualTo("732124");
        await Assert.That(title.Content.OfType<ParagraphElement>().First().Runs[0].Properties.ColorHex).IsEqualTo("FFFFFF");
    }

    // The title layout's leaf is drawn upright in a group turned 45° with flipH: its outline and
    // its stem turn with the group.
    [Test]
    public async Task A_rotated_groups_children_turn_with_it()
    {
        var turned = Slide("leaf", 1).OfType<FloatingShapeElement>()
            .Where(_ => _.LineColorHex != null && _.RotationDegrees != 0)
            .ToList();

        await Assert.That(turned).IsNotEmpty();
    }

    // pebble's agenda strokes an open curve around its photo: no a:close, so no segment back.
    [Test]
    public async Task An_open_custom_outline_stays_open() =>
        await Assert.That(Slide("pebble", 2).OfType<FloatingShapeElement>().Any(_ => _.OpenOutline)).IsTrue();

    // DrawingML writes a tab into a:t; it lays out as a tab, not as a glyph.
    [Test]
    public async Task A_tab_in_slide_text_is_a_tab_run()
    {
        var paragraph = Paragraph(TextBox(Slide("pebble", 2), "Topic one"), "Topic one");

        await Assert.That(paragraph.Runs.Any(_ => _.IsTab)).IsTrue();
        await Assert.That(paragraph.Runs.Any(_ => _.Text.Contains('\t') && !_.IsTab)).IsFalse();
    }

    // pebble's body text is tx1 at 70% on the terracotta slide: (232, 207, 202) in PowerPoint's
    // export, not white, which the colour's own alpha composites to; and its text stands 0.1in
    // inside its box.
    [Test]
    public async Task Translucent_text_keeps_its_alpha_and_sits_inside_its_insets()
    {
        var box = TextBox(Slide("pebble", 2), "Topic two");

        await Assert.That(Paragraph(box, "Topic two").Runs[0].Properties.ColorHex).IsEqualTo("B3FFFFFF");
        await Assert.That(box.Insets.Left).IsEqualTo(7.2).Within(0.001);
        await Assert.That(box.Insets.Top).IsEqualTo(3.6).Within(0.001);
    }

    // fashion-infographics-poster's "$2Bn" takes its accent fill from its layout's placeholder, and
    // its disclaimer runs in the layout's two columns.
    [Test]
    public async Task A_placeholder_inherits_its_layouts_fill_and_columns()
    {
        var slide = Slide("fashion-infographics-poster", 2);

        await Assert.That(TextBox(slide, "$2Bn").BackgroundColorHex).IsNotNull();
        await Assert.That(TextBox(slide, "Fabrikam does not guarantee").Columns).IsEqualTo(2);
    }

    // savon's title panel is useBgFill: the white of the slide background, not its style's accent.
    [Test]
    public async Task A_shape_with_use_background_fill_takes_the_slides_background()
    {
        var slide = Slide("savon", 1);
        var background = (FloatingShapeElement) slide[0];

        await Assert.That(slide.OfType<FloatingShapeElement>().Skip(1).Any(_ => _.FillColorHex == background.FillColorHex && _.WidthPoints < background.WidthPoints)).IsTrue();
    }

    // sketchlines' title is a blob its layout draws; the slide's copy declares no geometry.
    [Test]
    public async Task A_placeholder_inherits_its_layouts_geometry() =>
        await Assert.That(TextBox(Slide("sketchlines", 1), "Basic presentation").Subpaths).IsNotNull();

    // luminous's agenda shrinks its 110% lines by 10 percentage points; its titles run violet to
    // pink and are drawn in the colour halfway along.
    [Test]
    public async Task Autofit_line_reduction_and_gradient_text()
    {
        var slide = Slide("luminous", 2);

        await Assert.That(Paragraph(TextBox(slide, "Topic one"), "Topic one").Properties.LineSpacingMultiplier).IsEqualTo(1.0).Within(0.0001);
        await Assert.That(Paragraph(TextBox(slide, "Agenda"), "Agenda").Runs[0].Properties.ColorHex).IsNotEqualTo("000000");
    }

    // lines' title stands on tx1 at 40% over its photo.
    [Test]
    public async Task A_text_boxs_translucent_fill_keeps_its_alpha() =>
        await Assert.That(TextBox(Slide("lines", 1), "Presentation").BackgroundAlpha).IsEqualTo(0.4).Within(0.0001);

    // gradient's agenda body is an orphan placeholder (idx 4294967295): it takes the master's 28pt
    // body, not its layout's first body slot; and its trailing empty paragraph carries no bullet.
    [Test]
    public async Task An_orphan_placeholder_takes_only_the_masters_style()
    {
        var box = TextBox(Slide("gradient", 2), "Topic one");
        var paragraphs = box.Content.OfType<ParagraphElement>().ToList();

        await Assert.That(paragraphs[0].Runs[0].Properties.FontSizePoints).IsEqualTo(28);
        await Assert.That(paragraphs[^1].Runs.Count).IsEqualTo(0);
        await Assert.That(paragraphs[^1].Properties.Numbering).IsNull();
    }

    // PowerPoint blends gradients in gamma 2.2: the probe's 2D3BFF to FF8C2A is (188, 109, 188)
    // at its midpoint, where an sRGB blend gives (150, 100, 148).
    [Test]
    public async Task Gradients_blend_in_gamma_two_point_two() =>
        await Assert.That(SlideGradient.Midpoint("2D3BFF", "FF8C2A")).IsEqualTo("BC6DBC");

    // A gradient of more than two stops keeps them all.
    [Test]
    public async Task A_multi_stop_gradient_keeps_every_stop()
    {
        var fill = new A.GradientFill(
            new A.GradientStopList(
                new A.GradientStop(new A.RgbColorModelHex { Val = "0000FF" }) { Position = 0 },
                new A.GradientStop(new A.RgbColorModelHex { Val = "FF00FF" }) { Position = 50000 },
                new A.GradientStop(new A.RgbColorModelHex { Val = "FF8000" }) { Position = 100000 }),
            new A.LinearGradientFill { Angle = 0 });

        var gradient = ShapeParser.ExtractGradientFill(fill, null)!;

        await Assert.That(gradient.AllStops.Select(_ => _.ColorHex)).IsEquivalentTo(["0000FF", "FF00FF", "FF8000"]);
    }

    // The presets the corpus draws that had no outline of their own: angled-tri-fold-brochure's
    // triangles (a right angle at the top right with adj 100000), and gradient's chevrons,
    // home plate and corners.
    [Test]
    [Arguments("triangle")]
    [Arguments("rtTriangle")]
    [Arguments("chevron")]
    [Arguments("homePlate")]
    [Arguments("corner")]
    public async Task A_corpus_preset_has_an_outline(string preset)
    {
        var geometry = new A.PresetGeometry(new A.AdjustValueList()) { Preset = new(new A.ShapeTypeValues(preset)) };

        await Assert.That(PresetShapeGeometry.TryBuild(geometry, 100, 50)).IsNotNull();
    }

    // sketchlines' agenda layout fills three blobs with photos: each is the picture cut to its
    // outline, two of them duotoned.
    [Test]
    public async Task A_picture_filled_shape_is_its_picture_cut_to_the_shape()
    {
        var pictures = Slide("sketchlines", 2).OfType<FloatingImageElement>().Where(_ => _.ClipSubpaths != null).ToList();

        await Assert.That(pictures.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(pictures.Count(_ => _.DuotoneColorHex != null)).IsEqualTo(2);
    }

    // funky-shapes-dark's wave is a group filled tx1 whose members say a:grpFill.
    [Test]
    public async Task A_group_fill_reaches_its_members() =>
        await Assert.That(Slide("funky-shapes-dark", 1).OfType<FloatingShapeElement>().Count(_ => _.FillColorHex == "FFFFFF")).IsGreaterThan(5);

    // business-cards-horizontal-layout keeps a "Business Cards" title on its slide, hidden.
    [Test]
    public async Task A_hidden_shape_is_not_drawn() =>
        await Assert.That(Slide("business-cards-horizontal-layout", 1).OfType<FloatingTextBoxElement>()
            .Any(_ => _.Content.OfType<ParagraphElement>().Any(paragraph => paragraph.Runs.Any(run => run.Text.Contains("Business Cards"))))).IsFalse();

    // offset's layout frames the slide in dashed connectors.
    [Test]
    public async Task A_dashed_connector_keeps_its_dash() =>
        await Assert.That(Slide("offset", 1).OfType<FloatingShapeElement>().Any(_ => _.LineDashPattern is [4, 3])).IsTrue();

    // leaf's master bullets in Wingdings 0x9E, an orange middle dot; archive's at 80% of the text;
    // color-block's in Arial, drawn in it.
    [Test]
    public async Task A_bullet_keeps_its_glyph_colour_size_and_font()
    {
        var leaf = Paragraph(TextBox(Slide("leaf", 2), "Eco-friendly"), "Eco-friendly").Properties.Numbering!;
        var archive = Paragraph(TextBox(Slide("archive", 2), "Topic one"), "Topic one");
        var colorBlock = Paragraph(TextBox(Slide("color-block", 2), "Introduction"), "Introduction").Properties.Numbering!;

        await Assert.That(leaf.Text).IsEqualTo("·");
        await Assert.That(leaf.ColorHex).IsNotNull();
        await Assert.That(archive.Properties.Numbering!.FontSizePoints!.Value).IsEqualTo(archive.Runs[0].Properties.FontSizePoints * 0.8).Within(0.01);
        await Assert.That(colorBlock is { DrawsInOwnFont: true, FontFamily: "Arial" }).IsTrue();
    }

    // shoji's agenda leaves the master's 9.3pt before and the layout's 6pt after between its items,
    // both: the before carries the after it follows.
    [Test]
    public async Task Space_after_and_space_before_add_up()
    {
        var box = TextBox(Slide("shoji", 2), "Building confidence");

        await Assert.That(Paragraph(box, "Building confidence").Properties.SpacingBeforePoints).IsEqualTo(15.3).Within(0.01);
    }

    // sketchlines' title blob stands on a hard shadow of its own white at half alpha, 107% of its size
    // about its centre, drawn just behind it.
    [Test]
    public async Task A_hard_shadow_is_drawn_behind_its_shape()
    {
        var slide = Slide("sketchlines", 1);
        var title = TextBox(slide, "Basic presentation");
        var shadow = (FloatingShapeElement) slide[slide.IndexOf(title) - 1];

        await Assert.That(shadow.FillAlpha).IsEqualTo(0.5).Within(0.001);
        await Assert.That(shadow.WidthPoints).IsEqualTo(title.WidthPoints * 1.07).Within(0.01);
        await Assert.That(shadow.HorizontalPositionPoints + shadow.WidthPoints / 2)
            .IsEqualTo(title.HorizontalPositionPoints + title.WidthPoints / 2).Within(0.01);
    }
}
