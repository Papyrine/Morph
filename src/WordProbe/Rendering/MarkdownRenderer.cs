// Writes the generated parts of src/word-behavior.md from Results/*.json and the catalogue: the body of
// every `<!-- probe:begin <family> -->` … `<!-- probe:end <family> -->` pair, the one-line
// `<!-- probe:generated -->` provenance, and the pseudo-families `options` and `fixtures`. Everything
// outside the markers is hand-written and left alone. A family with results but no marker pair renders
// under `unfiled`.
static class MarkdownRenderer
{
    const int wrap = 118;

    public static void Render()
    {
        var environment = ResultStore.LoadEnvironment();
        var results = ResultStore.LoadAll().ToDictionary(_ => _.Family, StringComparer.Ordinal);
        var coverage = Coverage.Scan();
        var markdown = File.Exists(Paths.Markdown) ? File.ReadAllText(Paths.Markdown) : Template();
        var families = Catalog.Families;
        var rendered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var family in families)
        {
            results.TryGetValue(family.Id, out var familyResults);
            var body = RenderFamily(family, familyResults, coverage);
            if (Replace(ref markdown, family.Id, body))
            {
                rendered.Add(family.Id);
            }
        }

        var unfiled = new StringBuilder();
        foreach (var family in families.Where(_ => !rendered.Contains(_.Id)))
        {
            results.TryGetValue(family.Id, out var familyResults);
            unfiled.Append("### ").Append(family.Title).Append(" (`").Append(family.Id).Append("`)\n\n");
            unfiled.Append(RenderFamily(family, familyResults, coverage));
        }

        foreach (var orphan in results.Values.Where(_ => families.All(family => family.Id != _.Family)))
        {
            unfiled.Append("### `").Append(orphan.Family).Append("` (no longer in the catalogue)\n\n");
            foreach (var (id, result) in orphan.Cases)
            {
                unfiled.Append(RenderCase(id, result, null, coverage, "####"));
            }
        }

