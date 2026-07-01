// The component name collides with Morph's ExportOptions record in a C# generic argument (both are in
// scope via global usings); alias to the component so Render<ExportOptions> is unambiguous.
using ExportOptions = Morph.Web.Components.ExportOptions;

// Snapshots the dynamically-loaded option control for every output format — one verified file per
// format, so the markup each target surfaces (the PNG resolution knob or the "no options" note) is pinned.
public class ExportOptionsTests : BunitTestContext
{
    [Test]
    public Task Options_png() =>
        Verify(Render<ExportOptions>(_ => _.Add(component => component.Target, OutputFormat.Png)));

    [Test]
    public Task Options_pdf() =>
        Verify(Render<ExportOptions>(_ => _.Add(component => component.Target, OutputFormat.Pdf)));

    [Test]
    public Task Options_markdown() =>
        Verify(Render<ExportOptions>(_ => _.Add(component => component.Target, OutputFormat.Markdown)));

    [Test]
    public Task Options_text() =>
        Verify(Render<ExportOptions>(_ => _.Add(component => component.Target, OutputFormat.Text)));

    [Test]
    public async Task Png_ShowsResolutionSelector()
    {
        var cut = Render<ExportOptions>(_ => _.Add(component => component.Target, OutputFormat.Png));

        await Assert.That(cut.FindAll("#dpi-select").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".no-options").Count).IsEqualTo(0);
    }

    [Test]
    public async Task TextFormat_ShowsNoOptionsNote()
    {
        var cut = Render<ExportOptions>(_ => _.Add(component => component.Target, OutputFormat.Text));

        await Assert.That(cut.FindAll(".no-options").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("#dpi-select").Count).IsEqualTo(0);
    }

    [Test]
    public async Task Png_ResolutionChange_MutatesSettings()
    {
        var settings = new ImageSettings();
        var cut = Render<ExportOptions>(_ => _
            .Add(component => component.Target, OutputFormat.Png)
            .Add(component => component.Image, settings));

        await EventHandlerDispatchExtensions.ChangeAsync(cut.Find("#dpi-select"), "300");

        await Assert.That(settings.Dpi).IsEqualTo(300);
    }
}
