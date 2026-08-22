/// <summary>
/// The integer-overflow primitives behind the int.MaxValue grid-index infinite loop, asserted
/// against the FIXED behaviour: CellReference.ColumnOf now rejects a column past the worksheet
/// limit (and accumulates in long so a long reference cannot wrap back into range) instead of
/// returning an overflowed value that becomes a runaway grid bound. The end-to-end document
/// behaviour is covered by <see cref="GridIndexBoundsTests"/>.
///
/// The row axis needs the same bound in the same place. AssignImpliedReferences guards the
/// <c>&lt;row r="..."&gt;</c> attribute, but that is not the only way a row index reaches a grid
/// bound: CellReference.RowOf also feeds ParseRange, and ParseRange is handed the sheet's
/// <c>dimension</c> and its print-area defined name — both attacker-controlled strings that never
/// pass through AssignImpliedReferences. Columns on that path are covered for free, because the
/// check lives inside ColumnOf; rows are not.
///
/// These stay at the primitive layer deliberately. The end-to-end row case cannot be asserted the
/// way GridIndexBoundsTests asserts the column case: while it is red, the parse walks the
/// wrapped-counter loop and exhausts memory rather than returning, which takes the test runner
/// down with it. Bounding RowOf is what makes an end-to-end row-range test safe to add.
/// </summary>
public class GridIndexOverflowTests
{
    [Test]
    public async Task ColumnOf_AcceptsTheMaxColumn() =>
        await Assert.That(CellReference.ColumnOf("XFD")).IsEqualTo(CellReference.MaxColumn);

    [Test]
    public async Task ColumnOf_RejectsColumnBeyondMax()
    {
        var rejected = false;
        try { CellReference.ColumnOf("FXSHRXW"); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ColumnOf_RejectsOverflowingColumn()
    {
        var rejected = false;
        try { CellReference.ColumnOf("FXSHRXX"); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task RowOf_AcceptsTheMaxRow() =>
        await Assert.That(CellReference.RowOf($"A{CellReference.MaxRow}")).IsEqualTo(CellReference.MaxRow);

    [Test]
    public async Task RowOf_RejectsRowBeyondMax()
    {
        var rejected = false;
        try { CellReference.RowOf("A2147483647"); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task RowOf_RejectsRowJustPastMax()
    {
        var rejected = false;
        try { CellReference.RowOf($"A{CellReference.MaxRow + 1}"); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task RowOf_RejectsRowBeyondIntRange()
    {
        var rejected = false;
        try { CellReference.RowOf("A99999999999"); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ParseRange_SingleCellRowBeyondMax_IsRejected()
    {
        var rejected = false;
        try { CellReference.ParseRange("A2147483647"); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ParseRange_PrintAreaRowBeyondMax_IsRejected()
    {
        var rejected = false;
        try { CellReference.ParseRange("'Sheet1'!$A$2147483647:$A$2147483647"); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ParseRange_LastRowBeyondIntRange_IsRejected()
    {
        var rejected = false;
        try { CellReference.ParseRange("A1:A99999999999"); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task RowOf_RejectsRowBeyondLongRange()
    {
        // A digit run too long for long is, a fortiori, past MaxRow. long.TryParse only reports
        // failure, so the bound must treat that failure as out-of-range rather than as "no row here"
        // -- otherwise this degrades to 0 while A99999999999, the same kind of input one digit run
        // shorter, is rejected.
        var rejected = false;
        try { CellReference.RowOf("A" + new string('9', 25)); }
        catch (InvalidOperationException) { rejected = true; }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ParseRange_InRangeRows_AreAccepted()
    {
        var range = CellReference.ParseRange($"A1:B{CellReference.MaxRow}");

        await Assert.That(range!.Value.FirstRow).IsEqualTo(1);
        await Assert.That(range.Value.LastRow).IsEqualTo(CellReference.MaxRow);
    }

    [Test]
    public async Task SheetRange_AtIntMaxValue_IsASingleCell()
    {
        var range = new SheetRange(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);

        await Assert.That(range.RowCount).IsEqualTo(1);
        await Assert.That(range.ColumnCount).IsEqualTo(1);
    }
}
