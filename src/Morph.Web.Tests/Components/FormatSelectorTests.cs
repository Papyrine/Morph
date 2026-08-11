public class FormatSelectorTests : BunitTestContext
{
    [Test]
    public async Task Render_ListsEveryFormatAsOption()
    {
        var cut = Render<FormatSelector>(_ => _
            .Add(_ => _.Label, "Convert to")
            .Add(_ => _.Formats, ConversionService.WritableFormats)
            .Add(_ => _.Selected, OutputFormat.Png));

        var options = cut.FindAll("option");

        await Assert.That(options.Count).IsEqualTo(ConversionService.WritableFormats.Count);
        await Assert.That(options.Any(_ => _.TextContent.Contains("PNG image"))).IsTrue();
        await Assert.That(options.Any(_ => _.TextContent.Contains("PDF"))).IsTrue();
    }

    [Test]
    public async Task Render_UsesLabel()
    {
        var cut = Render<FormatSelector>(_ => _
            .Add(_ => _.Label, "Convert to")
            .Add(_ => _.Formats, ConversionService.WritableFormats)
            .Add(_ => _.Selected, OutputFormat.Png));

        await Assert.That(cut.Find("label").TextContent).IsEqualTo("Convert to");
    }

    [Test]
    public async Task Change_RaisesSelectedChanged()
    {
        OutputFormat? selected = null;
        var cut = Render<FormatSelector>(_ => _
            .Add(_ => _.Label, "Convert to")
            .Add(_ => _.Formats, ConversionService.WritableFormats)
            .Add(_ => _.Selected, OutputFormat.Png)
            .Add(_ => _.SelectedChanged, (OutputFormat format) => selected = format));

        await cut.Find("select").ChangeAsync(nameof(OutputFormat.Pdf));

        await Assert.That(selected).IsEqualTo(OutputFormat.Pdf);
    }
}
