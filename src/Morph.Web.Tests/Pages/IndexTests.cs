// The home page is a thin host for the package's MorphConverter — see MorphConverterTests for the
// converter's own behaviour.
public class IndexTests : BunitTestContext
{
    public IndexTests() =>
        JSInterop.Mode = JSRuntimeMode.Loose;

    [Test]
    public async Task HostsTheConverter()
    {
        var cut = Render<Morph.Web.Pages.Index>();

        await Assert.That(cut.FindComponents<MorphConverter>().Count).IsEqualTo(1);
        await Assert.That(cut.Find(".file-drop-text strong").TextContent).IsEqualTo("Choose an Office file");
    }
}
