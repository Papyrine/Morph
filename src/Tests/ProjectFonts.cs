/// <summary>
/// Path to the bundled <c>src/Fonts</c> directory. Render contexts constructed in tests
/// should pass this as <c>fontDirectory</c> so font resolution is deterministic across
/// CI agents regardless of what's installed system-wide (the scenario tests already do this
/// via <see cref="ConversionOptions.FontDirectory"/>).
/// </summary>
public static class ProjectFonts
{
    public static readonly string Directory =
        Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));
}
