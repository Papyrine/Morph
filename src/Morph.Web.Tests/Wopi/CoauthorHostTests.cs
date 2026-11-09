using Microsoft.Extensions.Time.Testing;
using OfficeHost;
using static WopiHostTests;

// Coauthoring against a real WOPI host: OfficeHost and Morph.Wopi in one test server, as WopiHostTests has
// them, and pages in a session driven through CoauthorClient as MorphWopi drives it. The session holds the
// file's one lock, merges the pages' edits, gives each the paragraph it opens, and saves the file. The clock
// is a fake one, moved on by hand for autosave and leaving.
public class CoauthorHostTests
{
    sealed class Page(CoauthorClient client, CoauthorJoined joined, long version, byte[] document)
    {
        public CoauthorClient Client => client;
        public CoauthorJoined Joined => joined;
        public long Version { get; set; } = version;
        public byte[] Document { get; set; } = document;
    }

    static FakeTimeProvider Clock() =>
        new(DateTimeOffset.UtcNow);

    static Task<Server> StartAsync(FakeTimeProvider clock, List<FileSavedContext>? saves = null, int autosave = 10) =>
        WopiHostTests.StartAsync(
            configure: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<MorphWopiOptions>(_ => _.AutosaveDelay = TimeSpan.FromSeconds(autosave));
                services.Configure<OfficeHostOptions>(
                    _ => _.OnFileSaved = context =>
                    {
                        lock (saves ?? [])
                        {
                            saves?.Add(context);
                        }

                        return Task.CompletedTask;
                    });
            });

    static async Task<Page> JoinAsync(Server server, string user, string action = "edit")
    {
        var (_, launch) = await PostAsync(server.Client(), await server.LaunchAsync(action, user: user));
        var client = new CoauthorClient(server.Client(), launch!);
        var joined = await client.JoinAsync();
        var (version, document) = await client.DocumentAsync();
        return new(client, joined!, version, document);
    }

    // The body's paragraphs with text, by ordinal.
    static List<EditParagraph> Editable(byte[] document) =>
        [.. DocumentOutline.Read(document).Paragraphs.Where(_ => _.Part == ReviewPart.Body && _.Units.Any(unit => unit.Editable))];

    // The paragraph's first stretch of text added to, as the editor does it when coauthoring.
    static byte[] Typed(byte[] document, EditParagraph paragraph, string added)
    {
        var first = paragraph.Units.First(_ => _.Editable);
        var content = new NewParagraph([.. paragraph.Units.Select(Item)]);
        using var stamping = ParagraphIds.Stamping();
        return DocumentEditor.Rewrite(document, paragraph.Ordinal, [content], new("Ann", DateTimeOffset.UtcNow, false)).Document;

        NewItem Item(EditUnit unit, int index)
        {
            if (!unit.Editable)
            {
                return new(index, null);
            }

            if (ReferenceEquals(unit, first))
            {
                return new(index, unit.Text + added);
            }

            return new(index, unit.Text);
        }
    }

    static async Task<CoauthorCommit> CommitAsync(Page page, byte[] edited)
    {
        var reply = await page.Client.CommitAsync(page.Version, DocumentPatch.Diff(page.Document, edited).ToBytes());
        if (reply.Result == "accepted")
        {
            page.Version = reply.Version;
            page.Document = edited;
        }
        else if (reply.Result == "rebased")
        {
            page.Version = reply.Version;
            page.Document = DocumentPatch.FromBytes(Convert.FromBase64String(reply.Patch!)).Apply(edited);
        }

        return reply;
    }

    static string Text(byte[] document) =>
        string.Join('\n', DocumentOutline.Read(document).Paragraphs.Select(_ => string.Concat(_.Units.Select(unit => unit.Text))));

    // Waits for what a timer set off on the session's own time.
    static Task Eventually(Func<bool> condition) =>
        Eventually(() => Task.FromResult(condition()));

    static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var tries = 0; tries < 100 && !await condition(); tries++)
        {
            await Task.Delay(50);
        }
    }

    [Test]
    public async Task TwoPages_CommittingToOneVersion_AreBothKept()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann");
        var bob = await JoinAsync(server, "bob");
        var paragraphs = Editable(ann.Document);

        var first = await CommitAsync(ann, Typed(ann.Document, paragraphs[0], " — Ann"));
        var second = await CommitAsync(bob, Typed(bob.Document, paragraphs[^1], " — Bob"));

        await Assert.That(first.Result).IsEqualTo("accepted");
        await Assert.That(second.Result).IsEqualTo("rebased");
        await Assert.That(second.Version).IsEqualTo(first.Version + 1);
        await Assert.That(Text(bob.Document)).Contains(" — Ann");
        await Assert.That(Text(bob.Document)).Contains(" — Bob");
        var (_, latest) = await ann.Client.DocumentAsync();
        await Assert.That(Text(latest)).IsEqualTo(Text(bob.Document));
    }

    [Test]
    public async Task APageCatchesUp_ByTheChangesSinceItsVersion()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann");
        var bob = await JoinAsync(server, "bob");
        await CommitAsync(ann, Typed(ann.Document, Editable(ann.Document)[0], " — Ann"));

        var patch = await bob.Client.PatchAsync(bob.Version);

        var caughtUp = DocumentPatch.FromBytes(Convert.FromBase64String(patch.Patch!)).Apply(bob.Document);
        await Assert.That(Text(caughtUp)).IsEqualTo(Text(ann.Document));
    }

    [Test]
    public async Task AParagraphSomeoneHasOpen_IsTheirs()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann");
        var bob = await JoinAsync(server, "bob");
        var paragraph = Editable(ann.Document)[0];

        var entered = await bob.Client.EnterAsync(paragraph.Id!, bob.Version);
        var asked = await ann.Client.EnterAsync(paragraph.Id!, ann.Version);
        var commit = await CommitAsync(ann, Typed(ann.Document, paragraph, " — Ann"));

        await Assert.That(entered.Result).IsEqualTo("granted");
        await Assert.That(asked.Result).IsEqualTo("refused");
        await Assert.That(asked.By).IsEqualTo("Bob");
        await Assert.That(commit.Result).IsEqualTo("refused");
        await Assert.That(commit.By).IsEqualTo("Bob");
        await Assert.That(commit.Paragraph).IsEqualTo(paragraph.Id);

        await bob.Client.ExitAsync();
        await Assert.That((await ann.Client.EnterAsync(paragraph.Id!, ann.Version)).Result).IsEqualTo("granted");
    }

    // A page asking for a paragraph that changed after the version it has takes the changes first.
    [Test]
    public async Task AParagraphChangedSinceThePagesVersion_IsStale()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann");
        var bob = await JoinAsync(server, "bob");
        var paragraph = Editable(ann.Document)[0];
        await CommitAsync(ann, Typed(ann.Document, paragraph, " — Ann"));

        var stale = await bob.Client.EnterAsync(paragraph.Id!, bob.Version);
        var current = await bob.Client.EnterAsync(paragraph.Id!, stale.Version);

        await Assert.That(stale.Result).IsEqualTo("stale");
        await Assert.That(current.Result).IsEqualTo("granted");
    }

    [Test]
    public async Task TheOtherPage_HearsOfTheNewVersion_AndWhereEveryoneIs()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann");
        var bob = await JoinAsync(server, "bob");
        using var cancel = new CancelSource(TimeSpan.FromSeconds(30));
        await using var events = bob.Client.EventsAsync(cancel.Token).GetAsyncEnumerator(cancel.Token);
        await events.MoveNextAsync();
        await Assert.That(events.Current.Users.Select(_ => _.Name)).IsEquivalentTo(["Ann", "Bob"]);

        var paragraph = Editable(ann.Document)[^1];
        await ann.Client.EnterAsync(paragraph.Id!, ann.Version, cancel.Token);
        await events.MoveNextAsync();
        await Assert.That(events.Current.Users.Single(_ => _.Name == "Ann").Paragraph).IsEqualTo(paragraph.Id);

        await CommitAsync(ann, Typed(ann.Document, Editable(ann.Document)[0], " — Ann"));
        await events.MoveNextAsync();
        await Assert.That(events.Current.Version).IsEqualTo(ann.Version);
        await cancel.CancelAsync();
    }

    [Test]
    public async Task TheDocument_IsSavedAWhileAfterAChange_WithWhoeverEditedIt()
    {
        var clock = Clock();
        var saves = new List<FileSavedContext>();
        await using var server = await StartAsync(clock, saves);
        var ann = await JoinAsync(server, "ann");
        var bob = await JoinAsync(server, "bob");
        var paragraphs = Editable(ann.Document);
        await CommitAsync(ann, Typed(ann.Document, paragraphs[0], " — Ann"));
        await CommitAsync(bob, Typed(bob.Document, paragraphs[^1], " — Bob"));
        await Assert.That(saves).IsEmpty();

        clock.Advance(TimeSpan.FromSeconds(10));
        await Eventually(() => saves.Count > 0);

        await Assert.That(saves.Count).IsEqualTo(1);
        await Assert.That(saves[0].Editors).IsEquivalentTo(["ann", "bob"]);
        await Assert.That(saves[0].IsAutosave).IsTrue();
        var stored = await File.ReadAllBytesAsync(Path.Combine(server.Storage.Root, "Report.docx"));
        await Assert.That(Text(stored)).IsEqualTo(Text(bob.Document));
    }

    [Test]
    public async Task Save_SavesAtOnce()
    {
        var clock = Clock();
        var saves = new List<FileSavedContext>();
        await using var server = await StartAsync(clock, saves);
        var ann = await JoinAsync(server, "ann");
        await CommitAsync(ann, Typed(ann.Document, Editable(ann.Document)[0], " — Ann"));

        var saved = await ann.Client.SaveAsync();

        await Assert.That(saved.Saved).IsTrue();
        await Assert.That(saves.Count).IsEqualTo(1);
        await Assert.That(saves[0].IsAutosave).IsTrue();
    }

    [Test]
    public async Task TheLastToLeave_SavesTheDocument_AndLetsTheLockGo()
    {
        var clock = Clock();
        var saves = new List<FileSavedContext>();
        // Autosave later than the session closes, so that the save is the exit's.
        await using var server = await StartAsync(clock, saves, autosave: 60);
        var ann = await JoinAsync(server, "ann");
        await CommitAsync(ann, Typed(ann.Document, Editable(ann.Document)[0], " — Ann"));

        await ann.Client.LeaveAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        await Eventually(() => saves.Count > 0);

        await Assert.That(saves.Count).IsEqualTo(1);
        await Assert.That(saves[0].IsExitSave).IsTrue();
        var (_, launch) = await PostAsync(server.Client(), await server.LaunchAsync());
        var session = new WopiSession(server.Client(), launch!);
        var locked = false;
        await Eventually(async () => locked = (await session.LockAsync("after")).Succeeded);
        await Assert.That(locked).IsTrue();
    }

    [Test]
    public async Task AViewer_JoinsReadOnly()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann", "view");

        var commit = await CommitAsync(ann, Typed(ann.Document, Editable(ann.Document)[0], " — Ann"));

        await Assert.That(ann.Joined.CanWrite).IsFalse();
        await Assert.That(commit.Result).IsEqualTo("refused");
    }

    // Another client holds the file's lock: the session cannot take it, and nobody's edits are taken.
    [Test]
    public async Task AFileLockedElsewhere_IsReadOnly()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var (_, launch) = await PostAsync(server.Client(), await server.LaunchAsync());
        await new WopiSession(server.Client(), launch!).LockAsync("elsewhere");
        var ann = await JoinAsync(server, "ann");

        var commit = await CommitAsync(ann, Typed(ann.Document, Editable(ann.Document)[0], " — Ann"));

        await Assert.That(commit.Result).IsEqualTo("refused");
    }

    // Another client held the lock as the session opened the file, and saved the file before it let the lock
    // go. The next page to join takes the lock, and the session takes the file as it now is: what it read
    // before is not saved over the other client's save.
    [Test]
    public async Task AFileSavedElsewhere_BeforeTheSessionCouldLockIt_IsTakenIn()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var (_, launch) = await PostAsync(server.Client(), await server.LaunchAsync(user: "eve"));
        var elsewhere = new WopiSession(server.Client(), launch!);
        await elsewhere.LockAsync("elsewhere");
        var ann = await JoinAsync(server, "ann");
        await elsewhere.PutFileAsync(Typed(ann.Document, Editable(ann.Document)[0], " — Elsewhere"), "elsewhere");
        await elsewhere.UnlockAsync("elsewhere");

        var bob = await JoinAsync(server, "bob");
        await CommitAsync(bob, Typed(bob.Document, Editable(bob.Document)[^1], " — Bob"));
        var saved = await bob.Client.SaveAsync();

        await Assert.That(Text(bob.Document)).Contains(" — Elsewhere");
        await Assert.That(saved.Saved).IsTrue();
        var stored = Text(await File.ReadAllBytesAsync(Path.Combine(server.Storage.Root, "Report.docx")));
        await Assert.That(stored).Contains(" — Elsewhere");
        await Assert.That(stored).Contains(" — Bob");
    }

    // The host's storage is written to while the session holds the lock. The save carries the time of the
    // file the session has, the host refuses it (1010), and the session merges the change with its edits
    // before it saves again.
    [Test]
    public async Task AFileChangedInStorage_IsMergedWithTheEdits_BeforeTheSave()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann");
        var paragraphs = Editable(ann.Document);
        var path = Path.Combine(server.Storage.Root, "Report.docx");
        await File.WriteAllBytesAsync(path, Typed(ann.Document, paragraphs[^1], " — Storage"));
        await CommitAsync(ann, Typed(ann.Document, paragraphs[0], " — Ann"));

        var saved = await ann.Client.SaveAsync();

        await Assert.That(saved.Saved).IsTrue();
        var stored = Text(await File.ReadAllBytesAsync(path));
        await Assert.That(stored).Contains(" — Storage");
        await Assert.That(stored).Contains(" — Ann");
        var (_, latest) = await ann.Client.DocumentAsync();
        await Assert.That(Text(latest)).IsEqualTo(stored);
    }

    // A change in storage to the paragraph the session's edits changed too cannot be merged: the edits are
    // not saved over it.
    [Test]
    public async Task AnEditThatCannotBeMerged_WithAChangeInStorage_IsNotSavedOverIt()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann");
        var paragraph = Editable(ann.Document)[0];
        var path = Path.Combine(server.Storage.Root, "Report.docx");
        await File.WriteAllBytesAsync(path, Typed(ann.Document, paragraph, " — Storage"));
        await CommitAsync(ann, Typed(ann.Document, paragraph, " — Ann"));

        var saved = await ann.Client.SaveAsync();

        await Assert.That(saved.Saved).IsFalse();
        await Assert.That(saved.Problem).Contains("changed elsewhere");
        var stored = Text(await File.ReadAllBytesAsync(path));
        await Assert.That(stored).Contains(" — Storage");
        await Assert.That(stored).DoesNotContain(" — Ann");
    }

    // The session's lock lapsed, and another client saved the file meanwhile. The session's save finds the
    // file unlocked, takes the lock again, and merges what changed before it saves.
    [Test]
    public async Task ALapsedLock_IsTakenAgain_AndWhatChangedMeanwhileIsMerged()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var ann = await JoinAsync(server, "ann");
        var paragraphs = Editable(ann.Document);
        var locks = server.App.Services.GetRequiredService<IOfficeLockStore>();
        var held = await locks.Get(server.Id);
        await locks.CompareExchange(server.Id, held!.Id, null);
        var (_, launch) = await PostAsync(server.Client(), await server.LaunchAsync(user: "eve"));
        var elsewhere = new WopiSession(server.Client(), launch!);
        await elsewhere.LockAsync("elsewhere");
        await elsewhere.PutFileAsync(Typed(ann.Document, paragraphs[^1], " — Elsewhere"), "elsewhere");
        await elsewhere.UnlockAsync("elsewhere");
        await CommitAsync(ann, Typed(ann.Document, paragraphs[0], " — Ann"));

        var saved = await ann.Client.SaveAsync();

        await Assert.That(saved.Saved).IsTrue();
        var stored = Text(await File.ReadAllBytesAsync(Path.Combine(server.Storage.Root, "Report.docx")));
        await Assert.That(stored).Contains(" — Elsewhere");
        await Assert.That(stored).Contains(" — Ann");
    }

    [Test]
    public async Task ATokenTheHostRefuses_JoinsNothing()
    {
        var clock = Clock();
        await using var server = await StartAsync(clock);
        var (_, launch) = await PostAsync(server.Client(), await server.LaunchAsync());
        var client = new CoauthorClient(server.Client(), launch! with {Token = "not a token"});

        await Assert.That(() => client.JoinAsync()).Throws<HttpRequestException>();
    }
}
