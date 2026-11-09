/// <summary>
/// Every committed <c>*.verified.md</c> snapshot starts with a UTF-8 BOM, because Verify writes one.
/// The BOM is not the point — its absence is. A missing BOM means something OTHER than Verify wrote
/// the file, and this guard exists to make that loud.
///
/// The concrete case: `mdsnippets` walks the repo and rewrites markdown in place. `src/mdsnippets.json`
/// shields these snapshots with `ExcludeMarkdownFiles`, a key MarkdownSnippets older than 28.4.0
/// ignores silently, rewriting the snapshots under `SpecTests/Export/` through `File.WriteAllText`,
/// which emits UTF-8 WITHOUT a BOM.
///
/// A stripped BOM does NOT fail the Verify comparison, so the corruption is silent, and the docs
/// workflow (`.github/workflows/on-push-do-docs.yml`) commits whatever mdsnippets changed with
/// `git commit -a`.
///
/// Scope is every `*.verified.md` in the tree, not only the vulnerable 34. The snapshots under
/// `Inputs/` are additionally covered by `ExcludeDirectories`, which predates the newer key, but the
/// invariant is the same for all of them and asserting it uniformly costs nothing.
/// </summary>
public class VerifiedMarkdownEncodingTests
{
    [Test]
    public async Task VerifiedMarkdownSnapshotsKeepTheirBom()
    {
        var root = ProjectFiles.ProjectDirectory;
        var files = Directory.GetFiles(root, "*.verified.md", SearchOption.AllDirectories).Order().ToList();

        // A guard over an empty set passes vacuously; the corpus has hundreds, so a zero here means
        // the glob or the root moved rather than that the invariant holds.
        await Assert.That(files).IsNotEmpty()
            .Because("no *.verified.md snapshots were found — the search root is wrong.");

        var missing = files
            .Where(_ => !StartsWithBom(_))
            .Select(_ => Path.GetRelativePath(root, _).Replace('\\', '/'))
            .ToList();

        await Assert.That(missing).IsEmpty()
            .Because(
                "these Verify snapshots have lost their UTF-8 BOM, so something other than Verify " +
                "rewrote them — most likely mdsnippets running with a MarkdownSnippets older than " +
                "28.4.0, which ignores the ExcludeMarkdownFiles key that shields them. Restore them " +
                "(git checkout) and update the tool; do not re-promote." +
                Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    static bool StartsWithBom(string file)
    {
        using var stream = File.OpenRead(file);
        Span<byte> head = stackalloc byte[3];
        return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length &&
               head is [0xEF, 0xBB, 0xBF];
    }
}
