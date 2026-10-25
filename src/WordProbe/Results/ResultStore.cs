// Results/<family>.json: one record per case, keyed by case id, written atomically after every case.
// `lines` is the notation (what a test compares against); `observation` is the raw record.
record WordInfo(string Version, string Build, string Ui, int Dpi, string ClickToRun);

record CaseResult(
    string DefinitionHash,
    string Title,
    string Fixture,
    List<string> Steps,
    Dictionary<string, string> Options,
    bool Tracked,
    string Status,
    int DurationMs,
    DateTimeOffset RunAt,
    List<string> Lines,
    Observation? Observation,
    List<string> Remarks,
    List<string> Tests,
    List<StepTrace>? Trace = null);

sealed class FamilyResults
{
    public string Family { get; set; } = "";

    public DateTimeOffset Generated { get; set; }

    public WordInfo? Word { get; set; }

    public SortedDictionary<string, CaseResult> Cases { get; set; } = new(StringComparer.Ordinal);
}

record EnvironmentInfo(
    WordInfo Word,
    DateTimeOffset Recorded,
    string Display,
    Dictionary<string, string> OptionsInEffect,
    Dictionary<string, int> HotspotOffsets,
    string UndoSource,
    bool FixtureOpenFallback,
    List<string> Remarks);

static class ResultStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string PathFor(string family) =>
        Path.Combine(Paths.Results, $"{family}.json");

    public static FamilyResults Load(string family)
    {
        var path = PathFor(family);
        if (!File.Exists(path))
        {
            return new()
            {
                Family = family
            };
        }

        return JsonSerializer.Deserialize<FamilyResults>(File.ReadAllText(path), Json) ?? new FamilyResults
        {
            Family = family
        };
    }

    public static IEnumerable<FamilyResults> LoadAll()
    {
        if (!Directory.Exists(Paths.Results))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(Paths.Results, "*.json").OrderBy(_ => _, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == "environment.json")
            {
                continue;
            }

            var results = JsonSerializer.Deserialize<FamilyResults>(File.ReadAllText(file), Json);
            if (results != null)
            {
                yield return results;
            }
        }
    }

    public static void Save(FamilyResults results)
    {
        Directory.CreateDirectory(Paths.Results);
        results.Generated = DateTimeOffset.Now;
        WriteAtomic(PathFor(results.Family), JsonSerializer.Serialize(results, Json));
    }

    public static EnvironmentInfo? LoadEnvironment()
    {
        var path = Path.Combine(Paths.Results, "environment.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<EnvironmentInfo>(File.ReadAllText(path), Json);
    }

    public static void SaveEnvironment(EnvironmentInfo environment)
    {
        Directory.CreateDirectory(Paths.Results);
        WriteAtomic(Path.Combine(Paths.Results, "environment.json"), JsonSerializer.Serialize(environment, Json));
    }

    public static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content + "\n", new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
