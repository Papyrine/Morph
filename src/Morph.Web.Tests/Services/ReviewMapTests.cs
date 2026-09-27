// The review pane's two-way street between the document and the pages: the stretches of page text a
// comment or a change covers, and the place in the document a selection's ends are.
public class ReviewMapTests
{
    // One page reading "1. Hello brave new world" then a header-ish tail nothing is traced to:
    //   offsets 0-2  "1. "        the list marker, from nowhere
    //           3-8  "Hello "     run 0
    //           9-13 "brave"      run 1 (positions 0-4)
    //           14   " "          a justified gap, from nowhere
    //           15-17 "new"       run 1 (positions 6-8)
    //           18   " "
    //           19-23 "world"     run 3
    //           24-29 " (end)"    from nowhere
    static readonly LayerSource[] first =
    [
        new(3, 6, 0, 0, false),
        new(9, 5, 1, 0, false),
        new(15, 3, 1, 6, false),
        new(19, 5, 3, 0, false)
    ];

    // A second page: "12" a note's reference (run 5, one position), then " after" from run 6.
    static readonly LayerSource[] second =
    [
        new(0, 2, 5, 2, true),
        new(2, 6, 6, 0, false)
    ];

    static ReviewMap Map() =>
        new([first, [], second]);

    static string Describe(IEnumerable<TextMatch> ranges) =>
        string.Join(' ', ranges.Select(_ => $"{_.Page}:{_.Start}+{_.Length}"));

    [Test]
    public async Task Ranges_AreTheStretchesTheRunsWereDrawnAs()
    {
        await Assert.That(Describe(Map().Ranges([0]))).IsEqualTo("0:3+6");
        await Assert.That(Describe(Map().Ranges([3, 6]))).IsEqualTo("0:19+5 2:2+6");
        await Assert.That(Map().Ranges([42])).IsEmpty();
    }

    // A change drawn as several pieces with only layout between them is painted as one.
    [Test]
    public async Task Ranges_BridgeWhatTheLayoutPutBetweenPieces()
    {
        await Assert.That(Describe(Map().Ranges([1]))).IsEqualTo("0:9+9");
        await Assert.That(Describe(Map().Ranges([0, 1, 3]))).IsEqualTo("0:3+21");
    }

    // Another run's text between two pieces keeps them apart, and so does a page break.
    [Test]
    public async Task Ranges_DoNotBridgeOtherText_OrPages()
    {
        await Assert.That(Describe(Map().Ranges([0, 3]))).IsEqualTo("0:3+6 0:19+5");
        await Assert.That(Describe(Map().Ranges([3, 5]))).IsEqualTo("0:19+5 2:0+2");

        // A run named twice is one run.
        await Assert.That(Describe(Map().Ranges([1, 1, 0]))).IsEqualTo("0:3+15");
    }

    // A run that draws nothing — a comment's reference mark — is placed at the next one that does.
    [Test]
    public async Task Place_IsWhereARunStarts_OrTheNextDrawnOne()
    {
        await Assert.That(Map().Place(1)).IsEqualTo(new TextMatch(0, 9, 0));
        await Assert.That(Map().Place(2)).IsEqualTo(new TextMatch(0, 19, 0));
        await Assert.That(Map().Place(4)).IsEqualTo(new TextMatch(2, 0, 0));
        await Assert.That(Map().Place(7)).IsNull();
        await Assert.That(Map().Place(-1)).IsNull();
    }

    [Test]
    public async Task RunAt_IsTheRunACharacterWasDrawnFrom()
    {
        await Assert.That(Map().RunAt(0, 3)).IsEqualTo(0);
        await Assert.That(Map().RunAt(0, 13)).IsEqualTo(1);
        await Assert.That(Map().RunAt(0, 14)).IsNull();
        await Assert.That(Map().RunAt(0, 0)).IsNull();
        await Assert.That(Map().RunAt(0, 40)).IsNull();
        await Assert.That(Map().RunAt(1, 0)).IsNull();
        await Assert.That(Map().RunAt(9, 0)).IsNull();
    }

    [Test]
    public async Task Before_IsThePlaceAheadOfACharacter()
    {
        await Assert.That(Map().Before(0, 3)).IsEqualTo(new SourcePosition(0, 0));
        await Assert.That(Map().Before(0, 5)).IsEqualTo(new SourcePosition(0, 2));
        await Assert.That(Map().Before(0, 16)).IsEqualTo(new SourcePosition(1, 7));
    }

    // The marker, a gap, the tail of a page: each stands for the traced text that follows it.
    [Test]
    public async Task Before_AnUntracedCharacter_IsAheadOfTheNextTracedOne()
    {
        await Assert.That(Map().Before(0, 0)).IsEqualTo(new SourcePosition(0, 0));
        await Assert.That(Map().Before(0, 14)).IsEqualTo(new SourcePosition(1, 6));
        await Assert.That(Map().Before(0, 26)).IsEqualTo(new SourcePosition(5, 2));
        await Assert.That(Map().Before(1, 0)).IsEqualTo(new SourcePosition(5, 2));
        await Assert.That(Map().Before(2, 40)).IsNull();
    }

