using static ReviewDocuments;

// The edits a reviewer makes. Each is asserted on the document it leaves behind, read back in the
// notation ReviewDocuments.Describe documents.
public class ReviewEditorTests
{
    // Half past two in the afternoon in Sydney, which is half past four in the morning in UTC.
    static readonly DateTimeOffset now = new(2026, 9, 27, 14, 30, 45, TimeSpan.FromHours(10));

    // Accepting and rejecting

    [Test]
    public async Task Fixture_AcceptAll_KeepsInsertionsAndDropsDeletions()
    {
        var docx = Corpus("tracked_changes", "01");
        await Assert.That(Describe(docx)).IsEqualTo("Hello {+inserted +}world {-removed.-}¶");

        var accepted = ReviewEditor.ResolveAll(docx, accept: true);

        await Assert.That(Describe(accepted)).IsEqualTo("Hello inserted world ¶");
        await Assert.That(DocumentReview.Read(accepted).Changes).IsEmpty();
    }

    [Test]
    public async Task Fixture_RejectAll_DropsInsertionsAndRestoresDeletions()
    {
        var rejected = ReviewEditor.ResolveAll(Corpus("tracked_changes", "01"), accept: false);

        await Assert.That(Describe(rejected)).IsEqualTo("Hello world removed.¶");
        await Assert.That(DocumentReview.Read(rejected).Changes).IsEmpty();

        // Restored text is ordinary text again, not w:delText.
        await Assert.That(BodyXml(rejected)).DoesNotContain("delText");
        await Assert.That(BodyXml(rejected)).Contains(">removed.</w:t>");
    }

    [Test]
    public async Task OneChange_IsSettledAlone()
    {
        var docx = Corpus("tracked_changes", "01");
        var deletion = DocumentReview.Read(docx).Changes.Single(_ => _.Kind == ReviewChangeKind.Deletion);

        var edited = ReviewEditor.Resolve(docx, [deletion.Key], accept: true);

        await Assert.That(Describe(edited)).IsEqualTo("Hello {+inserted +}world ¶");
        await Assert.That(DocumentReview.Read(edited).Changes.Single().Kind).IsEqualTo(ReviewChangeKind.Insertion);
    }

