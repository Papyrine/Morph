using static ReviewDocuments;

// Reading a document for review: comments with their threads and the text they are attached to, and
// tracked changes gathered from the many elements Word writes one revision as.
public class DocumentReviewTests
{
    [Test]
    public async Task TrackedChangesFixture_ReadsBothChanges()
    {
        var review = DocumentReview.Read(Corpus("tracked_changes", "01"));

        await Assert.That(review.Comments).IsEmpty();
        await Assert.That(Changes(review)).IsEqualTo("Insertion 'inserted ' Reviewer [1] @1; Deletion 'removed.' Reviewer [3] @3");
        await Assert.That(review.Changes[0].Date).IsEqualTo(new DateTime(2025, 4, 25, 10, 0, 0));
        await Assert.That(review.Changes[0].Part).IsEqualTo(ReviewPart.Body);
        await Assert.That(review.Editable).IsTrue();
        await Assert.That(review.AllowsResolving).IsTrue();
    }

    [Test]
    public async Task CommentsFixture_ReadsTheCommentAndWhatItIsOn()
    {
        var review = DocumentReview.Read(Corpus("comments", "01"));

        await Assert.That(review.Changes).IsEmpty();
        var comment = review.Comments.Single();
        await Assert.That(comment.Id).IsEqualTo("1");
        await Assert.That(comment.Author).IsEqualTo("Reviewer");
        await Assert.That(comment.Initials).IsEqualTo("R");
        await Assert.That(comment.Date).IsEqualTo(new DateTime(2025, 4, 25, 10, 0, 0));
        await Assert.That(comment.Text).IsEqualTo("Looks good to me.");
        await Assert.That(comment.Quote).IsEqualTo("Some text with a review note attached.");
        await Assert.That(comment.Runs.SequenceEqual([0])).IsTrue();
        await Assert.That(comment.Anchor).IsEqualTo(0);
        await Assert.That(comment.Resolved).IsFalse();
        await Assert.That(comment.Replies).IsEmpty();
    }

    // Word-probed: Word shows a w:date at face value whether it ends in Z or in nothing, and one
    // that states an offset as its UTC time.
    [Test]
    [Arguments("2026-01-15T12:00:00Z", 12)]
    [Arguments("2026-01-15T12:00:00", 12)]
    [Arguments("2026-01-15T12:00:00+02:00", 10)]
    public async Task ADate_IsReadAsWordReadsIt(string written, int hour)
    {
        var docx = Build(P(Ins("Ann", 1, R("added"))).Replace("2025-04-25T10:00:00Z", written));

        await Assert.That(DocumentReview.Read(docx).Changes.Single().Date).IsEqualTo(new DateTime(2026, 1, 15, hour, 0, 0));
    }

    [Test]
    public async Task UnrevisedDocument_HasNothingToReview()
    {
        var review = DocumentReview.Read(Corpus("all_caps"));

        await Assert.That(review.Comments).IsEmpty();
        await Assert.That(review.Changes).IsEmpty();
    }

    // Typing a sentence with a bold word in it and a paragraph break writes three w:ins and a revised
    // paragraph mark. It is one insertion.
    [Test]
    public async Task AdjacentInsertions_ByOneAuthor_AreOneChange()
    {
        var docx = Build(
            P(Mark("ins", "Ann", 3), R("Start "), Ins("Ann", 1, R("one ")), Ins("Ann", 2, R("two", bold: true))) +
            P(Ins("Ann", 4, R("three")), R(" end")));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Insertion 'one two\\nthree' Ann [1,2,3] @1");
    }

    [Test]
    public async Task AdjacentInsertions_ByDifferentAuthors_AreSeparate()
    {
        var docx = Build(P(Ins("Ann", 1, R("one ")), Ins("Bob", 2, R("two"))));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Insertion 'one ' Ann [0] @0; Insertion 'two' Bob [1] @1");
    }

