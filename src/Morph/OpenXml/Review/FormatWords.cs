using OoxmlTabStop = DocumentFormat.OpenXml.Wordprocessing.TabStop;

/// <summary>
/// What a tracked formatting change did, in Word's words: the text of its revision balloon after
/// "Formatted: " — "Font: Bold, Italic", "Centered, Space After: 12 pt", "Heading 1".
///
/// <para>The wording, the order the items come in, the colour names and the units are Word's own,
/// read over COM from <c>Revision.FormatDescription</c> (Word 16.0.20527.20150, 2026-11-07) for each
/// property changed alone and for many changed together; lengths are in centimetres, as the editor's
/// own dialogs give them. Word writes two spaces after some colons ("Space After:  12 pt") and leaves
/// a space before a comma once ("Condensed by , Not Highlight"); a page collapses the first, and the
/// second is left out here.</para>
///
/// <para>A change describes what its own markup says: the old properties it records against the
/// ones the run or paragraph now has. A font or a size taken off, which Word describes as the one the
/// style then gives ("Font: 11 pt"), is left out rather than worked out from the styles.</para>
/// </summary>
sealed class FormatWords
{
    static readonly CultureInfo invariant = CultureInfo.InvariantCulture;

    // Word's names for the colours it names; any other is a custom colour.
    static readonly Dictionary<string, string> colorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["000000"] = "Black",
        ["993300"] = "Brown",
        ["333300"] = "Olive Green",
        ["003300"] = "Dark Green",
        ["003366"] = "Dark Teal",
        ["000080"] = "Dark Blue",
        ["333399"] = "Indigo",
        ["333333"] = "Dark Gray",
        ["800000"] = "Dark Red",
        ["FF6600"] = "Orange",
        ["808000"] = "Dark Yellow",
        ["008000"] = "Green",
        ["008080"] = "Teal",
        ["0000FF"] = "Blue",
        ["666699"] = "Blue-Gray",
        ["808080"] = "Gray",
        ["FF0000"] = "Red",
        ["FF9900"] = "Light Orange",
        ["99CC00"] = "Lime",
        ["339966"] = "Sea Green",
        ["33CCCC"] = "Aqua",
        ["3366FF"] = "Light Blue",
        ["800080"] = "Violet",
        ["969696"] = "Gray",
        ["FF00FF"] = "Pink",
        ["FFCC00"] = "Gold",
        ["FFFF00"] = "Yellow",
        ["00FF00"] = "Bright Green",
        ["00FFFF"] = "Turquoise",
        ["00CCFF"] = "Sky Blue",
        ["993366"] = "Plum",
        ["C0C0C0"] = "Light Gray",
        ["FF99CC"] = "Rose",
        ["FFCC99"] = "Tan",
        ["FFFF99"] = "Light Yellow",
        ["CCFFCC"] = "Light Green",
        ["CCFFFF"] = "Light Turquoise",
        ["99CCFF"] = "Pale Blue",
        ["CC99FF"] = "Lavender",
        ["FFFFFF"] = "White",
        ["404040"] = "Gray-75%",
        ["595959"] = "Gray-65%",
        ["A6A6A6"] = "Gray-35%",
        ["D9D9D9"] = "Gray-15%",
        ["C00000"] = "Dark Red",
        ["FFC000"] = "Orange",
        ["92D050"] = "Light Green",
        ["00B050"] = "Green",
        ["00B0F0"] = "Light Blue",
        ["0070C0"] = "Blue",
        ["002060"] = "Dark Blue",
        ["7030A0"] = "Purple"
    };

    // A theme's colour by its slot, whatever tint or shade is laid over it.
    static readonly Dictionary<string, string> themeColorNames = new(StringComparer.Ordinal)
    {
        ["dark1"] = "Text/Background 1",
        ["light1"] = "Text/Background 2",
        ["dark2"] = "Text/Background 3",
        ["light2"] = "Text/Background 4",
        ["accent1"] = "Accent 1",
        ["accent2"] = "Accent 2",
        ["accent3"] = "Accent 3",
        ["accent4"] = "Accent 4",
        ["accent5"] = "Accent 5",
        ["accent6"] = "Accent 6",
        ["hyperlink"] = "Hyperlink",
        ["followedHyperlink"] = "Followed Hyperlink",
        ["background1"] = "Background 1",
        ["text1"] = "Text 1",
        ["background2"] = "Background 2",
        ["text2"] = "Text 2"
    };

    static readonly Dictionary<string, string> underlineNames = new(StringComparer.Ordinal)
    {
        ["single"] = "Underline",
        ["words"] = "Word underline",
        ["double"] = "Double underline",
        ["thick"] = "Thick underline",
        ["dotted"] = "Dotted underline",
        ["dottedHeavy"] = "Dotted heavy underline",
        ["dash"] = "Dashed underline",
        ["dashedHeavy"] = "Dashed heavy underline",
        ["dashLong"] = "Dashed long underline",
        ["dashLongHeavy"] = "Dashed long heavy underline",
        ["dotDash"] = "Dot-dash underline",
        ["dashDotHeavy"] = "Dot-dash heavy underline",
        ["dotDotDash"] = "Dot-dot-dash underline",
        ["dashDotDotHeavy"] = "Dot-dot-dash heavy underline",
        ["wave"] = "Wave underline",
        ["wavyHeavy"] = "Wave Heavy underline",
        ["wavyDouble"] = "Wave double underline",
        ["none"] = "No underline"
    };

    readonly Dictionary<string, string> styleNames;
    readonly string defaultParagraphStyle;
    readonly string defaultCharacterStyle;
    readonly string? minorFont;
    readonly string? majorFont;
    readonly Numbering? numbering;

    FormatWords(Dictionary<string, string> styleNames, string defaultParagraphStyle, string defaultCharacterStyle, string? minorFont, string? majorFont, Numbering? numbering)
    {
        this.styleNames = styleNames;
        this.defaultParagraphStyle = defaultParagraphStyle;
        this.defaultCharacterStyle = defaultCharacterStyle;
        this.minorFont = minorFont;
        this.majorFont = majorFont;
        this.numbering = numbering;
    }

    /// <summary>What the document's changes are described by: its styles' names, its theme's fonts, its lists.</summary>
    public static FormatWords Read(WordprocessingDocument document)
    {
        var main = document.MainDocumentPart;
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var defaultParagraph = "Normal";
        var defaultCharacter = "Default Paragraph Font";
        foreach (var style in main?.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is not { } id)
            {
                continue;
            }

            var name = Shown(style.StyleName?.Val?.Value ?? id);
            names[id] = name;
            if (style.Default?.Value != true)
            {
                continue;
            }

            if (style.Type?.Value == StyleValues.Paragraph)
            {
                defaultParagraph = name;
            }
            else if (style.Type?.Value == StyleValues.Character)
            {
                defaultCharacter = name;
            }
        }

        var fonts = main?.ThemePart?.Theme?.ThemeElements?.FontScheme;
        return new(
            names,
            defaultParagraph,
            defaultCharacter,
            fonts?.MinorFont?.LatinFont?.Typeface?.Value,
            fonts?.MajorFont?.LatinFont?.Typeface?.Value,
            main?.NumberingDefinitionsPart?.Numbering);
    }

    // A built-in style is named in lower case in the file ("heading 1") and shown capitalised.
    static string Shown(string name) =>
        name.Any(char.IsUpper)
            ? name
            : string.Join(' ', name.Split(' ').Select(_ => _.Length > 0 ? char.ToUpperInvariant(_[0]) + _[1..] : _));

    /// <summary>What a <c>w:rPrChange</c> or <c>w:pPrChange</c> changed; null for anything else, or when it says nothing Word would.</summary>
    public string? Describe(OpenXmlElement change)
    {
        var items = change switch
        {
            RunPropertiesChange {Parent: { } now} => Character(change.GetFirstChild<PreviousRunProperties>(), now),
            ParagraphMarkRunPropertiesChange {Parent: { } now} => Character(change.GetFirstChild<PreviousParagraphMarkRunProperties>(), now),
            ParagraphPropertiesChange {Parent: { } now} => Paragraph(change.GetFirstChild<ParagraphPropertiesExtended>(), now),
            _ => []
        };

        return items.Count == 0 ? null : string.Join(", ", items);
    }

    // Word's order: the character style; the font, its size, bold and italic, the first of them
    // headed "Font:"; then the underline and its colour, the colour, the strikes, raised or lowered
    // text, the capitals, hidden text, the scale, the spacing, the position, kerning and highlight.
    List<string> Character(OpenXmlElement? was, OpenXmlElement now)
    {
        var items = new List<string>();
        if (Value<RunStyle>(was, _ => _.Val?.Value) is var wasStyle &&
            Value<RunStyle>(now, _ => _.Val?.Value) is var nowStyle &&
            wasStyle != nowStyle)
        {
            items.Add(nowStyle == null ? defaultCharacterStyle : styleNames.GetValueOrDefault(nowStyle, nowStyle));
        }

        var font = new List<string>();
        if (Font(now) is { } nowFont &&
            nowFont != Font(was))
        {
            font.Add(nowFont);
        }

        if (Value<FontSize>(now, _ => _.Val?.Value) is { } size &&
            size != Value<FontSize>(was, _ => _.Val?.Value) &&
            decimal.TryParse(size, NumberStyles.Number, invariant, out var halfPoints))
        {
            font.Add($"{Points(halfPoints / 2)} pt");
        }

        Toggled<Bold>(font, was, now, "Bold");
        Toggled<Italic>(font, was, now, "Italic");
        if (font.Count > 0)
        {
            font[0] = $"Font: {font[0]}";
            items.AddRange(font);
        }

        var wasUnderline = Value<Underline>(was, _ => _.Val?.InnerText) ?? "none";
        var nowUnderline = Value<Underline>(now, _ => _.Val?.InnerText) ?? "none";
        if (wasUnderline != nowUnderline)
        {
            items.Add(underlineNames.GetValueOrDefault(nowUnderline, "Underline"));
        }

        var wasLine = Value<Underline>(was, _ => _.Color?.Value);
        var nowLine = Value<Underline>(now, _ => _.Color?.Value);
        if (wasLine != nowLine)
        {
            items.Add($"Underline color: {ColorName(nowLine, null)}");
        }

        var wasColor = (Value<Color>(was, _ => _.Val?.Value), Value<Color>(was, _ => _.ThemeColor?.InnerText));
        var nowColor = (Value<Color>(now, _ => _.Val?.Value), Value<Color>(now, _ => _.ThemeColor?.InnerText));
        if (wasColor != nowColor)
        {
            items.Add($"Font color: {ColorName(nowColor.Item1, nowColor.Item2)}");
        }

        Toggled<Strike>(items, was, now, "Strikethrough");
        Toggled<DoubleStrike>(items, was, now, "Double strikethrough");
        var wasRaise = Value<VerticalTextAlignment>(was, _ => _.Val?.InnerText) ?? "baseline";
        var nowRaise = Value<VerticalTextAlignment>(now, _ => _.Val?.InnerText) ?? "baseline";
        if (wasRaise != nowRaise)
        {
            items.Add(nowRaise switch
            {
                "superscript" => "Superscript",
                "subscript" => "Subscript",
                _ => "Not Superscript/ Subscript"
            });
        }

        Toggled<SmallCaps>(items, was, now, "Small caps");
        Toggled<Caps>(items, was, now, "All caps");
        Toggled<Vanish>(items, was, now, "Hidden");
        var wasScale = Scale(was) ?? 100;
        var nowScale = Scale(now) ?? 100;
        if (wasScale != nowScale)
        {
            items.Add($"Character scale: {nowScale.ToString(invariant)}%");
        }

        var wasSpacing = Value<Spacing>(was, _ => _.Val?.Value) ?? 0;
        var nowSpacing = Value<Spacing>(now, _ => _.Val?.Value) ?? 0;
        if (wasSpacing != nowSpacing)
        {
            items.Add(nowSpacing switch
            {
                > 0 => $"Expanded by {Points(nowSpacing / 20m)} pt",
                < 0 => $"Condensed by {Points(-nowSpacing / 20m)} pt",
                _ => "Not Expanded by / Condensed by"
            });
        }

        var wasPosition = HalfPoints(Value<Position>(was, _ => _.Val?.Value));
        var nowPosition = HalfPoints(Value<Position>(now, _ => _.Val?.Value));
        if (wasPosition != nowPosition)
        {
            items.Add(nowPosition switch
            {
                > 0 => $"Raised by {Points(nowPosition / 2)} pt",
                < 0 => $"Lowered by {Points(-nowPosition / 2)} pt",
                _ => "Not Raised by / Lowered by"
            });
        }

        // Kerning turned off is a change Word has no words for.
        var nowKern = Value<Kern>(now, _ => _.Val?.Value) ?? 0;
        if (nowKern > 0 &&
            nowKern != (Value<Kern>(was, _ => _.Val?.Value) ?? 0))
        {
            items.Add($"Kern at {Points(nowKern / 2m)} pt");
        }

        var wasHighlight = Value<Highlight>(was, _ => _.Val?.InnerText) ?? "none";
        var nowHighlight = Value<Highlight>(now, _ => _.Val?.InnerText) ?? "none";
        if (wasHighlight != nowHighlight)
        {
            items.Add(nowHighlight == "none" ? "Not Highlight" : "Highlight");
        }

        return items;
    }

    // The face text is set in, as Word names it: one the run names "(Default) Arial", the theme's
    // "+Body (Aptos)". Null where the run names none.
    string? Font(OpenXmlElement? properties)
    {
        if (properties?.GetFirstChild<RunFonts>() is not { } fonts)
        {
            return null;
        }

        if ((fonts.AsciiTheme ?? fonts.HighAnsiTheme)?.InnerText is { } theme)
        {
            var major = theme.StartsWith("major", StringComparison.Ordinal);
            var name = major ? majorFont : minorFont;
            var slot = major ? "+Headings" : "+Body";
            return name == null ? slot : $"{slot} ({name})";
        }

        return (fonts.Ascii ?? fonts.HighAnsi)?.Value is { } face ? $"(Default) {face}" : null;
    }

    static string ColorName(string? value, string? theme)
    {
        if (theme != null &&
            themeColorNames.TryGetValue(theme, out var named))
        {
            return named;
        }

        if (value == null ||
            value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return "Auto";
        }

        if (colorNames.TryGetValue(value, out var name))
        {
            return name;
        }

        if (value.Length == 6 &&
            int.TryParse(value, NumberStyles.HexNumber, invariant, out var rgb))
        {
            return $"Custom Color(RGB({rgb >> 16},{(rgb >> 8) & 0xFF},{rgb & 0xFF}))";
        }

        return value;
    }

    // Word's order: the paragraph style, the alignment, the indents, the spacing before and after, the
    // line spacing, the list, the pagination switches and the tab stops.
    List<string> Paragraph(OpenXmlElement? was, OpenXmlElement now)
    {
        var items = new List<string>();
        var wasStyle = Value<ParagraphStyleId>(was, _ => _.Val?.Value);
        var nowStyle = Value<ParagraphStyleId>(now, _ => _.Val?.Value);
        if (wasStyle != nowStyle)
        {
            items.Add(nowStyle == null ? defaultParagraphStyle : styleNames.GetValueOrDefault(nowStyle, nowStyle));
        }

        var wasAlignment = Value<Justification>(was, _ => _.Val?.InnerText) ?? "left";
        var nowAlignment = Value<Justification>(now, _ => _.Val?.InnerText) ?? "left";
        if (wasAlignment != nowAlignment)
        {
            items.Add(nowAlignment switch
            {
                "center" => "Centered",
                "right" or "end" => "Right",
                "both" => "Justified",
                "distribute" => "Distributed",
                _ => "Left"
            });
        }

        // A list taken off takes its indents with it: Word says only that it is gone. A paragraph in a
        // list hangs as far as the list's level does, unless it says otherwise.
        var wasList = List(was?.GetFirstChild<NumberingProperties>());
        var nowList = List(now.GetFirstChild<NumberingProperties>());
        if (wasList == null ||
            nowList != null)
        {
            var listHanging = nowList is { } inList ? Twips(LevelOf(inList.Id, inList.Level)?.PreviousParagraphProperties?.Indentation?.Hanging?.Value) : 0;
            Indents(items, was?.GetFirstChild<Indentation>(), now.GetFirstChild<Indentation>(), listHanging);
        }

        Spacing(items, was?.GetFirstChild<SpacingBetweenLines>(), now.GetFirstChild<SpacingBetweenLines>());
        if (wasList != nowList)
        {
            items.Add(nowList is { } list ? ListWords(list.Id, list.Level) : "No bullets or numbering");
        }

        // Widow and orphan control is on unless a paragraph turns it off.
        var wasWidows = Toggle(was?.GetFirstChild<WidowControl>()) ?? true;
        var nowWidows = Toggle(now.GetFirstChild<WidowControl>()) ?? true;
        if (wasWidows != nowWidows)
        {
            items.Add(nowWidows ? "Widow/Orphan control" : "No widow/orphan control");
        }

        Switched<PageBreakBefore>(items, was, now, "Page break before", "No page break before");
        Switched<KeepNext>(items, was, now, "Keep with next", "Don't keep with next");
        Switched<KeepLines>(items, was, now, "Keep lines together", "Don't keep lines together");
        Tabs(items, was?.GetFirstChild<Tabs>(), now.GetFirstChild<Tabs>());
        return items;
    }

    // "Indent: Left: 1.27 cm, Hanging: 0.63 cm, Right: 1.27 cm": Left is where the first line starts
    // when it hangs, and only the left and first-line indents are headed "Indent:" — the right one
    // changed alone is "Right: 1.27 cm". An indent taken off is the paragraph back at 0. listHanging
    // is how far a list the paragraph is in hangs it, where it says nothing of its own first line.
    static void Indents(List<string> items, Indentation? was, Indentation? now, long listHanging)
    {
        var wasLeft = Twips(was?.Left?.Value ?? was?.Start?.Value);
        var nowLeft = Twips(now?.Left?.Value ?? now?.Start?.Value);
        var wasHanging = Twips(was?.Hanging?.Value);
        var nowHanging = Twips(now?.Hanging?.Value);
        var wasFirst = Twips(was?.FirstLine?.Value);
        var nowFirst = Twips(now?.FirstLine?.Value);
        var indent = new List<string>();
        if (wasLeft != nowLeft)
        {
            var hangs = now?.Hanging != null || now?.FirstLine != null ? nowHanging : listHanging;
            indent.Add($"Left: {Centimetres(nowLeft - hangs)} cm");
        }

        if (wasHanging != nowHanging ||
            wasFirst != nowFirst)
        {
            indent.Add(nowHanging > 0 ? $"Hanging: {Centimetres(nowHanging)} cm" : $"First line: {Centimetres(nowFirst)} cm");
        }

        if (indent.Count > 0)
        {
            indent[0] = $"Indent: {indent[0]}";
            items.AddRange(indent);
        }

        var wasRight = Twips(was?.Right?.Value ?? was?.End?.Value);
        var nowRight = Twips(now?.Right?.Value ?? now?.End?.Value);
        if (wasRight != nowRight)
        {
            items.Add($"Right: {Centimetres(nowRight)} cm");
        }
    }

    // "Space Before: 6 pt, After: 12 pt" and "Line spacing: 1.5 lines". Spacing the paragraph gives
    // up is the style's again, which only the styles know: what is still stated is described.
    static void Spacing(List<string> items, SpacingBetweenLines? was, SpacingBetweenLines? now)
    {
        var space = new List<string>();
        if (now?.Before?.Value is { } before &&
            before != was?.Before?.Value)
        {
            space.Add($"Before: {Points(Twips(before) / 20m)} pt");
        }

        if (now?.After?.Value is { } after &&
            after != was?.After?.Value)
        {
            space.Add($"After: {Points(Twips(after) / 20m)} pt");
        }

        if (space.Count > 0)
        {
            space[0] = $"Space {space[0]}";
            items.AddRange(space);
        }

        var nowRule = now?.LineRule?.InnerText ?? "auto";
        if (now?.Line?.Value is { } line &&
            (line != was?.Line?.Value || nowRule != (was.LineRule?.InnerText ?? "auto")))
        {
            var value = Twips(line);
            items.Add("Line spacing: " + nowRule switch
            {
                "exact" => $"Exactly {Points(value / 20m)} pt",
                "atLeast" => $"At least {Points(value / 20m)} pt",
                _ => (value / 240m) switch
                {
                    1 => "single",
                    1.5m => "1.5 lines",
                    2 => "Double",
                    var multiple => $"Multiple {Points(multiple)} li"
                }
            });
        }
    }

    static (int Id, int Level)? List(NumberingProperties? properties)
    {
        if (properties?.NumberingId?.Val?.Value is not { } id ||
            id == 0)
        {
            return null;
        }

        return (id, properties.NumberingLevelReference?.Val?.Value ?? 0);
    }

    NumberingInstance? Instance(int id) =>
        numbering?.Elements<NumberingInstance>().FirstOrDefault(_ => _.NumberID?.Value == id);

    // A list's level: the list's own override of it, or its definition's.
    Level? LevelOf(int id, int level)
    {
        var instance = Instance(id);
        var abstractId = instance?.AbstractNumId?.Val?.Value;
        return instance?.Elements<LevelOverride>().FirstOrDefault(_ => _.LevelIndex?.Value == level)?.Level ??
               numbering?.Elements<AbstractNum>()
                   .FirstOrDefault(_ => _.AbstractNumberId?.Value == abstractId)?
                   .Elements<Level>()
                   .FirstOrDefault(_ => _.LevelIndex?.Value == level);
    }

    // "Bulleted + Level: 1 + Aligned at: 0.63 cm + Indent at: 1.27 cm", or for a numbered list its
    // numbers, where they start and how they are aligned too, from the list's own definition.
    string ListWords(int id, int level)
    {
        var instance = Instance(id);
        var definition = LevelOf(id, level);
        var indentation = definition?.PreviousParagraphProperties?.Indentation;
        var left = Twips(indentation?.Left?.Value ?? indentation?.Start?.Value);
        var hanging = Twips(indentation?.Hanging?.Value);
        var parts = new List<string>();
        var format = definition?.NumberingFormat?.Val?.InnerText ?? "decimal";
        if (format == "bullet")
        {
            parts.Add("Bulleted");
            parts.Add($"Level: {level + 1}");
        }
        else
        {
            parts.Add("Numbered");
            parts.Add($"Level: {level + 1}");
            if (format switch
                {
                    "decimal" => "1, 2, 3, …",
                    "upperLetter" => "A, B, C, …",
                    "lowerLetter" => "a, b, c, …",
                    "upperRoman" => "I, II, III, …",
                    "lowerRoman" => "i, ii, iii, …",
                    _ => null
                } is { } style)
            {
                parts.Add($"Numbering Style: {style}");
            }

            var start = instance?.Elements<LevelOverride>().FirstOrDefault(_ => _.LevelIndex?.Value == level)?.StartOverrideNumberingValue?.Val?.Value ??
                        definition?.StartNumberingValue?.Val?.Value ??
                        1;
            parts.Add($"Start at: {start}");
            parts.Add("Alignment: " + definition?.LevelJustification?.Val?.InnerText switch
            {
                "center" => "Centered",
                "right" or "end" => "Right",
                _ => "Left"
            });
        }

        parts.Add($"Aligned at: {Centimetres(left - hanging)} cm");
        parts.Add($"Indent at: {Centimetres(left)} cm");
        return string.Join(" + ", parts);
    }

    // "Tab stops: 3 cm, Right + 5 cm, Decimal aligned,Leader: …", and a stop taken off "Not at 3 cm".
    static void Tabs(List<string> items, Tabs? was, Tabs? now)
    {
        static string Key(OoxmlTabStop stop) =>
            $"{stop.Val?.InnerText}|{stop.Leader?.InnerText}|{stop.Position?.Value}";

        var before = was?.Elements<OoxmlTabStop>().Where(_ => _.Val?.InnerText != "clear").ToList() ?? [];
        var after = now?.Elements<OoxmlTabStop>().Where(_ => _.Val?.InnerText != "clear").ToList() ?? [];
        var stops = new List<string>();
        foreach (var gone in before.Where(_ => after.All(stop => stop.Position?.Value != _.Position?.Value)))
        {
            stops.Add($"Not at {Centimetres(gone.Position?.Value ?? 0)} cm");
        }

        foreach (var stop in after.Where(_ => before.All(old => Key(old) != Key(_))))
        {
            var kind = stop.Val?.InnerText switch
            {
                "center" => "Centered",
                "right" or "end" => "Right",
                "decimal" => "Decimal aligned",
                "bar" => "Bar",
                _ => "Left"
            };
            var leader = stop.Leader?.InnerText switch
            {
                "dot" => ",Leader: …",
                "hyphen" => ",Leader: ---",
                "underscore" => ",Leader: ___",
                "heavy" => ",Heavy",
                _ => ""
            };
            stops.Add($"{Centimetres(stop.Position?.Value ?? 0)} cm, {kind}{leader}");
        }

        if (stops.Count > 0)
        {
            items.Add($"Tab stops: {string.Join(" + ", stops)}");
        }
    }

    static string? Value<TElement>(OpenXmlElement? properties, Func<TElement, string?> read)
        where TElement : OpenXmlElement =>
        properties?.GetFirstChild<TElement>() is { } element ? read(element) : null;

    static int? Value<TElement>(OpenXmlElement? properties, Func<TElement, int?> read)
        where TElement : OpenXmlElement =>
        properties?.GetFirstChild<TElement>() is { } element ? read(element) : null;

    static uint? Value<TElement>(OpenXmlElement? properties, Func<TElement, uint?> read)
        where TElement : OpenXmlElement =>
        properties?.GetFirstChild<TElement>() is { } element ? read(element) : null;

    static long? Scale(OpenXmlElement? properties) =>
        properties?.GetFirstChild<CharacterScale>()?.Val?.Value;

    static bool? Toggle(OnOffType? element) =>
        element == null ? null : element.Val?.Value ?? true;

    // A switch of the font's: on and off, by what each set of properties says or, saying nothing, off.
    static void Toggled<TElement>(List<string> items, OpenXmlElement? was, OpenXmlElement now, string name)
        where TElement : OnOffType
    {
        var before = Toggle(was?.GetFirstChild<TElement>()) ?? false;
        var after = Toggle(now.GetFirstChild<TElement>()) ?? false;
        if (before != after)
        {
            items.Add(after ? name : $"Not {name}");
        }
    }

    static void Switched<TElement>(List<string> items, OpenXmlElement? was, OpenXmlElement now, string on, string off)
        where TElement : OnOffType
    {
        var before = Toggle(was?.GetFirstChild<TElement>()) ?? false;
        var after = Toggle(now.GetFirstChild<TElement>()) ?? false;
        if (before != after)
        {
            items.Add(after ? on : off);
        }
    }

    static long Twips(string? value) =>
        long.TryParse(value, NumberStyles.Integer, invariant, out var twips) ? twips : 0;

    static decimal HalfPoints(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, invariant, out var halfPoints) ? halfPoints : 0;

    static string Points(decimal value) =>
        value.ToString("0.##", invariant);

    // Word gives a length to two places, cut rather than rounded: 360 twips, 0.635 cm, is "0.63".
    static string Centimetres(long twips) =>
        (decimal.Truncate(twips * 2.54m / 1440m * 100) / 100).ToString("0.##", invariant);
}
