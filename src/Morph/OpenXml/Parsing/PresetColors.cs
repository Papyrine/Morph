/// <summary>
/// DrawingML's preset colours (<c>a:prstClr</c>, ST_PresetColorVal, ECMA-376 Part 1 §20.1.10.47): the
/// CSS/X11 colour names, with "dark", "light" and "medium" shortened to <c>dk</c>, <c>lt</c> and
/// <c>med</c> (<c>dkSlateGray</c>, <c>ltGoldenrodYellow</c>, <c>medVioletRed</c>) and each grey also
/// spelt <c>Grey</c>. Those are .NET's known colours, which supply the values: the standard defines the
/// names by the same RGB table.
/// </summary>
static class PresetColors
{
    /// <summary>The colour a preset name stands for, as six hex digits; null for a name that is none.</summary>
    public static string? Hex(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var known = Expand(name).Replace("Grey", "Gray", StringComparison.Ordinal).Replace("grey", "gray", StringComparison.Ordinal);
        var color = System.Drawing.Color.FromName(known);
        if (!color.IsKnownColor ||
            color.IsSystemColor)
        {
            return null;
        }

        return $"{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    static string Expand(string name)
    {
        foreach (var (prefix, word) in new[] {("dk", "Dark"), ("lt", "Light"), ("med", "Medium")})
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.Length > prefix.Length &&
                char.IsUpper(name[prefix.Length]))
            {
                return word + name[prefix.Length..];
            }
        }

        return name;
    }
}
