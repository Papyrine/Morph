/// <summary>
/// What follows a list marker (<c>w:lvl/w:suff</c>) decides where the first line's text starts, and the
/// marker itself starts at the first line's own indent. Word-probed in <c>list_marker_suffix</c> (see its
/// notes.md): a tab runs to the hanging indent's stop or the next default stop, a space sets the text one
/// space after the marker, nothing sets it against the marker.
/// </summary>
public class ListMarkerSuffixTests
{
    static string Fixture => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "list_marker_suffix", "input.docx");

    static ParagraphElement Item(ListMarkerSuffix suffix, double left, double hanging) =>
        new()
        {
            Runs = [new() {Text = "Item text", Properties = new() {FontFamily = "Arial", FontSizePoints = 28}}],
            Properties = new()
            {
                LeftIndentPoints = left,
                HangingIndentPoints = hanging,
                DefaultTabStopPoints = 36,
                Numbering = new() {Text = "1.", Suffix = suffix}
            }
        };

    static float MarkerWidth(ParagraphElement paragraph) =>
        LayoutTestFonts.Measurer.MeasureRunWidth("1.", CanonicalParagraphMeasurer.MarkerProperties(paragraph, paragraph.Properties.Numbering!));

    [Test]
    public async Task The_parser_reads_each_levels_suffix()
    {
        var document = new DocumentParser().Parse(Fixture);
        var suffixes = document.Elements
            .OfType<ParagraphElement>()
            .Where(_ => _.Properties.Numbering != null)
            .Select(_ => _.Properties.Numbering!.Suffix)
            .Distinct()
            .ToList();

        await Assert.That(suffixes).IsEquivalentTo([ListMarkerSuffix.Tab, ListMarkerSuffix.Space, ListMarkerSuffix.Nothing]);
    }

    [Test]
    public async Task A_tab_runs_to_the_hanging_indents_stop()
    {
        var (marker, text) = LayoutTestFonts.Measurer.MarkerPlacement(Item(ListMarkerSuffix.Tab, 72, 36));

        await Assert.That(marker).IsEqualTo(-36).Within(0.01f);
        await Assert.That(text).IsEqualTo(0).Within(0.01f);
    }

    [Test]
    public async Task With_no_hanging_indent_the_marker_starts_at_the_left_indent_and_a_tab_runs_to_the_next_default_stop()
    {
        // "1." at 36pt with its text at 72pt.
        var (marker, text) = LayoutTestFonts.Measurer.MarkerPlacement(Item(ListMarkerSuffix.Tab, 36, 0));

        await Assert.That(marker).IsEqualTo(0).Within(0.01f);
        await Assert.That(text).IsEqualTo(36).Within(0.01f);
    }

    [Test]
    [Arguments(72.0, 36.0)]
    [Arguments(144.0, 72.0)]
    [Arguments(36.0, 0.0)]
    public async Task A_space_sets_the_text_one_space_after_the_marker(double left, double hanging)
    {
        var paragraph = Item(ListMarkerSuffix.Space, left, hanging);
        var space = LayoutTestFonts.Measurer.MeasureRunWidth(" ", CanonicalParagraphMeasurer.MarkerProperties(paragraph, paragraph.Properties.Numbering!));
        var (marker, text) = LayoutTestFonts.Measurer.MarkerPlacement(paragraph);

        await Assert.That(marker).IsEqualTo((float) -hanging).Within(0.01f);
        await Assert.That(text).IsEqualTo((float) (-hanging + MarkerWidth(paragraph) + space)).Within(0.01f);
    }

    [Test]
    [Arguments(72.0, 36.0)]
    [Arguments(144.0, 72.0)]
    public async Task Nothing_sets_the_text_against_the_marker(double left, double hanging)
    {
        var paragraph = Item(ListMarkerSuffix.Nothing, left, hanging);
        var (_, text) = LayoutTestFonts.Measurer.MarkerPlacement(paragraph);

        await Assert.That(text).IsEqualTo((float) (-hanging + MarkerWidth(paragraph))).Within(0.01f);
    }
}
