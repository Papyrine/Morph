using static SvgTestSupport;

/// <summary>
/// What <see cref="SvgReader"/> makes of a document's geometry: the frame it stretches over the
/// picture, the basic shapes as figures, transforms, <c>use</c> and <c>clip-path</c> — and the
/// other half of its contract, which documents it gives up so that their raster is drawn instead.
/// </summary>
public class SvgReaderTests
{
    const string square = "d=\"M0 0H10V10H0Z\"";

    static (double X, double Y, double Width, double Height) Frame(string attributes)
    {
        var drawing = Draw($"<svg {Namespace} {attributes}><path {square}/></svg>");
        return (Round(drawing.X), Round(drawing.Y), Round(drawing.Width), Round(drawing.Height));
    }

    // The frame of a document fitted into an image so many pixels each way, with the half pixel
    // Office leaves round the drawing on every side.
    static (double X, double Y, double Width, double Height) Padded(double x, double y, double width, double height, double pixelsWide, double pixelsHigh)
    {
        var padX = width / pixelsWide / 2;
        var padY = height / pixelsHigh / 2;
        return (Round(x - padX), Round(y - padY), Round(width + padX * 2), Round(height + padY * 2));
    }

    static double Round(double value) =>
        Math.Round(value, 9);

    [Test]
    public async Task A_whole_number_viewBox_is_the_frame()
    {
        await Assert.That(Frame("viewBox=\"0 0 96 96\"")).IsEqualTo(Padded(0, 0, 96, 96, 96, 96));
        await Assert.That(Frame("viewBox=\"10 20 24 48\" width=\"24\" height=\"48\"")).IsEqualTo(Padded(10, 20, 24, 48, 24, 48));

        // A size alone is a frame from the origin; a percentage says nothing and reads as absent.
        await Assert.That(Frame("width=\"164\" height=\"131\"")).IsEqualTo(Padded(0, 0, 164, 131, 164, 131));
        await Assert.That(Frame("viewBox=\"0 0 24 24\" width=\"100%\" height=\"100%\"")).IsEqualTo(Padded(0, 0, 24, 24, 24, 24));
    }

    [Test]
    public async Task The_drawing_stands_half_a_pixel_in_from_each_edge()
    {
        // _probe_svgsize: Word draws a 24-unit document's full-size square from 0.31 to 14.71 of a
        // 15.0 canvas, and a 240-unit one's from 0.31 to 144.31 of 144.59 — the image a pixel larger
        // each way than the document, the drawing centred in it.
        await Assert.That(Frame("viewBox=\"0 0 24 24\" width=\"24\" height=\"24\"")).IsEqualTo((-0.5d, -0.5d, 25d, 25d));
        await Assert.That(Frame("viewBox=\"0 0 240 240\"")).IsEqualTo((-0.5d, -0.5d, 241d, 241d));
    }

    [Test]
    public async Task A_fractional_size_is_an_image_of_whole_pixels_with_the_viewBox_fitted_and_centred()
    {
        // menus/05's icon: 44.02 by 25.36 is an image 44 by 25 (Office frames it 548640 by 311728
        // EMU, 44:25). The height binds, so the drawing is scaled by 25/25.36 and the frame is the
        // viewBox widened to 44 at that scale, the spare split left and right.
        var (x, y, width, height) = Frame("width=\"44.02\" height=\"25.36\" viewBox=\"0 0 44.02 25.36\"");

        var fitted = 44 * 25.36 / 25;
        await Assert.That((x, y, width, height)).IsEqualTo(Padded(-(fitted - 44.02) / 2, 0, fitted, 25.36, 44, 25));
    }

    [Test]
    public async Task A_half_pixel_rounds_up()
    {
        // 38.5 by 22.16 is 39 by 22 — the frame Office gives it is 39:22 — not the 38 that rounding
        // to even would make it.
        var (_, _, width, height) = Frame("viewBox=\"0 0 38.5 22.16\"");

        await Assert.That(width / height).IsEqualTo(40d / 23).Within(1e-9);
    }

