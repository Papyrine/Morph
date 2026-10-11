using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// The envelope text warps (<see cref="WordArtEnvelope"/>): the preset definitions' guide formulas, and
/// Word's stretch of the text's ink box onto them. Word-probed 2026-10-11 (<c>_probe_warp_a</c> /
/// <c>_probe_warp_b</c>: 400x100pt boxes behind a hairline outline, black Arial capitals under each
/// preset, the ink's top and bottom read against the box at every eighth of its width).
/// </summary>
internal class WordArtEnvelopeTests
{
    // What Word drew, as fractions of the box height: (preset, fraction across, top edge, bottom edge).
    // The sampling window is a fortieth of the width, which the curved presets' tolerance allows for.
    [Test]
    [Arguments(WordArtTransform.Triangle, 0.25, 0.245, 1.003)]
    [Arguments(WordArtTransform.Triangle, 0.375, 0.120, 1.003)]
    [Arguments(WordArtTransform.Inflate, 0.125, 0.106, 0.902)]
    [Arguments(WordArtTransform.Inflate, 0.25, 0.048, 0.960)]
    [Arguments(WordArtTransform.Deflate, 0.25, 0.149, 0.859)]
    [Arguments(WordArtTransform.Deflate, 0.375, 0.182, 0.826)]
    [Arguments(WordArtTransform.CanUp, 0.125, 0.048, 0.907)]
    [Arguments(WordArtTransform.CanUp, 0.25, 0.024, 0.878)]
    [Arguments(WordArtTransform.CanDown, 0.25, 0.130, 0.984)]
    [Arguments(WordArtTransform.CanDown, 0.375, 0.144, 0.998)]
    [Arguments(WordArtTransform.Wave, 0.125, 0.024, 0.778)]
    [Arguments(WordArtTransform.Wave, 0.75, 0.245, 0.994)]
    [Arguments(WordArtTransform.Wave, 0.875, 0.235, 0.984)]
    public async Task The_envelope_runs_where_Word_draws_it(WordArtTransform transform, double across, double wordTop, double wordBottom)
    {
        // A 400x100 box: the deflating warps measure their depth in the shorter side, the height here.
        var top = WordArtEnvelope.Point(across, 0, transform, null, 1);
        var bottom = WordArtEnvelope.Point(across, 1, transform, null, 1);

        await Assert.That(top.X).IsEqualTo(across).Within(0.0001);
        await Assert.That(top.Y).IsEqualTo(wordTop).Within(0.012);
        await Assert.That(bottom.Y).IsEqualTo(wordBottom).Within(0.012);
    }

    // textSlantUp under "Hxgy": the H's top at the left end read 0.552 and its foot 0.902 — the baseline
    // 0.773 of the way down the ink, carried 0.773 of the way from the top guide (0.556) to the bottom (1).
    [Test]
    public async Task A_point_of_the_ink_lands_its_own_share_of_the_way_between_the_guides()
    {
        var foot = WordArtEnvelope.Point(0, 0.773, WordArtTransform.SlantUp, null, 1);
        var cap = WordArtEnvelope.Point(0, 0, WordArtTransform.SlantUp, null, 1);

        await Assert.That(cap.Y).IsEqualTo(0.552).Within(0.01);
        await Assert.That(foot.Y).IsEqualTo(0.902).Within(0.01);
    }

    [Test]
    public async Task An_adjust_value_moves_the_guides_and_is_pinned_to_the_presets_range()
    {
        // A chevron's rise is its adjust, 25% by default and never more than half the height.
        await Assert.That(WordArtEnvelope.Point(0, 0, WordArtTransform.ChevronUp, null, 1).Y).IsEqualTo(0.25).Within(0.0001);
        await Assert.That(WordArtEnvelope.Point(0, 0, WordArtTransform.ChevronUp, 0.4, 1).Y).IsEqualTo(0.4).Within(0.0001);
        await Assert.That(WordArtEnvelope.Point(0, 0, WordArtTransform.ChevronUp, 0.9, 1).Y).IsEqualTo(0.5).Within(0.0001);

        // Fading up narrows the top edge and leaves the bottom alone.
        await Assert.That(WordArtEnvelope.Point(0, 0, WordArtTransform.FadeUp, null, 1).X).IsEqualTo(0.33333).Within(0.0001);
        await Assert.That(WordArtEnvelope.Point(0, 1, WordArtTransform.FadeUp, null, 1).X).IsEqualTo(0).Within(0.0001);
    }

