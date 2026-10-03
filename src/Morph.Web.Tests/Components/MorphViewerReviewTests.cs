using Microsoft.AspNetCore.Components.Web;

// The viewer's review pane: a Word document's comments and tracked changes, listed, chosen and edited.
// The pages and their highlights are the script's; these tests pin what .NET does — what it reads from
// the file, what it asks the script to paint, and what each edit leaves in the document.
//
// Two fixtures, both one line on one page:
//   tracked_changes   "Hello inserted world removed."   an insertion (run 1) and a deletion (run 3)
//   comments          "Some text with a review note attached."   one comment on all of it
public class MorphViewerReviewTests : BunitTestContext
{
    readonly BunitJSModuleInterop controller;

    public MorphViewerReviewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        controller = SetupViewerController();
    }

    static byte[] Tracked { get; } = File.ReadAllBytes(ProjectFiles.corpus.tracked_changes_docx);

    static byte[] Commented { get; } = File.ReadAllBytes(ProjectFiles.corpus.comments_docx);

    async Task<IRenderedComponent<MorphViewer>> Open(byte[] bytes, Action<ComponentParameterCollectionBuilder<MorphViewer>>? parameters = null, string name = "review.docx")
    {
        var cut = Render(parameters ?? (_ => { }));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(bytes, name, Sample.FontDirectory));
        return cut;
    }

    async Task<IRenderedComponent<MorphViewer>> OpenPane(byte[] bytes, Action<ComponentParameterCollectionBuilder<MorphViewer>>? parameters = null)
    {
        var cut = await Open(bytes, parameters);
        await cut.Find(".viewer-review-toggle").ClickAsync(new());
        return cut;
    }

    // What the script was last asked to paint: the comments' ranges, the chosen item's, and whether to
    // scroll to it.
    (int[] Comments, int[] Current, bool Scroll) Painted()
    {
        var sent = controller.Invocations["setReview"][^1];
        return ((int[]) sent.Arguments[1]!, (int[]) sent.Arguments[2]!, (bool) sent.Arguments[3]!);
    }

    // Each card as the words on it, element by element.
    static string Cards(IRenderedComponent<MorphViewer> cut) =>
        string.Join(" | ", cut.FindAll(".review-card").Select(Words));

    static string Words(AngleSharp.Dom.INode node)
    {
        if (node is AngleSharp.Dom.IText text)
        {
            return string.Join(' ', text.Data.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
        }

        return string.Join(' ', node.ChildNodes.Select(Words).Where(_ => _.Length > 0));
    }

    [Test]
    public async Task AWordDocument_OffersReview_AndCountsWhatIsWaiting()
    {
        var cut = await Open(Tracked);

        await Assert.That(cut.Find(".viewer-review-toggle").GetAttribute("aria-pressed")).IsEqualTo("false");
        await Assert.That(cut.Find(".viewer-badge").TextContent).IsEqualTo("2");
        await Assert.That(cut.FindAll(".viewer-review")).IsEmpty();
    }

    [Test]
    public async Task ADocumentWithNothingToReview_HasNoCount()
    {
        var cut = await Open(Sample.DocxBytes);

        await Assert.That(cut.FindAll(".viewer-review-toggle").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".viewer-badge")).IsEmpty();
    }

    [Test]
    [Arguments(InputFormat.Xlsx, "book.xlsx")]
    [Arguments(InputFormat.Pptx, "deck.pptx")]
    public async Task AWorkbookOrADeck_OffersNoReview(InputFormat format, string name)
    {
        var cut = await Open(Sample.BytesFor(format), name: name);

        await Assert.That(cut.FindAll(".viewer-review-toggle")).IsEmpty();
    }

    [Test]
    public async Task ShowReview_Off_DropsTheButton()
    {
        var cut = await Open(Tracked, _ => _.Add(component => component.ShowReview, false));

        await Assert.That(cut.FindAll(".viewer-review-toggle")).IsEmpty();
    }

    [Test]
    public async Task ThePane_ListsTheChanges()
    {
        var cut = await OpenPane(Tracked);

        await Assert.That(cut.Find(".viewer-review-toggle").GetAttribute("aria-pressed")).IsEqualTo("true");
        await Assert.That(Cards(cut)).IsEqualTo(
            "R Reviewer 25 Apr 2025, 10:00 Inserted inserted Accept Reject | " +
            "R Reviewer 25 Apr 2025, 10:01 Deleted removed. Accept Reject");
        await Assert.That((bool) controller.Invocations["setReviewMode"][^1].Arguments[0]!).IsTrue();

        // Nothing chosen and no comments: nothing to paint yet.
        await Assert.That(Painted().Comments).IsEmpty();
        await Assert.That(Painted().Current).IsEmpty();
    }

    [Test]
    public async Task ThePane_ListsTheComments_AndPaintsWhatTheyAreOn()
    {
        var cut = await OpenPane(Commented);

        await Assert.That(Cards(cut)).IsEqualTo(
            "Some text with a review note attached. R Reviewer 25 Apr 2025, 10:00 Edit Delete Looks good to me. Reply Resolve");
        await Assert.That(Painted().Comments.SequenceEqual([0, 0, 38])).IsTrue();
    }

    [Test]
    public async Task ThePane_Snapshot()
    {
        var bytes = ReviewEditor.AddComment(Tracked, new(0, 0), new(0, 5), "Ann Lee", "A greeting.\nIs it needed?", new(2026, 9, 27, 14, 30, 0, TimeSpan.FromHours(10)));
        bytes = ReviewEditor.Reply(bytes, "0", "Bob", "It is.", new(2026, 9, 27, 15, 0, 0, TimeSpan.FromHours(10)));

        var cut = await Open(bytes, _ => _.Add(component => component.OpenReview, true));

        await Verify(cut);
    }

    [Test]
    public async Task ClosingThePane_TurnsReviewOff()
    {
        var cut = await OpenPane(Tracked);

        await cut.Find(".review-close").ClickAsync(new());

        await Assert.That(cut.FindAll(".viewer-review")).IsEmpty();
        await Assert.That((bool) controller.Invocations["setReviewMode"][^1].Arguments[0]!).IsFalse();
    }

    [Test]
    public async Task OpenReview_OpensThePane_WhereThereIsSomethingInIt()
    {
        var withChanges = await Open(Tracked, _ => _.Add(component => component.OpenReview, true));
        await Assert.That(withChanges.FindAll(".viewer-review").Count).IsEqualTo(1);

        var without = await Open(Sample.DocxBytes, _ => _.Add(component => component.OpenReview, true));
        await Assert.That(without.FindAll(".viewer-review")).IsEmpty();
    }

    [Test]
    public async Task AnEmptyPane_SaysSo()
    {
        var cut = await OpenPane(Sample.DocxBytes);

        await Assert.That(cut.Find(".review-empty").TextContent).IsEqualTo("This document has no comments or tracked changes.");
        await Assert.That(cut.FindAll(".review-card")).IsEmpty();
    }

    [Test]
    public async Task TheFilter_NarrowsTheList_AndWhatIsPainted()
    {
        var bytes = ReviewEditor.AddComment(Tracked, new(0, 0), new(0, 5), "Ann", "Note", new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
        var cut = await OpenPane(bytes);
        await Assert.That(cut.FindAll(".review-card").Count).IsEqualTo(3);

        await cut.Find(".review-filter").ChangeAsync(new() { Value = "Comments" });
        await Assert.That(cut.FindAll(".review-card").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".review-comment").Count).IsEqualTo(1);
        await Assert.That(Painted().Comments.SequenceEqual([0, 0, 5])).IsTrue();

        await cut.Find(".review-filter").ChangeAsync(new() { Value = "Changes" });
        await Assert.That(cut.FindAll(".review-change").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll(".review-comment")).IsEmpty();
        await Assert.That(Painted().Comments).IsEmpty();
    }

    // Choosing a card

    [Test]
    public async Task ChoosingACard_PaintsItsRange_AndScrollsToIt()
    {
        var cut = await OpenPane(Tracked);

        await cut.FindAll(".review-card")[0].ClickAsync(new());

        // "inserted " starts at the seventh character of the page.
        await Assert.That(Painted().Current.SequenceEqual([0, 6, 9])).IsTrue();
        await Assert.That(Painted().Scroll).IsTrue();
        await Assert.That(cut.FindAll(".review-card")[0].ClassList).Contains("review-active");
        await Assert.That(cut.FindAll(".review-card")[0].GetAttribute("aria-current")).IsEqualTo("true");
        await Assert.That(cut.FindAll(".review-card")[1].ClassList).DoesNotContain("review-active");
    }

    [Test]
    public async Task ACard_IsChosenFromTheKeyboard()
    {
        var cut = await OpenPane(Tracked);

        await cut.FindAll(".review-card")[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(Painted().Current.SequenceEqual([0, 21, 8])).IsTrue();
    }

    // A click on the page chooses what is there, without moving the page under the pointer.
    [Test]
    public async Task AClickOnThePage_ChoosesWhatIsThere()
    {
        var cut = await OpenPane(Tracked);

        await cut.InvokeAsync(() => cut.Instance.OnReviewHit(0, 24));

        await Assert.That(cut.FindAll(".review-card")[1].ClassList).Contains("review-active");
        await Assert.That(Painted().Current.SequenceEqual([0, 21, 8])).IsTrue();
        await Assert.That(Painted().Scroll).IsFalse();
        await Assert.That(controller.Invocations["revealReviewCard"][^1].Arguments[0]).IsEqualTo(cut.FindAll(".review-card")[1].GetAttribute("data-review-key"));
    }

    [Test]
    public async Task AClickOnUnrevisedText_ChoosesNothing()
    {
        var cut = await OpenPane(Tracked);

        await cut.InvokeAsync(() => cut.Instance.OnReviewHit(0, 2));

        await Assert.That(cut.FindAll(".review-active")).IsEmpty();
    }

    // Accepting and rejecting

    [Test]
    public async Task Accept_EditsTheDocument_AndSwapsThePagesInPlace()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        var opened = (int) controller.Invocations["load"].Single().Arguments[0]!;

        await cut.FindAll(".review-card")[1].QuerySelectorAll(".review-button")[0].ClickAsync(new());

        // The deletion is gone from the list, the file and the page.
        await Assert.That(Cards(cut)).IsEqualTo("R Reviewer 25 Apr 2025, 10:00 Inserted inserted Accept Reject");
        var reload = controller.Invocations["reload"].Single();
        await Assert.That((int) reload.Arguments[0]!).IsGreaterThan(opened);
        await Assert.That(((double[]) reload.Arguments[1]!).SequenceEqual([612d, 792])).IsTrue();
        await Assert.That(controller.Invocations["load"].Count).IsEqualTo(1);
        await Assert.That(reported).IsNotNull();
        var review = DocumentReview.Read(reported!);
        await Assert.That(review.Changes.Single().Kind).IsEqualTo(ReviewChangeKind.Insertion);
        await Assert.That(cut.Find(".viewer-badge").TextContent).IsEqualTo("1");

        // The change left is the one now chosen, and scrolled to: Accept moves on to the next.
        await Assert.That(cut.Find(".review-card").ClassList).Contains("review-active");
        await Assert.That(Painted().Current.SequenceEqual([0, 6, 9])).IsTrue();
        await Assert.That(Painted().Scroll).IsTrue();
    }

    [Test]
    public async Task Reject_PutsBackWhatWasThere()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.FindAll(".review-card")[1].QuerySelectorAll(".review-button")[1].ClickAsync(new());

        await Assert.That(Text(reported!)).IsEqualTo("Hello inserted world removed.");
        await Assert.That(DocumentReview.Read(reported!).Changes.Single().Kind).IsEqualTo(ReviewChangeKind.Insertion);
    }

    [Test]
    [Arguments("Accept all", "Hello inserted world ")]
    [Arguments("Reject all", "Hello world removed.")]
    public async Task AcceptAll_And_RejectAll_SettleEverything(string action, string expected)
    {
        byte[]? reported = null;
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.FindAll(".review-action").Single(_ => _.TextContent.Trim() == action).ClickAsync(new());

        await Assert.That(Text(reported!)).IsEqualTo(expected);
        await Assert.That(cut.FindAll(".review-card")).IsEmpty();
        await Assert.That(cut.FindAll(".viewer-badge")).IsEmpty();
        await Assert.That(cut.Find(".review-empty").TextContent).Contains("no comments or tracked changes");
    }

    [Test]
    public async Task Download_SavesTheDocumentAsEdited()
    {
        var morph = JSInterop.SetupModule($"./{MorphAssets.Script}");
        morph.Mode = JSRuntimeMode.Loose;
        var cut = await OpenPane(Tracked);

        await cut.FindAll(".review-action").Single(_ => _.TextContent.Trim() == "Accept all").ClickAsync(new());
        await cut.Find("button[aria-label=Download]").ClickAsync(new());

        var saved = (byte[]) morph.Invocations["download"].Single().Arguments[2]!;
        await Assert.That(Text(saved)).IsEqualTo("Hello inserted world ");
    }

    [Test]
    public async Task Undo_And_Redo_StepThroughTheEdits()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        await Assert.That(cut.Find("button[aria-label=Undo]").HasAttribute("disabled")).IsTrue();

        await cut.FindAll(".review-action").Single(_ => _.TextContent.Trim() == "Accept all").ClickAsync(new());
        await Assert.That(cut.Find("button[aria-label=Redo]").HasAttribute("disabled")).IsTrue();

        await cut.Find("button[aria-label=Undo]").ClickAsync(new());
        await Assert.That(reported!.AsSpan().SequenceEqual(Tracked)).IsTrue();
        await Assert.That(cut.FindAll(".review-card").Count).IsEqualTo(2);
        await Assert.That(cut.Find("button[aria-label=Undo]").HasAttribute("disabled")).IsTrue();

        await cut.Find("button[aria-label=Redo]").ClickAsync(new());
        await Assert.That(Text(reported!)).IsEqualTo("Hello inserted world ");
        await Assert.That(cut.FindAll(".review-card")).IsEmpty();
        await Assert.That(controller.Invocations["reload"].Count).IsEqualTo(3);
    }

    // Comments

    [Test]
    public async Task NewComment_WaitsForASelection()
    {
        var cut = await OpenPane(Tracked);
        await Assert.That(cut.Find(".review-action-primary").HasAttribute("disabled")).IsTrue();

        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, 0, 0, 5]));
        await Assert.That(cut.Find(".review-action-primary").HasAttribute("disabled")).IsFalse();

        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([]));
        await Assert.That(cut.Find(".review-action-primary").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task NewComment_IsWrittenOnTheSelection_AndPosted()
    {
        byte[]? reported = null;
        var cut = await OpenPane(
            Tracked,
            _ => _
                .Add(component => component.Author, "Simon Cropp")
                .Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        // "lo ins": from inside one run to inside the next, which is an insertion.
        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, 3, 0, 9]));
        await cut.Find(".review-action-primary").ClickAsync(new());

        // The text being commented on is quoted, and painted while the comment is written.
        await Assert.That(cut.Find(".review-new .review-quote").TextContent).IsEqualTo("lo ins");
        await Assert.That(cut.Find(".review-new .review-name").TextContent).IsEqualTo("Simon Cropp");
        await Assert.That(Painted().Current.SequenceEqual([0, 3, 6])).IsTrue();
        await Assert.That(cut.Find(".review-new .review-button-primary").HasAttribute("disabled")).IsTrue();

        await cut.Find(".review-input").InputAsync(new() { Value = "Why the change?" });
        await cut.Find(".review-new .review-button-primary").ClickAsync(new());

        var comment = DocumentReview.Read(reported!).Comments.Single();
        await Assert.That(comment.Author).IsEqualTo("Simon Cropp");
        await Assert.That(comment.Initials).IsEqualTo("SC");
        await Assert.That(comment.Text).IsEqualTo("Why the change?");
        await Assert.That(comment.Quote).IsEqualTo("lo ins");

        // Nothing but the comment changed: both revisions are still there, the deletion still a deletion.
        await Assert.That(Text(reported!)).IsEqualTo("Hello inserted world ");
        await Assert.That(DocumentReview.Read(reported!).Changes.Count).IsEqualTo(2);

        // Its card is there and chosen, its range painted; the draft is gone.
        await Assert.That(cut.FindAll(".review-new")).IsEmpty();
        await Assert.That(cut.FindAll(".review-card").Count).IsEqualTo(3);
        await Assert.That(cut.Find(".review-comment").ClassList).Contains("review-active");
        await Assert.That(Painted().Comments.SequenceEqual([0, 3, 6])).IsTrue();
        await Assert.That(Painted().Current.SequenceEqual([0, 3, 6])).IsTrue();
        controller.VerifyInvoke("reload");
    }

    [Test]
    public async Task NewComment_IsPostedWithControlEnter_AndAbandonedWithEscape()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, 0, 0, 5]));
        await cut.Find(".review-action-primary").ClickAsync(new());
        await cut.Find(".review-input").InputAsync(new() { Value = "Never mind" });
        await cut.Find(".review-input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await Assert.That(cut.FindAll(".review-new")).IsEmpty();
        await Assert.That(reported).IsNull();
        await Assert.That(Painted().Current).IsEmpty();

        await cut.Find(".review-action-primary").ClickAsync(new());
        await cut.Find(".review-input").InputAsync(new() { Value = "A greeting" });
        await cut.Find(".review-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        await Assert.That(DocumentReview.Read(reported!).Comments.Single().Text).IsEqualTo("A greeting");
    }

    // The clock is the browser's. .NET's own is UTC in an app published without time zone data, and
    // Word reads a comment dated in UTC as made that many hours ago.
    [Test]
    [Arguments(600)]
    [Arguments(-300)]
    public async Task AComment_IsDatedByTheReadersClock(int minutesEast)
    {
        controller.Setup<int>("zoneOffset", _ => true).SetResult(minutesEast);
        byte[]? reported = null;
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, 0, 0, 5]));
        await cut.Find(".review-action-primary").ClickAsync(new());
        await cut.Find(".review-input").InputAsync(new() { Value = "Hi" });
        var posted = DateTime.UtcNow.AddMinutes(minutesEast);
        await cut.Find(".review-new .review-button-primary").ClickAsync(new());

        var date = DocumentReview.Read(reported!).Comments.Single().Date!.Value;
        await Assert.That((date - posted).Duration()).IsLessThan(TimeSpan.FromMinutes(1));
    }

    // The reader is asked for a name, and is a guest until they give one.
    [Test]
    public async Task WithoutAnAuthor_TheReaderSignsTheirOwnName()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, 0, 0, 5]));
        await cut.Find(".review-action-primary").ClickAsync(new());
        await Assert.That(cut.Find(".review-new .review-name").TextContent).IsEqualTo("Guest");

        await cut.Find(".review-signature-input").InputAsync(new() { Value = "Ann Lee" });
        await cut.Find(".review-input").InputAsync(new() { Value = "Hi" });
        await cut.Find(".review-new .review-button-primary").ClickAsync(new());

        await Assert.That(DocumentReview.Read(reported!).Comments.Single().Author).IsEqualTo("Ann Lee");
    }

    [Test]
    public async Task WithAnAuthor_NoNameIsAskedFor()
    {
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.Author, "Ann"));

        await Assert.That(cut.FindAll(".review-signature")).IsEmpty();
    }

    // A selection of nothing but what the layout added has no place in the document.
    [Test]
    public async Task ASelectionWithNoPlaceInTheDocument_IsTurnedDown()
    {
        var cut = await OpenPane(Tracked);

        // The line break that ends the page's text.
        await cut.InvokeAsync(() => cut.Instance.OnReviewSelection([0, 29, 0, 30]));
        await cut.Find(".review-action-primary").ClickAsync(new());

        await Assert.That(cut.FindAll(".review-new")).IsEmpty();
        await Assert.That(cut.Find(".review-note").TextContent).Contains("Select some of the document's text");
    }

    [Test]
    public async Task Reply_JoinsTheThread()
    {
        byte[]? reported = null;
        var cut = await OpenPane(
            Commented,
            _ => _
                .Add(component => component.Author, "Ann")
                .Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.FindAll(".review-button").Single(_ => _.TextContent.Trim() == "Reply").ClickAsync(new());
        await cut.Find(".review-reply .review-input").InputAsync(new() { Value = "Agreed" });
        await cut.Find(".review-reply .review-button-primary").ClickAsync(new());

        var thread = DocumentReview.Read(reported!).Comments.Single();
        await Assert.That(thread.Replies.Single().Text).IsEqualTo("Agreed");
        await Assert.That(thread.Replies.Single().Author).IsEqualTo("Ann");
        await Assert.That(cut.FindAll(".review-card").Count).IsEqualTo(1);
        await Assert.That(cut.Find(".review-reply .review-comment-text").TextContent).IsEqualTo("Agreed");
        await Assert.That(cut.Find(".review-card").ClassList).Contains("review-active");
    }

    [Test]
    public async Task Resolve_MarksTheThread_AndStopsPaintingIt()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Commented, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.FindAll(".review-button").Single(_ => _.TextContent.Trim() == "Resolve").ClickAsync(new());

        await Assert.That(DocumentReview.Read(reported!).Comments.Single().Resolved).IsTrue();
        await Assert.That(cut.Find(".review-card").ClassList).Contains("review-resolved");
        await Assert.That(cut.FindAll(".viewer-badge")).IsEmpty();
        await Assert.That(Painted().Comments).IsEmpty();

        // Nothing on the page moved, so the page is not laid out again.
        await Assert.That(controller.Invocations["reload"]).IsEmpty();

        await cut.FindAll(".review-button").Single(_ => _.TextContent.Trim() == "Reopen").ClickAsync(new());

        await Assert.That(DocumentReview.Read(reported!).Comments.Single().Resolved).IsFalse();
        await Assert.That(Painted().Comments.SequenceEqual([0, 0, 38])).IsTrue();
    }

    [Test]
    public async Task Edit_RewordsAComment()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Commented, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.Find("button[aria-label='Edit comment']").ClickAsync(new());
        await Assert.That(cut.Find(".review-input").GetAttribute("value")).IsEqualTo("Looks good to me.");

        await cut.Find(".review-input").InputAsync(new() { Value = "Looks fine." });
        await cut.FindAll(".review-button").Single(_ => _.TextContent.Trim() == "Save").ClickAsync(new());

        var comment = DocumentReview.Read(reported!).Comments.Single();
        await Assert.That(comment.Text).IsEqualTo("Looks fine.");
        await Assert.That(comment.Author).IsEqualTo("Reviewer");
        await Assert.That(cut.Find(".review-comment-text").TextContent).IsEqualTo("Looks fine.");
        await Assert.That(controller.Invocations["reload"]).IsEmpty();
    }

    [Test]
    public async Task Delete_RemovesTheComment()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Commented, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));

        await cut.Find("button[aria-label='Delete comment']").ClickAsync(new());

        await Assert.That(DocumentReview.Read(reported!).Comments).IsEmpty();
        await Assert.That(Text(reported!)).IsEqualTo("Some text with a review note attached.");
        await Assert.That(cut.FindAll(".review-card")).IsEmpty();
        await Assert.That(Painted().Comments).IsEmpty();
    }

    // Permissions

    [Test]
    public async Task ReadOnly_ListsEverything_AndOffersNoEdit()
    {
        var bytes = ReviewEditor.AddComment(Tracked, new(0, 0), new(0, 5), "Ann", "Note", new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
        var cut = await OpenPane(bytes, _ => _.Add(component => component.ReadOnly, true));

        await Assert.That(cut.FindAll(".review-card").Count).IsEqualTo(3);
        await Assert.That(cut.FindAll(".review-action")).IsEmpty();
        await Assert.That(cut.FindAll(".review-button")).IsEmpty();
        await Assert.That(cut.FindAll(".review-tool")).IsEmpty();
        await Assert.That(cut.FindAll("button[aria-label=Undo]")).IsEmpty();
        await Assert.That(cut.Find(".review-note").TextContent).IsEqualTo("This document is read-only.");
    }

    // A document restricted to comments lets a reviewer comment, and leaves its changes to its owner.
    [Test]
    public async Task ADocumentsProtection_IsHonoured()
    {
        var cut = await OpenPane(Protect(Tracked, DocumentFormat.OpenXml.Wordprocessing.DocumentProtectionValues.Comments));

        await Assert.That(cut.FindAll(".review-action-primary").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".review-action").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".review-change .review-button")).IsEmpty();
    }

    // The host

    // A host that keeps the file hands the edited bytes back as Source; that is not a new file to open.
    [Test]
    public async Task TheEditedFile_HandedBackAsSource_IsNotReopened()
    {
        byte[]? reported = null;
        var cut = await OpenPane(Tracked, _ => _.Add(component => component.OnDocumentChanged, bytes => reported = bytes));
        await cut.FindAll(".review-action").Single(_ => _.TextContent.Trim() == "Accept all").ClickAsync(new());

        cut.Render(_ => _
            .Add(component => component.Source, reported)
            .Add(component => component.FileName, "review.docx"));

        await Assert.That(controller.Invocations["load"].Count).IsEqualTo(1);
        await Assert.That(controller.Invocations["unload"]).IsEmpty();
        await Assert.That(cut.FindAll(".error-message")).IsEmpty();
        await Assert.That(cut.FindAll(".review-card")).IsEmpty();
    }

    // The document's text as it reads with its deletions gone, which is where they are headed.
    static string Text(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        return string.Concat(
            package.MainDocumentPart!.Document!
                .Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>()
                .Where(_ => !_.Ancestors<DocumentFormat.OpenXml.Wordprocessing.DeletedRun>().Any())
                .Select(SourceRuns.Text));
    }

    static byte[] Protect(byte[] docx, DocumentFormat.OpenXml.Wordprocessing.DocumentProtectionValues edit)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, true))
        {
            var part = package.MainDocumentPart!.DocumentSettingsPart ?? package.MainDocumentPart.AddNewPart<DocumentFormat.OpenXml.Packaging.DocumentSettingsPart>();
            part.Settings = new(
                new DocumentFormat.OpenXml.Wordprocessing.DocumentProtection
                {
                    Edit = edit,
                    Enforcement = true
                });
        }

        return stream.ToArray();
    }
}
