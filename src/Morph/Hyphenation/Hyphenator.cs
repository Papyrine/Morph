/// <summary>
/// Where an English word may be hyphenated: Liang's algorithm over TeX's American English patterns
/// (<c>hyph-en-us.pat.txt</c>, with its exception list <c>hyph-en-us.hyp.txt</c>; notice in
/// <c>LICENSE.txt</c>), keeping at least two letters before a break and three after it, as the
/// patterns were made for.
/// </summary>
static class Hyphenator
{
    const int leftMinimum = 2;
    const int rightMinimum = 3;

    static readonly Lazy<(Dictionary<string, byte[]> Patterns, Dictionary<string, int[]> Exceptions, int Longest)> data = new(Load);

    /// <summary>
    /// The positions in <paramref name="word"/> before which a hyphen may break it, in ascending order.
    /// Empty for a word too short to break or holding anything but letters and an apostrophe.
    /// </summary>
    public static IReadOnlyList<int> Points(string word)
    {
        if (word.Length < leftMinimum + rightMinimum ||
            !word.All(_ => char.IsLetter(_) || _ is '\'' or '’'))
        {
            return [];
        }

        var (patterns, exceptions, longest) = data.Value;
        var lower = word.ToLowerInvariant();
        if (exceptions.TryGetValue(lower, out var listed))
        {
            return listed;
        }

        var dotted = $".{lower}.";
        var values = new byte[dotted.Length + 1];
        for (var start = 0; start < dotted.Length; start++)
        {
            for (var length = 1; length <= longest && start + length <= dotted.Length; length++)
            {
                if (!patterns.TryGetValue(dotted.Substring(start, length), out var pattern))
                {
                    continue;
                }

                for (var index = 0; index < pattern.Length; index++)
                {
                    values[start + index] = Math.Max(values[start + index], pattern[index]);
                }
            }
        }

        // values[i] sits before dotted[i]; dotted[i] is word[i - 1], so a break before word[k] is
        // values[k + 1].
        var points = new List<int>();
        for (var position = leftMinimum; position <= word.Length - rightMinimum; position++)
        {
            if (values[position + 1] % 2 == 1)
            {
                points.Add(position);
            }
        }

        return points;
    }

    static (Dictionary<string, byte[]>, Dictionary<string, int[]>, int) Load()
    {
        var patterns = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var longest = 0;
        foreach (var line in Lines("hyph-en-us.pat.txt"))
        {
            var letters = new System.Text.StringBuilder();
            var weights = new List<byte> {0};
            foreach (var character in line)
            {
                if (char.IsAsciiDigit(character))
                {
                    weights[^1] = (byte) (character - '0');
                }
                else
                {
                    letters.Append(character);
                    weights.Add(0);
                }
            }

            patterns[letters.ToString()] = [.. weights];
            longest = Math.Max(longest, letters.Length);
        }

        var exceptions = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var line in Lines("hyph-en-us.hyp.txt"))
        {
            var points = new List<int>();
            var length = 0;
            foreach (var character in line)
            {
                if (character == '-')
                {
                    points.Add(length);
                }
                else
                {
                    length++;
                }
            }

            exceptions[line.Replace("-", "")] = [.. points];
        }

        return (patterns, exceptions, longest);
    }

    static IEnumerable<string> Lines(string name)
    {
        var assembly = typeof(Hyphenator).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(_ => _.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 &&
                !trimmed.StartsWith('%'))
            {
                yield return trimmed;
            }
        }
    }
}
