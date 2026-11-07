namespace Morph;

/// <summary>
/// How Morph serves as a WOPI client: see <see cref="MorphWopiExtensions"/>.
/// </summary>
public sealed class MorphWopiOptions
{
    /// <summary>
    /// The URL WOPI hosts reach Morph at, which the action URLs of the discovery start with. Left null, the
    /// URL the discovery was asked for at.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The WOPI hosts Morph opens files for, as origins (<c>https://files.example.com</c>): a WOPISrc on any
    /// other is refused, and only these may frame the editor. Empty, every file is refused.
    /// </summary>
    public List<string> TrustedHosts { get; set; } = [];

    /// <summary>
    /// The page of the web root that runs the editor: a Blazor WebAssembly app whose root component is
    /// Morph.Blazor's <c>MorphWopi</c>. The action URLs answer with it, the file to open written into it.
    /// Default <c>index.html</c>, an app at the root. An app served under a path (its
    /// <c>StaticWebAssetBasePath</c>, and its <c>&lt;base href&gt;</c>) has its page there:
    /// <c>morph/index.html</c> for an app at <c>/morph/</c>. Morph.Blazor's assets, which the app asks for
    /// under its base, are then served there too (<see cref="MorphWopiExtensions.MapMorphAssets"/>).
    /// </summary>
    public string Page { get; set; } = "index.html";

    /// <summary>
    /// The key the requests to the hosts are signed with ([MS-WOPI] 2.2.1), published in the discovery.
    /// Left null, a key is made when the application starts, and the hosts read the discovery again after
    /// a restart. Set one that lasts when there are several instances of the application.
    /// </summary>
    public RSA? ProofKey { get; set; }

    /// <summary>
    /// The key before <see cref="ProofKey"/>, published beside it while the hosts move to the new one.
    /// </summary>
    public RSA? OldProofKey { get; set; }

    /// <summary>
    /// The largest file a save sends to the host, in bytes. Default 25 MB, the largest the editor opens.
    /// </summary>
    public long MaxFileSize { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Whether several people may edit a document at once. Default true: the pages editing a file join one
    /// session on the server, which holds the file's lock, merges their edits paragraph by paragraph, shows
    /// each where the others are, and saves the file (<see cref="AutosaveDelay"/>). The sessions are held in
    /// memory, so every page editing a file has to reach the same instance of the application. False, each
    /// page locks and saves the file itself, and a second user opens it read only.
    /// </summary>
    public bool Coauthoring { get; set; } = true;

    /// <summary>
    /// How long after the first change not yet saved a coauthoring session saves the document to the host.
    /// Default 10 seconds. The document is also saved when the last person leaves, and when someone saves.
    /// </summary>
    public TimeSpan AutosaveDelay { get; set; } = TimeSpan.FromSeconds(10);
}
