# Morph.Wopi

Morph's editor as a [WOPI](https://learn.microsoft.com/en-us/microsoft-365/cloud-storage-partner-program/online/)
client, for ASP.NET Core (.NET 10). A WOPI host — one built on [OfficeHost](https://www.nuget.org/packages/OfficeHost/),
or any other that opens files in a client named by its discovery — opens, edits and saves its Word documents
in Morph's editor, and shows its workbooks and decks in Morph's viewer, as it would in Office for the web or
Collabora Online.


## How it works

1. The host reads Morph's discovery, `/hosting/discovery`: the actions Morph offers (a `.docx` to view or
   edit, an `.xlsx` or `.pptx` to view), at which URL, and the key Morph signs its requests with.
2. To open a file, the host page POSTs an access token to the action URL, `/morph/wopi/edit` or
   `/morph/wopi/view` with the file's `WOPISrc`, in a frame. Morph answers with the editor's page, the
   file to open written into it.
3. The editor, Morph.Blazor's `MorphWopi` running in the browser, gets the file's info through
   `/morph/wopi/proxy/{operation}`: the request goes from the server to the host's WOPI endpoints, signed
   with the proof key ([MS-WOPI] 2.2.1).
4. A Word document joins the file's coauthoring session, `/morph/wopi/session`, which holds the file's lock
   and its content on the server and saves it (see [Several people at once](#several-people-at-once)).
   Without coauthoring, the editor gets the content, locks the file and saves it itself, through the proxy.

The editor is a Blazor WebAssembly app whose root component is `MorphWopi`, which the same application
serves:

```csharp
// the WebAssembly app
builder.RootComponents.Add<MorphWopi>("#app");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMorph();
```

```csharp
// the ASP.NET Core application that serves it
builder.Services.AddMorphWopi(
    options =>
    {
        options.BaseUrl = "https://morph.example.com";
        options.TrustedHosts.Add("https://files.example.com");
    });
var app = builder.Build();
app.MapStaticAssets();
app.MapMorphWopi();
```

Then point the host at `https://morph.example.com/hosting/discovery`. See
[the sample](/src/Morph.Wopi.Sample/README.md) for both sides running together.

The WebAssembly app is referenced by the application like any other project or package, and its files are
served by `MapStaticAssets` (or `UseStaticFiles`) like any other static web assets. An application with pages
of its own at the root serves the editor under a path instead: give the app's project a
`StaticWebAssetBasePath` of `morph`, its `index.html` a `<base href="/morph/">`, and `Page` the path the
page then has in the web root, `morph/index.html`. The app asks for Morph.Blazor's assets under its base,
`/morph/_content/Morph.Blazor/...`, where an application serves no library's; `MapMorphWopi` serves them
there, from the web root. An application serving such an app without `MapMorphWopi` does the same with
`app.MapMorphAssets("/morph")`.


## Options

| Option | Default | |
|---|---|---|
| `BaseUrl` | the request's | The URL the hosts reach Morph at, which the discovery's action URLs start with. |
| `TrustedHosts` | none | The hosts Morph opens files for, as origins: a `WOPISrc` on any other is refused, and only these may frame the editor. |
| `Page` | `index.html` | The page of the web root that runs the editor, which the action URLs answer with: `morph/index.html` for an app served under `/morph`, where Morph.Blazor's assets are then served too. |
| `ProofKey`, `OldProofKey` | a key made at startup | The RSA keys requests are signed with, published in the discovery. Set them when the application has several instances, or the hosts should not read the discovery again after a restart. |
| `MaxFileSize` | 25 MB | The largest file a save sends to the host. |
| `Coauthoring` | true | Whether several people edit a Word document at once, through a session on the server. False, each page locks and saves the file itself, and a second user opens it read only. |
| `AutosaveDelay` | 10 seconds | How long after the first change not yet saved a session saves the document to the host. |


## What the editor does with the file

A file the user may not save (`UserCanWrite`, `ReadOnly`, `SupportsUpdate`, `SupportsLocks`), or that
someone else has locked, opens read only with the reason; so do a workbook, a deck, and a file opened to
view. The host's breadcrumb and `CloseUrl` go in a bar over the editor, and `DisablePrint` and
`DisableExport` take Print and Download away.

Without coauthoring, a Word document opened to edit is locked while it is open, the lock refreshed every ten
minutes, and Save (Ctrl+S) puts it back over itself; a lock that lapsed is taken again before a save.
Leaving the page unlocks the file.


## Several people at once

With `Coauthoring` on (the default), every page that opens a Word document joins the file's session on the
server, which:

* opens the document from the host once, takes the file's one WOPI lock, refreshes it, and lets it go
  when the last page leaves;
* takes each page's edits as they are made (a patch against the version the page has) and merges an edit
  made to an older version with what came since, paragraph by paragraph: edits to different paragraphs are
  both kept, and an edit is refused whole where it changed a paragraph someone else changed too;
* gives a page the paragraph it opens, and refuses it to everyone else until they leave it: the others see
  a bar beside it with the name of who has it open, and cannot change it;
* sends every page the latest version and who is where, as Server-Sent Events: a page shows the others'
  edits when its reader leaves the paragraph they are typing in, or pauses there for two seconds;
* saves the document to the host `AutosaveDelay` after the first change not yet saved, when someone presses
  Save, and when the last page leaves, with `X-WOPI-Editors` naming everyone who edited it since the last
  save.

Undo takes back the reader's own edit, and leaves the others'. A page that loses the connection keeps its
edits and sends them once it is back. A file locked by another client (desktop Word, Collabora) opens read
only for everyone.

The sessions are held in memory, so every page editing a file has to reach the same instance of the
application: with several instances, route a file's pages to one of them (sticky sessions). An instance
that cannot take a file's lock, because another holds it, opens the file read only. The sessions need no
middleware: the events are plain HTTP.

| Endpoint | |
|---|---|
| `POST /morph/wopi/session/join` | Joins the file's session, with the WOPISrc and the token as the proxy takes them: a connection id, who the user is, whether their edits are taken. |
| `GET …/{connection}/document` | The document as the session has it, and its version. |
| `POST …/{connection}/commit?base={version}` | An edit, as a patch against a version: accepted, rebased onto what came since, refused, or stale. |
| `GET …/{connection}/patch?from={version}` | What changed since a version. |
| `POST …/{connection}/enter?paragraph={id}&version={version}`, `…/exit` | The paragraph the page opens, and leaves. |
| `POST …/{connection}/save`, `…/leave` | Saves now; leaves (also the page's beacon as it goes). |
| `GET …/{connection}/events` | The session's state as Server-Sent Events: the version, who is in, where, and whether it is saved. |

[MS-WOPI]: https://learn.microsoft.com/en-us/openspecs/office_protocols/ms-wopi/