    [Test]
    public async Task PreserveAspectRatio_places_the_viewBox_in_the_image()
    {
        // A 10 by 10 viewBox in an image 20 by 10 fits at scale 1, leaving 10 spare across.
        await Assert.That(Frame("viewBox=\"0 0 10 10\" width=\"20\" height=\"10\"")).IsEqualTo(Padded(-5, 0, 20, 10, 20, 10));
        await Assert.That(Frame("viewBox=\"0 0 10 10\" width=\"20\" height=\"10\" preserveAspectRatio=\"xMinYMid\"")).IsEqualTo(Padded(0, 0, 20, 10, 20, 10));
        await Assert.That(Frame("viewBox=\"0 0 10 10\" width=\"20\" height=\"10\" preserveAspectRatio=\"xMaxYMax meet\"")).IsEqualTo(Padded(-10, 0, 20, 10, 20, 10));

        // Sliced, it covers the image at scale 2 and the frame is the middle half of its height.
        await Assert.That(Frame("viewBox=\"0 0 10 10\" width=\"20\" height=\"10\" preserveAspectRatio=\"xMidYMid slice\"")).IsEqualTo(Padded(0, 2.5, 10, 5, 20, 10));

        // And with none the viewBox itself is stretched.
        await Assert.That(Frame("viewBox=\"0 0 10 10\" width=\"20\" height=\"10\" preserveAspectRatio=\"none\"")).IsEqualTo(Padded(0, 0, 10, 10, 20, 10));
    }

    [Test]
    public async Task A_size_in_real_units_is_read_at_96_to_the_inch()
    {
        // One inch by half an inch is 96 by 48 pixels, which a viewBox twice as wide as tall fills
        // exactly; were the points read as pixels the image would be 96 by 36 and the frame taller.
        var (_, y, width, height) = Frame("viewBox=\"0 0 200 100\" width=\"1in\" height=\"36pt\"");

        await Assert.That(width).IsEqualTo(200 + 200d / 96).Within(1e-9);
        await Assert.That(height).IsEqualTo(100 + 100d / 48).Within(1e-9);
        await Assert.That(y).IsEqualTo(-100d / 48 / 2).Within(1e-9);
    }

    [Test]
    public async Task The_basic_shapes_become_figures()
    {
        await Assert.That(Describe(DrawBody("<rect x=\"1\" y=\"2\" width=\"10\" height=\"20\"/>").Paths.Single().Figures)).IsEqualTo("M1,2 L11,2 L11,22 L1,22 Z");
        await Assert.That(Describe(DrawBody("<polygon points=\"0,0 10,0 5,8\"/>").Paths.Single().Figures)).IsEqualTo("M0,0 L10,0 L5,8 Z");
        await Assert.That(Describe(DrawBody("<polyline points=\"0 0 10 0 5 8\" stroke=\"#000\"/>").Paths.Single().Figures)).IsEqualTo("M0,0 L10,0 L5,8");
        await Assert.That(Describe(DrawBody("<line x1=\"1\" y1=\"2\" x2=\"3\" y2=\"4\" stroke=\"#000\"/>").Paths.Single().Figures)).IsEqualTo("M1,2 L3,4");
        await Assert.That(Describe(DrawBody("<circle cx=\"10\" cy=\"10\" r=\"10\"/>").Paths.Single().Figures))
            .IsEqualTo("M20,10 C20,15.523 15.523,20 10,20 C4.477,20 0,15.523 0,10 C0,4.477 4.477,0 10,0 C15.523,0 20,4.477 20,10 Z");
        await Assert.That(Describe(DrawBody("<ellipse cx=\"0\" cy=\"0\" rx=\"20\" ry=\"10\"/>").Paths.Single().Figures))
            .StartsWith("M20,0 C20,5.523 11.046,10 0,10 ");
    }

    [Test]
    public async Task A_rectangle_rounds_its_corners_by_one_radius_when_given_one_and_no_further_than_its_middle()
    {
        var figures = DrawBody("<rect width=\"20\" height=\"10\" rx=\"4\"/>").Paths.Single().Figures;
        await Assert.That(Describe(figures)).StartsWith("M4,0 L16,0 C18.209,0 20,1.791 20,4 L20,6 ");

        // A radius past the middle of a side stops there: the short sides become half circles.
        var capped = DrawBody("<rect width=\"20\" height=\"10\" rx=\"50\"/>").Paths.Single().Figures;
        await Assert.That(Describe(capped)).StartsWith("M10,0 L10,0 C15.523,0 20,2.239 20,5 L20,5 ");
    }

