using Microsoft.Extensions.Time.Testing;
using static WopiHostTests;

// A page's side of coauthoring (Coauthor) over a real session, with an editor that stands in for MorphViewer:
// each edit committed as it is made, others' versions shown when the editor is ready for them, an edit
// refused where someone else has the paragraph open, and edits made while a commit was in flight kept.
public class CoauthorTests
{
    // An editor: its document, what it was told, and whether it is ready for a version from the session.
    sealed class Editor : ICoauthorEditor
    {
        public byte[]? Current { get; set; }

        public bool Ready { get; set; } = true;

        public List<string> Notes { get; } = [];

        public List<byte[]> Accepted { get; } = [];

        public CoauthorState? State { get; private set; }

        // An edit made just as a version is shown: the editor's document changes, and the version is not shown.
        public Func<byte[], byte[]>? EditFirst { get; set; }

        public Task<bool> AdoptAsync(byte[] expected, byte[] next)
        {
            if (EditFirst is { } edit)
            {
                EditFirst = null;
                Current = edit(Current!);
                return Task.FromResult(false);
            }

            if (!ReferenceEquals(Current, expected))
            {
                return Task.FromResult(false);
            }

            Current = next;
            return Task.FromResult(true);
        }

        public Task AcceptedAsync(byte[] document)
        {
            Accepted.Add(document);
            return Task.CompletedTask;
        }

        public Task<bool> ReadyAsync() =>
            Task.FromResult(Ready);

        public void Note(string message) =>
            Notes.Add(message);

        public Task PresenceAsync(CoauthorState state, string self)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    sealed class Page(Coauthor coauthor, Editor editor) : IAsyncDisposable
    {
        public Coauthor Coauthor => coauthor;
        public Editor Editor => editor;

        // Edits the editor's document, and tells coauthoring, as MorphViewer does after every edit.
        public Task EditAsync(Func<byte[], byte[]> edit)
        {
            editor.Current = edit(editor.Current!);
            coauthor.Changed();
            return coauthor.SyncAsync();
        }

        public ValueTask DisposeAsync() =>
            coauthor.DisposeAsync();
    }

    static async Task<Page> JoinAsync(Server server, string user)
    {
        var (_, launch) = await PostAsync(server.Client(), await server.LaunchAsync(user: user));
        var coauthor = new Coauthor(new(server.Client(), launch!), TimeProvider.System);
        var editor = new Editor
        {
            Current = await coauthor.StartAsync()
        };
        coauthor.Attach(editor);
        return new(coauthor, editor);
    }