        Replace(ref markdown, "unfiled", unfiled.Length == 0 ? "_Nothing unfiled._\n" : unfiled.ToString());
        Replace(ref markdown, "options", RenderOptions(environment));
        Replace(ref markdown, "fixtures", RenderFixtures());
        Replace(ref markdown, "environment", RenderEnvironment(environment));
        markdown = Regex.Replace(markdown, "<!-- probe:generated -->.*", $"<!-- probe:generated --> {Provenance(environment)}");
        File.WriteAllText(Paths.Markdown, markdown, new UTF8Encoding(false));
        Log.Info($"Rendered {Paths.Markdown}");
    }

    static string Provenance(EnvironmentInfo? environment)
    {
        if (environment == null)
        {
            return "Not yet generated: run `dotnet run --project src/WordProbe -- smoke`.";
        }

        var word = environment.Word;
        return $"Generated {environment.Recorded:yyyy-MM-dd} on Word for Microsoft 365, version {word.Version} build {word.Build} (Click-to-Run {word.ClickToRun}), Windows 11, {word.Ui} UI, one display {environment.Display}. Undo names {(environment.UndoSource == "control128" ? "from the Undo list" : "unavailable (depth only)")}.";
    }

    static bool Replace(ref string markdown, string name, string body)
    {
        var begin = $"<!-- probe:begin {name} -->";
        var end = $"<!-- probe:end {name} -->";
        var start = markdown.IndexOf(begin, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var stop = markdown.IndexOf(end, start, StringComparison.Ordinal);
        if (stop < 0)
        {
            return false;
        }

        markdown = markdown[..(start + begin.Length)] + "\n" + body.TrimEnd('\n') + "\n" + markdown[stop..];
        return true;
    }

    static string RenderFamily(FamilyDefinition family, FamilyResults? results, Dictionary<string, List<string>> coverage)
    {
        var builder = new StringBuilder();
        var cases = results?.Cases ?? new SortedDictionary<string, CaseResult>(StringComparer.Ordinal);
        var scenarios = family.Scenarios();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var notRun = new List<string>();
        foreach (var scenario in scenarios)
        {
            var expanded = CaseExpander.Expand(scenario);
            var present = expanded.Where(_ => cases.ContainsKey(_.Id)).ToList();
            foreach (var probeCase in present)
            {
                seen.Add(probeCase.Id);
            }

            if (present.Count == 0)
            {
                notRun.Add(scenario.Id);
                continue;
            }

            builder.Append(RenderScenario(scenario, expanded, cases, coverage));
        }

        foreach (var (id, result) in cases.Where(_ => !seen.Contains(_.Key)))
        {
            builder.Append(RenderCase(id, result, null, coverage, "####"));
        }

        if (notRun.Count > 0)
        {
            builder.Append("Not yet run: ").Append(string.Join(", ", notRun.Select(_ => $"`{_}`"))).Append("\n\n");
        }

        if (builder.Length == 0)
        {
            builder.Append("_No results yet._\n");
        }

        return builder.ToString();
    }

    static string RenderScenario(Scenario scenario, List<ProbeCase> expanded, SortedDictionary<string, CaseResult> cases, Dictionary<string, List<string>> coverage)
    {
        var builder = new StringBuilder();
        builder.Append("#### ").Append(scenario.Id).Append(" · ").Append(scenario.Title).Append("\n\n");
        var intro = new List<string>
        {
            $"`{scenario.Fixture}`"
        };
        if (scenario.StartAt != null)
        {
            intro.Add($"start `{scenario.StartAt}`");
        }

        if (scenario.Morph != null)
        {
            intro.Add($"Morph: **{scenario.Morph}**");
        }

        builder.Append(string.Join(" · ", intro)).Append("\n\n");
        if (scenario.Hypotheses != null)
        {
            builder.Append(Wrap("Expected before the run: " + scenario.Hypotheses)).Append("\n\n");
        }

        var tests = coverage.TryGetValue(scenario.Id, out var scenarioTests) ? scenarioTests : [];
        if (tests.Count > 0)
        {
            builder.Append("Tests: ").Append(string.Join(", ", tests.Select(_ => $"`{_}`"))).Append("\n\n");
        }

        if (scenario.Observe.HasFlag(Observe.Ribbon))
        {
            builder.Append(RenderRibbon(scenario, expanded, cases));
        }
        else if (scenario is
                 {
                     IsParameterised: true,
                     IsTree: false
                 })
        {
            builder.Append(RenderTable(scenario, expanded, cases));
        }
        else if (scenario.IsTree)
        {
            builder.Append(RenderTree(scenario, expanded, cases));
        }
        else
        {
            foreach (var probeCase in expanded)
            {
                if (cases.TryGetValue(probeCase.Id, out var result))
                {
                    if (result.Status == "not-applicable")
                    {
                        builder.Append("_`").Append(probeCase.Id).Append("`: set-up does not apply (").Append(string.Join("; ", result.Remarks)).Append(")._\n\n");
                        continue;
                    }

                    builder.Append(RenderCase(probeCase.Id, result, probeCase, coverage, null));
                }
            }
        }

        if (scenario.Notes != null)
        {
            builder.Append(Wrap("Notes: " + scenario.Notes)).Append("\n\n");
        }

        return builder.ToString();
    }

    static string RenderCase(string id, CaseResult result, ProbeCase? probeCase, Dictionary<string, List<string>> coverage, string? heading)
    {
        var builder = new StringBuilder();
        if (heading != null)
        {
            builder.Append(heading).Append(' ').Append(id).Append(" · ").Append(result.Title).Append("\n\n");
        }

        var variant = Variant(result, probeCase);
        if (variant.Length > 0)
        {
            builder.Append("**").Append(variant).Append("**\n\n");
        }

        var steps = result.Steps.Where(_ => !_.StartsWith("select(", StringComparison.Ordinal) || probeCase?.Scenario.StartAt == null).ToList();
        for (var i = 0; i < steps.Count; i++)
        {
            builder.Append(i + 1).Append(". `").Append(steps[i]).Append("`\n");
        }

        builder.Append('\n');
        if (result.Status != "ok")
        {
            builder.Append("_Status: ").Append(result.Status);
            if (result.Remarks.Count > 0)
            {
                builder.Append(" — ").Append(string.Join("; ", result.Remarks));
            }

            builder.Append("._\n\n");
        }

        if (result.Trace is {Count: > 0})
        {
            builder.Append("| step | selection | where |\n| --- | --- | --- |\n");
            foreach (var trace in result.Trace)
            {
                var selection = trace.Selection;
                var active = selection.IsCollapsed ? "" : selection.StartIsActive ? " start-active" : " end-active";
                builder.Append("| `").Append(trace.Step).Append("` | ").Append(selection.Type).Append(' ').Append(selection.Start).Append('–').Append(selection.End).Append(active).Append(" | `").Append(Cell(trace.Line)).Append("` |\n");
            }

            builder.Append('\n');
        }

        var lines = Lines(result);
        if (lines.Count > 0)
        {
            builder.Append("```text\n");
            foreach (var line in lines.Take(lines.Count - 1))
            {
                builder.Append(line).Append('\n');
            }

            builder.Append("```\n\n");
            builder.Append(lines[^1]).Append("\n\n");
        }

        if (result.Observation?.Ribbon is { } ribbonState)
        {
            builder.Append(RibbonReader.Summary(ribbonState)).Append("\n\n");
        }

        if (result is
            {
                Status: "ok",
                Remarks.Count: > 0
            })
        {
            builder.Append('_').Append(string.Join("; ", result.Remarks)).Append("_\n\n");
        }

        if (result.Observation?.MarkupDiff != null)
        {
            builder.Append("Markup diff: `WordProbe/Results/").Append(result.Observation.MarkupDiff).Append("`\n\n");
        }

        if (coverage.TryGetValue(id, out var tests) &&
            tests.Count > 0)
        {
            builder.Append("Tests: ").Append(string.Join(", ", tests.Select(_ => $"`{_}`"))).Append("\n\n");
        }

        return builder.ToString();
    }

    /// <summary>The notation, re-rendered from the stored observation so a notation fix reaches old results too.</summary>
    static List<string> Lines(CaseResult result)
    {
        if (result.Observation == null)
        {
            return result.Lines;
        }

        try
        {
            return StateNotation.Lines(result.Observation);
        }
        catch (Exception)
        {
            return result.Lines;
        }
    }

    static string Variant(CaseResult result, ProbeCase? probeCase)
    {
        var parts = new List<string>();
        foreach (var (member, value) in result.Options)
        {
            parts.Add($"{member.Replace("Options.", "").Replace("AutoCorrect.", "")} {value}");
        }

        if (result.Tracked)
        {
            parts.Add("Track Changes on");
        }

        if (probeCase?.BranchPath != null)
        {
            parts.Add($"branch {probeCase.BranchPath}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>One row per case: where the selection was, and what the ribbon greyed out and pressed there.</summary>
    static string RenderRibbon(Scenario scenario, List<ProbeCase> expanded, SortedDictionary<string, CaseResult> cases)
    {
        var builder = new StringBuilder();
        builder.Append("steps `").Append(string.Join("` · `", scenario.Steps)).Append("`\n\n");
        var headings = scenario.Axes.Select(_ => _.Name).ToList();
        if (scenario.AlsoTracked)
        {
            headings.Add("tracked");
        }

        headings.AddRange(["selection", "greyed out", "pressed"]);
        builder.Append("| ").Append(string.Join(" | ", headings)).Append(" |\n");
        builder.Append('|').Append(string.Concat(headings.Select(_ => " --- |"))).Append('\n');
        foreach (var probeCase in expanded)
        {
            if (!cases.TryGetValue(probeCase.Id, out var result) ||
                result.Status == "not-applicable")
            {
                continue;
            }

            var row = probeCase.AxisValues.Select(_ => Cell(_.Value)).ToList();
            if (scenario.AlsoTracked)
            {
                row.Add(probeCase.Tracked ? "on" : "off");
            }

            var ribbon = result.Observation?.Ribbon;
            if (result.Status != "ok" ||
                ribbon == null)
            {
                row.AddRange([$"_{result.Status}_", Cell(string.Join("; ", result.Remarks)), ""]);
                builder.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
                continue;
            }

            var selection = result.Observation!.Selection;
            var where = $"{selection.Type} {selection.Start}–{selection.End}";
            if (selection.StoryType != nameof(Word.WdStoryType.wdMainTextStory))
            {
                where += " " + selection.StoryType.Replace("wd", "").Replace("Story", "");
            }

            row.AddRange([where, Cell(Join(ribbon.Disabled)), Cell(Join(ribbon.Pressed))]);
            builder.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
        }

        builder.Append('\n');
        return builder.ToString();

        static string Join(List<string> ids)
        {
            if (ids.Count == 0)
            {
                return "—";
            }

            return string.Join(", ", ids);
        }
    }

    static string RenderTable(Scenario scenario, List<ProbeCase> expanded, SortedDictionary<string, CaseResult> cases)
    {
        var builder = new StringBuilder();
        var hasOptions = scenario.Toggles.Count > 0;
        var hasTracked = scenario.AlsoTracked;
        builder.Append("steps `").Append(string.Join("` · `", scenario.Steps)).Append("`\n\n");
        builder.Append("| ").Append(string.Join(" | ", scenario.Axes.Select(_ => _.Name)));
        if (hasOptions)
        {
            builder.Append(" | options");
        }

        if (hasTracked)
        {
            builder.Append(" | tracked");
        }

        builder.Append(" | selection | observed |\n");
        builder.Append("| ").Append(string.Join(" | ", scenario.Axes.Select(_ => "---")));
        if (hasOptions)
        {
            builder.Append(" | ---");
        }

        if (hasTracked)
        {
            builder.Append(" | ---");
        }

        builder.Append(" | --- | --- |\n");
        var overflow = new StringBuilder();
        var notApplicable = 0;
        foreach (var probeCase in expanded)
        {
            if (!cases.TryGetValue(probeCase.Id, out var result))
            {
                continue;
            }

            if (result.Status == "not-applicable")
            {
                notApplicable++;
                continue;
            }

            builder.Append("| ").Append(string.Join(" | ", probeCase.AxisValues.Select(_ => Cell(_.Value))));
            if (hasOptions)
            {
                builder.Append(" | ").Append(Cell(string.Join(", ", probeCase.Options.Select(_ => $"{_.Key.Replace("Options.", "").Replace("AutoCorrect.", "")}={_.Value}"))));
            }

            if (hasTracked)
            {
                builder.Append(" | ").Append(probeCase.Tracked ? "on" : "off");
            }

            if (result.Status != "ok")
            {
                builder.Append(" | _").Append(result.Status).Append("_ | ").Append(Cell(string.Join("; ", result.Remarks))).Append(" |\n");
                continue;
            }

            var resultLines = Lines(result);
            var summary = resultLines.Count > 0 ? resultLines[^1] : "";
            var body = resultLines.Take(Math.Max(0, resultLines.Count - 1)).ToList();
            var observed = string.Join("<br>", body.Select(Cell));
            if (observed.Length > 220)
            {
                observed = "see below";
                overflow.Append('`').Append(probeCase.Id).Append("`\n\n```text\n").Append(string.Join("\n", body)).Append("\n```\n\n").Append(summary).Append("\n\n");
            }

            builder.Append(" | ").Append(Cell(SelectionOnly(summary))).Append(" | ").Append(observed.Length == 0 ? "" : $"`{observed}`").Append(" |\n");
        }

        builder.Append('\n');
        if (notApplicable > 0)
        {
            builder.Append('_').Append(notApplicable).Append(" combination(s) whose set-up does not apply to the fixture are left out._\n\n");
        }

        builder.Append(overflow);
        return builder.ToString();
    }

    static string SelectionOnly(string summary)
    {
        var parts = summary.Split(" · ");
        return string.Join(" · ", parts.Where(_ => !_.StartsWith("Selection: ", StringComparison.Ordinal)).Prepend(parts[0].Replace("Selection: ", "")).Where(_ => !_.Contains(" → ", StringComparison.Ordinal)));
    }

    static string Cell(string text) =>
        text.Replace("|", "\\|").Replace("\n", "<br>");

    static string RenderTree(Scenario scenario, List<ProbeCase> expanded, SortedDictionary<string, CaseResult> cases)
    {
        var builder = new StringBuilder();
        builder.Append("steps before the branches `").Append(string.Join("` · `", scenario.Steps)).Append("`\n\n");
        var groups = expanded.GroupBy(_ => (Axis: string.Join(",", _.AxisValues.Select(value => $"{value.Key}={value.Value}")), Options: string.Join(",", _.Options.Select(option => $"{option.Key}={option.Value}")), _.Tracked));
        foreach (var group in groups)
        {
            var label = new List<string>();
            if (group.Key.Axis.Length > 0)
            {
                label.Add(group.Key.Axis);
            }

            if (group.Key.Options.Length > 0)
            {
                label.Add(group.Key.Options);
            }

            if (group.Key.Tracked)
            {
                label.Add("Track Changes on");
            }

            if (label.Count > 0)
            {
                builder.Append("**").Append(string.Join(" · ", label)).Append("**\n\n");
            }

            foreach (var probeCase in group)
            {
                var depth = probeCase.BranchPath?.Count(_ => _ == '/') ?? 1;
                // The branch's own steps are the last ones after its parent's; show the last step of this node.
                var ownStep = LastBranchStep(scenario, probeCase);
                builder.Append(' ', (depth - 1) * 2).Append("- `").Append(ownStep).Append("` → ");
                if (cases.TryGetValue(probeCase.Id, out var result))
                {
                    if (result.Status != "ok")
                    {
                        builder.Append('_').Append(result.Status).Append('_');
                    }
                    else
                    {
                        var resultLines = Lines(result);
                        var body = resultLines.Take(Math.Max(0, resultLines.Count - 1));
                        builder.Append('`').Append(Cell(string.Join(" / ", body))).Append("` · ").Append(SelectionOnly(resultLines[^1]));
                    }
                }
                else
                {
                    builder.Append("_not run_");
                }

                builder.Append('\n');
            }

            builder.Append('\n');
        }

        builder.Append("```mermaid\nflowchart TD\n");
        builder.Append("  root[\"").Append(Mermaid($"{scenario.Fixture}{(scenario.StartAt == null ? "" : " · " + scenario.StartAt)}")).Append("\"]\n");
        var first = expanded.FirstOrDefault();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var probeCase in expanded.Where(_ => first == null || (_.Options.Count == first.Options.Count && _.Tracked == first.Tracked && _.AxisValues.SequenceEqual(first.AxisValues))))
        {
            var path = probeCase.BranchPath ?? "";
            var node = NodeId(path);
            if (!emitted.Add(node))
            {
                continue;
            }

            var parentPath = path.Contains('/', StringComparison.Ordinal) && path.LastIndexOf('/') > 0 ? path[..path.LastIndexOf('/')] : "";
            var parent = parentPath.Length == 0 ? "root" : NodeId(parentPath);
            builder.Append("  ").Append(parent).Append(" --> ").Append(node).Append("[\"").Append(Mermaid(LastBranchStep(scenario, probeCase))).Append("\"]\n");
        }

        builder.Append("```\n\n");
        return builder.ToString();
    }

    static string LastBranchStep(Scenario scenario, ProbeCase probeCase)
    {
        var path = probeCase.BranchPath ?? "";
        var ids = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var node = Find(scenario.Branches, ids, 0);
        return node == null ? probeCase.StepTexts[^1] : string.Join(" · ", node.Steps);
    }

    static Branch? Find(IReadOnlyList<Branch> branches, string[] ids, int index)
    {
        if (index >= ids.Length)
        {
            return null;
        }

        var branch = branches.FirstOrDefault(_ => _.Id == ids[index]);
        if (branch == null)
        {
            return null;
        }

        return index == ids.Length - 1 ? branch : Find(branch.Children, ids, index + 1);
    }

    static string NodeId(string path) =>
        "n_" + Regex.Replace(path, "[^A-Za-z0-9]+", "_").Trim('_');

    static string Mermaid(string text) =>
        text.Replace("\"", "#quot;").Replace("<", "#lt;").Replace(">", "#gt;");

    static string RenderOptions(EnvironmentInfo? environment)
    {
        if (environment == null)
        {
            return "_Not yet recorded._\n";
        }

        var builder = new StringBuilder();
        builder.Append("| option | value during the run |\n| --- | --- |\n");
        foreach (var (member, value) in environment.OptionsInEffect.OrderBy(_ => _.Key, StringComparer.Ordinal))
        {
            builder.Append("| `").Append(member).Append("` | ").Append(value).Append(" |\n");
        }

        return builder.ToString();
    }

    static string RenderEnvironment(EnvironmentInfo? environment)
    {
        if (environment == null)
        {
            return "_Not yet recorded._\n";
        }

        var builder = new StringBuilder();
        builder.Append("| what | value |\n| --- | --- |\n");
        builder.Append("| Word | ").Append(environment.Word.Version).Append(" build ").Append(environment.Word.Build).Append(" (Click-to-Run ").Append(environment.Word.ClickToRun).Append(") |\n");
        builder.Append("| UI language | ").Append(environment.Word.Ui).Append(" |\n");
        builder.Append("| display | ").Append(environment.Display).Append(" |\n");
        builder.Append("| undo entry names | ").Append(environment.UndoSource).Append(" |\n");
        builder.Append("| fixture opening | ").Append(environment.FixtureOpenFallback ? "Documents.Open on a copy (Documents.Add refused the .docx)" : "Documents.Add(Template: fixture)").Append(" |\n");
        foreach (var (name, value) in environment.HotspotOffsets.OrderBy(_ => _.Key, StringComparer.Ordinal))
        {
            builder.Append("| hot spot `").Append(name).Append("` | ").Append(value).Append(" px |\n");
        }

        foreach (var remark in environment.Remarks)
        {
            builder.Append("| note | ").Append(remark).Append(" |\n");
        }

        return builder.ToString();
    }

    static string RenderFixtures()
    {
        var builder = new StringBuilder();
        builder.Append("| fixture | content |\n| --- | --- |\n");
        foreach (var fixture in FixtureCatalog.All)
        {
            builder.Append("| `").Append(fixture.Name).Append("` | ").Append(fixture.Summary).Append(" |\n");
        }

        return builder.ToString();
    }

    static string Wrap(string text)
    {
        var words = text.Split(' ');
        var builder = new StringBuilder();
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length + word.Length + 1 > wrap &&
                line.Length > 0)
            {
                builder.Append(line.ToString().TrimEnd()).Append('\n');
                line.Clear();
            }

            line.Append(word).Append(' ');
        }

        builder.Append(line.ToString().TrimEnd());
        return builder.ToString();
    }

    /// <summary>The first version of the file, before the hand-written prose is added: marker pairs per family.</summary>
    static string Template()
    {
        var builder = new StringBuilder();
        builder.Append("# Word editing behaviour\n\n<!-- probe:generated -->\n\n## Options in effect\n\n<!-- probe:begin options -->\n<!-- probe:end options -->\n\n## Fixtures\n\n<!-- probe:begin fixtures -->\n<!-- probe:end fixtures -->\n\n");
        foreach (var family in Catalog.Families)
        {
            builder.Append("## ").Append(family.Title).Append("\n\n").Append(family.Description).Append("\n\n### Observations\n\n<!-- probe:begin ").Append(family.Id).Append(" -->\n<!-- probe:end ").Append(family.Id).Append(" -->\n\n");
        }

        builder.Append("## Unfiled\n\n<!-- probe:begin unfiled -->\n<!-- probe:end unfiled -->\n");
        return builder.ToString();
    }
}