    [Test]
    public async Task UnrevisedText_SeparatesChanges()
    {
        var docx = Build(P(Ins("Ann", 1, R("one")), R(" and "), Ins("Ann", 2, R("two"))));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Insertion 'one' Ann [0] @0; Insertion 'two' Ann [2] @2");
    }

    [Test]
    public async Task AnUnrevisedParagraphMark_SeparatesChanges()
    {
        var docx = Build(P(R("a "), Ins("Ann", 1, R("one"))) + P(Ins("Ann", 2, R("two"))));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Insertion 'one' Ann [1] @1; Insertion 'two' Ann [2] @2");
    }

    // A replacement is a deletion followed by an insertion: two changes, and the insertions either side
    // of a deletion are not one insertion.
    [Test]
    public async Task ADeletion_SeparatesTheInsertionsAroundIt()
    {
        var docx = Build(P(Ins("Ann", 1, R("one")), Del("Ann", 2, "gone"), Ins("Ann", 3, R("two"))));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Insertion 'one' Ann [0] @0; Deletion 'gone' Ann [1] @1; Insertion 'two' Ann [2] @2");
    }

    [Test]
    public async Task ADeletedParagraph_IsOneDeletion_WithItsMark()
    {
        var docx = Build(P(R("keep")) + P(Mark("del", "Ann", 2), Del("Ann", 1, "gone")) + P(R("after")));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Deletion 'gone\\n' Ann [1] @1");
    }

    // A paragraph break inserted on its own covers no run: it sits after the last run of its paragraph.
    [Test]
    public async Task ALoneParagraphMark_IsPlacedAtTheEndOfItsParagraph()
    {
        var docx = Build(P(Mark("ins", "Ann", 1), R("first")) + P(R("second")));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Insertion '\\n' Ann [] @0");
    }

    // One reviewer's insertion, deleted by another: the deletion is a change of its own inside it.
    [Test]
    public async Task ARevisionInsideAnother_IsItsOwnChange()
    {
        var docx = Build(P(Ins("Ann", 1, R("kept "), Del("Bob", 2, "dropped"), R("too"))));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Insertion 'kept droppedtoo' Ann [0,1,2] @0; Deletion 'dropped' Bob [1] @1");
    }

    [Test]
    public async Task AMove_IsOneChange_PlacedAtItsDestination()
    {
        var docx = Build(
            P(
                $"<w:moveFromRangeStart w:id=\"10\" w:name=\"move1\" w:author=\"Ann\" w:date=\"2025-04-25T10:00:00Z\"/>",
                $"<w:moveFrom {By("Ann", 11)}>{R("moved")}</w:moveFrom>",
                "<w:moveFromRangeEnd w:id=\"10\"/>",
                R(" rest")) +
            P(
                R("before "),
                $"<w:moveToRangeStart w:id=\"12\" w:name=\"move1\" w:author=\"Ann\" w:date=\"2025-04-25T10:00:00Z\"/>",
                $"<w:moveTo {By("Ann", 13)}>{R("moved")}</w:moveTo>",
                "<w:moveToRangeEnd w:id=\"12\"/>"));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Move 'moved' Ann [0,3] @3");
    }

    // Making a phrase bold with tracking on writes a w:rPrChange into every run of it.
    [Test]
    public async Task FormattingAcrossRuns_IsOneChange()
    {
        const string change = "<w:rPrChange w:id=\"1\" w:author=\"Ann\" w:date=\"2025-04-25T10:00:00Z\"><w:rPr/></w:rPrChange>";
        var docx = Build(
            P(
                R("plain "),
                $"<w:r><w:rPr><w:b/>{change}</w:rPr><w:t xml:space=\"preserve\">now </w:t></w:r>",
                $"<w:r><w:rPr><w:b/><w:i/>{change}</w:rPr><w:t>bold</w:t></w:r>",
                R(" plain")));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("Formatting 'now bold' Ann [1,2] @1");
    }

