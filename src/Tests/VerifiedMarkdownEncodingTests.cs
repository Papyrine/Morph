/// <summary>
/// Every committed <c>*.verified.md</c> snapshot starts with a UTF-8 BOM, because Verify writes one.
/// The BOM is not the point — its absence is. A missing BOM means something OTHER than Verify wrote
/// the file, and this guard exists to make that loud.
///
/// The concrete case: `mdsnippets` walks the repo and rewrites markdown in place. `src/mdsnippets.json`
/// shields these snapshots with `ExcludeMarkdownFiles`, a key added in MarkdownSnippets 28.4.0 — an
/// older tool ignores it silently (unknown JSON properties bind to nothing), processes the 34
/// snapshots under `SpecTests/Export/`, and rewrites them through `File.WriteAllText`, which emits
/// UTF-8 WITHOUT a BOM. 28.4.1 added a skip-if-unchanged guard that also prevents it, so current
/// tooling is doubly safe; this test covers the case where neither protection holds.
///
/// Why it matters even though nothing visibly breaks: a stripped BOM does NOT fail the Verify
/// comparison — verified against `MarkdownExporterTests.Headings`, which passes with its BOM removed.
/// So the corruption is silent. The docs workflow (`.github/workflows/on-push-do-docs.yml`) runs
/// mdsnippets and then commits whatever changed with `git commit -a`, meaning a silent rewrite of 34
/// baselines can land on the default branch inside a commit labelled "Docs changes". Today that is
/// blocked incidentally: `todo.md` contains banned words and is itself excluded by the same key, so a
/// tool that ignores the key fails content validation, the step exits non-zero, and the push step
/// (which has no `if:`) is skipped. That safety net disappears with `todo.md`, which is slated for
/// deletion — this test is what remains.
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
