/// <summary>
/// The paragraph styles of a document an editor offers: every one it defines that Word would show,
/// and the ones Word's gallery offers whether or not they are defined yet (<see cref="BuiltInStyles.Gallery"/>),
/// in the order of Word's gallery — by <c>w:uiPriority</c>, then by name.
/// </summary>
static class StyleCatalog
{
    // What Word gives a style that names no priority.
    const int lastPriority = 99;

    public static IReadOnlyList<StyleChoice> Read(MainDocumentPart? main)
    {
        var defined = main?.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .Where(_ => _.StyleId?.Value != null)
            .GroupBy(_ => _.StyleId!.Value!, StringComparer.Ordinal)
            .ToDictionary(_ => _.Key, _ => _.First(), StringComparer.Ordinal) ?? [];
        var defaults = main?.StyleDefinitionsPart?.Styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
        var baseSize = Size(defaults?.FontSize) ?? 11;

        var found = new List<(int Priority, StyleChoice Choice)>();
        foreach (var style in defined.Values)
        {
            if (style.Type?.Value != StyleValues.Paragraph ||
                style.GetFirstChild<StyleHidden>() != null)
            {
                continue;
            }

            var gallery = BuiltInStyles.Gallery.Contains(style.StyleId!.Value!) ||
                          (style.PrimaryStyle != null && style.SemiHidden == null);
            if (!gallery &&
                style.SemiHidden != null)
            {
                continue;
            }

            found.Add((Priority(style), Choice(style, defined, baseSize, gallery, true)));
        }

        foreach (var id in BuiltInStyles.Gallery)
        {
            if (defined.ContainsKey(id) ||
                BuiltInStyles.Definition(id) is not { } style)
            {
                continue;
            }

            found.Add((Priority(style), Choice(style, defined, baseSize, true, false)));
        }

        return found
            .OrderBy(_ => _.Priority)
            .ThenBy(_ => _.Choice.Name, StringComparer.OrdinalIgnoreCase)
            .Select(_ => _.Choice)
            .ToList();
    }

    // Normal names no priority, and leads the gallery all the same.
    static int Priority(Style style)
    {
        if (style.Default?.Value == true)
        {
            return 0;
        }

        return style.UIPriority?.Val?.Value ?? lastPriority;
    }

    // A style's look, through the styles it is based on: the first of them to say each thing.
    static StyleChoice Choice(Style style, Dictionary<string, Style> defined, double baseSize, bool gallery, bool isDefined)
    {
        double? size = null;
        bool? bold = null;
        bool? italic = null;
        string? color = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = (Style?) style;
        while (current != null &&
               seen.Add(current.StyleId?.Value ?? ""))
        {
            var run = current.StyleRunProperties;
            size ??= Size(run?.FontSize);
            bold ??= Toggle(run?.Bold);
            italic ??= Toggle(run?.Italic);
            if (run?.Color?.Val?.Value is {Length: 6} hex)
            {
                color ??= hex;
            }

            current = Based(current);
        }

        return new(style.StyleId!.Value!, Name(style), gallery, isDefined)
        {
            SizePoints = size ?? baseSize,
            Bold = bold ?? false,
            Italic = italic ?? false,
            ColorHex = color
        };

        Style? Based(Style current)
        {
            if (current.BasedOn?.Val?.Value is not { } basedOn)
            {
                return null;
            }

            if (defined.TryGetValue(basedOn, out var parent))
            {
                return parent;
            }

            return BuiltInStyles.Definition(basedOn);
        }
    }

    // Word writes the names of some styles it builds in in lower case, and shows them capitalised.
    static string Name(Style style)
    {
        var name = style.StyleName?.Val?.Value ?? style.StyleId!.Value!;
        if (name.Length > 0 &&
            char.IsLower(name[0]))
        {
            return char.ToUpperInvariant(name[0]) + name[1..];
        }

        return name;
    }

    static double? Size(FontSize? size)
    {
        if (double.TryParse(size?.Val?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints))
        {
            return halfPoints / 2;
        }

        return null;
    }

    static bool? Toggle(OnOffType? toggle)
    {
        if (toggle == null)
        {
            return null;
        }

        return toggle.Val?.Value ?? true;
    }
}