    [Test]
    public async Task ParagraphFormatting_CoversTheParagraph()
    {
        var docx = Build(
            P(R("untouched")) +
            P($"<w:pPr><w:jc w:val=\"center\"/><w:pPrChange {By("Ann")}><w:pPr/></w:pPrChange></w:pPr>", R("centred "), R("now")));

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("ParagraphFormatting 'centred now' Ann [1,2] @1");
    }

    [Test]
    public async Task InsertedRows_AreOneChange()
    {
        var row = $"<w:tr><w:trPr><w:ins {By("Ann", 5)}/></w:trPr><w:tc>{P(R("new"))}</w:tc></w:tr>";
        var docx = Build($"<w:tbl><w:tr><w:tc>{P(R("old"))}</w:tc></w:tr>{row}{row}</w:tbl>" + P());

        var review = DocumentReview.Read(docx);

        await Assert.That(Changes(review)).IsEqualTo("RowInsertion 'new\\nnew' Ann [1,2] @1");
    }

    // A drawing's text box is written twice — DrawingML, and a VML twin for old readers. A revision in
    // it is one revision.
    [Test]
    public async Task ATextBoxsFallback_IsNotListedTwice()
    {
        var content = $"<w:txbxContent>{P(Ins("Ann", 1, R("boxed")))}</w:txbxContent>";
        var docx = Build(
            P(
                R("lead "),
                "<w:r><mc:AlternateContent>" +
                $"<mc:Choice Requires=\"wps\"><w:drawing><wp:inline><a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"><wps:wsp><wps:txbx>{content}</wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice>" +
                $"<mc:Fallback><w:pict><v:shape><v:textbox>{content}</v:textbox></v:shape></w:pict></mc:Fallback>" +
                "</mc:AlternateContent></w:r>",
                R(" tail")));

        var review = DocumentReview.Read(docx);

        // Runs: lead 0, the drawing's run 1, the box's 2, its twin 3, tail 4.
        await Assert.That(Changes(review)).IsEqualTo("Insertion 'boxed' Ann [2] @2");
    }

    [Test]
    public async Task ChangesOutsideTheBody_AreListedWithoutAPlace()
    {
        var docx = Build(P(R("body")), header: P(Ins("Ann", 1, R("header text"))));

        var review = DocumentReview.Read(docx);

        var change = review.Changes.Single();
        await Assert.That(change.Part).IsEqualTo(ReviewPart.Header);
        await Assert.That(change.Text).IsEqualTo("header text");
        await Assert.That(change.Runs).IsEmpty();
        await Assert.That(change.Anchor).IsEqualTo(-1);
    }

    [Test]
    public async Task Comments_AreThreaded_AndCarryTheirResolution()
    {
        var docx = Build(
            P("<w:commentRangeStart w:id=\"0\"/>", R("the point"), "<w:commentRangeEnd w:id=\"0\"/>", "<w:r><w:commentReference w:id=\"0\"/></w:r>", "<w:r><w:commentReference w:id=\"1\"/></w:r>") +
            P("<w:commentRangeStart w:id=\"2\"/>", R("another"), "<w:commentRangeEnd w:id=\"2\"/>", "<w:r><w:commentReference w:id=\"2\"/></w:r>"),
            comments:
            "<w:comment w:id=\"0\" w:author=\"Ann\" w:initials=\"A\"><w:p w14:paraId=\"0000000A\"><w:r><w:t>Why?</w:t></w:r></w:p></w:comment>" +
            "<w:comment w:id=\"1\" w:author=\"Bob\" w:initials=\"B\"><w:p w14:paraId=\"0000000B\"><w:r><w:t>Because.</w:t></w:r></w:p><w:p w14:paraId=\"0000000C\"><w:r><w:t>See above.</w:t></w:r></w:p></w:comment>" +
            "<w:comment w:id=\"2\" w:author=\"Ann\" w:initials=\"A\"><w:p w14:paraId=\"0000000D\"><w:r><w:t>Open</w:t></w:r></w:p></w:comment>",
            commentsExtended:
            "<w15:commentEx w15:paraId=\"0000000A\" w15:done=\"1\"/>" +
            "<w15:commentEx w15:paraId=\"0000000C\" w15:paraIdParent=\"0000000A\" w15:done=\"1\"/>" +
            "<w15:commentEx w15:paraId=\"0000000D\" w15:done=\"0\"/>");

        await Assert.That(DescribeComments(docx)).IsEqualTo(
            """
            0 Ann: Why? (resolved)
              1 Bob: Because.\nSee above. (resolved)
            2 Ann: Open
            """);

        var review = DocumentReview.Read(docx);
        await Assert.That(review.Comments[0].Quote).IsEqualTo("the point");
        await Assert.That(review.Comments[0].Runs.SequenceEqual([0])).IsTrue();
        await Assert.That(review.Comments[1].Anchor).IsEqualTo(3);
    }

