/// <summary>
/// What a bordered run (<c>w:bdr</c>) costs the line it stands on, as against where it is drawn.
/// Word-probed 2026-10-11 (<c>_probe_runbdr5</c>): six plain words, then one or two bordered runs,
/// then a plain tail, 12pt Calibri, set once per whole pixel of measure on Word's 120-dpi grid; the
/// thresholds below are the narrowest measures at which the last bordered run still stands on the
/// first line. Each side of the run costs the border's whole declared stack plus <c>w:space</c>,
/// unfloored (<see cref="BorderStroke.RunBorderReserve"/>), which is more than the pixel-floored
/// inset the run is drawn at (<see cref="BorderStroke.RunBorderGlyphInset"/>).
/// </summary>
public class RunBorderLineChargeTests
{
    const string prefix = "nnnn nnnn nnnn nnnn nnnn nnnn";
    const string tail = " zzzz zzzz zzzz";

    static Run Text(string text, BorderEdge? border = null) =>
        new() {Text = text, Properties = new() {FontFamily = "Calibri", FontSizePoints = 12, Border = border}};

    // "tight" borders the bare word; "padded" borders it with a space either side inside the run and
    // two plain spaces between runs, the shape border_style_variants uses.
    static ParagraphElement Line(BorderEdge border, bool padded, int count)
    {
        List<Run> runs = [Text(prefix)];
        foreach (var word in new[] {"XXXX", "YYYY"}.Take(count))
        {
            if (padded)
            {
                runs.Add(Text("  "));
                runs.Add(Text($" {word} ", border));
            }
            else
            {
                runs.Add(Text(" "));
                runs.Add(Text(word, border));
            }
        }

        runs.Add(Text(tail));
        return new() {Runs = runs, Properties = new()};
    }

    static string FirstLine(ParagraphElement paragraph, int measurePixels)
    {
        var measure = (float) (measurePixels * 72.0 / CanonicalTextMeasurer.ReferenceDpi);
        return string.Concat(LayoutTestFonts.Measurer.LayoutLineContents(paragraph, measure)[0].Runs.Select(_ => _.Text));
    }

    [Test]
    [Arguments("Single", 0.5, 0, 323, 368, 332, 391)]
    [Arguments("Single", 2.25, 1, 332, 387, 341, 409)]
    [Arguments("Single", 2.25, 4, 342, 407, 351, 429)]
    [Arguments("Single", 6.0, 0, 341, 405, 350, 427)]
    [Arguments("Double", 2.25, 1, 347, 417, 356, 439)]
    [Arguments("Triple", 2.25, 1, 362, 447, 371, 469)]
    [Arguments("ThinThickThinMediumGap", 2.25, 1, 347, 417, 356, 439)]
    [Arguments("ThickThinLargeGap", 2.25, 1, 340, 402, 349, 424)]
    [Arguments("ThinThickSmallGap", 2.25, 1, 337, 397, 346, 419)]
    [Arguments("Single", 0.75, 8, 351, 423, 360, 446)]
    public async Task A_bordered_run_wraps_where_Word_wraps_it(string style, double width, int space, int tightOne, int tightTwo, int paddedOne, int paddedTwo)
    {
        var border = new BorderEdge {IsVisible = true, Style = Enum.Parse<BorderLineStyle>(style), WidthPoints = width, SpacePoints = space};
        foreach (var (padded, count, threshold) in new[] {(false, 1, tightOne), (false, 2, tightTwo), (true, 1, paddedOne), (true, 2, paddedTwo)})
        {
            var last = count == 1 ? "XXXX" : "YYYY";
            var paragraph = Line(border, padded, count);

            await Assert.That(FirstLine(paragraph, threshold)).Contains(last);
            await Assert.That(FirstLine(paragraph, threshold - 1)).DoesNotContain(last);
        }
    }

    // A run that begins with a space leaves that space hanging on the line above when its word wraps,
    // and Word charges the line above for the piece of the box the space stands in. So the FIRST of
    // two padded runs stays up only from the measure that also holds the second run's two sides: with
    // a 2.25pt triple that is 412px, where the run alone needs 371.
    [Test]
    public async Task A_hanging_leading_space_charges_its_runs_border_to_the_line_it_hangs_on()
    {
        var border = new BorderEdge {IsVisible = true, Style = BorderLineStyle.Triple, WidthPoints = 2.25, SpacePoints = 1};
        var paragraph = Line(border, padded: true, count: 2);

        await Assert.That(FirstLine(paragraph, 412)).Contains("XXXX");
        await Assert.That(FirstLine(paragraph, 411)).DoesNotContain("XXXX");
    }

    // Whether a bordered run grows its line is settled by the RUN's first character. The piece of
    // " boxed " that wraps starts its line with a letter and still adds nothing to the line's height
    // (border_style_variants' fourth box), and the reserve that opens the box moves down with it.
    [Test]
    public async Task The_wrapped_part_of_a_run_that_begins_with_a_space_grows_no_line()
    {
        var border = new BorderEdge {IsVisible = true, Style = BorderLineStyle.Triple, WidthPoints = 2.25, SpacePoints = 1};
        var plain = LayoutTestFonts.Measurer.LayoutLineContents(Line(border, padded: true, count: 1), 1000)[0];
        var measure = (float) (360 * 72.0 / CanonicalTextMeasurer.ReferenceDpi);
        var lines = LayoutTestFonts.Measurer.LayoutLineContents(Line(border, padded: true, count: 1), measure);
        var wrapped = lines[1];
        var boxed = wrapped.Runs.First(_ => _.Properties.Border != null);

        await Assert.That(boxed.Text).StartsWith("XXXX");
        await Assert.That(boxed.BorderInside).IsTrue();
        await Assert.That(wrapped.Height).IsEqualTo(plain.Height).Within(0.01f);

        // The glyphs start one drawn inset in from the line's start: five 3px bands and a 1px space.
        await Assert.That(boxed.X).IsEqualTo(16 * 0.6f).Within(0.05f);
    }
}
