// Turns a scenario into the cases the harness runs: the Cartesian product of its axes, every node of its
// branch tree (each replays fixture + StartAt + the path's steps), once per toggle value, and again under
// Track Changes when asked. Case ids are the scenario id plus suffixes, in this order:
//   id[axis=value,axis=value] /branch/path {Options.Member=value} ~tracked
record ProbeCase(
    string Id,
    Scenario Scenario,
    string Fixture,
    IReadOnlyList<string> StepTexts,
    IReadOnlyDictionary<string, string> Options,
    bool Tracked,
    IReadOnlyList<KeyValuePair<string, string>> AxisValues,
    string? BranchPath,
    string DefinitionHash)
{
    public IReadOnlyList<Step> Steps() =>
        StepTexts.Select(StepParser.Parse).ToList();
}

static class CaseExpander
{
    public static List<ProbeCase> Expand(Scenario scenario)
    {
        var cases = new List<ProbeCase>();
        foreach (var axisValues in Product(scenario.Axes))
        {
            var fixture = Substitute(scenario.Fixture, axisValues);
            var startAt = scenario.StartAt == null ? null : Substitute(scenario.StartAt, axisValues);
            var baseSteps = scenario.Steps.Select(_ => Substitute(_, axisValues)).ToList();
            var axisSuffix = axisValues.Count == 0 ? "" : $"[{string.Join(",", axisValues.Select(_ => $"{_.Key}={_.Value}"))}]";

            // The root (the common steps alone) is a case too: every node of the tree is observed.
            var paths = new List<(string? Path, List<string> Steps)>
            {
                (null, baseSteps)
            };
            foreach (var branch in scenario.Branches)
            {
                Walk(branch, "", baseSteps, axisValues, paths);
            }

            foreach (var (path, steps) in paths)
            {
                foreach (var toggleValues in Product(scenario.Toggles.Select(_ => new Axis(_.Member, _.Values)).ToList()))
                {
                    var toggleSuffix = toggleValues.Count == 0 ? "" : $"{{{string.Join(",", toggleValues.Select(_ => $"{_.Key}={_.Value}"))}}}";
                    var options = toggleValues.ToDictionary(_ => _.Key, _ => _.Value, StringComparer.Ordinal);
                    var trackedValues = scenario.AlsoTracked ? [false, true] : new[] {false};
                    foreach (var tracked in trackedValues)
                    {
                        var id = $"{scenario.Id}{axisSuffix}{path ?? ""}{toggleSuffix}{(tracked ? "~tracked" : "")}";
                        var allSteps = new List<string>();
                        if (startAt != null)
                        {
                            allSteps.Add($"select({startAt})");
                        }

                        allSteps.AddRange(steps);
                        cases.Add(new(id, scenario, fixture, allSteps, options, tracked, axisValues, path, Hash(fixture, options, tracked, allSteps, scenario.Observe, scenario.Trace)));
                    }
                }
            }
        }

        return cases;
    }

    static void Walk(Branch branch, string parentPath, List<string> parentSteps, List<KeyValuePair<string, string>> axisValues, List<(string?, List<string>)> paths)
    {
        var path = $"{parentPath}/{branch.Id}";
        List<string> steps = [.. parentSteps, .. branch.Steps.Select(_ => Substitute(_, axisValues))];
        paths.Add((path, steps));
        foreach (var child in branch.Children)
        {
            Walk(child, path, steps, axisValues, paths);
        }
    }

    static IEnumerable<List<KeyValuePair<string, string>>> Product(IReadOnlyList<Axis> axes)
    {
        if (axes.Count == 0)
        {
            yield return [];
            yield break;
        }

        var indexes = new int[axes.Count];
        while (true)
        {
            yield return axes.Select((axis, i) => new KeyValuePair<string, string>(axis.Name, axis.Values[indexes[i]])).ToList();
            var position = axes.Count - 1;
            while (position >= 0)
            {
                indexes[position]++;
                if (indexes[position] < axes[position].Values.Count)
                {
                    break;
                }

                indexes[position] = 0;
                position--;
            }

            if (position < 0)
            {
                yield break;
            }
        }
    }

    static string Substitute(string text, List<KeyValuePair<string, string>> axisValues)
    {
        foreach (var (name, value) in axisValues)
        {
            text = text.Replace($"{{{name}}}", value, StringComparison.Ordinal);
        }

        return text;
    }

    static string Hash(string fixture, Dictionary<string, string> options, bool tracked, List<string> steps, Observe observe, bool trace)
    {
        var builder = new StringBuilder();
        builder.Append(fixture).Append('\n');
        builder.Append(trace ? "trace\n" : "");
        foreach (var (member, value) in options.OrderBy(_ => _.Key, StringComparer.Ordinal))
        {
            builder.Append(member).Append('=').Append(value).Append('\n');
        }

        builder.Append(tracked ? "tracked\n" : "untracked\n");
        builder.Append((int) observe).Append('\n');
        foreach (var step in steps)
        {
            builder.Append(step).Append('\n');
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
