/// <summary>
/// Covers header/footer-hosted drawings whose image relationships (r:embed)
/// belong to the header/footer part, not the main document part. Parser must
/// resolve the relationship against the element's host part (see
/// <c>DocumentParser.ResolveHostPart</c>) — otherwise <c>mainPart.GetPartById</c>
/// returns null and the image is silently dropped from the parsed model.
/// </summary>
public class HeaderFloatingImageParseTests
{
    [Test]
    public async Task CoverLetters12_HeaderContainsFullPageBehindTextImage()
    {
        var parser = new DocumentParser();
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "cover-letters", "12", "input.docx"));
        var doc = parser.Parse(stream);

        var headerImages = doc.Header!.Elements.OfType<FloatingImageElement>().ToList();
        await Assert.That(headerImages.Count).IsEqualTo(1);

        var background = headerImages[0];
        await Assert.That(background.BehindText).IsTrue();
        await Assert.That(background.ImageData).IsNotNull();
        await Assert.That(background.ImageData.Length).IsGreaterThan(0);
        // Full-page gradient: ~8.19" × 11.27" in points
        await Assert.That(background.WidthPoints).IsGreaterThan(500);
        await Assert.That(background.HeightPoints).IsGreaterThan(700);
    }

    [Test]
    public async Task Letters03_HeaderContainsBannerImage()
    {
        var parser = new DocumentParser();
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "letters", "03", "input.docx"));
        var doc = parser.Parse(stream);

        var headerImages = doc.Header!.Elements.OfType<FloatingImageElement>().ToList();
        await Assert.That(headerImages.Count).IsGreaterThanOrEqualTo(1);

        foreach (var image in headerImages)
        {
            await Assert.That(image.ImageData).IsNotNull();
            await Assert.That(image.ImageData.Length).IsGreaterThan(0);
        }
    }
}
