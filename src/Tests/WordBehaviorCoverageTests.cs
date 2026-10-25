/// <summary>
/// The join between <c>src/word-behavior.md</c> (what Word does, measured by <c>src/WordProbe</c>) and the
/// tests that make the editor do the same. A test cites the case, scenario or rule it enforces with
/// <c>[Property("WordBehavior", "&lt;id&gt;")]</c>; the renderer lists those tests under the scenario. This
/// keeps the two honest: every cited id has to exist (a renamed scenario keeps its old id as an alias, a
/// deleted one would strand its tests), and a rule the file marks <c>DONE</c> has to have a test behind it,
/// since <c>DONE</c> is a claim about the editor and only a test can keep it true.
/// </summary>
public class WordBehaviorCoverageTests
{
    static readonly string solution = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, ".."));
    static readonly string results = Path.Combine(solution, "WordProbe", "Results");
    static readonly string markdown = Path.Combine(solution, "word-behavior.md");

    static readonly Regex property = new(
        """Property\(\s*"WordBehavior"\s*,\s*"(?<id>[^"]+)"\s*\)""",
        RegexOptions.Compiled);

    // "- **R-E-3 Backspace at a paragraph start …** … — Morph: DONE". The id opens the bold title and the
    // bullet wraps over several lines (continuation lines are indented), so the match runs from the bullet's
    // start to the next bullet or blank line.
    static readonly Regex doneRule = new(@"^- \*\*(?<id>R-[A-Z]+-\d+)\b(?:(?!\n\n|\n- ).)*?Morph:\s*DONE\b", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline);

    [Test]
    public async Task EveryCitedWordBehaviorIdExists()
    {
        var known = KnownIds();
        var missing = new List<string>();
        foreach (var (file, id) in Citations())
        {
            if (!known.Contains(id) &&
                !known.Contains(ScenarioOf(id)))
            {
                missing.Add($"{Path.GetFileName(file)}: {id}");
            }
        }

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task EveryRuleMarkedDoneHasATest()
    {
        if (!File.Exists(markdown))
        {
            return;
        }

        var cited = Citations().Select(_ => _.Id).ToHashSet(StringComparer.Ordinal);
        var text = await File.ReadAllTextAsync(markdown);
        var unbacked = doneRule.Matches(text)
            .Select(_ => _.Groups["id"].Value)
            .Where(_ => !cited.Contains(_))
            .ToList();

        await Assert.That(unbacked).IsEmpty();
    }

    static HashSet<string> KnownIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(results))
        {
            foreach (var file in Directory.GetFiles(results, "*.json"))
            {
                if (Path.GetFileName(file) == "environment.json")
                {
                    continue;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(file));
                if (!document.RootElement.TryGetProperty("Cases", out var cases))
                {
                    continue;
                }

                foreach (var probeCase in cases.EnumerateObject())
                {
                    ids.Add(probeCase.Name);
                    ids.Add(ScenarioOf(probeCase.Name));
                }
            }
        }

        if (File.Exists(markdown))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(markdown), @"^- \*\*(R-[A-Z]+-\d+)\b", RegexOptions.Multiline))
            {
                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    static string ScenarioOf(string id)
    {
        var cut = id.IndexOfAny(['[', '/', '{', '~']);
        return cut < 0 ? id : id[..cut];
    }

    static IEnumerable<(string File, string Id)> Citations()
    {
        foreach (var root in new[] {Path.Combine(solution, "Tests"), Path.Combine(solution, "Morph.Web.Tests")})
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.EndsWith("WordBehaviorCoverageTests.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in property.Matches(File.ReadAllText(file)))
                {
                    yield return (file, match.Groups["id"].Value);
                }
            }
        }
    }
}