    [Test]
    public async Task ACommentAcrossParagraphs_QuotesALinePerParagraph()
    {
        var docx = Build(
            P(R("before "), "<w:commentRangeStart w:id=\"0\"/>", R("first")) +
            P(R("second"), "<w:commentRangeEnd w:id=\"0\"/>", "<w:r><w:commentReference w:id=\"0\"/></w:r>", R(" after")),
            comments: "<w:comment w:id=\"0\" w:author=\"Ann\"><w:p><w:r><w:t>Both</w:t></w:r></w:p></w:comment>");

        var comment = DocumentReview.Read(docx).Comments.Single();

        await Assert.That(comment.Quote).IsEqualTo("first\nsecond");
        await Assert.That(comment.Runs.SequenceEqual([1, 2])).IsTrue();
    }

    // A comment on a point has a reference mark and no range.
    [Test]
    public async Task ACommentWithoutARange_SitsAtItsReference()
    {
        var docx = Build(
            P(R("text"), "<w:r><w:commentReference w:id=\"0\"/></w:r>", R(" more")),
            comments: "<w:comment w:id=\"0\" w:author=\"Ann\"><w:p><w:r><w:t>Here</w:t></w:r></w:p></w:comment>");

        var comment = DocumentReview.Read(docx).Comments.Single();

        await Assert.That(comment.Runs).IsEmpty();
        await Assert.That(comment.Quote).IsEqualTo("");
        await Assert.That(comment.Anchor).IsEqualTo(1);
    }

    [Test]
    [Arguments("readOnly", "1", false, false)]
    [Arguments("forms", "1", false, false)]
    [Arguments("comments", "1", true, false)]
    [Arguments("trackedChanges", "1", true, false)]
    [Arguments("readOnly", "0", true, true)]
    public async Task Protection_LimitsWhatAReviewerMayDo(string edit, string enforcement, bool comments, bool resolving)
    {
        var docx = Build(
            P(Ins("Ann", 1, R("text"))),
            settings: $"<w:documentProtection w:edit=\"{edit}\" w:enforcement=\"{enforcement}\"/>");

        var review = DocumentReview.Read(docx);

        await Assert.That(review.AllowsComments).IsEqualTo(comments);
        await Assert.That(review.AllowsResolving).IsEqualTo(resolving);
    }

    [Test]
    public async Task Keys_RoundTrip()
    {
        var key = RevisionElements.Key(2, [7, 3, 4, 5, 9, 3]);

        await Assert.That(key).IsEqualTo("2:3-5,7,9");
        await Assert.That(RevisionElements.TryParseKey(key, out var part, out var ordinals)).IsTrue();
        await Assert.That(part).IsEqualTo(2);
        await Assert.That(ordinals.SequenceEqual([3, 4, 5, 7, 9])).IsTrue();
        await Assert.That(RevisionElements.TryParseKey("nonsense", out _, out _)).IsFalse();
        await Assert.That(RevisionElements.TryParseKey("1:5-2", out _, out _)).IsFalse();
    }

    static string Changes(DocumentReview review) =>
        string.Join("; ", review.Changes.Select(_ => $"{_.Kind} '{_.Text.Replace("\n", "\\n")}' {_.Author} [{string.Join(',', _.Runs)}] @{_.Anchor}"));
}
