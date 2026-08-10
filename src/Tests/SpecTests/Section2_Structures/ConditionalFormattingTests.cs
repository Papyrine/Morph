using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Covers <c>w:cnfStyle</c> parsing and the table-style cascade that resolves
/// per-cell borders + shading from <c>w:tblStylePr</c> conditional regions.
/// </summary>
public class ConditionalFormattingTests
{
    [Test]
    public async Task ParseFlags_FirstRow()
    {
        var cnf = new ConditionalFormatStyle { FirstRow = true };

        var flags = DocumentParser.ParseConditionalFormatFlags(cnf);

        await Assert.That(flags).IsEqualTo(ConditionalFormatFlags.FirstRow);
    }

    [Test]
    public async Task ParseFlags_OddHBandWithFirstColumn()
    {
        var cnf = new ConditionalFormatStyle { FirstColumn = true, OddHorizontalBand = true };

        var flags = DocumentParser.ParseConditionalFormatFlags(cnf);

        await Assert.That(flags).IsEqualTo(ConditionalFormatFlags.FirstColumn | ConditionalFormatFlags.OddHBand);
    }

    [Test]
    public async Task ParseFlags_NullReturnsNone()
    {
        var flags = DocumentParser.ParseConditionalFormatFlags(null);

        await Assert.That(flags).IsEqualTo(ConditionalFormatFlags.None);
    }

    [Test]
    public async Task ResolveActiveConditions_FirstRowOverridesBanding()
    {
        var conditions = DocumentParser.ResolveActiveConditions(
            ConditionalFormatFlags.FirstRow | ConditionalFormatFlags.OddHBand,
            rowIndex: 0, colIndex: 0,
            totalRows: 5, totalCols: 3,
            rowBandSize: 1, colBandSize: 1).ToList();

        // Banding lowest priority, firstRow highest — firstRow must come last.
        await Assert.That(conditions).IsEquivalentTo(
            [TableStyleOverrideValues.Band1Horizontal, TableStyleOverrideValues.FirstRow]);
    }

    [Test]
    public async Task ResolveActiveConditions_DerivedFromPositionWhenNoFlags()
    {
        // Row 0 of a 4-row, 3-col grid → firstRow + firstColumn for the 0,0 cell.
        var conditions = DocumentParser.ResolveActiveConditions(
            ConditionalFormatFlags.None,
            rowIndex: 0, colIndex: 0,
            totalRows: 4, totalCols: 3,
            rowBandSize: 1, colBandSize: 1).ToList();

        await Assert.That(conditions).Contains(TableStyleOverrideValues.FirstRow);
        await Assert.That(conditions).Contains(TableStyleOverrideValues.FirstColumn);
    }

    [Test]
    public async Task ResolveActiveConditions_BandingForBodyRows()
    {
        // Row 1 (after header) should be Band1Horizontal under rowBandSize=1.
        var row1 = DocumentParser.ResolveActiveConditions(
            ConditionalFormatFlags.None,
            rowIndex: 1, colIndex: 1,
            totalRows: 5, totalCols: 4,
            rowBandSize: 1, colBandSize: 1).ToList();

        await Assert.That(row1).Contains(TableStyleOverrideValues.Band1Horizontal);

        // Row 2 alternates to Band2Horizontal.
        var row2 = DocumentParser.ResolveActiveConditions(
            ConditionalFormatFlags.None,
            rowIndex: 2, colIndex: 1,
            totalRows: 5, totalCols: 4,
            rowBandSize: 1, colBandSize: 1).ToList();

        await Assert.That(row2).Contains(TableStyleOverrideValues.Band2Horizontal);
    }

    [Test]
    public async Task ResolveActiveConditions_LastRowAndLastColumn()
    {
        // Bottom-right cell of a 4x3 grid.
        var conditions = DocumentParser.ResolveActiveConditions(
            ConditionalFormatFlags.None,
            rowIndex: 3, colIndex: 2,
            totalRows: 4, totalCols: 3,
            rowBandSize: 1, colBandSize: 1).ToList();

        await Assert.That(conditions).Contains(TableStyleOverrideValues.LastRow);
        await Assert.That(conditions).Contains(TableStyleOverrideValues.LastColumn);
    }

