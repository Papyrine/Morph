// `describe [fixture ...]`: opens each fixture in Word and prints it in the notation, so a scenario's
// positions can be checked against what the fixture holds.
static class Describe
{
    public static void Run(IReadOnlyList<string> names)
    {
        var fixtures = names.Count == 0 ? FixtureCatalog.All.Select(_ => _.Name).ToList() : names.ToList();
        using var session = WordSession.Start();
        foreach (var name in fixtures)
        {
            var path = Path.Combine(Paths.Fixtures, $"{name}.docx");
            if (!File.Exists(path))
            {
                Console.WriteLine($"{name}: not built");
                continue;
            }

            var probe = session.OpenFixture(path);
            var remarks = new List<string>();
            var observation = Observer.Read(probe, Observe.Selection | Observe.Text | Observe.Paragraphs | Observe.Formatting | Observe.Lists | Observe.Tables | Observe.Shapes | Observe.Revisions | Observe.Story, [], null, null, remarks);
            Console.WriteLine($"== {name}");
            foreach (var line in StateNotation.Lines(observation))
            {
                Console.WriteLine("   " + line);
            }

            foreach (var paragraph in observation.Paragraphs)
            {
                var odd = paragraph.Text.Where(_ => _ is < ' ' or > '~').Select(_ => $"\\u{(int) _:X4}").Distinct().ToList();
                Console.WriteLine($"   P{paragraph.Index} {paragraph.Start}-{paragraph.End}{(paragraph.Table > 0 ? $" T{paragraph.Table} R{paragraph.Row} C{paragraph.Column}" : "")}{(odd.Count > 0 ? " codes " + string.Join(" ", odd) : "")}");
            }

            foreach (var remark in remarks)
            {
                Console.WriteLine($"   ! {remark}");
            }
        }
    }
}
