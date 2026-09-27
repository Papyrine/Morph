// What changed between two texts, as the editor works it out from a paragraph before and after.
public class TextDiffTests
{
    // Each hunk as "old text → new text" at its place in the old text.
    static string Changes(string before, string after) =>
        string.Join(
            "; ",
            TextDiff.Compare(before, after)
                .Select(_ => $"{_.OldStart}: '{before.Substring(_.OldStart, _.OldLength)}' → '{after.Substring(_.NewStart, _.NewLength)}'"));

    [Test]
    [Arguments("Hello world", "Hello world", "")]
    [Arguments("", "", "")]
    [Arguments("", "new", "0: '' → 'new'")]
    [Arguments("old", "", "0: 'old' → ''")]
    [Arguments("Hello world", "Hello brave world", "6: '' → 'brave '")]
    [Arguments("Hello brave world", "Hello world", "6: 'brave ' → ''")]
    [Arguments("Hello world", "Hello world!", "11: '' → '!'")]
    [Arguments("Hello world", "Well, Hello world", "0: '' → 'Well, '")]
    [Arguments("Hello cruel world", "Hello brave world", "6: 'cruel' → 'brave'")]
    public async Task OneChange(string before, string after, string expected) =>
        await Assert.That(Changes(before, after)).IsEqualTo(expected);

    [Test]
    public async Task ChangesFarApart_AreChangesOfTheirOwn() =>
        await Assert.That(Changes("The quick brown fox jumps over the lazy dog", "The quick red fox jumps over the sleepy dog"))
            .IsEqualTo("10: 'brown' → 'red'; 35: 'lazy' → 'sleepy'");

    // "quick" and "quiet" share their first three letters, and "lazy" and "sleepy" their last;
    // nobody rewrote two letters of five.
    [Test]
    [Arguments("the quick fox", "the quiet fox", "4: 'quick' → 'quiet'")]
    [Arguments("teh fox", "the fox", "0: 'teh' → 'the'")]
    [Arguments("it's here", "its here", "2: ''' → ''")]
    [Arguments("a b c d e f", "a x c y e f", "2: 'b' → 'x'; 6: 'd' → 'y'")]
    public async Task TextReplacedWithinAWord_IsTheWordReplaced(string before, string after, string expected) =>
        await Assert.That(Changes(before, after)).IsEqualTo(expected);

    // Only typed, or only deleted, it is what was typed or deleted.
    [Test]
    [Arguments("colr", "color", "3: '' → 'o'")]
    [Arguments("colour", "color", "4: 'u' → ''")]
    public async Task ALetterAddedToAWord_IsALetterAdded(string before, string after, string expected) =>
        await Assert.That(Changes(before, after)).IsEqualTo(expected);

    [Test]
    public async Task TheTextGivenBack_IsTheNewText()
    {
        var before = "It was the best of times, it was the worst of times, it was the age of wisdom";
        var after = "It was the worst of times; it was the age of foolishness, it was the epoch of belief";

        var hunks = TextDiff.Compare(before, after);

        var rebuilt = new StringBuilder();
        var at = 0;
        foreach (var hunk in hunks)
        {
            rebuilt.Append(before, at, hunk.OldStart - at);
            rebuilt.Append(after, hunk.NewStart, hunk.NewLength);
            at = hunk.OldStart + hunk.OldLength;
        }

        rebuilt.Append(before, at, before.Length - at);
        await Assert.That(rebuilt.ToString()).IsEqualTo(after);
    }

    // Two emoji that share their first UTF-16 unit differ only in their second.
    [Test]
    public async Task AChange_NeverPartsTheHalvesOfOneCharacter() =>
        await Assert.That(Changes("a \U0001F600 b", "a \U0001F601 b")).IsEqualTo("2: '\U0001F600' → '\U0001F601'");

    [Test]
    public async Task TextsTooFarApartToSearch_AreOneChange()
    {
        var before = string.Concat(Enumerable.Range(0, 3000).Select(_ => (char) ('a' + _ % 7)));
        var after = string.Concat(Enumerable.Range(0, 3000).Select(_ => (char) ('k' + _ % 11)));

        var hunks = TextDiff.Compare(before, after);

        await Assert.That(hunks.Count).IsEqualTo(1);
        await Assert.That(hunks[0]).IsEqualTo(new TextHunk(0, 3000, 0, 3000));
    }
}
