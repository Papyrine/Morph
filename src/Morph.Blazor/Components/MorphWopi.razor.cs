namespace Morph;

/// <summary>
/// Opens the file a WOPI host launched Morph for, and saves it back to the host. It is the root component
/// of the WebAssembly app that an ASP.NET Core application serves with Morph.Wopi: the host posts an access
/// token to the action URL, Morph.Wopi writes the file to open into the editor's page, and this reads it.
///
/// A Word document opened where Morph.Wopi coauthors (<see cref="Coauthor"/>) joins the file's session, and
/// is edited with everyone else in it: the session holds the file's lock and saves it, and the bar shows who
/// else is in. Elsewhere, a Word document opened to edit is locked while it is open, the lock refreshed as
/// WOPI asks, and Save puts it back over itself. A file the user may not save, or that someone else has
/// locked, opens read only, as do a workbook and a deck, and a file opened to view. The host's breadcrumb
/// and its Close link go over the editor. Everything goes through Morph.Wopi, which signs it for the host.
/// </summary>
public partial class MorphWopi : IAsyncDisposable
{
    /// <summary>
    /// Whether an unexpected failure offers a pre-filled GitHub issue link against the Morph repository.
    /// </summary>
    [Parameter]
    public bool ShowIssueLink { get; set; } = true;

    /// <summary>Extra CSS classes for the root element, alongside the <c>wopi</c> class.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Any other attribute, splatted onto the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [Inject]
    HttpClient Http { get; set; } = null!;

    [Inject]
    MorphInterop Interop { get; set; } = null!;

    // A lock lasts 30 minutes unless it is refreshed ([MS-WOPI] 3.3.5.1.3).
    static readonly TimeSpan refreshInterval = TimeSpan.FromMinutes(10);

    readonly CancelSource lifetime = new();
    WopiSession? session;
    Coauthor? coauthor;
    bool coauthorAttached;
    MorphEditor? editorComponent;
    MorphViewer? viewerComponent;
    WopiFileInfo? info;
    byte[]? bytes;
    bool editing;
    bool readOnly;
    string? lockId;
    string? problem;
    string? status;

    EventCallback<byte[]> SaveCallback
    {
        get
        {
            if (coauthor is {Joined.CanWrite: true})
            {
                return EventCallback.Factory.Create<byte[]>(this, SaveCoauthoredAsync);
            }

            if (lockId is null)
            {
                return default;
            }

            return EventCallback.Factory.Create<byte[]>(this, SaveAsync);
        }
    }

