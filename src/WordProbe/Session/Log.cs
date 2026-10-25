// Console plus a run log under Results/ (gitignored). Each step, settle result and COM retry goes here,
// which is what makes a case's odd result explainable after the fact.
static class Log
{
    static StreamWriter? writer;

    public static void Open(string name)
    {
        Directory.CreateDirectory(Paths.Results);
        var path = Path.Combine(Paths.Results, $"run-{name}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        writer = new(path, append: false)
        {
            AutoFlush = true
        };
        Info($"Log: {path}");
    }

    public static void Info(string message) =>
        Write("     ", message);

    public static void Warn(string message) =>
        Write("WARN ", message);

    public static void Step(string message) =>
        Write("  >  ", message);

    public static void Detail(string message) =>
        writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff}        {message}");

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level}{message}";
        Console.WriteLine(line);
        writer?.WriteLine(line);
    }

    public static void Close()
    {
        writer?.Dispose();
        writer = null;
    }
}
