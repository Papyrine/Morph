// A scenario is a fixture, a sequence of steps and what to observe afterwards. Axes expand it into a
// table of cases, branches into a tree, toggles run it once per option value, and Tracked repeats it
// with Track Changes on. The Id is the join key between the C# definition, Results/<family>.json,
// src/word-behavior.md and any test that cites it with [Property("WordBehavior", "<id>")].
[Flags]
enum Observe
{
    Selection = 1,
    Text = 2,
    Paragraphs = 4,
    Formatting = 8,
    Lists = 16,
    Tables = 32,
    Shapes = 64,
    Undo = 128,
    Revisions = 256,
    Story = 512,
    Markup = 1024,
    Ribbon = 2048,
    Default = Selection | Text | Paragraphs | Undo
}

record Axis(string Name, IReadOnlyList<string> Values);

record Branch(string Id, IReadOnlyList<string> Steps, IReadOnlyList<Branch> Children, string? Title = null);

record OptionToggle(string Member, IReadOnlyList<string> Values);

record Scenario(
    string Id,
    string Family,
    string Title,
    string Fixture,
    IReadOnlyList<string> Steps,
    Observe Observe,
    string? Notes,
    string? Hypotheses,
    IReadOnlyList<Axis> Axes,
    IReadOnlyList<Branch> Branches,
    IReadOnlyList<OptionToggle> Toggles,
    bool AlsoTracked,
    string? StartAt,
    IReadOnlyList<string> Aliases,
    string? Morph,
    bool Trace)
{
    public bool IsParameterised => Axes.Count > 0;

    public bool IsTree => Branches.Count > 0;
}

sealed class ScenarioBuilder
{
    readonly string id;
    readonly string title;
    string fixture = "plain";
    readonly List<string> steps = [];
    Observe observe = Observe.Default;
    string? notes;
    string? hypotheses;
    readonly List<Axis> axes = [];
    readonly List<Branch> branches = [];
    readonly List<OptionToggle> toggles = [];
    bool tracked;
    string? startAt;
    readonly List<string> aliases = [];
    string? morph;
    bool trace;

    public ScenarioBuilder(string id, string title)
    {
        this.id = id;
        this.title = title;
    }

    public ScenarioBuilder In(string fixtureName)
    {
        fixture = fixtureName;
        return this;
    }

    public ScenarioBuilder StartAt(string position)
    {
        startAt = position;
        return this;
    }

    public ScenarioBuilder Steps(params string[] stepTexts)
    {
        steps.AddRange(stepTexts);
        return this;
    }

    public ScenarioBuilder Observing(Observe flags)
    {
        observe = flags;
        return this;
    }

    public ScenarioBuilder Also(Observe flags)
    {
        observe |= flags;
        return this;
    }

    public ScenarioBuilder Notes(string text)
    {
        notes = text;
        return this;
    }

    /// <summary>The competing expectations, written before the run, so the record can say which one Word confirmed.</summary>
    public ScenarioBuilder Hypotheses(string text)
    {
        hypotheses = text;
        return this;
    }

    public ScenarioBuilder Axis(string name, params string[] values)
    {
        axes.Add(new(name, values));
        return this;
    }

    public ScenarioBuilder Branch(string branchId, string[] branchSteps, params Branch[] children)
    {
        branches.Add(new(branchId, branchSteps, children));
        return this;
    }

    public ScenarioBuilder Toggle(string member, params string[] values)
    {
        toggles.Add(new(member, values));
        return this;
    }

    public ScenarioBuilder Tracked()
    {
        tracked = true;
        return this;
    }

    public ScenarioBuilder Alias(params string[] oldIds)
    {
        aliases.AddRange(oldIds);
        return this;
    }

    /// <summary>The Blazor editor's standing on this scenario, from reading its code: DONE, PARTIAL, TODO or UNKNOWN.</summary>
    public ScenarioBuilder Morph(string status)
    {
        morph = status;
        return this;
    }

    /// <summary>Records the selection after every step, so one case shows a whole key sequence as a table.</summary>
    public ScenarioBuilder Trace()
    {
        trace = true;
        return this;
    }

    public Scenario Build(string family) =>
        new(id, family, title, fixture, steps, observe, notes, hypotheses, axes, branches, toggles, tracked, startAt, aliases, morph, trace);

    public static Branch Node(string branchId, string[] branchSteps, params Branch[] children) =>
        new(branchId, branchSteps, children);
}

/// <summary>A family: an id (the markdown marker name), a title, and its scenarios in order.</summary>
record FamilyDefinition(string Id, string Title, string Description, Func<IEnumerable<ScenarioBuilder>> Builders)
{
    public IReadOnlyList<Scenario> Scenarios() =>
        Builders().Select(_ => _.Build(Id)).ToList();
}

static class Define
{
    public static ScenarioBuilder Probe(string id, string title) =>
        new(id, title);

    public static Branch Node(string branchId, string[] branchSteps, params Branch[] children) =>
        new(branchId, branchSteps, children);

    public static string[] S(params string[] steps) =>
        steps;
}
