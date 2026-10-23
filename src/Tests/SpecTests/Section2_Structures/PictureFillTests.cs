using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// A shape filled with a picture: the part of the picture its box shows, the outline the picture is
/// cut to, and where a member of a nested group stands.
/// </summary>
public class PictureFillTests
{
    const string a = "http://schemas.openxmlformats.org/drawingml/2006/main";

    static ImageCrop? Crop(string inner) =>
        DocumentParser.ReadCrop(new A.BlipFill($"<a:blipFill xmlns:a=\"{a}\">{inner}</a:blipFill>"));

    static string Scenario(string name) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", name.Replace('/', Path.DirectorySeparatorChar), "input.docx");

    static ParsedDocument Parse(string name)
    {
        using var stream = File.OpenRead(Scenario(name));
        return new DocumentParser().Parse(stream);
    }

    // a:fillRect insets are from the box's edges, negative beyond them: newsletters/03's banner
    // stretches its picture a third of the box's width past the left edge and half past the right,
    // so the box shows the picture from 18.2% in to 26.6% short of its right edge.
    [Test]
    public async Task A_fill_rectangle_beyond_the_box_crops_the_picture()
    {
        var crop = Crop("""<a:srcRect/><a:stretch><a:fillRect l="-33058" t="-1" r="-48258" b="3"/></a:stretch>""")!;

        await Assert.That(crop.Left).IsEqualTo(0.33058 / 1.81316).Within(1e-4);
        await Assert.That(crop.Right).IsEqualTo(0.48258 / 1.81316).Within(1e-4);
        await Assert.That(Math.Abs(crop.Top)).IsLessThan(1e-4);

        // The picture drawn whole, at the size that puts that part on the box, is the fill rectangle.
        var (x, _, width, _) = crop.Expand(0, 0, 100, 50);
        await Assert.That(x).IsEqualTo(-33.058).Within(0.01);
        await Assert.That(width).IsEqualTo(181.316).Within(0.01);
    }

    // An inset fill rectangle is padding, as a negative a:srcRect is.
    [Test]
    public async Task A_fill_rectangle_inside_the_box_pads_the_picture()
    {
        var crop = Crop("""<a:stretch><a:fillRect l="25000" r="25000"/></a:stretch>""")!;

        await Assert.That(crop.Left).IsEqualTo(-0.5).Within(1e-9);
        await Assert.That(crop.Right).IsEqualTo(-0.5).Within(1e-9);
        await Assert.That(crop.HasPadding).IsTrue();
    }

    // The source rectangle picks the part of the picture that is stretched over the fill rectangle.
    [Test]
    public async Task The_two_rectangles_fold_into_one_crop()
    {
        var crop = Crop("""<a:srcRect l="20000" r="30000"/><a:stretch><a:fillRect l="-100000" r="-100000"/></a:stretch>""")!;

        // Half the picture is used (20%..70%), and the box shows the middle third of that.
        await Assert.That(crop.Left).IsEqualTo(0.2 + 0.5 / 3).Within(1e-9);
        await Assert.That(crop.Right).IsEqualTo(0.3 + 0.5 / 3).Within(1e-9);
    }

    [Test]
    public async Task An_empty_fill_rectangle_is_no_crop()
    {
        await Assert.That(Crop("<a:srcRect/><a:stretch><a:fillRect/></a:stretch>")).IsNull();
        await Assert.That(Crop("""<a:srcRect l="10000"/><a:stretch><a:fillRect/></a:stretch>""")!.Left).IsEqualTo(0.1);
    }

    // brochures/04's brick wall is a triangle cut from a photo; its construction photo a rectangle
    // whose fill reaches past it. Both paint as pictures, carrying the cut and the crop.
    [Test]
    public async Task A_picture_filled_shape_paints_cut_to_its_outline()
    {
        var document = Parse("brochures/04");
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility).Layout(document.Elements, document.PageSettings);

        static IEnumerable<PlacedImage> Images(IEnumerable<PlacedItem> items) =>
            items.SelectMany(_ => _ switch
            {
                PlacedImage image => [image],
                PlacedTableRow row => row.Cells.SelectMany(cell => Images(cell.Floats).Concat(Images(cell.Content))),
                _ => (IEnumerable<PlacedImage>) []
            });

