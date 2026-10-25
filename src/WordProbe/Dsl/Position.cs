// Where in the document a step acts, as the scenarios write it:
//
//   position := story? target
//   story    := 'HDR:' | 'FTR:' | 'FN' n ':' | 'EN' n ':' | 'CMT' n ':' | 'TB' n ':'
//   target   := 'P' n (':' offset)?                       paragraph n of the story, 1-based
//             | ('T' n '.')? 'C(' r ',' c ')' (':' offset)? cell of table n (table 1 when omitted)
//             | 'T' n | 'R' n | 'COL' n                    whole table, row, column (of table 1; 'T2.R1' for another)
//             | 'IMG' n | 'SHP' n                          inline shape n, shape n
//             | 'DOC:start' | 'DOC:end'
//             | 'below P' n                                blank page area under the paragraph (mouse only)
//   offset   := int                                       0-based character offset from the paragraph (or cell) start
//             | 'start' | 'end' | 'mark'                   end = before the paragraph/cell mark; mark = the mark itself
//             | 'word(' n ')' | 'wordend(' n ')'           Range.Words(n).Start / .End, Word's own word boundaries
//             | 'find("text")' | 'find("text"):end'        IndexOf in the paragraph's text, field codes included
//   range    := position ('-' position)?                   P1:2-7 is shorthand for P1:2-P1:7
enum StoryKind
{
    Main,
    Header,
    Footer,
    Footnote,
    Endnote,
    Comment,
    TextBox
}

record Story(StoryKind Kind, int Index = 1)
{
    public static Story Main { get; } = new(StoryKind.Main);

    public override string ToString() =>
        Kind switch
        {
            StoryKind.Main => "",
            StoryKind.Header => "HDR:",
            StoryKind.Footer => "FTR:",
            StoryKind.Footnote => $"FN{Index}:",
            StoryKind.Endnote => $"EN{Index}:",
            StoryKind.Comment => $"CMT{Index}:",
            _ => $"TB{Index}:"
        };
}

enum OffsetKind
{
    Index,
    Start,
    End,
    Mark,
    Word,
    WordEnd,
    Find,
    FindEnd
}

record Offset(OffsetKind Kind, int Value = 0, string? Text = null)
{
    public static Offset Start { get; } = new(OffsetKind.Start);

    public static Offset End { get; } = new(OffsetKind.End);

    public override string ToString() =>
        Kind switch
        {
            OffsetKind.Index => Value.ToString(CultureInfo.InvariantCulture),
            OffsetKind.Start => "start",
            OffsetKind.End => "end",
            OffsetKind.Mark => "mark",
            OffsetKind.Word => $"word({Value})",
            OffsetKind.WordEnd => $"wordend({Value})",
            OffsetKind.Find => $"find(\"{Text}\")",
            _ => $"find(\"{Text}\"):end"
        };
}

abstract record Position
{
    public abstract string Text { get; }

    public override string ToString() =>
        Text;
}

record ParagraphPosition(Story Story, int Paragraph, Offset Offset) : Position
{
    public override string Text => $"{Story}P{Paragraph}:{Offset}";
}

record CellPosition(int Table, int Row, int Column, Offset Offset) : Position
{
    public override string Text => $"{(Table == 1 ? "" : $"T{Table}.")}C({Row},{Column}):{Offset}";
}

record TablePosition(int Table) : Position
{
    public override string Text => $"T{Table}";
}

record RowPosition(int Table, int Row) : Position
{
    public override string Text => $"{(Table == 1 ? "" : $"T{Table}.")}R{Row}";
}

record ColumnPosition(int Table, int Column) : Position
{
    public override string Text => $"{(Table == 1 ? "" : $"T{Table}.")}COL{Column}";
}

record InlineShapePosition(int Index) : Position
{
    public override string Text => $"IMG{Index}";
}

record ShapePosition(int Index) : Position
{
    public override string Text => $"SHP{Index}";
}

record DocumentEdgePosition(bool AtEnd) : Position
{
    public override string Text => AtEnd ? "DOC:end" : "DOC:start";
}

record BelowPosition(int Paragraph) : Position
{
    public override string Text => $"below P{Paragraph}";
}

record PositionRange(Position From, Position? To)
{
    public bool IsCollapsed => To == null;

    public override string ToString() =>
        To == null ? From.Text : $"{From.Text}-{To.Text}";
}

static class PositionParser
{
    static readonly Regex story = new(@"^(?<story>HDR:|FTR:|FN(?<fn>\d+):|EN(?<en>\d+):|CMT(?<cmt>\d+):|TB(?<tb>\d+):)?(?<rest>.+)$", RegexOptions.Compiled);
    static readonly Regex paragraph = new(@"^P(?<n>\d+)(?::(?<offset>.+))?$", RegexOptions.Compiled);
    static readonly Regex cell = new(@"^(?:T(?<t>\d+)\.)?C\((?<r>\d+),\s*(?<c>\d+)\)(?::(?<offset>.+))?$", RegexOptions.Compiled);
    static readonly Regex table = new(@"^T(?<t>\d+)$", RegexOptions.Compiled);
    static readonly Regex row = new(@"^(?:T(?<t>\d+)\.)?R(?<r>\d+)$", RegexOptions.Compiled);
    static readonly Regex column = new(@"^(?:T(?<t>\d+)\.)?COL(?<c>\d+)$", RegexOptions.Compiled);
    static readonly Regex inlineShape = new(@"^IMG(?<n>\d+)$", RegexOptions.Compiled);
    static readonly Regex shape = new(@"^SHP(?<n>\d+)$", RegexOptions.Compiled);
    static readonly Regex below = new(@"^below\s+P(?<n>\d+)$", RegexOptions.Compiled);
    static readonly Regex word = new(@"^word\((?<n>\d+)\)$", RegexOptions.Compiled);
    static readonly Regex wordEnd = new(@"^wordend\((?<n>\d+)\)$", RegexOptions.Compiled);
    static readonly Regex find = new(@"^find\(""(?<text>[^""]*)""\)(?<end>:end)?$", RegexOptions.Compiled);

