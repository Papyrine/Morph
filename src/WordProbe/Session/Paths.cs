// Where everything lives, from the solution directory the ProjectFiles generator gives and the local
// application-data folder for the one file that must outlive a crashed run (the options snapshot).
static class Paths
{
    public static string Solution { get; } = Resolve();

    public static string Project => Path.Combine(Solution, "WordProbe");

    public static string Fixtures => Path.Combine(Project, "Fixtures");

    public static string Results => Path.Combine(Project, "Results");

    public static string Markup => Path.Combine(Results, "markup");

    public static string Markdown => Path.Combine(Solution, "word-behavior.md");

    public static string BlankDocument => Path.Combine(Solution, "Morph.Blazor", "wwwroot", "blank.docx");

    public static string LocalState { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WordProbe");

    public static string OptionsSnapshot => Path.Combine(LocalState, "options-snapshot.json");

    public static string OptionsRestored => Path.Combine(LocalState, "options-snapshot.restored");

    public static string SessionFile => Path.Combine(LocalState, "session.json");

    public static string Temp { get; } = Path.Combine(Path.GetTempPath(), "WordProbe");

    static string Resolve()
    {
        var directory = ProjectFiles.SolutionDirectory.ToString();
        if (!File.Exists(Path.Combine(directory, "Morph.slnx")))
        {
            // Walk up from the executable: `dotnet run` and a copied build both land under src/WordProbe/bin.
            var candidate = new DirectoryInfo(AppContext.BaseDirectory);
            while (candidate != null &&
                   !File.Exists(Path.Combine(candidate.FullName, "Morph.slnx")))
            {
                candidate = candidate.Parent;
            }

            directory = candidate?.FullName ?? throw new InvalidOperationException("Morph.slnx was not found above the executable.");
        }

        return directory.TrimEnd(Path.DirectorySeparatorChar);
    }
}
