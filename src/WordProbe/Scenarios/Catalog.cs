// Every family in the order the markdown shows them and the run visits them. A new family is one entry
// here plus a static class under Families/; its marker pair in src/word-behavior.md is where its results
// land (until then they render under Unfiled).
static class Catalog
{
    public static IReadOnlyList<FamilyDefinition> Families { get; } =
    [
        NavigationFamily.Definition,
        SelectionFamily.Definition,
        MouseFamily.Definition,
        EditingFamily.Definition,
        TablesFamily.Definition,
        ListsFamily.Definition,
        UndoFamily.Definition,
        ClipboardFamily.Definition,
        ImagesFamily.Definition,
        AutoCorrectFamily.Definition,
        FieldsFamily.Definition,
        TrackingFamily.Definition,
        StoriesFamily.Definition,
        MiscFamily.Definition,
        DialogsFamily.Definition,
        RibbonFamily.Definition,
        KeepFamily.Definition
    ];

    public static List<FamilyDefinition> Select(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return Families.ToList();
        }

        var selected = new List<FamilyDefinition>();
        foreach (var name in names)
        {
            var family = Families.FirstOrDefault(_ => _.Id.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (family == null)
            {
                Log.Warn($"Unknown family '{name}'. Known: {string.Join(", ", Families.Select(_ => _.Id))}");
                continue;
            }

            selected.Add(family);
        }

        return selected;
    }

    /// <summary>Every scenario, checked for duplicate ids across families.</summary>
    public static Dictionary<string, Scenario> AllScenarios()
    {
        var all = new Dictionary<string, Scenario>(StringComparer.Ordinal);
        foreach (var family in Families)
        {
            foreach (var scenario in family.Scenarios())
            {
                if (!all.TryAdd(scenario.Id, scenario))
                {
                    throw new InvalidOperationException($"Scenario id '{scenario.Id}' is defined twice ({all[scenario.Id].Family} and {family.Id}).");
                }

                foreach (var alias in scenario.Aliases)
                {
                    if (!all.TryAdd(alias, scenario))
                    {
                        throw new InvalidOperationException($"Alias '{alias}' of '{scenario.Id}' collides with another scenario.");
                    }
                }
            }
        }

        return all;
    }

    public static void List(string? familyName)
    {
        var families = familyName == null ? Families : Select([familyName]);
        var total = 0;
        var problems = 0;
        AllScenarios();
        foreach (var family in families)
        {
            var scenarios = family.Scenarios();
            var cases = 0;
            foreach (var scenario in scenarios)
            {
                foreach (var probeCase in CaseExpander.Expand(scenario))
                {
                    cases++;
                    try
                    {
                        probeCase.Steps();
                    }
                    catch (Exception exception)
                    {
                        problems++;
                        Console.WriteLine($"  PARSE ERROR {probeCase.Id}: {exception.Message}");
                    }
                }

                if (!FixtureCatalog.All.Any(_ => _.Name == scenario.Fixture) &&
                    !scenario.Fixture.Contains('{', StringComparison.Ordinal))
                {
                    problems++;
                    Console.WriteLine($"  UNKNOWN FIXTURE {scenario.Id}: {scenario.Fixture}");
                }

                foreach (var axis in scenario.Axes.Where(_ => _.Name == "fixture"))
                {
                    foreach (var value in axis.Values.Where(value => FixtureCatalog.All.All(_ => _.Name != value)))
                    {
                        problems++;
                        Console.WriteLine($"  UNKNOWN FIXTURE {scenario.Id}: {value}");
                    }
                }
            }

            total += cases;
            Console.WriteLine($"{family.Id}: {scenarios.Count} scenarios, {cases} cases — {family.Title}");
            if (familyName != null)
            {
                foreach (var scenario in scenarios)
                {
                    var expanded = CaseExpander.Expand(scenario);
                    Console.WriteLine($"  {scenario.Id} · {scenario.Title} ({expanded.Count} cases)");
                    foreach (var probeCase in expanded)
                    {
                        Console.WriteLine($"      {probeCase.Id}: {string.Join(" · ", probeCase.StepTexts)}");
                    }
                }
            }
        }

        Console.WriteLine($"{total} cases in all{(problems > 0 ? $", {problems} problems" : "")}");
    }
}
