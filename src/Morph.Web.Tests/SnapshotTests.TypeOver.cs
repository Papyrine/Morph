using static EditFixtures;

// Typing over text selected across paragraphs replaces it, the paragraphs joined into one with the first's
// properties, and typing goes on after it (WordProbe E-10c, R-E-4).
public partial class SnapshotTests
{
    [Test]
    [Property("WordBehavior", "R-E-4")]
    public async Task TypeOver_AcrossParagraphs_JoinsThemIntoTheFirst()
    {
        var page = await OpenEditAsync(Paragraphs);

        await SelectOnPagesAsync(page, "here", "Second ");
        await page.Keyboard.TypeAsync("x");
        await WaitForEditorAsync(page, "First paragraph xbold one.");
        await page.Keyboard.TypeAsync("y");
        await HandInAsync(page);

        var read = Read(await SaveAsync(page));
        await Assert.That(read.Count).IsEqualTo(2);
        await Assert.That(string.Concat(read[0].Split('[', ']')).Replace("b:", "")).IsEqualTo("First paragraph xybold one.");
        await Assert.That(read[0]).DoesNotContain("(center)");
    }
}
