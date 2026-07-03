public class MarkdownPreviewTests
{
    [Test]
    public async Task ElideImages_ReplacesLongPayloadKeepingMime()
    {
        var payload = new string('A', 300);
        var markdown = $"![alt](data:image/svg+xml;base64,{payload})";

        var elided = MarkdownPreview.ElideImages(markdown);

        await Assert.That(elided).Contains("![alt](data:image/svg+xml;base64,…");
        await Assert.That(elided).Contains("KB elided");
        await Assert.That(elided).DoesNotContain(payload);
    }

    [Test]
    public async Task HasElidableImages_TracksWhatElideImagesWouldReplace()
    {
        var withImage = $"![alt](data:image/svg+xml;base64,{new string('A', 300)})";
        var shortDataUri = "![icon](data:image/png;base64,iVBORw0KGgo=)";
        var plain = "# Title\n\nSome text.";

        await Assert.That(MarkdownPreview.HasElidableImages(withImage)).IsTrue();
        await Assert.That(MarkdownPreview.HasElidableImages(shortDataUri)).IsFalse();
        await Assert.That(MarkdownPreview.HasElidableImages(plain)).IsFalse();
    }

    [Test]
    public async Task ElideImages_KeepsShortDataUris()
    {
        var markdown = "![icon](data:image/png;base64,iVBORw0KGgo=)";

        await Assert.That(MarkdownPreview.ElideImages(markdown)).IsEqualTo(markdown);
    }

    [Test]
    public async Task ElideImages_LeavesPlainMarkdownAlone()
    {
        var markdown = "# Title\n\nSome [link](https://example.com) and text.";

        await Assert.That(MarkdownPreview.ElideImages(markdown)).IsEqualTo(markdown);
    }

    // The pane view of the sample document: the full Markdown export with every embedded image payload
    // swapped for a size note — small enough to snapshot readably (the raw export is ~430 KB).
    [Test]
    public Task PaneView_Snapshot() =>
        Verify(MarkdownPreview.ElideImages(ConversionService.ToMarkdown(Sample.DocxBytes)), extension: "txt");
}
