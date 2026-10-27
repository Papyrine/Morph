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
3. The editor, Morph.Blazor's `MorphWopi` running in the browser, gets the file's info and its content,
   locks it, and saves it back, through `/morph/wopi/proxy/{operation}`: the request goes from the server
   to the host's WOPI endpoints, signed with the proof key ([MS-WOPI] 2.2.1).

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


## Options

| Option | Default | |
|---|---|---|
| `BaseUrl` | the request's | The URL the hosts reach Morph at, which the discovery's action URLs start with. |
| `TrustedHosts` | none | The hosts Morph opens files for, as origins: a `WOPISrc` on any other is refused, and only these may frame the editor. |
| `Page` | `index.html` | The page of the web root that runs the editor, which the action URLs answer with. |
| `ProofKey`, `OldProofKey` | a key made at startup | The RSA keys requests are signed with, published in the discovery. Set them when the application has several instances, or the hosts should not read the discovery again after a restart. |
| `MaxFileSize` | 25 MB | The largest file a save sends to the host. |


## What the editor does with the file

A Word document opened to edit is locked while it is open, the lock refreshed every ten minutes, and Save
(Ctrl+S) puts it back over itself; a lock that lapsed is taken again before a save. A file the user may not
save (`UserCanWrite`, `ReadOnly`, `SupportsUpdate`, `SupportsLocks`), or that someone else has locked,
opens read only with the reason; so do a workbook, a deck, and a file opened to view. The host's breadcrumb
and `CloseUrl` go in a bar over the editor, and `DisablePrint` and `DisableExport` take Print and Download
away. Leaving the page unlocks the file.

[MS-WOPI]: https://learn.microsoft.com/en-us/openspecs/office_protocols/ms-wopi/