    [Test]
    public async Task A_line_has_no_inside_to_fill_and_a_shape_of_no_size_is_not_there()
    {
        await Assert.That(DrawBody("<line x1=\"1\" y1=\"2\" x2=\"3\" y2=\"4\" fill=\"#F00\"/>").Paths.Count).IsEqualTo(0);
        await Assert.That(DrawBody("<rect width=\"0\" height=\"10\"/><circle r=\"0\"/><ellipse rx=\"5\"/><path d=\"\"/>").Paths.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Transforms_are_multiplied_down_the_tree_and_applied_to_every_point()
    {
        var path = DrawBody("<g transform=\"translate(-935 -195)\"><g transform=\"scale(2)\"><path d=\"M500 100 L510 100 C510 105 505 110 500 110Z\" transform=\"translate(1 2)\"/></g></g>").Paths.Single();

        await Assert.That(Describe(path.Figures)).IsEqualTo("M67,9 L87,9 C87,19 77,29 67,29 Z");
    }

    [Test]
    public async Task A_stroke_width_is_scaled_by_the_transforms_over_it()
    {
        var path = DrawBody("<g transform=\"rotate(30) scale(3)\"><path d=\"M0 0L10 0\" stroke=\"#000\" stroke-width=\"2\"/></g>").Paths.Single();

        await Assert.That(path.Stroke!.Width).IsEqualTo(6d).Within(1e-9);
    }

    [Test]
    public async Task A_use_draws_what_it_points_at_where_it_says_in_its_own_style()
    {
        var paths = DrawBody(
            """
            <defs><g id="pair"><rect id="tile" width="4" height="4"/><rect x="5" width="4" height="4" fill="#00F"/></g></defs>
            <use href="#pair" x="10" y="20" fill="#F00"/>
            <use xmlns:xlink="http://www.w3.org/1999/xlink" xlink:href="#tile" transform="scale(2)" width="100%" height="100%"/>
            """).Paths;

        await Assert.That(paths.Count).IsEqualTo(3);
        await Assert.That(Describe(paths[0].Figures)).IsEqualTo("M10,20 L14,20 L14,24 L10,24 Z");
        await Assert.That(paths[0].FillColorHex).IsEqualTo("FF0000");
        await Assert.That(paths[1].FillColorHex).IsEqualTo("0000FF");
        await Assert.That(Describe(paths[2].Figures)).IsEqualTo("M0,0 L8,0 L8,8 L0,8 Z");
    }

    [Test]
    public async Task A_clip_path_rides_on_every_path_under_it_in_the_space_of_what_it_is_set_on()
    {
        // The shape Office writes: a rectangle in defs, a clipPath that uses it, set on a group.
        var paths = DrawBody(
            $"""
             <defs><rect id="box" x="1" y="2" width="30" height="40" fill="none"/></defs>
             <clipPath id="clip"><use xmlns:xlink="http://www.w3.org/1999/xlink" width="100%" height="100%" xlink:href="#box" overflow="visible"/></clipPath>
             <g transform="translate(100 0)" clip-path="url(#clip)"><path {square}/><path {square}/></g>
             <path {square}/>
             """).Paths;

        await Assert.That(Describe(paths[0].Clip!.Figures)).IsEqualTo("M101,2 L131,2 L131,42 L101,42 Z");
        await Assert.That(paths[0].Clip!.EvenOdd).IsFalse();
        await Assert.That(paths[0].Clip!.Parent).IsNull();

        // One instance for the run, so a painter sets the clip once; and none outside the group.
        await Assert.That(ReferenceEquals(paths[0].Clip, paths[1].Clip)).IsTrue();
        await Assert.That(paths[2].Clip).IsNull();
    }

    [Test]
    public async Task Nested_clip_paths_chain_and_keep_their_own_rule()
    {
        var path = DrawBody(
            $"""
             <defs>
               <clipPath id="outer"><rect width="50" height="50"/></clipPath>
               <clipPath id="inner"><path d="M0 0H20V20H0Z M5 5H15V15H5Z" clip-rule="evenodd" transform="translate(1 1)"/></clipPath>
             </defs>
             <g clip-path="url(#outer)"><g clip-path="url(#inner)"><path {square}/></g></g>
             """).Paths.Single();

        await Assert.That(path.Clip!.EvenOdd).IsTrue();
        await Assert.That(Describe(path.Clip.Figures)).StartsWith("M1,1 L21,1 ");
        await Assert.That(path.Clip.Parent!.EvenOdd).IsFalse();
        await Assert.That(Describe(path.Clip.Parent.Figures)).IsEqualTo("M0,0 L50,0 L50,50 L0,50 Z");
        await Assert.That(path.Clip.Parent.Parent).IsNull();
    }

    [Test]
    public async Task An_empty_clip_path_leaves_nothing() =>
        await Assert.That(DrawBody($"<clipPath id=\"none\"/><g clip-path=\"url(#none)\"><path {square}/></g>").Paths.Count).IsEqualTo(0);

    [Test]
    public async Task A_group_fades_its_shapes_one_by_one_when_they_keep_clear_of_each_other()
    {
        var paths = DrawBody("<g opacity=\"0.5\"><rect width=\"10\" height=\"10\"/><rect x=\"20\" width=\"10\" height=\"10\" fill-opacity=\"0.5\"/></g>").Paths;

        await Assert.That(paths[0].FillAlpha).IsEqualTo(0.5).Within(1e-9);
        await Assert.That(paths[1].FillAlpha).IsEqualTo(0.25).Within(1e-9);
    }

    [Test]
    public async Task A_group_faded_over_shapes_that_overlap_gives_the_document_up()
    {
        // Faded one by one, the lower square would show through the upper where they cross; faded
        // as a group it does not. postcards/02's brush strokes are four such paths a group.
        await Assert.That(Unsupported("<g opacity=\"0.8\"><rect width=\"10\" height=\"10\"/><rect x=\"5\" width=\"10\" height=\"10\"/></g>"))
            .IsEqualTo("opacity on a group of shapes that overlap");
        await Assert.That(Unsupported($"<path {square} opacity=\"0.5\" stroke=\"#000\"/>"))
            .IsEqualTo("opacity on a shape that is both filled and stroked");
    }

    [Test]
    public async Task What_the_reader_does_not_draw_gives_the_document_up_and_is_named()
    {
        const string gradient = "<linearGradient id=\"fade\"><stop offset=\"0\" stop-color=\"#A7B17D\"/><stop offset=\"1\" stop-color=\"#DCE1B1\"/></linearGradient>";

        await Assert.That(Unsupported($"{gradient}<path {square} fill=\"url(#fade)\"/>")).IsEqualTo("a gradient fill");
        await Assert.That(Unsupported($"{gradient}<path {square} fill=\"none\" stroke=\"url(#fade)\"/>")).IsEqualTo("a gradient stroke");
        await Assert.That(Unsupported($"<pattern id=\"dots\"/><path {square} fill=\"url(#dots)\"/>")).IsEqualTo("a pattern fill");
        await Assert.That(Unsupported($"<mask id=\"m\"/><path {square} mask=\"url(#m)\"/>")).IsEqualTo("a mask");
        await Assert.That(Unsupported($"<filter id=\"f\"/><g filter=\"url(#f)\"><path {square}/></g>")).IsEqualTo("a filter");
        await Assert.That(Unsupported("<image width=\"10\" height=\"10\" href=\"data:image/png;base64,AAAA\"/>")).IsEqualTo("the image element");
        await Assert.That(Unsupported("<text x=\"1\" y=\"10\">Hi</text>")).IsEqualTo("the text element");
        await Assert.That(Unsupported($"<svg viewBox=\"0 0 1 1\"><path {square}/></svg>")).IsEqualTo("a nested svg element");
        await Assert.That(Unsupported($"<path {square} fill=\"none\" stroke=\"#000\" stroke-dasharray=\"4 2\"/>")).IsEqualTo("a dashed stroke");
        await Assert.That(Unsupported($"<path {square} fill=\"none\" stroke=\"#000\" transform=\"scale(1 3)\"/>")).IsEqualTo("a stroke under a transform that squashes or skews it");
        await Assert.That(Unsupported("<path d=\"M0 0 L5\"/>")).IsEqualTo("path data that does not parse");
        await Assert.That(Unsupported("<rect width=\"50%\" height=\"10\"/>")).IsEqualTo("the length '50%'");
        await Assert.That(Unsupported($"<clipPath id=\"two\"><rect width=\"5\" height=\"5\"/><rect x=\"3\" width=\"5\" height=\"5\"/></clipPath><path {square} clip-path=\"url(#two)\"/>"))
            .IsEqualTo("a clipPath of more than one shape");
        await Assert.That(Unsupported("<symbol id=\"s\"/><use href=\"#s\"/>")).IsEqualTo("a use of a symbol or a nested svg");
        await Assert.That(Unsupported("<g id=\"loop\"><use href=\"#loop\"/></g>")).IsEqualTo("a use that leads back to itself");
    }

    [Test]
    public async Task Only_what_is_actually_drawn_can_give_a_document_up()
    {
        const string gradient = "<linearGradient id=\"fade\"><stop offset=\"0\" stop-color=\"#A7B17D\"/></linearGradient>";

        // A gradient nothing uses, one set on a group whose shapes all name their own colour, and
        // anything at all under display:none or in defs never reaches the page.
        await Assert.That(DrawBody($"{gradient}<path {square}/>").Paths.Count).IsEqualTo(1);
        await Assert.That(DrawBody($"{gradient}<g fill=\"url(#fade)\"><path {square} fill=\"#123456\"/></g>").Paths.Single().FillColorHex).IsEqualTo("123456");
        await Assert.That(DrawBody($"<g display=\"none\"><text>Hi</text></g><defs><image/></defs><path {square}/>").Paths.Count).IsEqualTo(1);

        // A squash is no trouble to a fill, nor a dash array to a shape with no stroke.
        await Assert.That(Describe(DrawBody($"<path {square} transform=\"scale(1 3)\" stroke-dasharray=\"4 2\"/>").Paths.Single().Figures)).IsEqualTo("M0,0 L10,0 L10,30 L0,30 Z");

        // An editor's own elements are not SVG and not the picture.
        await Assert.That(DrawBody($"<sodipodi:namedview xmlns:sodipodi=\"http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd\"/><path {square}/>").Paths.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Bytes_that_are_not_an_SVG_document_are_given_up_not_thrown_on()
    {
        await Assert.That(SvgReader.Read("PNG"u8.ToArray()).Unsupported).IsEqualTo("markup that is not well-formed XML");
        await Assert.That(SvgReader.Read([]).Unsupported).IsEqualTo("markup that is not well-formed XML");
        await Assert.That(Read("<html xmlns=\"http://www.w3.org/1999/xhtml\"/>").Unsupported).IsEqualTo("a root that is not an svg element");
        await Assert.That(Read($"<svg {Namespace}><path {square}/></svg>").Unsupported).IsEqualTo("neither a viewBox nor a size");

        // A DOCTYPE is skipped, but its entities are not expanded — nothing is fetched or unrolled.
        await Assert.That(Read($"<!DOCTYPE svg [<!ENTITY fill \"#F00\">]><svg {Namespace} viewBox=\"0 0 9 9\"><path {square} fill=\"&fill;\"/></svg>").Unsupported)
            .IsEqualTo("markup that is not well-formed XML");
        await Assert.That(Draw($"<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\"><svg {Namespace} viewBox=\"0 0 9 9\"><path {square}/></svg>").Paths.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_document_nested_past_all_reason_is_given_up_rather_than_followed_down()
    {
        var body = string.Concat(Enumerable.Repeat("<g>", 200)) + $"<path {square}/>" + string.Concat(Enumerable.Repeat("</g>", 200));

        await Assert.That(Unsupported(body)).IsEqualTo("nesting more than 64 deep");
    }
}