    [Test]
    public async Task TheOriginal_IsNeverTouched()
    {
        var docx = Corpus("tracked_changes", "01");
        var before = docx.ToArray();

        ReviewEditor.ResolveAll(docx, accept: true);

        await Assert.That(docx.AsSpan().SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task AnUnknownKey_ChangesNothing()
    {
        var docx = Corpus("tracked_changes", "01");

        var edited = ReviewEditor.Resolve(docx, ["0:99", "7:1", "rubbish"], accept: true);

        await Assert.That(Describe(edited)).IsEqualTo(Describe(docx));
    }

    // A change spread over several elements and a paragraph mark is settled whole.
    [Test]
    public async Task AnInsertionAcrossParagraphs_Rejected_RejoinsTheParagraph()
    {
        var docx = Build(
            P(Mark("ins", "Ann", 3), R("Start "), Ins("Ann", 1, R("one "))) +
            P(Ins("Ann", 4, R("two ")), R("end")));
        await Assert.That(Describe(docx)).IsEqualTo("Start {+one +}¶+{+two +}end¶");
        var change = DocumentReview.Read(docx).Changes.Single();

        await Assert.That(Describe(ReviewEditor.Resolve(docx, [change.Key], accept: false))).IsEqualTo("Start end¶");
        await Assert.That(Describe(ReviewEditor.Resolve(docx, [change.Key], accept: true))).IsEqualTo("Start one ¶two end¶");
    }

    // The common case of deleting a whole paragraph: accepted, it goes, and the paragraph after it is
    // left exactly as it was — its own formatting included.
    [Test]
    public async Task AParagraphDeletedWhole_Accepted_LeavesTheNextAsItWas()
    {
        var docx = Build(
            P(R("keep")) +
            P(Mark("del", "Ann", 2, "<w:jc w:val=\"center\"/>"), Del("Ann", 1, "gone")) +
            P("<w:pPr><w:jc w:val=\"right\"/></w:pPr>", R("after")));

        var accepted = ReviewEditor.ResolveAll(docx, accept: true);

        await Assert.That(Describe(accepted)).IsEqualTo("keep¶after¶");
        await Assert.That(BodyXml(accepted)).Contains("w:val=\"right\"");
        await Assert.That(BodyXml(accepted)).DoesNotContain("w:val=\"center\"");
    }

    [Test]
    public async Task AParagraphDeletedWhole_Rejected_ComesBack()
    {
        var docx = Build(P(R("keep")) + P(Mark("del", "Ann", 2), Del("Ann", 1, "gone")) + P(R("after")));

        var rejected = ReviewEditor.ResolveAll(docx, accept: false);

        await Assert.That(Describe(rejected)).IsEqualTo("keep¶gone¶after¶");
    }

    // Deleting only the break joins two paragraphs. Formatting lives in a paragraph's mark, so the joined
    // paragraph keeps the following one's: the mark left standing. Word-probed — Word accepts this
    // document to one right-aligned paragraph.
    [Test]
    public async Task ADeletedParagraphBreak_Accepted_JoinsTheParagraphs_UnderTheSecondOnesFormatting()
    {
        var docx = Build(
            P(Mark("del", "Ann", 1, "<w:jc w:val=\"center\"/>"), R("first ")) +
            P("<w:pPr><w:jc w:val=\"right\"/></w:pPr>", R("second")) +
            P(R("last")));

        var accepted = ReviewEditor.ResolveAll(docx, accept: true);

        await Assert.That(Describe(accepted)).IsEqualTo("first second¶last¶");
        await Assert.That(BodyXml(accepted)).Contains("w:val=\"right\"");
        await Assert.That(BodyXml(accepted)).DoesNotContain("w:val=\"center\"");

        // Rejected, the break stays and both paragraphs are as they were.
        var rejected = ReviewEditor.ResolveAll(docx, accept: false);
        await Assert.That(Describe(rejected)).IsEqualTo("first ¶second¶last¶");
        await Assert.That(BodyXml(rejected)).Contains("w:val=\"center\"");
        await Assert.That(DocumentReview.Read(rejected).Changes).IsEmpty();
    }

    // The mirror of it, and Word-probed the same way: rejecting an inserted break.
    [Test]
    public async Task AnInsertedParagraphBreak_Rejected_JoinsTheParagraphs_UnderTheSecondOnesFormatting()
    {
        var docx = Build(
            P(Mark("ins", "Ann", 1, "<w:jc w:val=\"center\"/>"), R("first ")) +
            P("<w:pPr><w:jc w:val=\"right\"/></w:pPr>", R("second")) +
            P(R("last")));

        var rejected = ReviewEditor.ResolveAll(docx, accept: false);

        await Assert.That(Describe(rejected)).IsEqualTo("first second¶last¶");
        await Assert.That(BodyXml(rejected)).Contains("w:val=\"right\"");
        await Assert.That(BodyXml(rejected)).DoesNotContain("w:val=\"center\"");
    }

    // A revision on the second paragraph's mark is still on the mark of the two joined.
    [Test]
    public async Task AJoinedParagraph_KeepsTheRevisionOnTheMarkLeftStanding()
    {
        var docx = Build(
            P(Mark("del", "Ann", 1), R("a")) +
            P(Mark("ins", "Bob", 2), R("b")) +
            P(R("c")));
        var deletion = DocumentReview.Read(docx).Changes.Single(_ => _.Kind == ReviewChangeKind.Deletion);

        var accepted = ReviewEditor.Resolve(docx, [deletion.Key], accept: true);

        await Assert.That(Describe(accepted)).IsEqualTo("ab¶+c¶");
        await Assert.That(DocumentReview.Read(accepted).Changes.Single().Kind).IsEqualTo(ReviewChangeKind.Insertion);
    }

    // Three paragraphs deleted into one: each break goes in turn.
    [Test]
    public async Task ConsecutiveDeletedBreaks_AllGo()
    {
        var docx = Build(
            P(Mark("del", "Ann", 1), R("a")) +
            P(Mark("del", "Ann", 2), R("b")) +
            P(R("c")));

        var accepted = ReviewEditor.ResolveAll(docx, accept: true);

        await Assert.That(Describe(accepted)).IsEqualTo("abc¶");
    }

    // The last paragraph of a cell has nothing after it to join.
    [Test]
    public async Task ADeletedBreak_WithNoParagraphAfterIt_OnlyLosesItsMark()
    {
        var docx = Build($"<w:tbl><w:tr><w:tc>{P(Mark("del", "Ann", 1), R("cell"))}</w:tc></w:tr></w:tbl>" + P(R("after")));

        var accepted = ReviewEditor.ResolveAll(docx, accept: true);

        await Assert.That(Describe(accepted)).IsEqualTo("<cell>after¶");
        await Assert.That(DocumentReview.Read(accepted).Changes).IsEmpty();
    }

    [Test]
    public async Task ANestedRevision_SurvivesTheOneAroundIt()
    {
        var docx = Build(P(Ins("Ann", 1, R("kept "), Del("Bob", 2, "dropped"), R(" too"))));
        var changes = DocumentReview.Read(docx).Changes;
        var insertion = changes.Single(_ => _.Kind == ReviewChangeKind.Insertion);

        var accepted = ReviewEditor.Resolve(docx, [insertion.Key], accept: true);
        await Assert.That(Describe(accepted)).IsEqualTo("kept {-dropped-} too¶");

        var rejected = ReviewEditor.Resolve(docx, [insertion.Key], accept: false);
        await Assert.That(Describe(rejected)).IsEqualTo("¶");
        await Assert.That(DocumentReview.Read(rejected).Changes).IsEmpty();
    }

    [Test]
    public async Task AMove_IsSettledAtBothEnds()
    {
        var docx = Build(
            P(
                "<w:moveFromRangeStart w:id=\"10\" w:name=\"move1\" w:author=\"Ann\" w:date=\"2025-04-25T10:00:00Z\"/>",
                $"<w:moveFrom {By("Ann", 11)}>{R("moved ")}</w:moveFrom>",
                "<w:moveFromRangeEnd w:id=\"10\"/>",
                R("rest")) +
            P(
                R("before "),
                "<w:moveToRangeStart w:id=\"12\" w:name=\"move1\" w:author=\"Ann\" w:date=\"2025-04-25T10:00:00Z\"/>",
                $"<w:moveTo {By("Ann", 13)}>{R("moved ")}</w:moveTo>",
                "<w:moveToRangeEnd w:id=\"12\"/>"));
        await Assert.That(Describe(docx)).IsEqualTo("{<moved <}rest¶before {>moved >}¶");
        var move = DocumentReview.Read(docx).Changes.Single();

        var accepted = ReviewEditor.Resolve(docx, [move.Key], accept: true);
        await Assert.That(Describe(accepted)).IsEqualTo("rest¶before moved ¶");
        await Assert.That(BodyXml(accepted)).DoesNotContain("w:move");

        var rejected = ReviewEditor.Resolve(docx, [move.Key], accept: false);
        await Assert.That(Describe(rejected)).IsEqualTo("moved rest¶before ¶");
        await Assert.That(BodyXml(rejected)).DoesNotContain("w:move");
    }

    [Test]
    public async Task Formatting_Rejected_RestoresWhatItReplaced()
    {
        const string change = "<w:rPrChange w:id=\"1\" w:author=\"Ann\" w:date=\"2025-04-25T10:00:00Z\"><w:rPr><w:i/></w:rPr></w:rPrChange>";
        var docx = Build(P($"<w:r><w:rPr><w:b/>{change}</w:rPr><w:t>text</w:t></w:r>"));
        await Assert.That(Describe(docx)).IsEqualTo("~text~¶");

        var accepted = ReviewEditor.ResolveAll(docx, accept: true);
        await Assert.That(BodyXml(accepted)).Contains("<w:rPr><w:b /></w:rPr>");

        var rejected = ReviewEditor.ResolveAll(docx, accept: false);
        await Assert.That(BodyXml(rejected)).Contains("<w:rPr><w:i /></w:rPr>");
        await Assert.That(Describe(rejected)).IsEqualTo("text¶");
    }

    // A paragraph's old properties come back; its mark's own properties and its section break were never
    // part of the change, and stay.
    [Test]
    public async Task ParagraphFormatting_Rejected_KeepsTheMarkAndTheSectionBreak()
    {
        var docx = Build(
            P(
                "<w:pPr><w:jc w:val=\"center\"/><w:rPr><w:b/></w:rPr><w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>" +
                $"<w:pPrChange {By("Ann")}><w:pPr><w:jc w:val=\"right\"/></w:pPr></w:pPrChange></w:pPr>",
                R("text")));

        var rejected = ReviewEditor.ResolveAll(docx, accept: false);

        await Assert.That(BodyXml(rejected)).Contains("<w:pPr><w:jc w:val=\"right\" /><w:rPr><w:b /></w:rPr><w:sectPr>");
        await Assert.That(BodyXml(rejected)).DoesNotContain("center");
        await Assert.That(BodyXml(rejected)).DoesNotContain("pPrChange");
    }

    [Test]
    public async Task InsertedRows_Rejected_GoAndTakeAnEmptiedTableWithThem()
    {
        var inserted = $"<w:tr><w:trPr><w:ins {By("Ann", 5)}/></w:trPr><w:tc>{P(R("new"))}</w:tc></w:tr>";
        var mixed = Build($"<w:tbl><w:tr><w:tc>{P(R("old"))}</w:tc></w:tr>{inserted}</w:tbl>" + P(R("after")));
        await Assert.That(Describe(mixed)).IsEqualTo("<old/new+>after¶");

        await Assert.That(Describe(ReviewEditor.ResolveAll(mixed, accept: false))).IsEqualTo("<old>after¶");
        await Assert.That(Describe(ReviewEditor.ResolveAll(mixed, accept: true))).IsEqualTo("<old/new>after¶");

        var whole = Build($"<w:tbl>{inserted}{inserted}</w:tbl>" + P(R("after")));
        await Assert.That(Describe(ReviewEditor.ResolveAll(whole, accept: false))).IsEqualTo("after¶");
    }

    [Test]
    public async Task DeletedRows_Accepted_Go()
    {
        var deleted = $"<w:tr><w:trPr><w:del {By("Ann", 5)}/></w:trPr><w:tc>{P(Del("Ann", 6, "old"))}</w:tc></w:tr>";
        var docx = Build($"<w:tbl><w:tr><w:tc>{P(R("stays"))}</w:tc></w:tr>{deleted}</w:tbl>" + P(R("after")));

        await Assert.That(Describe(ReviewEditor.ResolveAll(docx, accept: true))).IsEqualTo("<stays>after¶");
        await Assert.That(Describe(ReviewEditor.ResolveAll(docx, accept: false))).IsEqualTo("<stays/old>after¶");
    }

    [Test]
    public async Task ChangesInAHeader_AreSettledToo()
    {
        var docx = Build(P(R("body")), header: P(Ins("Ann", 1, R("added")), Del("Ann", 2, "dropped")));
        var changes = DocumentReview.Read(docx).Changes;

        var one = ReviewEditor.Resolve(docx, [changes[0].Key], accept: false);
        await Assert.That(DocumentReview.Read(one).Changes.Single().Kind).IsEqualTo(ReviewChangeKind.Deletion);

        var all = ReviewEditor.ResolveAll(docx, accept: true);
        await Assert.That(DocumentReview.Read(all).Changes).IsEmpty();
        await Assert.That(Read(all, _ => _.MainDocumentPart!.HeaderParts.Single().Header!.InnerText)).IsEqualTo("added");
    }

    // Text that goes takes the comments attached to it along: a comment about nothing is noise.
    [Test]
    public async Task RejectingAnInsertion_DropsTheCommentOnIt()
    {
        var docx = Build(
            P(
                R("before "),
                Ins("Ann", 1, "<w:commentRangeStart w:id=\"0\"/>", R("added"), "<w:commentRangeEnd w:id=\"0\"/>", "<w:r><w:commentReference w:id=\"0\"/></w:r>"),
                R(" after")),
            comments: "<w:comment w:id=\"0\" w:author=\"Bob\"><w:p><w:r><w:t>Really?</w:t></w:r></w:p></w:comment>");

        var rejected = ReviewEditor.ResolveAll(docx, accept: false);

        await Assert.That(Describe(rejected)).IsEqualTo("before  after¶");
        await Assert.That(DocumentReview.Read(rejected).Comments).IsEmpty();

        var accepted = ReviewEditor.ResolveAll(docx, accept: true);
        await Assert.That(Describe(accepted)).IsEqualTo("before [0:added:0](0) after¶");
        await Assert.That(DocumentReview.Read(accepted).Comments.Count).IsEqualTo(1);
    }

    // A comment that only reaches into text that goes keeps the rest of what it was on: its range is
    // closed where the text was, not left with one end.
    [Test]
    public async Task ACommentPartlyOnRejectedText_KeepsTheRestOfItsRange()
    {
        var docx = ReviewEditor.AddComment(Corpus("tracked_changes", "01"), new(0, 3), new(1, 3), "Ann", "Why?", now);
        await Assert.That(Describe(docx)).IsEqualTo("Hel[0:lo {+ins:0]erted +}(0)world {-removed.-}¶");

        var rejected = ReviewEditor.ResolveAll(docx, accept: false);

        await Assert.That(Describe(rejected)).IsEqualTo("Hel[0:lo :0](0)world removed.¶");
        var comment = DocumentReview.Read(rejected).Comments.Single();
        await Assert.That(comment.Quote).IsEqualTo("lo ");
        await Assert.That(comment.Text).IsEqualTo("Why?");
    }

    [Test]
    public async Task ACommentInADeletedRow_GoesWithIt_AndOneThatReachesIntoItStays()
    {
        var deleted =
            $"<w:tr><w:trPr><w:del {By("Ann", 5)}/></w:trPr><w:tc>" +
            P("<w:commentRangeEnd w:id=\"0\"/>", "<w:commentRangeStart w:id=\"1\"/>", Del("Ann", 6, "old"), "<w:commentRangeEnd w:id=\"1\"/>", "<w:r><w:commentReference w:id=\"1\"/></w:r>") +
            "</w:tc></w:tr>";
        var docx = Build(
            P("<w:commentRangeStart w:id=\"0\"/>", R("lead"), "<w:r><w:commentReference w:id=\"0\"/></w:r>") +
            $"<w:tbl><w:tr><w:tc>{P(R("stays"))}</w:tc></w:tr>{deleted}</w:tbl>" +
            P(R("after")),
            comments:
            "<w:comment w:id=\"0\" w:author=\"Ann\"><w:p><w:r><w:t>Reaches in</w:t></w:r></w:p></w:comment>" +
            "<w:comment w:id=\"1\" w:author=\"Bob\"><w:p><w:r><w:t>Inside</w:t></w:r></w:p></w:comment>");

        var accepted = ReviewEditor.ResolveAll(docx, accept: true);

        await Assert.That(DescribeComments(accepted)).IsEqualTo("0 Ann: Reaches in");
        await Assert.That(BodyXml(accepted)).DoesNotContain("w:id=\"1\"");
        await Assert.That(BodyXml(accepted).Split("<w:commentRangeEnd w:id=\"0\"").Length - 1).IsEqualTo(1);
        await Assert.That(DocumentReview.Read(accepted).Comments.Single().Quote).IsEqualTo("lead\nstays");
    }

    // Comments

    [Test]
    public async Task AddComment_OnPartOfARun_SplitsIt()
    {
        var docx = Build(P(R("Hello world", bold: true)));

        var edited = ReviewEditor.AddComment(docx, new(0, 2), new(0, 9), "Simon Cropp", "Why this?", now);

        await Assert.That(Describe(edited)).IsEqualTo("He[0:llo wor:0](0)ld¶");
        await Assert.That(DescribeComments(edited)).IsEqualTo("0 Simon Cropp: Why this?");

        // Each piece keeps the run's formatting.
        await Assert.That(BodyXml(edited).Split("<w:b />").Length - 1).IsEqualTo(3);

        var comment = DocumentReview.Read(edited).Comments.Single();
        await Assert.That(comment.Quote).IsEqualTo("llo wor");
        await Assert.That(comment.Initials).IsEqualTo("SC");
        await Assert.That(comment.Date).IsEqualTo(now.DateTime);
        await Assert.That(comment.Runs.SequenceEqual([1])).IsTrue();
    }

    // Word-probed: Word writes a comment's time twice, and reads w:date back at face value whatever
    // it is stamped. So w:date carries the author's clock, and the moment itself goes beside it.
    [Test]
    public async Task AComment_IsDatedByItsAuthorsClock_AndInUtcBesideIt()
    {
        var docx = ReviewEditor.AddComment(Build(P(R("Hello world"))), new(0, 0), new(0, 5), "Ann", "Note", now);
        docx = ReviewEditor.Reply(docx, "0", "Bob", "Agreed", now.AddHours(1).ToOffset(TimeSpan.FromHours(-5)));

        await Assert.That(Read(docx, Dates)).IsEqualTo("2026-09-27T14:30:45Z 2026-09-27T00:30:45Z");
        await Assert.That(Read(docx, DatesUtc)).IsEqualTo("2026-09-27T04:30:45Z 2026-09-27T05:30:45Z");

        static string Dates(DocumentFormat.OpenXml.Packaging.WordprocessingDocument package) =>
            string.Join(
                ' ',
                package.MainDocumentPart!.WordprocessingCommentsPart!.Comments!
                    .Elements<DocumentFormat.OpenXml.Wordprocessing.Comment>()
                    .Select(_ => _.Date!.InnerText));

        // Each comment's last paragraph is given a durable id, and the durable id its date.
        static string DatesUtc(DocumentFormat.OpenXml.Packaging.WordprocessingDocument package)
        {
            var main = package.MainDocumentPart!;
            var durable = main.WordprocessingCommentsIdsPart!.CommentsIds!
                .Elements<DocumentFormat.OpenXml.Office2019.Word.Cid.CommentId>()
                .ToDictionary(_ => _.ParaId!.Value!, _ => _.DurableId!.Value!);
            var stamped = main.WordCommentsExtensiblePart!.CommentsExtensible!
                .Elements<DocumentFormat.OpenXml.Office2021.Word.CommentsExt.CommentExtensible>()
                .ToDictionary(_ => _.DurableId!.Value!, _ => _.DateUtc!.InnerText!);
            return string.Join(
                ' ',
                main.WordprocessingCommentsPart!.Comments!
                    .Elements<DocumentFormat.OpenXml.Wordprocessing.Comment>()
                    .Select(_ => _.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Last().ParagraphId!.Value!)
                    .Select(_ => stamped[durable[_]]));
        }
    }

    [Test]
    public async Task DeleteComment_TakesItsDateWithIt()
    {
        var docx = ReviewEditor.AddComment(Build(P(R("Hello world"))), new(0, 0), new(0, 5), "Ann", "Note", now);

        // Run 1 is the first comment's reference mark.
        docx = ReviewEditor.AddComment(docx, new(2, 0), new(2, 3), "Bob", "Other", now);

        var edited = ReviewEditor.DeleteComment(docx, "0");

        await Assert.That(Read(edited, _ => _.MainDocumentPart!.WordprocessingCommentsIdsPart!.CommentsIds!.ChildElements.Count)).IsEqualTo(1);
        await Assert.That(Read(edited, _ => _.MainDocumentPart!.WordCommentsExtensiblePart!.CommentsExtensible!.ChildElements.Count)).IsEqualTo(1);
    }

    [Test]
    public async Task AddComment_OnWholeRuns_SplitsNothing()
    {
        var docx = Build(P(R("one "), R("two "), R("three")));

        var edited = ReviewEditor.AddComment(docx, new(1, 0), new(1, 4), "Ann", "Note", now);

        await Assert.That(Describe(edited)).IsEqualTo("one [0:two :0](0)three¶");
        await Assert.That(Read(edited, _ => _.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>().Count())).IsEqualTo(4);
    }

    [Test]
    public async Task AddComment_AcrossParagraphs()
    {
        var docx = Build(P(R("first paragraph")) + P(R("second paragraph")));

        var edited = ReviewEditor.AddComment(docx, new(0, 6), new(1, 6), "Ann", "Both", now);

        await Assert.That(Describe(edited)).IsEqualTo("first [0:paragraph¶second:0](0) paragraph¶");
        await Assert.That(DocumentReview.Read(edited).Comments.Single().Quote).IsEqualTo("paragraph\nsecond");
    }

    // The ends may arrive in either order: a selection dragged backwards is the same selection.
    [Test]
    public async Task AddComment_TakesItsEndsInEitherOrder()
    {
        var docx = Build(P(R("Hello world")));

        var edited = ReviewEditor.AddComment(docx, new(0, 9), new(0, 2), "Ann", "Note", now);

        await Assert.That(Describe(edited)).IsEqualTo("He[0:llo wor:0](0)ld¶");
    }

    [Test]
    public async Task AddComment_OnAPoint()
    {
        var docx = Build(P(R("Hello world")));

        var edited = ReviewEditor.AddComment(docx, new(0, 5), new(0, 5), "Ann", "Here", now);

        await Assert.That(Describe(edited)).IsEqualTo("Hello[0::0](0) world¶");
    }

    // A tab is one position, and a split lands beside it rather than inside anything.
    [Test]
    public async Task AddComment_CountsATabAsOnePosition()
    {
        var docx = Build(P("<w:r><w:t>ab</w:t><w:tab/><w:t>cd</w:t></w:r>"));

        var edited = ReviewEditor.AddComment(docx, new(0, 3), new(0, 5), "Ann", "After the tab", now);

        await Assert.That(Describe(edited)).IsEqualTo("ab\t[0:cd:0](0)¶");
    }

    // A comment inside a deletion keeps its range there; its reference mark is no part of the
    // deleted text, so it goes after.
    [Test]
    public async Task AddComment_InsideARevision_PutsTheReferenceAfterIt()
    {
        var docx = Build(P(R("keep "), Del("Ann", 1, "dropped"), R(" more")));

        var edited = ReviewEditor.AddComment(docx, new(1, 0), new(1, 4), "Bob", "Why?", now);

        await Assert.That(Describe(edited)).IsEqualTo("keep {-[0:drop:0]ped-}(0) more¶");
        await Assert.That(BodyXml(edited)).Contains(">ped</w:delText>");
    }

    [Test]
    public async Task AddComment_TakesTheNextFreeId()
    {
        var docx = Corpus("comments", "01");

        var edited = ReviewEditor.AddComment(docx, new(0, 5), new(0, 9), "Ann", "Second", now);

        await Assert.That(Describe(edited)).IsEqualTo("[1:Some [2:text:2](2) with a review note attached.:1](1)¶");
        await Assert.That(DescribeComments(edited)).IsEqualTo(
            """
            1 Reviewer: Looks good to me.
            2 Ann: Second
            """);
    }

    [Test]
    public async Task AddComment_WritesAParagraphPerLine_InTheDocumentsCommentStyles()
    {
        var docx = Build(
            P(R("Hello world")),
            styles: "<w:style w:type=\"paragraph\" w:styleId=\"CommentText\"><w:name w:val=\"annotation text\"/></w:style>" +
                    "<w:style w:type=\"character\" w:styleId=\"CommentReference\"><w:name w:val=\"annotation reference\"/></w:style>");

        var edited = ReviewEditor.AddComment(docx, new(0, 0), new(0, 5), "Ann", "One\r\nTwo", now);

        await Assert.That(DescribeComments(edited)).IsEqualTo("0 Ann: One\\nTwo");
        var xml = Read(edited, _ => _.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.InnerXml);
        await Assert.That(xml.Split("<w:pStyle w:val=\"CommentText\" />").Length - 1).IsEqualTo(2);
        await Assert.That(xml).Contains("<w:rStyle w:val=\"CommentReference\" /></w:rPr><w:annotationRef />");
        await Assert.That(BodyXml(edited)).Contains("<w:rStyle w:val=\"CommentReference\" /></w:rPr><w:commentReference w:id=\"0\" />");
    }

    [Test]
    public async Task Reply_JoinsTheThread_AndCoversWhatItCovers()
    {
        var docx = Corpus("comments", "01");

        var replied = ReviewEditor.Reply(docx, "1", "Ann", "Agreed", now);
        var again = ReviewEditor.Reply(replied, "2", "Bob", "Me too", now.AddMinutes(1));

        await Assert.That(DescribeComments(again)).IsEqualTo(
            """
            1 Reviewer: Looks good to me.
              2 Ann: Agreed
              3 Bob: Me too
            """);
        // Each reply's range is the thread's own, mark for mark; the reference marks follow in order.
        await Assert.That(Describe(again)).IsEqualTo("[1:[3:[2:Some text with a review note attached.:1]:3]:2](1)(2)(3)¶");
        var thread = DocumentReview.Read(again).Comments.Single();
        await Assert.That(thread.Replies.All(_ => _.Quote == thread.Quote)).IsTrue();
        await Assert.That(thread.Replies.All(_ => _.Runs.SequenceEqual(thread.Runs))).IsTrue();
    }

    // Word-probed: with the reply's range ending at its own reference mark instead, Word read the
    // reply as covering more text than the comment it answers.
    [Test]
    public async Task Reply_ToACommentThatEndsInsideARevision_EndsThereToo()
    {
        var docx = ReviewEditor.AddComment(Corpus("tracked_changes", "01"), new(0, 3), new(1, 3), "Ann", "Why?", now);

        var replied = ReviewEditor.Reply(docx, "0", "Bob", "Because.", now);

        await Assert.That(Describe(replied)).IsEqualTo("Hel[0:[1:lo {+ins:0]:1]erted +}(0)(1)world {-removed.-}¶");
        var thread = DocumentReview.Read(replied).Comments.Single();
        await Assert.That(thread.Quote).IsEqualTo("lo ins");
        await Assert.That(thread.Replies.Single().Quote).IsEqualTo("lo ins");
    }

    [Test]
    public async Task EditComment_KeepsItsPlaceInTheThread()
    {
        var docx = ReviewEditor.Reply(Corpus("comments", "01"), "1", "Ann", "Agreed", now);

        var edited = ReviewEditor.EditComment(docx, "1", "Looks good.\nShip it.");
        edited = ReviewEditor.EditComment(edited, "2", "Agreed, mostly");

        await Assert.That(DescribeComments(edited)).IsEqualTo(
            """
            1 Reviewer: Looks good.\nShip it.
              2 Ann: Agreed, mostly
            """);
    }

    [Test]
    public async Task SetResolved_MarksTheWholeThread_FromAnyCommentInIt()
    {
        var docx = ReviewEditor.Reply(Corpus("comments", "01"), "1", "Ann", "Agreed", now);

        var resolved = ReviewEditor.SetResolved(docx, "2", true);

        await Assert.That(DescribeComments(resolved)).IsEqualTo(
            """
            1 Reviewer: Looks good to me. (resolved)
              2 Ann: Agreed (resolved)
            """);

        var reopened = ReviewEditor.SetResolved(resolved, "1", false);

        await Assert.That(DescribeComments(reopened)).IsEqualTo(
            """
            1 Reviewer: Looks good to me.
              2 Ann: Agreed
            """);
    }

    // The fixture has no commentsExtended part and its comment's paragraph no id: resolving makes both.
    [Test]
    public async Task SetResolved_OnADocumentWithoutThreadState_AddsIt()
    {
        var resolved = ReviewEditor.SetResolved(Corpus("comments", "01"), "1", true);

        await Assert.That(DocumentReview.Read(resolved).Comments.Single().Resolved).IsTrue();
        await Assert.That(Describe(resolved)).IsEqualTo(Describe(Corpus("comments", "01")));
    }

    [Test]
    public async Task AReplyToAResolvedThread_IsResolvedWithIt()
    {
        var resolved = ReviewEditor.SetResolved(Corpus("comments", "01"), "1", true);

        var replied = ReviewEditor.Reply(resolved, "1", "Ann", "Late", now);

        await Assert.That(DescribeComments(replied)).IsEqualTo(
            """
            1 Reviewer: Looks good to me. (resolved)
              2 Ann: Late (resolved)
            """);
    }

    [Test]
    public async Task DeleteComment_OnAReply_LeavesTheThread()
    {
        var docx = ReviewEditor.Reply(Corpus("comments", "01"), "1", "Ann", "Agreed", now);

        var edited = ReviewEditor.DeleteComment(docx, "2");

        await Assert.That(DescribeComments(edited)).IsEqualTo("1 Reviewer: Looks good to me.");
        await Assert.That(Describe(edited)).IsEqualTo("[1:Some text with a review note attached.:1](1)¶");
    }

    [Test]
    public async Task DeleteComment_OnAThread_TakesItsRepliesAndEveryMark()
    {
        var docx = ReviewEditor.Reply(Corpus("comments", "01"), "1", "Ann", "Agreed", now);

        var edited = ReviewEditor.DeleteComment(docx, "1");

        await Assert.That(DocumentReview.Read(edited).Comments).IsEmpty();
        await Assert.That(Describe(edited)).IsEqualTo("Some text with a review note attached.¶");
        await Assert.That(Read(edited, _ => _.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>().Count())).IsEqualTo(1);
        await Assert.That(Read(edited, _ => _.MainDocumentPart!.WordprocessingCommentsExPart!.CommentsEx!.ChildElements.Count)).IsEqualTo(0);
    }

    [Test]
    public async Task AnUnknownComment_IsAnError()
    {
        var docx = Corpus("comments", "01");

        await Assert.That(() => ReviewEditor.EditComment(docx, "42", "text")).Throws<InvalidOperationException>();
    }

    // The same edit to the same file writes the same bytes: nothing random goes into an id.
    [Test]
    public async Task Edits_AreReproducible()
    {
        var docx = Corpus("comments", "01");

        var first = ReviewEditor.Reply(docx, "1", "Ann", "Agreed", now);
        var second = ReviewEditor.Reply(docx, "1", "Ann", "Agreed", now);

        await Assert.That(Read(first, Parts)).IsEqualTo(Read(second, Parts));

        static string Parts(DocumentFormat.OpenXml.Packaging.WordprocessingDocument package)
        {
            var main = package.MainDocumentPart!;
            return main.Document!.OuterXml +
                   main.WordprocessingCommentsPart!.Comments!.OuterXml +
                   main.WordprocessingCommentsExPart!.CommentsEx!.OuterXml +
                   main.WordprocessingCommentsIdsPart!.CommentsIds!.OuterXml +
                   main.WordCommentsExtensiblePart!.CommentsExtensible!.OuterXml;
        }
    }

    // What the viewer does after every edit: parse the result and render it.
    [Test]
    public async Task AnEditedDocument_StillParses()
    {
        var docx = ReviewEditor.AddComment(Corpus("tracked_changes", "01"), new(0, 2), new(2, 3), "Ann", "Note", now);
        docx = ReviewEditor.ResolveAll(docx, accept: true);

        using var stream = new MemoryStream(docx);
        var parsed = DocumentConverter.Parse(stream, null, null, captureSources: true);

        var text = string.Concat(parsed.Elements.OfType<ParagraphElement>().SelectMany(_ => _.Runs).Select(_ => _.Text));
        await Assert.That(text).IsEqualTo("Hello inserted world ");
        await Assert.That(parsed.Comments.Count).IsEqualTo(1);
    }
}