/// <summary>Which tests cite which scenario or case ids, by scanning the test sources for the WordBehavior property.</summary>
static class Coverage
{
    static readonly Regex property = new("""Property\(\s*"WordBehavior"\s*,\s*"(?<id>[^"]+)"\s*\)""", RegexOptions.Compiled);
    static readonly Regex method = new(@"(?:Task|void|ValueTask)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);
    static readonly Regex type = new(@"class\s+(?<name>\w+)", RegexOptions.Compiled);

    public static Dictionary<string, List<string>> Scan()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var root in new[] {Path.Combine(Paths.Solution, "Tests"), Path.Combine(Paths.Solution, "Morph.Web.Tests")})
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                foreach (Match match in property.Matches(text))
                {
                    var id = match.Groups["id"].Value;
                    var methodMatch = method.Match(text, match.Index);
                    var typeMatch = type.Matches(text[..match.Index]).LastOrDefault();
                    var name = $"{typeMatch?.Groups["name"].Value ?? Path.GetFileNameWithoutExtension(file)}.{(methodMatch.Success ? methodMatch.Groups["name"].Value : "?")}";
                    if (!result.TryGetValue(id, out var list))
                    {
                        list = [];
                        result[id] = list;
                    }

                    if (!list.Contains(name))
                    {
                        list.Add(name);
                    }
                }
            }
        }

        return result;
    }
}
