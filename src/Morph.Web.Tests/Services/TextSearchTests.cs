// The viewer's find. Offsets index into each page's text-layer text — the characters the DOM holds — so
// morph-text.js can turn a match straight into a highlight range.
public class TextSearchTests
{
    [Test]
    public async Task FindsEveryOccurrence_OnEveryPage_IgnoringCase()
    {
        var search = new TextSearch(["Budget review\n", "No match here\n", "the BUDGET and the budget\n"]);

        var matches = search.Find("budget", matchCase: false);

        await Assert.That(matches).IsEquivalentTo(new TextMatch[] { new(0, 0, 6), new(2, 4, 6), new(2, 19, 6) });
    }

    [Test]
    public async Task MatchCase_RespectsCase()
    {
        var search = new TextSearch(["Budget budget BUDGET\n"]);

        var matches = search.Find("budget", matchCase: true);

        await Assert.That(matches).IsEquivalentTo([new TextMatch(0, 7, 6)]);
    }

    // A phrase matches across a soft wrap, a paragraph break or a cell tab — any whitespace run matches any
    // other — and the match spans the original characters, separators included.
    [Test]
    public async Task WhitespaceRuns_MatchAcrossLinesAndCells()
    {
        const string text = "was nominated as the new \nSecretary.\nName\t\tValue\n";
        var search = new TextSearch([text]);

        var phrase = search.Find("the new Secretary", matchCase: false).Single();
        var cells = search.Find("name  value", matchCase: false).Single();

        await Assert.That(text.Substring(phrase.Start, phrase.Length)).IsEqualTo("the new \nSecretary");
        await Assert.That(text.Substring(cells.Start, cells.Length)).IsEqualTo("Name\t\tValue");
    }

    [Test]
    public async Task BlankQuery_FindsNothing()
    {
        var search = new TextSearch(["anything\n"]);

        await Assert.That(search.Find("   ", matchCase: false)).IsEmpty();
    }

    [Test]
    public async Task Matches_DoNotOverlap()
    {
        var search = new TextSearch(["aaaa\n"]);

        var matches = search.Find("aa", matchCase: false);

        await Assert.That(matches).IsEquivalentTo(new TextMatch[] { new(0, 0, 2), new(0, 2, 2) });
    }

    [Test]
    public async Task Results_AreCapped()
    {
        var search = new TextSearch([string.Concat(Enumerable.Repeat("x ", TextSearch.MaxMatches + 50))]);

        await Assert.That(search.Find("x", matchCase: false).Count).IsEqualTo(TextSearch.MaxMatches);
    }
}
