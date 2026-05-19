/// <summary>
/// Covers <c>w:noWrap</c> (cell) parsing. The flag would tell an autofit table to grow
/// the column to fit the longest run; Morph captures it on
/// <see cref="TableCellProperties.NoWrap"/> but doesn't currently feed it into
/// <c>CalculateColumnWidths</c> (parsed-but-not-applied — corpus cells with noWrap also
/// carry an explicit <c>w:tcW</c>, so visual impact is nil).
/// </summary>
public class TableNoWrapTests
{
    [Test]
    public async Task TableCellProperties_DefaultNoWrap_IsFalse()
    {
        var props = new TableCellProperties();

        await Assert.That(props.NoWrap).IsFalse();
    }

    [Test]
    public async Task DocumentParser_ParsesNoWrap_FromCorpusCell()
    {
        // agendas-minutes/11 has cells with both w:tcW and w:noWrap inside w:tcPr.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "agendas-minutes", "11", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var anyNoWrap = doc.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .Any(_ => _.Properties.NoWrap);

        await Assert.That(anyNoWrap).IsTrue();
    }
}