    /// <summary>
    /// End-to-end: agendas-minutes/15 uses BlueCurveMinutesTable, whose firstRow
    /// region defines fill #546421. Row 0 carries w:cnfStyle w:firstRow="1", so
    /// the cascade must propagate the fill onto every cell of that row.
    /// </summary>
    [Test]
    public async Task DocumentParser_AppliesFirstRowShadingFromTableStyle()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "agendas-minutes", "15", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var tables = doc.Elements.OfType<TableElement>().ToList();
        // The doc has two tables; the second uses BlueCurveMinutesTable.
        await Assert.That(tables.Count).IsGreaterThanOrEqualTo(2);
        var table = tables[1];

        foreach (var cell in table.Rows[0].Cells)
        {
            await Assert.That(cell.Properties.BackgroundColorHex)
                .IsEqualTo("546421")
                .Because("BlueCurveMinutesTable firstRow shading should cascade onto header cells");
        }
    }

    /// <summary>
    /// Wholetable shading defined on the table style's <c>StyleTableCellProperties</c>
    /// should apply to body cells that have no explicit shd and no conditional override.
    /// In agendas-minutes/15 the BlueCurveMinutesTable wholeTable region defines fill
    /// <c>ECF2DA</c>, so body rows should inherit it.
    /// </summary>
    [Test]
    public async Task DocumentParser_AppliesWholeTableShadingToBodyCells()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "agendas-minutes", "15", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var table = doc.Elements.OfType<TableElement>().Skip(1).First();

        // Row 1 (first body row) should have wholeTable fill.
        await Assert.That(table.Rows[1].Cells[0].Properties.BackgroundColorHex)
            .IsEqualTo("ECF2DA")
            .Because("body cells without explicit shd should inherit BlueCurveMinutesTable's wholeTable fill");
    }

    [Test]
    public async Task ParseTableLookMask_NullReturnsAllConditions()
    {
        var mask = DocumentParser.ParseTableLookMask(null);

        await Assert.That(mask).IsEqualTo(ConditionalFormatFlagsExtensions.AllConditions);
    }

    [Test]
    public async Task ParseTableLookMask_FirstRowDisabledByDefault()
    {
        // Word's default tblLook has every flag explicitly false; only those Word writers
        // bump up to "1" should be derivable from position.
        var look = new TableLook { FirstRow = false, LastRow = false, FirstColumn = false, LastColumn = false };

        var mask = DocumentParser.ParseTableLookMask(look);

        await Assert.That(mask & ConditionalFormatFlags.FirstRow).IsEqualTo(ConditionalFormatFlags.None);
        await Assert.That(mask & ConditionalFormatFlags.LastRow).IsEqualTo(ConditionalFormatFlags.None);
    }

    [Test]
    public async Task ParseTableLookMask_NoBandingFlagsSuppressBanding()
    {
        // cards/09 ships w:tblLook w:noHBand="1" w:noVBand="1" — banding must be off,
        // even though firstRow / firstColumn are still derivable.
        var look = new TableLook { FirstRow = true, NoHorizontalBand = true, NoVerticalBand = true };

        var mask = DocumentParser.ParseTableLookMask(look);

        await Assert.That(mask & ConditionalFormatFlags.OddHBand).IsEqualTo(ConditionalFormatFlags.None);
        await Assert.That(mask & ConditionalFormatFlags.EvenHBand).IsEqualTo(ConditionalFormatFlags.None);
        await Assert.That(mask & ConditionalFormatFlags.OddVBand).IsEqualTo(ConditionalFormatFlags.None);
        await Assert.That(mask & ConditionalFormatFlags.EvenVBand).IsEqualTo(ConditionalFormatFlags.None);
        await Assert.That(mask & ConditionalFormatFlags.FirstRow).IsEqualTo(ConditionalFormatFlags.FirstRow);
    }

    [Test]
    public async Task ResolveActiveConditions_MaskSuppressesPositionalBanding()
    {
        // No explicit cnfStyle, body row 1 with rowBandSize=1 would normally derive OddHBand,
        // but a tableLookMask without OddHBand should drop it entirely.
        var maskWithoutBanding = ConditionalFormatFlagsExtensions.AllConditions
            & ~(ConditionalFormatFlags.OddHBand | ConditionalFormatFlags.EvenHBand);

        var conditions = DocumentParser.ResolveActiveConditions(
            ConditionalFormatFlags.None,
            rowIndex: 1, colIndex: 1,
            totalRows: 5, totalCols: 4,
            rowBandSize: 1, colBandSize: 1,
            tableLookMask: maskWithoutBanding).ToList();

        await Assert.That(conditions).DoesNotContain(TableStyleOverrideValues.Band1Horizontal);
        await Assert.That(conditions).DoesNotContain(TableStyleOverrideValues.Band2Horizontal);
    }
}
