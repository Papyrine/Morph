/// <summary>
/// An unwarped inline text box (<c>prstTxWarp="textNoShape"</c>) is routed through Morph's WordArt
/// dispatch but must render like an ordinary text box: its glyph colour, size and weight come from
/// the run's RESOLVED properties (direct run rPr over the paragraph style chain over the document
/// defaults), not the WordArt defaults. menus/03's "EVENT INTRO"/"EVENT DATE" labels carry NO run
/// rPr — their formatting lives entirely in the Heading1 -> Normal chain (10pt, white via
/// <c>w:color w:val="FFFFFF" w:themeColor="background1"</c>). Reading only the first run's direct
/// properties left them 36pt black — invisible on the template's dark band and oversized. This guards
/// that <c>ParseWordArt</c> resolves the same cascade the body text path uses.
/// </summary>
public class WordArtTextBoxStyleColorTests
{
    [Test]
    public async Task UnwarpedTextBoxLabelsInheritTheirStyleColourAndSize()
    {
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "menus", "03", "input.docx"));
        var doc = parser.Parse(stream);

        var labels = CollectWordArt(doc.Elements)
            .Where(_ => _.Text.Contains("EVENT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // "EVENT INTRO" and "EVENT DATE".
        await Assert.That(labels.Count).IsEqualTo(2);
        foreach (var label in labels)
        {
            // Heading1 -> Normal resolves to white; the WordArt used to leave this null (black).
            await Assert.That(label.FillColorHex).IsEqualTo("FFFFFF");
            // Heading1 sets w:sz 20 half-points = 10pt; the WordArt used to default to 36pt.
            await Assert.That(label.FontSizePoints).IsEqualTo(10d);
        }
    }

    static IEnumerable<WordArtElement> CollectWordArt(IEnumerable<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            if (element is WordArtElement wordArt)
            {
                yield return wordArt;
            }
            else if (element is TableElement table)
            {
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var inner in CollectWordArt(cell.Content))
                        {
                            yield return inner;
                        }
                    }
                }
            }
        }
    }
}