        var images = laidOut.Pages.SelectMany(_ => Images(_.Items)).Where(_ => _.Data != null).ToList();
        var triangle = images.Single(_ => _.ClipSubpaths is [{Count: 3 or 4}]);
        var cropped = images.Where(_ => _.Crop is {Left: > 0.1}).ToList();

        await Assert.That(triangle.Width).IsGreaterThan(700);
        await Assert.That(cropped.Count).IsEqualTo(2);
    }

    // newsletters/03's page-4 banner holds its two hexagons in a nested group drawn at 65% of the
    // size they are declared at: 3,995,318 EMU wide in the group's own space, 2,586,628 on the page.
    [Test]
    public async Task A_member_of_a_nested_group_takes_the_groups_scale()
    {
        var groups = Runs(Parse("newsletters/03").Elements).Select(_ => _.InlineShapeGroup).OfType<InlineShapeGroup>().ToList();
        var banner = groups
            .First(_ => Math.Abs(_.ChildExtentX - 7315200) < 1 &&
                        _.Shapes.Any(_ => _.Subpaths is [{Count: 6}]));
        var hexagons = banner.Shapes.Where(_ => _.Subpaths is [{Count: 6}]).ToList();

        await Assert.That(hexagons.Count).IsEqualTo(2);
        foreach (var hexagon in hexagons)
        {
            await Assert.That(hexagon.Width).IsEqualTo(3995318 * 2877612.0 / 4444768).Within(400);
            await Assert.That(hexagon.X).IsGreaterThanOrEqualTo(4221480);
            await Assert.That(hexagon.X + hexagon.Width).IsLessThanOrEqualTo(4221480 + 2877612 + 1);
        }
    }

    static IEnumerable<Run> Runs(IEnumerable<DocumentElement> elements) =>
        elements.SelectMany(_ => _ switch
        {
            ParagraphElement paragraph => paragraph.Runs,
            TableElement table => table.Rows.SelectMany(row => row.Cells).SelectMany(cell => Runs(cell.Content)),
            _ => []
        });

    // business-plans/12's SWOT ring: an inline doughnut chart of four equal slices around a hole
    // half its width, each slice the colour its c:dPt names, starting at twelve o'clock.
    [Test]
    public async Task An_inline_doughnut_chart_is_a_group_of_its_slices()
    {
        var chart = Runs(Parse("business-plans/12").Elements)
            .Select(_ => _.InlineShapeGroup)
            .OfType<InlineShapeGroup>()
            .Single(_ => _.Shapes.Count == 4 &&
                         _.Shapes.All(_ => _.Subpaths is [{Count: > 40}]));
        await Assert.That(chart.Shapes.Select(_ => _.FillColorHex).Distinct().Count()).IsEqualTo(4);

        // One plot circle, square, inside the chart's box.
        var first = chart.Shapes[0];
        await Assert.That(first.Width).IsEqualTo(first.Height).Within(1);
        await Assert.That(chart.Shapes.All(_ => _.X == first.X &&
                                                _.Y == first.Y &&
                                                _.Width == first.Width)).IsTrue();
        await Assert.That(first.Width).IsLessThanOrEqualTo(chart.ChildExtentX);

        // The first slice is the top right quarter of the ring: from the hole's rim to the circle's.
        var contour = first.Subpaths![0];
        await Assert.That(contour.All(_ => _ is { X: >= 0.5 - 1e-9, Y: <= 0.5 + 1e-9 })).IsTrue();
        var radii = contour.Select(_ => Math.Sqrt(Math.Pow(_.X - 0.5, 2) + Math.Pow(_.Y - 0.5, 2))).ToList();
        await Assert.That(radii.Max()).IsEqualTo(0.5).Within(1e-6);
        await Assert.That(radii.Min()).IsEqualTo(0.25).Within(1e-6);

        // Each is stroked in the white its c:dPt outline names.
        await Assert.That(chart.Shapes.All(_ => _ is { LineWidthEmu: 19050, ColorHex: "FFFFFF" })).IsTrue();
    }
}
