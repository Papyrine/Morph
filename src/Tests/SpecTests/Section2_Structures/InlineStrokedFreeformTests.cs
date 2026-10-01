/// <summary>
/// An inline <c>wps:wsp</c> whose only mark is its outline: brochures/04's roof chevrons are an
/// unfilled three-point <c>a:custGeom</c> (<c>moveTo</c>/<c>lnTo</c>/<c>lnTo</c>, no <c>a:close</c>)
/// stroked 5pt or 6pt with round caps. The single-shape gate admitted only filled shapes, so all five were
/// dropped, and a closed contour would have stroked them as triangles.
/// </summary>
public class InlineStrokedFreeformTests
{
    [Test]
    public async Task An_unfilled_open_freeform_is_kept_as_an_open_round_capped_stroke()
    {
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "brochures", "04", "input.docx"));
        var document = parser.Parse(stream);

        var chevrons = CollectShapeRuns(document.Elements)
            .Where(_ => _.InlineShapeGroup!.Shapes is [{ FillColorHex: null, OpenOutline: true }])
            .ToList();

        await Assert.That(chevrons.Count).IsEqualTo(5);
        foreach (var run in chevrons)
        {
            var chevron = run.InlineShapeGroup!.Shapes.Single();
            await Assert.That(chevron.RoundCap).IsTrue();
            // 5pt, and 6pt over the brochure title.
            await Assert.That(chevron.LineWidthEmu is 63500 or 76200).IsTrue();
            await Assert.That(chevron.Subpaths!.Single().Count).IsEqualTo(3);
            // The line reserves the stroke's spill (wp:effectExtent), as for a picture.
            await Assert.That(run.InlineImageEffectExtent).IsNotNull();
        }
    }

    static IEnumerable<Run> CollectShapeRuns(IEnumerable<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            var nested = element switch
            {
                ParagraphElement paragraph => paragraph.Runs.Where(_ => _.InlineShapeGroup != null),
                TableElement table => table.Rows.SelectMany(_ => _.Cells).SelectMany(cell => CollectShapeRuns(cell.Content)),
                FloatingTextBoxElement textBox => CollectShapeRuns(textBox.Content),
                _ => []
            };

            foreach (var run in nested)
            {
                yield return run;
            }
        }
    }
}
