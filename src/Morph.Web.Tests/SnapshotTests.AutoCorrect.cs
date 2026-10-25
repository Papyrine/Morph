using static EditFixtures;

// Word's AutoCorrect and AutoFormat as you type in the open paragraph (morph-autocorrect.js), each test
// citing the rule it keeps true.
public partial class SnapshotTests
{
    // A sentence's first word is capitalised on the delimiter after it, but not after an abbreviation;
    // TWo INitial CApitals are fixed and a day's name capitalised (WordProbe A-01, A-02, A-03).
    [Test]
    [Property("WordBehavior", "R-A-1")]
    public async Task AutoCorrect_Capitalises_AsWordDoes()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" so then. this is monday. THis works e.g. like this ");

        await Assert.That(await page.InnerTextAsync(editor))
            .IsEqualTo("Third and last. So then. This is Monday. This works e.g. like this ");
    }

    // Quotes are curly by context, ` - ` becomes an en dash and `--` between words an em dash, and
    // fractions and `*bold*` are written as Word writes them (WordProbe A-05, A-07, A-08, A-09).
    [Test]
    [Property("WordBehavior", "R-A-2")]
    public async Task AutoFormat_Replaces_AsYouType()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" \"don't\" a - b x--y 1/2 *big* ");

        await Assert.That(await page.InnerTextAsync(editor))
            .IsEqualTo("Third and last. “don’t” a – b x—y ½ big ");
        await HandInAsync(page);

        await Assert.That(Read(await SaveAsync(page))[2]).Contains("[b:big]");
    }

    // `* ` at a paragraph's start makes it a bulleted item, the leader gone (WordProbe A-11, A-26).
    [Test]
    [Property("WordBehavior", "R-A-3")]
    public async Task AutoFormat_StartsAList()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.TypeAsync("* ");
        await WaitForBodyAsync(page, _ => _.Contains("<w:numPr>"));

        await Assert.That(Read(await SaveAsync(page))[2]).StartsWith("[Third and last.]");
    }

    // The replacement entries fire on Space, and the first undo takes the correction back, leaving what
    // was typed (WordProbe A-19, A-20).
    [Test]
    [Property("WordBehavior", "R-A-6")]
    public async Task AutoCorrect_Replaces_AndUndoTakesItBack()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "and last");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" (c) teh i ");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. © the I ");

        await page.Keyboard.PressAsync("Control+z");
        await Assert.That(await page.InnerTextAsync(editor)).IsEqualTo("Third and last. © the i ");
    }
}
