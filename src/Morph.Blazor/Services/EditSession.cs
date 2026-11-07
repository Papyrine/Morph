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
/// {"v":1,"id":3,"p":12,"page":0,"x":72,"y":80,"w":451.3,"h":29.2,"skip":0,"indent":0,"align":0,
///  "tabs":{"o":72,"d":36,"l":0,"e":451.3,"s":[[432,2,1]]},
///  "line":14.6,"base":11.4,"size":11,"face":0,"font":"Aptos","color":"000000","fill":null,"from":5,"to":5,
///  "previous":true,"next":true,
///  "u":[{"k":0,"t":"Hello ","z":11,"n":"Aptos","f":1,"c":"000000","a":5.9},{"k":1,"s":"note","t":"1","z":11,"v":1},…]}
/// </code>
/// <c>p</c> is the paragraph's ordinal, which the script names the place a ribbon command acts at in
/// (<see cref="ViewerHandle.EditTargetAsync"/>). A unit's <c>k</c> is 0 for text to edit, 1 for
/// something shown and left alone (<c>s</c> says what: deleted, field, note, drawing, break) and 2
/// for something unseen; <c>f</c> is the
/// <see cref="Face"/> bits, and <c>a</c> the room a character of it took on the page
/// (<see cref="EditMap.Advance"/>), which the script spaces its own text out to; <c>l</c> is the address
/// of the link the text is (<see cref="EditMap.Link"/>, "#name" for a bookmark). A drawing in the line
/// carries its size (<c>w</c>, <c>e</c>) and, where the layout drew a picture, the picture as a data
/// URL (<c>img</c>). Under tracking, text the tracking author inserted carries <c>o</c>. <c>from</c> and <c>to</c> are where the selection starts and ends, in
/// places of the paragraph (<see cref="EditParagraph.Length"/>).
///
/// <para>From the script, a paragraph to each the one has become:</para>
/// <code>[{"a":null,"i":[[0,"Hello, ",1],[1],[2,"world",0]]},{"a":2,"i":[[-1,"New",0]]}]</code>
/// An item is a unit's index with its text and face, or the index alone of a unit that is not text.
/// Under tracking a text item carries a fourth number: 1 for the unit's text struck where it stands,
/// 2 for text typed (<see cref="NewItem.Struck"/>, <see cref="NewItem.Typed"/>).
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

    const Face editable = Face.Bold |
                          Face.Italic |
                          Face.Underline |
                          Face.Strike;

    Face[] faces;
    Face markFace;
    Dictionary<int, string> fieldsDrawn;

    public EditSession(int id, EditParagraph paragraph, EditBlock block, EditMap map)
    {
        Id = id;
        Paragraph = paragraph;
        Block = block;
        Map = map;
        faces = paragraph.Units.Select(_ => FaceOf(map.Format(_.Run))).ToArray();
        markFace = FaceOf(block.Element.Properties.ParagraphMarkRunProperties);
        fieldsDrawn = DrawnFields(paragraph, block.Element);
    }

    // What each page field of the paragraph read where the block was drawn, by its unit's index. The
    // layout works a page number or a page count out for the page it draws on, and the file holds only
    // what the field read when it was last updated, so a footer opened on page 2 would otherwise show
    // page 1's number. The parser makes each page field one run with no source of its own, in its
    // place among the runs that have one, and the layout's copy of the paragraph holds the same runs
    // with the page's text in theirs (ParagraphElement.RebuiltFrom). A field whose result the file
    // and the parsed run do not agree on — one inside another field — is left showing the file's.
    static Dictionary<int, string> DrawnFields(EditParagraph paragraph, ParagraphElement element)
    {
        var drawn = new Dictionary<int, string>();
        if (element.RebuiltFrom is not {Runs: var parsed} ||
            parsed.Count != element.Runs.Count)
        {
            return drawn;
        }

        var fields = paragraph.Units
            .Select((unit, index) => (Unit: unit, Index: index))
            .Where(_ => _.Unit.Kind == EditUnitKind.Field)
            .ToList();
        var next = 0;
        for (var index = 0; index < element.Runs.Count; index++)
        {
            var run = element.Runs[index];
            if (run.Source is { } source)
            {
                // Text from a place at or after a field's beginning: that field is passed.
                while (next < fields.Count &&
                       (fields[next].Unit.Run < source.Run ||
                        (fields[next].Unit.Run == source.Run &&
                         fields[next].Unit.Start <= source.Start)))
                {
                    next++;
                }

                continue;
            }

            if (parsed[index].PageField == PageFieldKind.None ||
                next >= fields.Count)
            {
                continue;
            }

            var (unit, at) = fields[next++];
            if (parsed[index].Text == unit.Text)
            {
                drawn[at] = run.Text;
            }
        }

        return drawn;
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

    /// <summary>
    /// What the script shows, with the selection from one place of the paragraph to another. Under
    /// tracking, <paramref name="author"/> is who the edit is tracked as: text that author inserted is
    /// marked (<c>o</c>), since deleting it takes it out rather than striking it (WordProbe R-R-1, R-R-6).
    /// </summary>
    public string Json(int from, int to, bool tracked = false, string? author = null)
    {
        var properties = Block.Element.Properties;
        var mark = properties.ParagraphMarkRunProperties;
        var first = Block.Lines[0];

        // A block that holds the rest of a paragraph shows its own lines: the ones on the pages
        // before are scrolled out of sight above it.
        // A header or footer is the whole paragraph again on every page: a block that starts with the
        // paragraph's first line starts it afresh.
        var skip = 0f;
        foreach (var earlier in Map.Blocks(Block.Paragraph))
        {
            if (ReferenceEquals(earlier, Block))
            {
                break;
            }

            if (earlier.Lines[0].LineIndex == 0)
            {
                skip = 0;
            }

            skip += earlier.Height;
        }

        if (first.LineIndex == 0)
        {
            skip = 0;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, writerOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteNumber("id", Id);
            json.WriteNumber("p", Paragraph.Ordinal);
            json.WriteNumber("page", Block.Page);
            Number(json, "x", Block.Left);
            Number(json, "y", Block.Top);
            Number(json, "w", Block.Width);
            Number(json, "h", Block.Height);
            Number(json, "skip", skip);
            Number(json, "indent", Block.Indent);
            json.WriteNumber("align", (int) properties.Alignment);
            Tabs(json, properties);
            Number(json, "line", first.Height);
            Number(json, "base", first.Baseline - first.Y);
            Number(json, "size", mark?.FontSizePoints ?? properties.ParagraphMarkFontSizePoints ?? Size());
            json.WriteNumber("face", (int) markFace);
            json.WriteString("font", mark?.FontFamily ?? Font());
            json.WriteString("color", Color(mark));
            if (Block.Fill is { } fill)
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
            // A story's first and last paragraph: the arrow keys go no further (WordProbe R-N-1, R-N-2).
            if (Paragraph.PreviousInStory == null)
            {
                json.WriteBoolean("first", true);
            }

            if (Paragraph.NextInStory == null)
            {
                json.WriteBoolean("last", true);
            }
            if (Paragraph.ListLevel is { } level)
            {
                json.WriteNumber("list", level);
            }

            // Overtype types over nothing under Track Changes (WordProbe E-11).
            if (tracked)
            {
                json.WriteBoolean("tracked", true);
            }

            // Ctrl+A in a header, a footer or a note selects that story's text (WordProbe H-07, H-09).
            if (Paragraph.Part != ReviewPart.Body)
            {
                json.WriteBoolean("story", true);
            }

            // F8 steps from a word to the cell rather than the sentence (WordProbe S-10).
            if (Block.InCell)
            {
                json.WriteBoolean("cell", true);
            }

            if (Paragraph.NextStyle != null)
            {
                json.WriteBoolean("follow", true);
            }

            if (Paragraph.Indented)
            {
                json.WriteBoolean("indented", true);
            }
            json.WriteStartArray("u");
            for (var index = 0; index < Paragraph.Units.Count; index++)
            {
                Unit(json, index, tracked ? author : null);
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

    // The face of the text around, for what has none of its own to go by.
    string Font()
    {
        foreach (var unit in Paragraph.Units)
        {
            if (Map.Format(unit.Run) is { } format)
            {
                return format.FontFamily;
            }
        }

        return DefaultFontSettings.DefaultFont;
    }

    static string Color(RunProperties? properties) =>
        properties?.ColorHex ?? "000000";

    void Unit(Utf8JsonWriter json, int index, string? author)
    {
        var unit = Paragraph.Units[index];
        var format = Map.Format(unit.Run);
        json.WriteStartObject();
        json.WriteNumber("k", unit.Editable ? 0 : 1);
        if (author != null &&
            unit.Editable &&
            string.Equals(unit.InsertedBy, author, StringComparison.Ordinal))
        {
            json.WriteNumber("o", 1);
        }

        if (unit.Editable &&
            format == null)
        {
            // Text the layout did not draw — hidden text, or a run a wrap left nothing of — is text all
            // the same: it is typed over and handed back as text, so that what the file holds and what
            // the script holds count the same places. The box shows it as Word shows hidden text.
            json.WriteNumber("h", 1);
        }
        json.WriteString("t", Shown(unit, index));
        Number(json, "z", format?.FontSizePoints ?? Size());
        json.WriteString("n", format?.FontFamily ?? Font());
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
                if (Map.Picture(unit.Run, unit.Start)
                    is
                    {
                        InlineImageData: { } data,
                        InlineImageContentType: { } type
                    })
                {
                    // The picture itself, so the box shows it where the page did rather than a stand-in.
                    json.WriteString("img", $"data:{type};base64,{Convert.ToBase64String(data)}");
                }

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

        // The address of the link the text is, which Ctrl+click follows: "#name" for a bookmark.
        if (unit.Linked &&
            Map.Link(unit.Run) is { } address)
        {
            json.WriteString("l", address);
        }

        if (unit.Editable &&
            format is {VerticalAlignment: not VerticalRunAlignment.Baseline})
        {
            json.WriteNumber("v", format.VerticalAlignment == VerticalRunAlignment.Superscript ? 1 : 2);
        }

        json.WriteEndObject();
    }

    // What a unit shows. A note's reference shows its number, and a page field the page's number or
    // count, which are the layout's to know.
    string Shown(EditUnit unit, int index)
    {
        if (unit.Kind == EditUnitKind.Field)
        {
            return fieldsDrawn.GetValueOrDefault(index, unit.Text);
        }

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

    // Where the paragraph's tabs stop, for the box to set them where the page did (morph-edit.js,
    // layoutTabs): "o" the page's x the stops count from — the margin, or the cell's content edge —
    // "d" the document's default interval, "l" the left indent, "e" the edge a right, centred or
    // decimal stop past it is pulled back to (TabStopResolver), and "s" each stop as its position,
    // its alignment (0 left, 1 centre, 2 right, 3 decimal) and its leader (0 none, 1 dots, 2
    // hyphens, 3 a line, 4 middle dots, 5 a heavy line). A bar tab draws a rule and stops nothing.
    void Tabs(Utf8JsonWriter json, ParagraphProperties properties)
    {
        var origin = Block.Left - properties.LeftIndentPoints;
        json.WriteStartObject("tabs");
        Number(json, "o", origin);
        Number(json, "d", properties.DefaultTabStopPoints);
        Number(json, "l", properties.LeftIndentPoints);
        Number(json, "e", Block.Right + properties.RightIndentPoints - origin);
        json.WriteStartArray("s");
        foreach (var stop in properties.TabStops
                     .Where(_ => _.Alignment is not (TabAlignment.Bar or TabAlignment.Clear))
                     .OrderBy(_ => _.PositionPoints))
        {
            json.WriteStartArray();
            json.WriteNumberValue(Math.Round(stop.PositionPoints, 2));
            json.WriteNumberValue(stop.Alignment switch
            {
                TabAlignment.Center => 1,
                TabAlignment.Right => 2,
                TabAlignment.Decimal => 3,
                _ => 0
            });
            json.WriteNumberValue((int) stop.Leader);
            json.WriteEndArray();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

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
                        (unit >= 0 &&
                         !Paragraph.Units[unit].Editable))
                    {
                        return null;
                    }

                    var text = item[1].GetString() ?? "";
                    // Under tracking the script says what it struck (1) and what was typed (2).
                    var said = item.GetArrayLength() > 3 ? item[3].GetInt32() : 0;
                    if (text.Length > 0)
                    {
                        items.Add(new(Math.Max(unit, -1), text, Changed(unit, (Face) item[2].GetInt32()), said == 1, said == 2));
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
        catch (Exception exception)
            when (exception is
                      JsonException or
                      InvalidOperationException or
                      KeyNotFoundException or
                      IndexOutOfRangeException or
                      FormatException)
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