    [Test]
    public async Task After_IsThePlacePastACharacter()
    {
        await Assert.That(Map().After(0, 3)).IsEqualTo(new SourcePosition(0, 1));
        await Assert.That(Map().After(0, 8)).IsEqualTo(new SourcePosition(0, 6));
        await Assert.That(Map().After(0, 17)).IsEqualTo(new SourcePosition(1, 9));
    }

    [Test]
    public async Task After_AnUntracedCharacter_IsPastTheLastTracedOne()
    {
        await Assert.That(Map().After(0, 14)).IsEqualTo(new SourcePosition(1, 5));
        await Assert.That(Map().After(0, 29)).IsEqualTo(new SourcePosition(3, 5));
        await Assert.That(Map().After(1, 5)).IsEqualTo(new SourcePosition(3, 5));

        // A selection that ends at the very start of a page ends with the page before it.
        await Assert.That(Map().After(2, -1)).IsEqualTo(new SourcePosition(3, 5));
        await Assert.That(Map().After(0, 1)).IsNull();
    }

    // However many digits a note's number is drawn with, it is one position: before it, or after it.
    [Test]
    public async Task AnAtomicStretch_HasTwoPlaces()
    {
        await Assert.That(Map().Before(2, 0)).IsEqualTo(new SourcePosition(5, 2));
        await Assert.That(Map().Before(2, 1)).IsEqualTo(new SourcePosition(5, 2));
        await Assert.That(Map().After(2, 0)).IsEqualTo(new SourcePosition(5, 3));
        await Assert.That(Map().After(2, 1)).IsEqualTo(new SourcePosition(5, 3));
    }

    [Test]
    public async Task AnEmptyMap_PlacesNothing()
    {
        await Assert.That(ReviewMap.Empty.HasSources).IsFalse();
        await Assert.That(ReviewMap.Empty.Ranges([0])).IsEmpty();
        await Assert.That(ReviewMap.Empty.Place(0)).IsNull();
        await Assert.That(ReviewMap.Empty.Before(0, 0)).IsNull();
        await Assert.That(ReviewMap.Empty.After(0, 0)).IsNull();
        await Assert.That(ReviewMap.Empty.RunAt(0, 0)).IsNull();
    }

    // The whole chain on a real file: select a phrase on the page, find its place in the document,
    // comment on it there, and the comment's range comes back as the phrase that was selected.
    [Test]
    [Arguments("the new Secretary")]
    [Arguments("MEETING")]
    public async Task ASelection_RoundTripsThroughAComment(string phrase)
    {
        var (map, texts) = Open(Sample.DocxBytes);
        var page = Array.FindIndex(texts, _ => Collapse(_).Contains(phrase));
        var start = IndexOfCollapsed(texts[page], phrase);
        var length = LengthOfCollapsed(texts[page], start, phrase);
        var from = map.Before(page, start)!.Value;
        var to = map.After(page, start + length - 1)!.Value;

        var edited = ReviewEditor.AddComment(Sample.DocxBytes, from, to, "Ann", "Note", new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));

        // The page may show in capitals what the file holds in lower case (w:caps).
        var comment = DocumentReview.Read(edited).Comments.Single();
        await Assert.That(Collapse(comment.Quote).ToUpperInvariant()).IsEqualTo(phrase.ToUpperInvariant());

        var (editedMap, editedTexts) = Open(edited);
        var ranges = editedMap.Ranges(comment.Runs);
        var painted = string.Concat(ranges.Select(_ => editedTexts[_.Page].Substring(_.Start, _.Length)));
        await Assert.That(Collapse(painted)).IsEqualTo(phrase);
        await Assert.That(ranges.All(_ => _.Page == page)).IsTrue();
    }

    static (ReviewMap Map, string[] Texts) Open(byte[] docx)
    {
        using var document = PagedDocument.Open(docx, InputFormat.Docx, Sample.FontDirectory, traceSources: true);
        var layers = Enumerable.Range(0, document.PageCount).Select(document.TextLayer).ToList();
        return (new(layers.Select(_ => _.Sources)), layers.Select(_ => _.Text).ToArray());
    }

    static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));

    // Where a phrase starts in a page's text, reading any run of whitespace as one space — the phrase
    // may wrap, and a wrap is a line break in the text.
    static int IndexOfCollapsed(string text, string phrase)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsWhiteSpace(text[index]) &&
                LengthOfCollapsed(text, index, phrase) > 0)
            {
                return index;
            }
        }

        return -1;
    }

    static int LengthOfCollapsed(string text, int start, string phrase)
    {
        var at = start;
        foreach (var ch in phrase)
        {
            if (ch == ' ')
            {
                if (at >= text.Length || !char.IsWhiteSpace(text[at]))
                {
                    return 0;
                }

                while (at < text.Length && char.IsWhiteSpace(text[at]))
                {
                    at++;
                }

                continue;
            }

            if (at >= text.Length || text[at] != ch)
            {
                return 0;
            }

            at++;
        }

        return at - start;
    }
}
