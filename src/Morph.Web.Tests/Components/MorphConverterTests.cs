// The converter widget the Morph.Blazor package ships. It owns the whole upload → preview → convert →
// download flow, so these are the tests of what the app actually shows.
public class MorphConverterTests : BunitTestContext
{
    public MorphConverterTests() =>
        JSInterop.Mode = JSRuntimeMode.Loose;

    [Test]
    public Task LayoutStructure()
    {
        var cut = Render<MorphConverter>();

        return Verify(cut);
    }

    [Test]
    public async Task InitialRender_ShowsUploadPrompt()
    {
        var cut = Render<MorphConverter>();

        await Assert.That(cut.Find(".file-drop-text strong").TextContent).IsEqualTo("Choose an Office file");
        await Assert.That(cut.FindAll(".convert-panel")).IsEmpty();
    }

    // One sample button per readable format, each labelled with that format's short name.
    [Test]
    public async Task InitialRender_ShowsASampleButtonPerReadableFormat()
    {
        var cut = Render<MorphConverter>();

        var labels = cut.FindAll(".sample-btn").Select(_ => _.TextContent.Trim()).ToList();

        await Assert.That(labels.Count).IsEqualTo(ConversionService.ReadableFormats.Count);
        foreach (var format in ConversionService.ReadableFormats)
        {
            await Assert.That(labels).Contains($"{format.Icon} Sample {format.ShortName}");
        }
    }

    // Samples are opt-out for a host that only wants its users uploading their own files.
    [Test]
    public async Task ShowSamplesFalse_DropsTheSampleRow()
    {
        var cut = Render<MorphConverter>(_ => _.Add(component => component.ShowSamples, false));

        await Assert.That(cut.FindAll(".sample-btn")).IsEmpty();
        await Assert.That(cut.FindAll(".upload-or")).IsEmpty();
        // The upload panel itself is untouched.
        await Assert.That(cut.FindAll(".file-drop").Count).IsEqualTo(1);
    }

    // Extra classes and arbitrary attributes land on the root element, so a host can position the widget
    // in its own layout without wrapping it.
    [Test]
    public async Task Class_AndSplattedAttributes_ReachTheRootElement()
    {
        var cut = Render<MorphConverter>(_ => _
            .Add(component => component.Class, "wide")
            .AddUnmatched("data-test", "converter"));

        var root = cut.Find("div.converter");
        await Assert.That(root.GetAttribute("class")).IsEqualTo("converter wide");
        await Assert.That(root.GetAttribute("data-test")).IsEqualTo("converter");
    }
}
