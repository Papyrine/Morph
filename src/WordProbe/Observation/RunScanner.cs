// Effective formatting runs of a paragraph, found by bisection on Word's "mixed" answer (wdUndefined,
// 9999999, or "" for a mixed font name) rather than one COM call per character. A lone space asked on
// its own answers with a neighbour's formatting (Word's display rule for spaces), so a whitespace
// character reached alone takes the marks of the character before it, as its run does in the markup.
//
// Marks are letters (b i u s, ^ superscript, v subscript, h hidden, k small caps, K all caps) followed,
// when the run differs from its paragraph style, by the size in points and the font name: "b 14 Georgia".
static class RunScanner
{
    public static List<RunState> Runs(Word.Range paragraph, int start, int end, string text)
    {
        var baseline = Baseline.Of(paragraph);
        var runs = new List<RunState>();
        Scan(paragraph, start, Math.Max(start, end - 1), text, runs, baseline);
        for (var i = 0; i < runs.Count; i++)
        {
            if (runs[i].Marks != "?")
            {
                continue;
            }

            var marks = i > 0 ? runs[i - 1].Marks : runs.Skip(i + 1).FirstOrDefault(_ => _.Marks != "?")?.Marks ?? "";
            runs[i] = runs[i] with
            {
                Marks = marks
            };
        }

        var merged = new List<RunState>();
        foreach (var run in runs)
        {
            if (merged.Count > 0 &&
                merged[^1].Marks == run.Marks &&
                merged[^1].End == run.Start)
            {
                merged[^1] = merged[^1] with
                {
                    End = run.End
                };
            }
            else
            {
                merged.Add(run);
            }
        }

        if (merged.Count == 0)
        {
            merged.Add(new(start, end, ""));
        }
        else
        {
            merged[^1] = merged[^1] with
            {
                End = end
            };
        }

        return merged;
    }

    static void Scan(Word.Range paragraph, int start, int end, string text, List<RunState> runs, Baseline baseline)
    {
        if (end <= start)
        {
            return;
        }

        // Word folds a space into its neighbour's formatting when asked about a range, so whitespace at
        // either edge of the range is set aside ("?") and only the core is asked.
        var offset = paragraph.Start;
        var first = start;
        while (first < end && IsWhitespace(text, first - offset))
        {
            first++;
        }

        var last = end;
        while (last > first && IsWhitespace(text, last - 1 - offset))
        {
            last--;
        }

        if (first > start)
        {
            runs.Add(new(start, first, "?"));
        }

        if (last > first)
        {
            ScanCore(paragraph, first, last, text, runs, baseline);
        }

        if (end > last)
        {
            runs.Add(new(last, end, "?"));
        }
    }

    static bool IsWhitespace(string text, int index) =>
        index >= 0 && index < text.Length && (text[index] == ' ' || text[index] == ' ' || text[index] == '\t');

    static void ScanCore(Word.Range paragraph, int start, int end, string text, List<RunState> runs, Baseline baseline)
    {
        var range = paragraph.Duplicate;
        range.SetRange(start, end);
        var look = Look.Of(range.Font);
        if (!look.Mixed ||
            end - start == 1)
        {
            runs.Add(new(start, end, look.Marks(baseline)));
            return;
        }

        var middle = start + (end - start) / 2;
        Scan(paragraph, start, middle, text, runs, baseline);
        Scan(paragraph, middle, end, text, runs, baseline);
    }

    /// <summary>The paragraph style's size and face, which a run's marks leave unsaid.</summary>
    sealed record Baseline(float Size, string Name)
    {
        public static Baseline Of(Word.Range paragraph)
        {
            try
            {
                var style = (Word.Style) paragraph.get_Style();
                return new(style.Font.Size, style.Font.Name);
            }
            catch (Exception)
            {
                return new(float.NaN, "");
            }
        }
    }

    sealed record Look(int Bold, int Italic, int Underline, int Strike, int Superscript, int Subscript, int Hidden, int SmallCaps, int AllCaps, float Size, string Name)
    {
        public static Look Of(Word.Font font) =>
            new(font.Bold, font.Italic, (int) font.Underline, font.StrikeThrough, font.Superscript, font.Subscript, font.Hidden, font.SmallCaps, font.AllCaps, font.Size, font.Name);

        public bool Mixed =>
            Observer.IsUndefined(Bold) || Observer.IsUndefined(Italic) || Observer.IsUndefined(Underline) || Observer.IsUndefined(Strike) ||
            Observer.IsUndefined(Superscript) || Observer.IsUndefined(Subscript) || Observer.IsUndefined(Hidden) ||
            Observer.IsUndefined(SmallCaps) || Observer.IsUndefined(AllCaps) || Observer.IsUndefined((int) Size) || Name.Length == 0;

        public string Marks(Baseline baseline)
        {
            var marks = new StringBuilder();
            Letter(marks, Bold, 'b');
            Letter(marks, Italic, 'i');
            Letter(marks, Underline, 'u');
            Letter(marks, Strike, 's');
            Letter(marks, Superscript, '^');
            Letter(marks, Subscript, 'v');
            Letter(marks, Hidden, 'h');
            Letter(marks, SmallCaps, 'k');
            Letter(marks, AllCaps, 'K');
            if (!Observer.IsUndefined((int) Size) && !float.IsNaN(baseline.Size) && Math.Abs(Size - baseline.Size) > 0.01f)
            {
                Token(marks, Size.ToString(CultureInfo.InvariantCulture));
            }

            if (Name.Length > 0 && baseline.Name.Length > 0 && Name != baseline.Name)
            {
                Token(marks, Name);
            }

            return marks.ToString();
        }

        static void Letter(StringBuilder marks, int value, char letter)
        {
            if (value != 0 && !Observer.IsUndefined(value))
            {
                marks.Append(letter);
            }
        }

        static void Token(StringBuilder marks, string token)
        {
            if (marks.Length > 0)
            {
                marks.Append(' ');
            }

            marks.Append(token);
        }
    }
}