    public static Position Parse(string text)
    {
        text = text.Trim();
        var storyMatch = story.Match(text);
        var storyValue = Story.Main;
        var rest = text;
        if (storyMatch.Success &&
            storyMatch.Groups["story"].Success)
        {
            var token = storyMatch.Groups["story"].Value;
            storyValue = token switch
            {
                "HDR:" => new(StoryKind.Header),
                "FTR:" => new(StoryKind.Footer),
                _ when token.StartsWith("FN", StringComparison.Ordinal) => new(StoryKind.Footnote, Number(storyMatch, "fn")),
                _ when token.StartsWith("EN", StringComparison.Ordinal) => new(StoryKind.Endnote, Number(storyMatch, "en")),
                _ when token.StartsWith("CMT", StringComparison.Ordinal) => new(StoryKind.Comment, Number(storyMatch, "cmt")),
                _ => new(StoryKind.TextBox, Number(storyMatch, "tb"))
            };
            rest = storyMatch.Groups["rest"].Value;
        }

        if (rest == "DOC:start")
        {
            return new DocumentEdgePosition(false);
        }

        if (rest == "DOC:end")
        {
            return new DocumentEdgePosition(true);
        }

        var match = paragraph.Match(rest);
        if (match.Success)
        {
            return new ParagraphPosition(storyValue, Number(match, "n"), ParseOffset(match.Groups["offset"]));
        }

        match = cell.Match(rest);
        if (match.Success)
        {
            return new CellPosition(TableNumber(match), Number(match, "r"), Number(match, "c"), ParseOffset(match.Groups["offset"]));
        }

        match = table.Match(rest);
        if (match.Success)
        {
            return new TablePosition(Number(match, "t"));
        }

        match = row.Match(rest);
        if (match.Success)
        {
            return new RowPosition(TableNumber(match), Number(match, "r"));
        }

        match = column.Match(rest);
        if (match.Success)
        {
            return new ColumnPosition(TableNumber(match), Number(match, "c"));
        }

        match = inlineShape.Match(rest);
        if (match.Success)
        {
            return new InlineShapePosition(Number(match, "n"));
        }

        match = shape.Match(rest);
        if (match.Success)
        {
            return new ShapePosition(Number(match, "n"));
        }

        match = below.Match(rest);
        if (match.Success)
        {
            return new BelowPosition(Number(match, "n"));
        }

        throw new ArgumentException($"Unrecognised position '{text}'.");
    }

    /// <summary>"P1:2-7" or "P1:2-P2:4"; a lone position is a collapsed range.</summary>
    public static PositionRange ParseRange(string text)
    {
        text = text.Trim();
        var arrow = text.IndexOf('-', StringComparison.Ordinal);
        // A '-' inside find("...") or a story prefix is not a range separator: find the one outside quotes/parens.
        var depth = 0;
        var quoted = false;
        arrow = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && character == '(')
            {
                depth++;
            }
            else if (!quoted && character == ')')
            {
                depth--;
            }
            else if (!quoted && depth == 0 && character == '-')
            {
                arrow = i;
                break;
            }
        }

        if (arrow < 0)
        {
            return new(Parse(text), null);
        }

        var from = Parse(text[..arrow]);
        var toText = text[(arrow + 1)..].Trim();
        Position to;
        if (Regex.IsMatch(toText, @"^(\d+|start|end|mark|word\(\d+\)|wordend\(\d+\)|find\("".*""\)(:end)?)$"))
        {
            // Offset only: same paragraph or cell as 'from'.
            var offset = ParseOffsetText(toText);
            to = from switch
            {
                ParagraphPosition p => p with
                {
                    Offset = offset
                },
                CellPosition c => c with
                {
                    Offset = offset
                },
                _ => throw new ArgumentException($"A bare offset after '-' needs a paragraph or cell before it: '{text}'.")
            };
        }
        else
        {
            to = Parse(toText);
        }

        return new(from, to);
    }

    static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    static int TableNumber(Match match) =>
        match.Groups["t"].Success ? Number(match, "t") : 1;

    static Offset ParseOffset(Group group) =>
        group.Success ? ParseOffsetText(group.Value) : Offset.Start;

    public static Offset ParseOffsetText(string text)
    {
        text = text.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return new(OffsetKind.Index, index);
        }

        switch (text)
        {
            case "start":
                return Offset.Start;
            case "end":
                return Offset.End;
            case "mark":
                return new(OffsetKind.Mark);
        }

        var match = word.Match(text);
        if (match.Success)
        {
            return new(OffsetKind.Word, Number(match, "n"));
        }

        match = wordEnd.Match(text);
        if (match.Success)
        {
            return new(OffsetKind.WordEnd, Number(match, "n"));
        }

        match = find.Match(text);
        if (match.Success)
        {
            return new(match.Groups["end"].Success ? OffsetKind.FindEnd : OffsetKind.Find, 0, match.Groups["text"].Value);
        }

        throw new ArgumentException($"Unrecognised offset '{text}'.");
    }
}
