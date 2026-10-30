/// <summary>
/// Spec tests for a worksheet's printed header and footer (<c>headerFooter</c>, ECMA-376 §18.3.1.46),
/// read by <c>SheetHeaderFooter</c> into the section's bands. Thirteen of the corpus workbooks declare
/// one; invoice-accessibility-guide prints "Page &amp;P of &amp;N" on every sheet but the first page.
/// </summary>
public class HeaderFooterTests
{
    [Test]
    public async Task Codes_SplitIntoParts()
    {
        var parts = SheetHeaderFooter.Parts("&LLeft &&co&C&\"Arial,Bold\"&14Page &P of &N&R&A&K00FF00!", "Sheet1");

        await Assert.That(parts[0].Select(_ => _.Text)).IsEquivalentTo(["Left &co"]);
        await Assert.That(parts[1].Select(_ => (_.Text, _.Field)))
            .IsEquivalentTo([("Page ", PageFieldKind.None), ("1", PageFieldKind.Page), (" of ", PageFieldKind.None), ("1", PageFieldKind.NumberOfPages)]);
        await Assert.That(parts[2].Select(_ => _.Text)).IsEquivalentTo(["Sheet1!"]);
    }

    [Test]
    public async Task TextBeforeAnySection_IsTheCentrePart()
    {
        var parts = SheetHeaderFooter.Parts("Page &P", "Sheet1");

        await Assert.That(parts[0]).IsEmpty();
        await Assert.That(parts[1].Count).IsEqualTo(2);
        await Assert.That(parts[2]).IsEmpty();
    }

    [Test]
    public async Task DifferentFirst_LeavesTheFirstPageBare()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "invoice-accessibility-guide", "input.xlsx");
        using var stream = File.OpenRead(inputFile);
        var settings = ExcelConverter.Parse(stream, defaultFont: null, fontDirectory: null).PageSettings;

        await Assert.That(settings.DifferentFirstPage).IsTrue();
        await Assert.That(settings.Bands!.FirstPageFooter).IsNull();
        var footer = (ParagraphElement) settings.Bands.Footer!.Elements.Single();
        await Assert.That(footer.Properties.Alignment).IsEqualTo(TextAlignment.Center);
        await Assert.That(footer.Runs.Select(_ => _.PageField)).Contains(PageFieldKind.NumberOfPages);
    }
}
