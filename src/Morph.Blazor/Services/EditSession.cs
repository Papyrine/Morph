using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

/// <summary>
/// One paragraph open for editing in place: what the script is given to show — the paragraph's text
/// in its runs, formatted as the page drew it, in a box the size of the one the text is set in — and
/// the reading of what the script hands back when the reader is done.
///
/// The text comes from the file (<see cref="EditParagraph"/>) and the formatting from the layout
/// (<see cref="EditMap.Format"/>), because each is the authority on its own: the edit is made to the
/// file, and has to be worked out against exactly what the file says, while how a run looks is
/// settled by styles the file only names. A run the layout never drew — hidden text — is kept in its
/// place, unseen.
///
/// <para>To the script (<c>v</c> 1), lengths in points:</para>
/// <code>
/// {"v":1,"id":3,"page":0,"x":72,"y":80,"w":451.3,"h":29.2,"skip":0,"indent":0,"align":0,
///  "line":14.6,"base":11.4,"size":11,"face":0,"color":"000000","fill":null,"from":5,"to":5,
///  "previous":true,"next":true,
///  "u":[{"k":0,"t":"Hello ","z":11,"f":1,"c":"000000","a":5.9},{"k":1,"s":"note","t":"1","z":11,"v":1},…]}
/// </code>
/// A unit's <c>k</c> is 0 for text to edit, 1 for something shown and left alone (<c>s</c> says
/// what: deleted, field, note, drawing, break) and 2 for something unseen; <c>f</c> is the
/// <see cref="Face"/> bits, and <c>a</c> the room a character of it took on the page
/// (<see cref="EditMap.Advance"/>), which the script spaces its own text out to. <c>from</c> and <c>to</c> are where the selection starts and ends, in
/// places of the paragraph (<see cref="EditParagraph.Length"/>).
///
/// <para>From the script, a paragraph to each the one has become:</para>
/// <code>[{"a":null,"i":[[0,"Hello, ",1],[1],[2,"world",0]]},{"a":2,"i":[[-1,"New",0]]}]</code>
/// An item is a unit's index with its text and face, or the index alone of a unit that is not text.
/// </summary>
sealed class EditSession
{
    const string revisionColor = "D13438";

