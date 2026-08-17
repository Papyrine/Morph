// Snapshots the dynamically-loaded option control for every output format — one verified file per
// format, so the markup each target surfaces (the PNG resolution knob or the "no options" note) is pinned.
public class ExportOptionsPanelTests : BunitTestContext
{
    [Test]
    public Task Options_png() =>
        Verify(Render<ExportOptionsPanel>(_ => _.Add(component => component.Target, OutputFormat.Png)));

    [Test]
    public Task Options_pdf() =>
        Verify(Render<ExportOptionsPanel>(_ => _.Add(component => component.Target, OutputFormat.Pdf)));

    [Test]
    public Task Options_html() =>
        Verify(Render<ExportOptionsPanel>(_ => _.Add(component => component.Target, OutputFormat.Html)));

    [Test]
    public Task Options_markdown() =>
        Verify(Render<ExportOptionsPanel>(_ => _.Add(component => component.Target, OutputFormat.Markdown)));

    [Test]
    public Task Options_text() =>
        Verify(Render<ExportOptionsPanel>(_ => _.Add(component => component.Target, OutputFormat.Text)));

    [Test]
    public async Task Png_ShowsResolutionSelector()
    {
        var cut = Render<ExportOptionsPanel>(_ => _.Add(component => component.Target, OutputFormat.Png));

        await Assert.That(cut.FindAll("#dpi-select").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".no-options").Count).IsEqualTo(0);
    }

    [Test]
    public async Task Png_ShowsCropSelector()
    {
        var cut = Render<ExportOptionsPanel>(_ => _.Add(component => component.Target, OutputFormat.Png));

        await Assert.That(cut.FindAll("#crop-select").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("#crop-select option").Count).IsEqualTo(ExportOptionChoices.Crops.Length);
    }

    [Test]
    public async Task TextFormat_ShowsNoOptionsNote()
    {
        var cut = Render<ExportOptionsPanel>(_ => _.Add(component => component.Target, OutputFormat.Text));

        await Assert.That(cut.FindAll(".no-options").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("#dpi-select").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("#crop-select").Count).IsEqualTo(0);
    }

    [Test]
    public async Task Png_ResolutionChange_MutatesSettings()
    {
        var settings = new ImageSettings();
        var cut = Render<ExportOptionsPanel>(_ => _
            .Add(component => component.Target, OutputFormat.Png)
            .Add(component => component.Image, settings));

        await cut.Find("#dpi-select").ChangeAsync("300");

        await Assert.That(settings.Dpi).IsEqualTo(300);
    }

    // The crop knob binds to an enum rather than an int, so the round trip through the select's
    // string value is worth pinning as well as the presence of the control.
    [Test]
    public async Task Png_CropChange_MutatesSettings()
    {
        var settings = new ImageSettings();
        var cut = Render<ExportOptionsPanel>(_ => _
            .Add(component => component.Target, OutputFormat.Png)
            .Add(component => component.Image, settings));

        await cut.Find("#crop-select").ChangeAsync(nameof(PageCrop.ContentBox));

        await Assert.That(settings.Crop).IsEqualTo(PageCrop.ContentBox);
    }
}
