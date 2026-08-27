/// <summary>
/// Covers the OOXML rule that a shape's <c>&lt;a:grpFill/&gt;</c> inherits the fill of its ancestor
/// <c>wpg:wgp</c> group (the group's <c>a:solidFill</c>). brochures/06's decorative clusters — its
/// accent stripes and its hot-air-balloon line-art — fill the whole group once and let each child
/// rectangle defer via <c>a:grpFill</c>. The inline-group parser previously read only a shape's own
/// <c>a:solidFill</c>, so every grpFill child resolved to no fill and drew as nothing (their outline
/// is <c>a:noFill</c> too), dropping those decorations from every backend. The floating-shape path
/// already resolved grpFill (<see cref="ShapeNoFillTests"/>'s sibling scenarios / labels/07); this
/// guards the same resolution on the inline path.
/// </summary>
public class InlineGroupGroupFillTests
{
    [Test]
    public async Task GrpFillInlineGroupShapesInheritTheirGroupFill()
    {
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "brochures", "06", "input.docx"));
        var doc = parser.Parse(stream);

        var groups = CollectInlineGroups(doc.Elements).ToList();

        // The accent-stripe cluster: four full-width rectangles that defer to the group's accent1
        // fill (#492A86). Before grpFill resolution every one resolved to FillColorHex == null.
        var stripes = groups.Single(_ => _.Shapes.Count == 4);
        await Assert.That(stripes.Shapes.All(_ => _.FillColorHex == "492A86")).IsTrue();
    }

    static IEnumerable<InlineShapeGroup> CollectInlineGroups(IEnumerable<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            if (element is ParagraphElement paragraph)
            {
                foreach (var run in paragraph.Runs)
                {
                    if (run.InlineShapeGroup is { } group)
                    {
                        yield return group;
                    }
                }
            }
            else if (element is TableElement table)
            {
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var inner in CollectInlineGroups(cell.Content))
                        {
                            yield return inner;
                        }
                    }
                }
            }
        }
    }
}