    static readonly JsonWriterOptions writerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>How text looks, as bits: what the toolbar's buttons turn on and off, and what only shows.</summary>
    [Flags]
    public enum Face
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Underline = 4,
        Strike = 8,
        Caps = 16,
        SmallCaps = 32
    }

    const Face editable = Face.Bold | Face.Italic | Face.Underline | Face.Strike;

    readonly Face[] faces;
    readonly Face markFace;

    public EditSession(int id, EditParagraph paragraph, EditBlock block, EditMap map)
    {
        Id = id;
        Paragraph = paragraph;
        Block = block;
        Map = map;
        faces = paragraph.Units.Select(_ => FaceOf(map.Format(_.Run))).ToArray();
        markFace = FaceOf(block.Element.Properties.ParagraphMarkRunProperties);
    }

    public int Id { get; }

    public EditParagraph Paragraph { get; }

    public EditBlock Block { get; }

    EditMap Map { get; }

    static Face FaceOf(RunProperties? properties)
    {
        var face = Face.None;
        if (properties == null)
        {
            return face;
        }

        if (properties.Bold)
        {
            face |= Face.Bold;
        }

        if (properties.Italic)
        {
            face |= Face.Italic;
        }

        if (properties.Underline)
        {
            face |= Face.Underline;
        }

        if (properties.Strikethrough)
        {
            face |= Face.Strike;
        }

        if (properties.AllCaps)
        {
            face |= Face.Caps;
        }

        if (properties.SmallCaps)
        {
            face |= Face.SmallCaps;
        }

        return face;
    }

    /// <summary>What the script shows, with the selection from one place of the paragraph to another.</summary>
    public string Json(int from, int to)
    {
        var block = Block;
        var properties = block.Element.Properties;
        var mark = properties.ParagraphMarkRunProperties;
        var first = block.Lines[0];

        // A block that holds the rest of a paragraph shows its own lines: the ones on the pages
        // before are scrolled out of sight above it.
        var skip = 0f;
        foreach (var earlier in Map.Blocks(block.Paragraph))
        {
            if (ReferenceEquals(earlier, block))
            {
                break;
            }

            skip += earlier.Height;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, writerOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteNumber("id", Id);
            json.WriteNumber("page", block.Page);
            Number(json, "x", block.Left);
            Number(json, "y", block.Top);
            Number(json, "w", block.Width);
            Number(json, "h", block.Height);
            Number(json, "skip", skip);
            Number(json, "indent", block.Indent);
            json.WriteNumber("align", (int) properties.Alignment);
            Number(json, "line", first.Height);
            Number(json, "base", first.Baseline - first.Y);
            Number(json, "size", mark?.FontSizePoints ?? properties.ParagraphMarkFontSizePoints ?? Size());
            json.WriteNumber("face", (int) markFace);
            json.WriteString("color", Color(mark));
            if (block.Fill is { } fill)
            {
                json.WriteString("fill", fill);
            }
            else
            {
                json.WriteNull("fill");
            }

            json.WriteNumber("from", Math.Clamp(from, 0, Paragraph.Length));
            json.WriteNumber("to", Math.Clamp(to, 0, Paragraph.Length));
            json.WriteBoolean("previous", Paragraph.HasPrevious);
            json.WriteBoolean("next", Paragraph.HasNext);
            json.WriteStartArray("u");
            for (var index = 0; index < Paragraph.Units.Count; index++)
            {
                Unit(json, index);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The size of the text around, for what has none of its own to go by.
    double Size()
    {
        foreach (var unit in Paragraph.Units)
        {
            if (Map.Format(unit.Run) is { } format)
            {
                return format.FontSizePoints;
            }
        }

        return 11;
    }

    static string Color(RunProperties? properties) =>
        properties?.ColorHex ?? "000000";

    void Unit(Utf8JsonWriter json, int index)
    {
        var unit = Paragraph.Units[index];
        var format = Map.Format(unit.Run);
        json.WriteStartObject();
        if (unit.Editable &&
            format == null)
        {
            // Text the layout did not draw.
            json.WriteNumber("k", 2);
            json.WriteEndObject();
            return;
        }

        json.WriteNumber("k", unit.Editable ? 0 : 1);
        json.WriteString("t", Shown(unit));
        Number(json, "z", format?.FontSizePoints ?? Size());
        var face = faces[index];
        var color = Color(format);
        switch (unit.Kind)
        {
            case EditUnitKind.Deleted:
                json.WriteString("s", "deleted");
                face |= Face.Strike;
                color = revisionColor;
                break;
            case EditUnitKind.Field:
                json.WriteString("s", "field");
                break;
            case EditUnitKind.Note:
                json.WriteString("s", "note");
                json.WriteNumber("v", 1);
                break;
            case EditUnitKind.Drawing:
                json.WriteString("s", "drawing");
                Number(json, "w", unit.WidthPoints);
                Number(json, "e", unit.HeightPoints);
                break;
            case EditUnitKind.Break:
                json.WriteString("s", "break");
                break;
        }

        json.WriteNumber("f", (int) face);
        json.WriteString("c", color);
        if (Map.Advance(unit.Run) is { } advance)
        {
            Number(json, "a", advance);
        }

        if (format?.BackgroundColorHex is { } highlight)
        {
            json.WriteString("g", highlight);
        }

        if (unit.Editable &&
            format is {VerticalAlignment: not VerticalRunAlignment.Baseline})
        {
            json.WriteNumber("v", format.VerticalAlignment == VerticalRunAlignment.Superscript ? 1 : 2);
        }

        json.WriteEndObject();
    }

    // What a unit shows. A note's reference shows its number, which is the layout's to know.
    string Shown(EditUnit unit)
    {
        if (unit.Kind != EditUnitKind.Note)
        {
            return unit.Text;
        }

        foreach (var run in Block.Element.Runs)
        {
            if (run.Source is {Atomic: true} source &&
                source.Run == unit.Run &&
                source.Start == unit.Start)
            {
                return run.Text;
            }
        }

        return "*";
    }

    static void Number(Utf8JsonWriter json, string name, double value) =>
        json.WriteNumber(name, Math.Round(value, 2));

    /// <summary>
    /// What the script handed back, as the paragraphs the one is to become; null when it is not
    /// what a session hands back, which is not worth more to the reader than the edit not being made.
    /// </summary>
    public IReadOnlyList<NewParagraph>? Read(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var paragraphs = new List<NewParagraph>();
            foreach (var paragraph in document.RootElement.EnumerateArray())
            {
                TextAlignment? alignment = null;
                if (paragraph.TryGetProperty("a", out var aligned) &&
                    aligned.ValueKind == JsonValueKind.Number &&
                    aligned.GetInt32() is >= 0 and <= 3 and var value)
                {
                    alignment = (TextAlignment) value;
                }

                var items = new List<NewItem>();
                foreach (var item in paragraph.GetProperty("i").EnumerateArray())
                {
                    var unit = item[0].GetInt32();
                    if (item.GetArrayLength() == 1)
                    {
                        items.Add(new(unit, null));
                        continue;
                    }

                    if (unit >= Paragraph.Units.Count ||
                        (unit >= 0 && !Paragraph.Units[unit].Editable))
                    {
                        return null;
                    }

                    var text = item[1].GetString() ?? "";
                    if (text.Length > 0)
                    {
                        items.Add(new(Math.Max(unit, -1), text, Changed(unit, (Face) item[2].GetInt32())));
                    }
                }

                paragraphs.Add(new(items, alignment));
            }

            if (paragraphs.Count == 0)
            {
                return null;
            }

            return paragraphs;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or FormatException)
        {
            return null;
        }
    }

    // What the reader turned on or off, against how the text it was typed in looked.
    RunFormat Changed(int unit, Face face)
    {
        var was = unit >= 0 ? faces[unit] : markFace;
        var differs = (was ^ face) & editable;
        return new(
            Bold: Set(Face.Bold),
            Italic: Set(Face.Italic),
            Underline: Set(Face.Underline),
            Strike: Set(Face.Strike));

        bool? Set(Face bit)
        {
            if ((differs & bit) == 0)
            {
                return null;
            }

            return (face & bit) != 0;
        }
    }
}
