using static EditFixtures;

// Still more of Word's keys in the open paragraph, each test citing the rule it keeps true.
public partial class SnapshotTests
{
    // Tab at the start of a paragraph with text indents its first line 36pt (WordProbe E-30, A-16).
    [Test]
    [Property("WordBehavior", "R-E-10")]
    [Property("WordBehavior", "R-A-5")]
    public async Task Tab_AtAParagraphsStart_IndentsItsFirstLine()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Tab");
        await WaitForBodyAsync(page, _ => _.Contains("w:firstLine=\"720\""));
    }
}