    // The others in the session, for the bar: everyone but this page.
    IEnumerable<CoauthorPresence> Others =>
        coauthor?.State?.Users.Where(_ => _.Connection != coauthor.Joined?.Connection) ?? [];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await OpenAsync();
            StateHasChanged();
            return;
        }

        // The editor or viewer showing the session's document edits through the session from here on.
        if (coauthor != null &&
            !coauthorAttached &&
            ((MorphViewer?) editorComponent ?? viewerComponent) is { } shown)
        {
            coauthorAttached = true;
            shown.Coauthor(coauthor);
        }
    }

    async Task OpenAsync()
    {
        var launch = WopiLaunchInfo.Parse(await Interop.WopiLaunchAsync());
        if (launch is null)
        {
            problem = "This page opens a file for the place it is kept. Open the file from there.";
            return;
        }

        var opened = new WopiSession(Http, launch);
        session = opened;
        try
        {
            var file = await opened.CheckFileInfoAsync(lifetime.Token);
            info = file;
            var word = ConversionService.Detect(file.BaseFileName)?.Format == InputFormat.Docx;
            editing = launch.Action == "edit" &&
                      word;
            if (word &&
                await JoinAsync(launch) is { } shared)
            {
                bytes = shared;
                return;
            }

            var content = await opened.GetFileAsync(lifetime.Token);
            if (editing)
            {
                await LockAsync(opened, file);
            }

            bytes = content;
        }
        catch (HttpRequestException exception)
        {
            problem = exception.Message;
        }
    }

    // The file's coauthoring session, joined: its document, or null where Morph.Wopi has no sessions and the
    // page edits the file on its own.
    async Task<byte[]?> JoinAsync(WopiLaunchInfo launch)
    {
        var joining = new Coauthor(new(Http, launch), TimeProvider.System);
        var document = await joining.StartAsync(lifetime.Token);
        if (document == null)
        {
            await joining.DisposeAsync();
            return null;
        }

        coauthor = joining;
        coauthor.StateChanged += OnSessionChanged;
        readOnly = !joining.Joined!.CanWrite;
        if (readOnly &&
            editing)
        {
            status = "Read-only";
        }

        // The page leaving leaves the session at once, rather than once its events have stopped a while.
        await Interop.UnlockOnLeaveAsync(joining.LeaveUrl, []);
        return document;
    }

    void OnSessionChanged()
    {
        if (coauthor?.State is { } state)
        {
            status = state.Problem ?? state.Saved;
        }

        _ = InvokeAsync(StateHasChanged);
    }

    // Save, coauthoring: the session saves the document as everyone in it has left it.
    async Task SaveCoauthoredAsync(byte[] _)
    {
        status = "Saving…";
        StateHasChanged();
        if (await coauthor!.SaveAsync() is { } problem)
        {
            status = "Not saved";
            throw new InvalidOperationException(problem);
        }

        status = "Saved";
    }

    // Takes the file's lock, without which it opens read only: the user may not save it, or someone else
    // has it open.
    async Task LockAsync(WopiSession opened, WopiFileInfo file)
    {
        if (!file.Editable)
        {
            readOnly = true;
            status = "Read-only";
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        var reply = await opened.LockAsync(id, lifetime.Token);
        if (!reply.Succeeded)
        {
            readOnly = true;
            status = $"Read-only: {Problem(reply)}";
            return;
        }

        lockId = id;
        await Interop.UnlockOnLeaveAsync(opened.Url("Unlock"), opened.Fields(id));
        _ = RefreshAsync(opened, id, lifetime.Token);
    }

    async Task RefreshAsync(WopiSession opened, string id, Cancel cancel)
    {
        using var timer = new PeriodicTimer(refreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancel))
            {
                var reply = await opened.RefreshLockAsync(id, cancel);
                // The lock lapsed, as while the computer slept: take it again.
                if (reply.Unlocked)
                {
                    reply = await opened.LockAsync(id, cancel);
                }

                if (!reply.Succeeded)
                {
                    status = $"The file's lock is lost: {Problem(reply)}";
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException)
        {
            status = "The host of the file could not be reached.";
            await InvokeAsync(StateHasChanged);
        }
    }

    // Puts the file back over itself, under the lock; the editor shows what is thrown.
    async Task SaveAsync(byte[] file)
    {
        if (session is not { } opened ||
            lockId is not { } id)
        {
            return;
        }

        status = "Saving…";
        StateHasChanged();
        WopiReply reply;
        try
        {
            reply = await opened.PutFileAsync(file, id, lifetime.Token);
            if (reply.Unlocked &&
                (await opened.LockAsync(id, lifetime.Token)).Succeeded)
            {
                reply = await opened.PutFileAsync(file, id, lifetime.Token);
            }
        }
        catch (HttpRequestException)
        {
            status = "Not saved";
            throw new InvalidOperationException(WopiSession.Problem(503));
        }

        if (!reply.Succeeded)
        {
            status = "Not saved";
            throw new InvalidOperationException(Problem(reply));
        }

        status = "Saved";
    }

    void OnChanged(byte[] _) =>
        status = null;

    static string Problem(WopiReply reply)
    {
        if (reply is
            {
                Status: 409,
                Reason: {Length: > 0} reason
            })
        {
            return reason;
        }

        return WopiSession.Problem(reply.Status);
    }

    // The editor's page goes, and the beacon unlocks the file; a prompt for unsaved edits may stop it.
    Task CloseAsync()
    {
        if (info?.CloseUrl is { } url)
        {
            return Interop.NavigateTopAsync(url);
        }

        return Task.CompletedTask;
    }

    /// <summary>Leaves the session, or stops refreshing the lock and unlocks the file.</summary>
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        if (coauthor is { } joined)
        {
            coauthor = null;
            joined.StateChanged -= OnSessionChanged;
            try
            {
                await Interop.ForgetLeaveAsync();
            }
            catch (Exception exception)
                when (exception is
                          JSException or
                          JSDisconnectedException)
            {
            }

            await joined.DisposeAsync();
        }

        if (session is { } opened &&
            lockId is { } id)
        {
            lockId = null;
            try
            {
                await Interop.ForgetLeaveAsync();
                await opened.UnlockAsync(id);
            }
            catch (Exception exception)
                when (exception is
                          HttpRequestException or
                          JSException or
                          JSDisconnectedException)
            {
            }
        }

        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
