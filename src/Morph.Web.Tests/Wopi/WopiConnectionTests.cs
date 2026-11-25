using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions.Enums;

// MorphWopi with a connection of the host's own (IWopiConnection) in place of Morph.Wopi's launch and proxy.
// The editor and the viewer open nothing and draw nothing, so these pin what MorphWopi asks of the host, and
// what it shows and hands the editor of the answers.
public class WopiConnectionTests : BunitTestContext
{
    // MorphWopi holds the editor it shows by reference, so a stand-in has to be one.
    sealed class QuietEditor : MorphEditor
    {
        protected override Task OnInitializedAsync() =>
            Task.CompletedTask;

        protected override Task OnParametersSetAsync() =>
            Task.CompletedTask;

        protected override Task OnAfterRenderAsync(bool firstRender) =>
            Task.CompletedTask;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
        }
    }

    sealed class QuietViewer : MorphViewer
    {
        protected override Task OnInitializedAsync() =>
            Task.CompletedTask;

        protected override Task OnParametersSetAsync() =>
            Task.CompletedTask;

        protected override Task OnAfterRenderAsync(bool firstRender) =>
            Task.CompletedTask;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
        }
    }

    readonly BunitJSModuleInterop module;

    // The clock the lock is kept by, moved on by hand.
    readonly FakeTimeProvider clock = new();

    public WopiConnectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        module = JSInterop.SetupModule($"./{MorphAssets.Script}");
        module.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<TimeProvider>(clock);
        ComponentFactories.Add<MorphEditor, QuietEditor>();
        ComponentFactories.Add<MorphViewer, QuietViewer>();
    }

    // A host that answers from what the test gives it, and keeps what it was asked.
    sealed class Host : IWopiConnection
    {
        public List<string> Asked { get; } = [];
        public List<string> Locks { get; } = [];
        public Queue<WopiReply> LockReplies { get; } = new();
        public Queue<WopiReply> RefreshReplies { get; } = new();
        public Queue<WopiReply> PutReplies { get; } = new();
        public byte[]? Saved { get; private set; }
        public Exception? Failure { get; init; }
        public int Leaves { get; private set; }

        public WopiFileInfo Info { get; init; } = new(
            "Report.docx",
            "Ann",
            UserCanWrite: true,
            ReadOnly: false,
            SupportsLocks: true,
            SupportsUpdate: true,
            DisablePrint: false,
            DisableExport: false,
            CloseUrl: "https://host/briefs",
            BrandName: "Host",
            BrandUrl: null,
            FolderName: "Briefs",
            FolderUrl: null);

        public Task<WopiFileInfo> CheckFileInfoAsync(Cancel cancel = default)
        {
            Asked.Add("CheckFileInfo");
            if (Failure is { } failure)
            {
                throw failure;
            }

            return Task.FromResult(Info);
        }

        public Task<byte[]> GetFileAsync(Cancel cancel = default)
        {
            Asked.Add("GetFile");
            return Task.FromResult("the file"u8.ToArray());
        }

        public Task<WopiReply> LockAsync(string id, Cancel cancel = default)
        {
            Asked.Add("Lock");
            Locks.Add(id);
            return Task.FromResult(Next(LockReplies));
        }

        public Task<WopiReply> RefreshLockAsync(string id, Cancel cancel = default)
        {
            Asked.Add("RefreshLock");
            return Task.FromResult(Next(RefreshReplies));
        }

        public Task<WopiReply> UnlockAsync(string id, Cancel cancel = default)
        {
            Asked.Add("Unlock");
            Locks.Add(id);
            return Task.FromResult(new WopiReply(200, null, null));
        }

        public Task<WopiReply> PutFileAsync(byte[] bytes, string id, Cancel cancel = default)
        {
            Asked.Add("PutFile");
            var reply = Next(PutReplies);
            if (reply.Succeeded)
            {
                Saved = bytes;
            }

            return Task.FromResult(reply);
        }

        public Task<WopiLeave?> LeaveAsync(string id, Cancel cancel = default)
        {
            Leaves++;
            return Task.FromResult<WopiLeave?>(
                new($"https://host/unlock/{id}")
                {
                    Headers = new Dictionary<string, string>
                    {
                        ["X-RequestDigest"] = $"digest {Leaves}"
                    }
                });
        }

        static WopiReply Next(Queue<WopiReply> replies)
        {
            if (replies.TryDequeue(out var reply))
            {
                return reply;
            }

            return new(200, null, null);
        }
    }

    async Task<IRenderedComponent<MorphWopi>> OpenAsync(Host host, string action = "edit")
    {
        var cut = Render<MorphWopi>(_ => _
            .Add(_ => _.Connection, host)
            .Add(_ => _.Action, action));
        await cut.WaitForStateAsync(() => cut.FindAll(".spinner").Count == 0);
        return cut;
    }

    static MorphEditor Editor(IRenderedComponent<MorphWopi> cut) =>
        cut.FindComponent<QuietEditor>().Instance;

    // The connection's file opens to edit under its lock, with no launch read from the page, and the request
    // that lets the lock go as the page goes is the connection's, headers and all.
    [Test]
    public async Task AFileTheHostLocks_OpensToEdit()
    {
        var host = new Host();

        var cut = await OpenAsync(host);

        await Assert.That(host.Asked).IsEquivalentTo(["CheckFileInfo", "GetFile", "Lock"], CollectionOrdering.Matching);
        var editor = Editor(cut);
        await Assert.That(editor.Source).IsEquivalentTo("the file"u8.ToArray());
        await Assert.That(editor.ReadOnly).IsFalse();
        await Assert.That(editor.OnSave.HasDelegate).IsTrue();
        await Assert.That(cut.Find(".wopi-file").TextContent).IsEqualTo("Report.docx");
        await Assert.That(cut.Find(".wopi-close")).IsNotNull();
        await Assert.That(JSInterop.Invocations.Concat(module.Invocations).Any(_ => _.Identifier == "wopiLaunch")).IsFalse();

        var leave = module.Invocations["unlockOnLeave"].Single();
        await Assert.That(leave.Arguments[0]).IsEqualTo($"https://host/unlock/{host.Locks.Single()}");
        var headers = (IReadOnlyDictionary<string, string>) leave.Arguments[2]!;
        await Assert.That(headers["X-RequestDigest"]).IsEqualTo("digest 1");
    }

    // A file someone else has opens read only, and says who.
    [Test]
    public async Task AFileLockedElsewhere_OpensReadOnly_WithTheReason()
    {
        var host = new Host();
        host.LockReplies.Enqueue(new(409, "", "Checked out to Bob"));

        var cut = await OpenAsync(host);

        var editor = Editor(cut);
        await Assert.That(editor.ReadOnly).IsTrue();
        await Assert.That(editor.OnSave.HasDelegate).IsFalse();
        await Assert.That(cut.Find(".wopi-status").TextContent).IsEqualTo("Read-only: Checked out to Bob");
        await Assert.That(module.Invocations["unlockOnLeave"]).IsEmpty();
    }

    // A file the user may not save is not locked at all.
    [Test]
    public async Task AFileTheUserMayNotSave_IsNotLocked()
    {
        var host = new Host
        {
            Info = new Host().Info with
            {
                UserCanWrite = false
            }
        };

        var cut = await OpenAsync(host);

        await Assert.That(host.Asked).IsEquivalentTo(["CheckFileInfo", "GetFile"], CollectionOrdering.Matching);
        await Assert.That(Editor(cut).ReadOnly).IsTrue();
        await Assert.That(cut.Find(".wopi-status").TextContent).IsEqualTo("Read-only");
    }

    // Save puts the file back under the lock; a lock that lapsed is taken again and the file put once more.
    [Test]
    public async Task Save_TakesALapsedLockAgain()
    {
        var host = new Host();
        host.PutReplies.Enqueue(new(409, null, null));
        var cut = await OpenAsync(host);
        var save = Editor(cut).OnSave;

        await cut.InvokeAsync(() => save.InvokeAsync("edited"u8.ToArray()));

        await Assert.That(host.Asked.Skip(3)).IsEquivalentTo(["PutFile", "Lock", "PutFile"], CollectionOrdering.Matching);
        await Assert.That(host.Saved).IsEquivalentTo("edited"u8.ToArray());
        await Assert.That(host.Locks.Distinct().Count()).IsEqualTo(1);
        await Assert.That(cut.Find(".wopi-status").TextContent).IsEqualTo("Saved");
    }

    // A save the host refuses says why, to the editor, which shows it.
    [Test]
    public async Task ASaveTheHostRefuses_SaysWhy()
    {
        var host = new Host();
        host.PutReplies.Enqueue(new(413, null, null));
        var cut = await OpenAsync(host);
        var save = Editor(cut).OnSave;

        var thrown = await Assert.That(async () => await cut.InvokeAsync(() => save.InvokeAsync("edited"u8.ToArray())))
            .Throws<InvalidOperationException>();

        await Assert.That(thrown!.Message).IsEqualTo("The file is too large for the host to save.");
        await Assert.That(cut.Find(".wopi-status").TextContent).IsEqualTo("Not saved");
    }

    // A file opened to view is shown, and not locked.
    [Test]
    public async Task AFileToView_IsNotLocked()
    {
        var host = new Host();

        var cut = await OpenAsync(host, "view");

        await Assert.That(host.Asked).IsEquivalentTo(["CheckFileInfo", "GetFile"], CollectionOrdering.Matching);
        await Assert.That(cut.FindComponents<QuietViewer>()).IsNotEmpty();
        await Assert.That(cut.FindComponents<QuietEditor>()).IsEmpty();
    }

    // A host that cannot answer says so in place of the file.
    [Test]
    public async Task AHostThatFails_SaysWhy()
    {
        var host = new Host
        {
            Failure = new HttpRequestException("The file is not there any more.")
        };

        var cut = await OpenAsync(host);

        await Assert.That(cut.Find(".wopi-message[role=alert]").TextContent).IsEqualTo("The file is not there any more.");
    }

    // The editor going lets the lock go, and drops the request the page would have sent.
    [Test]
    public async Task Disposing_Unlocks()
    {
        var host = new Host();
        await OpenAsync(host);

        await DisposeComponentsAsync();

        await Assert.That(host.Asked[^1]).IsEqualTo("Unlock");
        await Assert.That(host.Locks.Distinct().Count()).IsEqualTo(1);
        await Assert.That(module.Invocations["forgetLeave"]).IsNotEmpty();
    }

    // Kept every ten minutes, a lock that lapsed is taken again, and the leave request asked for afresh, so
    // what it carries that expires is current.
    [Test]
    public async Task ALapsedLock_IsTakenAgain_AndItsLeaveRenewed()
    {
        var host = new Host();
        host.RefreshReplies.Enqueue(new(409, null, null));
        await OpenAsync(host);

        clock.Advance(TimeSpan.FromMinutes(10));
        await UntilAsync(() => module.Invocations["unlockOnLeave"].Count == 2);

        await Assert.That(host.Asked.Skip(3)).IsEquivalentTo(["RefreshLock", "Lock"], CollectionOrdering.Matching);
        await Assert.That(host.Locks.Distinct().Count()).IsEqualTo(1);
        var headers = (IReadOnlyDictionary<string, string>) module.Invocations["unlockOnLeave"][^1].Arguments[2]!;
        await Assert.That(headers["X-RequestDigest"]).IsEqualTo("digest 2");
    }

    // A lock someone else holds now is not the page's to let go as it goes, and the bar says it is lost.
    [Test]
    public async Task ALockTakenElsewhere_IsNotLetGoAsThePageGoes()
    {
        var host = new Host();
        host.RefreshReplies.Enqueue(new(409, "theirs", "Checked out to Bob"));
        var cut = await OpenAsync(host);

        clock.Advance(TimeSpan.FromMinutes(10));
        await cut.WaitForStateAsync(() => cut.Find(".wopi-status").TextContent.Length > 0);

        await Assert.That(cut.Find(".wopi-status").TextContent).IsEqualTo("The file's lock is lost: Checked out to Bob");
        await Assert.That(module.Invocations["forgetLeave"]).IsNotEmpty();
        await Assert.That(host.Asked).DoesNotContain("Unlock");
    }

    // What the lock's refresh does happens on the clock's tick, with nothing rendered when it succeeds.
    static async Task UntilAsync(Func<bool> condition)
    {
        for (var tries = 0; tries < 200 && !condition(); tries++)
        {
            await Task.Delay(25);
        }

        if (!condition())
        {
            throw new TimeoutException("The refresh never happened.");
        }
    }
}