    static Task<Server> StartAsync() =>
        WopiHostTests.StartAsync(configure: services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow)));

    static List<EditParagraph> Editable(byte[] document) =>
        [.. DocumentOutline.Read(document).Paragraphs.Where(_ => _.Part == ReviewPart.Body && _.Units.Any(unit => unit.Editable))];

    // The body's n-th paragraph with text added to, found in the document edited.
    static Func<byte[], byte[]> Typing(int index, string added) =>
        document =>
        {
            var paragraph = Editable(document)[index < 0 ? Editable(document).Count + index : index];
            var first = paragraph.Units.First(_ => _.Editable);
            var content = new NewParagraph([.. paragraph.Units.Select((unit, at) => Item(unit, at, first, added))]);
            using var stamping = ParagraphIds.Stamping();
            return DocumentEditor.Rewrite(document, paragraph.Ordinal, [content], new("Ann", DateTimeOffset.UtcNow, false)).Document;
        };

    static NewItem Item(EditUnit unit, int index, EditUnit first, string added)
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

    static string Text(byte[] document) =>
        string.Join('\n', DocumentOutline.Read(document).Paragraphs.Select(_ => string.Concat(_.Units.Select(unit => unit.Text))));

    static async Task Eventually(Func<bool> condition)
    {
        for (var tries = 0; tries < 200 && !condition(); tries++)
        {
            await Task.Delay(50);
        }
    }

    [Test]
    public async Task AnEdit_ReachesTheOtherPage()
    {
        await using var server = await StartAsync();
        await using var ann = await JoinAsync(server, "ann");
        await using var bob = await JoinAsync(server, "bob");

        await ann.EditAsync(Typing(0, " — Ann"));
        await Eventually(() => Text(bob.Editor.Current!).Contains(" — Ann"));

        await Assert.That(Text(bob.Editor.Current!)).Contains(" — Ann");
        await Assert.That(ann.Editor.Accepted.Last()).IsSameReferenceAs(ann.Editor.Current);
        await Assert.That(bob.Coauthor.Version).IsEqualTo(ann.Coauthor.Version);
    }

    [Test]
    public async Task EditsMadeAtOnce_AreBothShownToBoth()
    {
        await using var server = await StartAsync();
        await using var ann = await JoinAsync(server, "ann");
        await using var bob = await JoinAsync(server, "bob");
        bob.Editor.Ready = false;

        await ann.EditAsync(Typing(0, " — Ann"));
        await bob.EditAsync(Typing(-1, " — Bob"));
        // The answer to Bob's commit waits until his editor is ready for it.
        bob.Editor.Ready = true;
        await bob.Coauthor.SyncAsync();
        await Eventually(() => Text(ann.Editor.Current!).Contains(" — Bob"));

        await Assert.That(Text(bob.Editor.Current!)).Contains(" — Ann");
        await Assert.That(Text(bob.Editor.Current!)).Contains(" — Bob");
        await Assert.That(Text(ann.Editor.Current!)).IsEqualTo(Text(bob.Editor.Current!));
    }

    // Bob is typing: Ann's edit waits until Bob's editor is ready for it.
    [Test]
    public async Task AVersion_WaitsUntilTheEditorIsReady()
    {
        await using var server = await StartAsync();
        await using var ann = await JoinAsync(server, "ann");
        await using var bob = await JoinAsync(server, "bob");
        bob.Editor.Ready = false;
        var before = bob.Editor.Current;

        await ann.EditAsync(Typing(0, " — Ann"));
        await Eventually(() => bob.Editor.State?.Version == ann.Coauthor.Version);
        await bob.Coauthor.SyncAsync();
        await Assert.That(bob.Editor.Current).IsSameReferenceAs(before);

        bob.Editor.Ready = true;
        await bob.Coauthor.SyncAsync();
        await Assert.That(Text(bob.Editor.Current!)).Contains(" — Ann");
    }

    [Test]
    public async Task AnEditToAParagraphSomeoneHasOpen_IsTakenBack()
    {
        await using var server = await StartAsync();
        await using var ann = await JoinAsync(server, "ann");
        await using var bob = await JoinAsync(server, "bob");
        var paragraph = Editable(bob.Editor.Current!)[0];
        var original = ann.Editor.Current;

        var entered = await bob.Coauthor.EnterAsync(paragraph.Id!);
        await ann.EditAsync(Typing(0, " — Ann"));

        await Assert.That(entered.Result).IsEqualTo("granted");
        await Assert.That(ann.Editor.Current).IsSameReferenceAs(original);
        await Assert.That(ann.Editor.Notes.Last()).IsEqualTo("Bob is editing that paragraph, so that edit was not made.");
    }

    // Asking for a paragraph changed since the editor's version shows the change first, then gives it.
    [Test]
    public async Task EnteringAParagraphChangedSince_ShowsTheChangeFirst()
    {
        await using var server = await StartAsync();
        await using var ann = await JoinAsync(server, "ann");
        await using var bob = await JoinAsync(server, "bob");
        bob.Editor.Ready = false;
        var paragraph = Editable(bob.Editor.Current!)[0];
        await ann.EditAsync(Typing(0, " — Ann"));
        bob.Editor.Ready = true;

        var entered = await bob.Coauthor.EnterAsync(paragraph.Id!);

        await Assert.That(entered.Result).IsEqualTo("granted");
        await Assert.That(Text(bob.Editor.Current!)).Contains(" — Ann");
    }

    // An edit made while the answer to the last commit was on its way is made to the merged version too.
    [Test]
    public async Task AnEditMadeWhileACommitWasAnswered_IsKept()
    {
        await using var server = await StartAsync();
        await using var ann = await JoinAsync(server, "ann");
        await using var bob = await JoinAsync(server, "bob");
        bob.Editor.Ready = false;
        await ann.EditAsync(Typing(0, " — Ann"));

        bob.Editor.EditFirst = Typing(1, " — Bob again");
        await bob.EditAsync(Typing(-1, " — Bob"));
        bob.Editor.Ready = true;
        await bob.Coauthor.SyncAsync();

        var text = Text(bob.Editor.Current!);
        await Assert.That(text).Contains(" — Ann");
        await Assert.That(text).Contains(" — Bob");
        await Assert.That(text).Contains(" — Bob again");
        await Eventually(() => Text(ann.Editor.Current!).Contains(" — Bob again"));
        await Assert.That(Text(ann.Editor.Current!)).IsEqualTo(text);
    }

    [Test]
    public async Task Save_CommitsThenSaves()
    {
        await using var server = await StartAsync();
        await using var ann = await JoinAsync(server, "ann");
        ann.Editor.Current = Typing(0, " — Ann")(ann.Editor.Current!);

        var problem = await ann.Coauthor.SaveAsync();

        await Assert.That(problem).IsNull();
        var stored = await File.ReadAllBytesAsync(Path.Combine(server.Storage.Root, "Report.docx"));
        await Assert.That(Text(stored)).Contains(" — Ann");
    }
}