    // textPlain: "HHHH", "xxxx", "gggg" and "Hxgy" each spanned 0.001..1.001 by 0.005..1.003 of the box,
    // at 36pt and at 12pt alike — the INK box is what is stretched, whatever its size or place.
    [Test]
    public async Task The_ink_box_of_the_outline_fills_the_rect()
    {
        IReadOnlyList<(float X, float Y)> small = [(10, 50), (30, 50), (30, 58), (10, 58)];
        IReadOnlyList<(float X, float Y)> offset = [(14, 52), (18, 52), (18, 56)];

        var warped = WordArtEnvelope.Warp([small, offset], WordArtTransform.Plain, null, 100, 200, 400, 100);
        var points = warped.SelectMany(_ => _).ToList();

        await Assert.That(points.Min(_ => _.X)).IsEqualTo(100f).Within(0.01f);
        await Assert.That(points.Max(_ => _.X)).IsEqualTo(500f).Within(0.01f);
        await Assert.That(points.Min(_ => _.Y)).IsEqualTo(200f).Within(0.01f);
        await Assert.That(points.Max(_ => _.Y)).IsEqualTo(300f).Within(0.01f);

        // The same outline four times the size lands on the same points.
        IReadOnlyList<(float X, float Y)> large = [.. small.Select(_ => (_.X * 4, _.Y * 4))];
        IReadOnlyList<(float X, float Y)> largeOffset = [.. offset.Select(_ => (_.X * 4, _.Y * 4))];
        var again = WordArtEnvelope.Warp([large, largeOffset], WordArtTransform.Plain, null, 100, 200, 400, 100).SelectMany(_ => _).ToList();
        await Assert.That(again.Count).IsEqualTo(points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            await Assert.That(again[index].X).IsEqualTo(points[index].X).Within(0.01f);
            await Assert.That(again[index].Y).IsEqualTo(points[index].Y).Within(0.01f);
        }
    }

    [Test]
    public async Task An_edge_that_crosses_the_text_is_cut_so_it_follows_the_curve()
    {
        IReadOnlyList<(float X, float Y)> bar = [(0, 0), (100, 0), (100, 10), (0, 10)];

        var warped = WordArtEnvelope.Warp([bar], WordArtTransform.Inflate, null, 0, 0, 400, 100)[0];

        // The bar's top edge bows up to the rect's top at its middle, and its upright ends stay two points.
        await Assert.That(warped.Count).IsGreaterThan(100);
        await Assert.That(warped.Min(_ => _.Y)).IsEqualTo(0f).Within(0.5f);
        await Assert.That(warped[0].Y).IsEqualTo(18.75f).Within(0.01f);
    }

    [Test]
    public async Task Each_preset_parses_to_its_own_warp_with_its_adjust_value()
    {
        const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
        static string Shape(int id, string warp) =>
            $"""
             <w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><wp:extent cx="5486400" cy="1371600"/><wp:docPr id="{id}" name="w{id}"/>
             <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
             <wps:wsp xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:cNvSpPr txBox="1"/><wps:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="5486400" cy="1371600"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></wps:spPr>
             <wps:txbx><w:txbxContent><w:p><w:r><w:rPr><w:sz w:val="72"/></w:rPr><w:t>Text</w:t></w:r></w:p></w:txbxContent></wps:txbx>
             <wps:bodyPr lIns="0" tIns="0" rIns="0" bIns="0" fromWordArt="1">{warp}</wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
             """;

        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var body =
                Shape(1, """<a:prstTxWarp prst="textWave2"><a:avLst/></a:prstTxWarp>""") +
                Shape(2, """<a:prstTxWarp prst="textTriangleInverted"><a:avLst><a:gd name="adj" fmla="val 30000"/></a:avLst></a:prstTxWarp>""") +
                Shape(3, """<a:prstTxWarp prst="textDoubleWave1"><a:avLst><a:gd name="adj1" fmla="val 10000"/><a:gd name="adj2" fmla="val 0"/></a:avLst></a:prstTxWarp>""");
            document.AddMainDocumentPart().Document = new($"<w:document {wNs}><w:body>{body}</w:body></w:document>");
        }

        stream.Position = 0;
        var warps = new DocumentParser().Parse(stream).Elements.OfType<WordArtElement>().ToList();

        await Assert.That(warps.Count).IsEqualTo(3);
        await Assert.That(warps[0].Transform).IsEqualTo(WordArtTransform.WaveInverted);
        await Assert.That(warps[0].WarpAdjust).IsNull();
        await Assert.That(warps[1].Transform).IsEqualTo(WordArtTransform.TriangleInverted);
        await Assert.That(warps[1].WarpAdjust!.Value).IsEqualTo(0.3).Within(0.0001);
        await Assert.That(warps[2].Transform).IsEqualTo(WordArtTransform.DoubleWave);
        await Assert.That(warps[2].WarpAdjust!.Value).IsEqualTo(0.1).Within(0.0001);
        await Assert.That(warps[1].Insets).IsEqualTo(new WordArtInsets(0, 0, 0, 0));
    }
}
