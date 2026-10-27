static class FontHelpers
{
    // Font fallback mappings for fonts that may not be installed
    internal static Dictionary<string, string> FontFallbacks { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Variable fonts to their non-variable equivalents. The bare family is the exception:
        // Word cannot select it at all (WordCannotSelect), and draws it in Calibri.
        ["Segoe UI Variable"] = "Calibri",
        ["Segoe UI Variable Display"] = "Segoe UI",
        ["Segoe UI Variable Text"] = "Segoe UI",
        ["Segoe UI Variable Small"] = "Segoe UI",
        // Common premium font fallbacks
        // MS Mincho is the docDefaults East Asian face of many templates (wedding/04) and is not
        // bundled; MS Gothic is (msgothic.ttc) and draws the same symbol range, which is all a
        // hint-switched Latin-document run asks of it.
        ["MS Mincho"] = "MS Gothic",
        ["MS PMincho"] = "MS PGothic",
        ["Avenir Next LT Pro"] = "Century Gothic",
        // A name one space short of Avenir Next's is no font at all, and Word does not repair it:
        // newsletters/07 asks for "AvenirNext LT Pro Medium" on a machine where "Avenir Next LT Pro"
        // is installed, and Word's XPS embeds Calibri for that body text (the package has no
        // fontTable to suggest anything closer). Morph matched the bundled Avenir Next by restoring
        // the space, which set every line wider than Word's.
        ["AvenirNext LT Pro"] = "Calibri",
        ["AvenirNext LT Pro Medium"] = "Calibri",
        ["Eras Light ITC"] = "Century Gothic",
        ["Eras Medium ITC"] = "Century Gothic",
        ["Sagona"] = "Georgia",
        ["Sagona ExtraLight"] = "Georgia",
        ["Sagona Light"] = "Georgia",
        ["Daytona Light"] = "Calibri Light",
        // Univers is a wide grotesque sans that no bundled face matches: scored against real
        // Univers line spans read from business-plans/15's own XPS, every candidate runs narrow —
        // Tahoma −5.9%, Arial/Helvetica −6.4%, Segoe UI −7.3%, Aptos −9.5%. Tahoma is the closest
        // and is what this maps to; without an entry the family fell to the resolver's Aptos last
        // resort, the worst of the measured candidates.
        ["Univers"] = "Tahoma",
        ["Grandview Display"] = "Grandview",
        ["Cambria Math"] = "Cambria",
        // LibreOffice's metric-compatible stand-ins for the core Windows faces, which a file it wrote
        // names where Office would have named the originals: the EMF preview of sample.pptx's chart
        // sets every label in Liberation Sans. Each has the original's advance widths, so the original
        // lays the text out exactly as the stand-in would.
        ["Liberation Sans"] = "Arial",
        ["Liberation Serif"] = "Times New Roman",
        ["Liberation Mono"] = "Courier New",
        ["Liberation Sans Narrow"] = "Arial Narrow",
        // LibreOffice's default sans, which it writes into a file's fontTable with Verdana as its
        // altName — what Word substitutes on a machine without it. DejaVu Sans grew from Bitstream
        // Vera, drawn by Verdana's foundry to much the same wide, open proportions.
        ["DejaVu Sans"] = "Verdana",
    };

    // Common font style suffixes to strip when looking for base family
    // Note: Do NOT include vendor suffixes like " MT", " Pro", " LT", " ITC" as these are part of the font name
    static string[] StyleSuffixes { get; } =
    [
        " Condensed",
        " Compressed",
        " Narrow",
        " Extended",
        " Wide",
        " UltraBlack",
        " Black",
        " Heavy",
        " UltraBold",
        " ExtraBold",
        " Demibold",
        " Bold",
        " Semibold",
        " Demi",
        " Medium",
        " Regular",
        " Book",
        " UltraLight",
        " ExtraLight",
        " Semilight",
        " Light",
        " Thin",
        " Hairline",
        " Italic",
        " Oblique",
        " Cond"
    ];

    /// <summary>
    /// Maps weight-name suffixes to OS/2 <c>usWeightClass</c> values, so a request for
    /// <c>"Segoe UI Semilight"</c> can be scored against face metadata as weight 350
    /// rather than the generic 400/700 derived from the bold flag alone.
    /// </summary>
    /// <summary>OS/2 weight class at which a face counts as bold in its own right.</summary>
    internal const int BoldWeight = 700;

    /// <summary>
    /// Horizontal shear Word applies when it synthesizes an oblique for an italic run whose
    /// family has no italic face (glyphs lean right about the baseline; advances unchanged).
    /// Word-measured on <c>_probe_synitalic</c>: 96pt and 48pt Century Gothic italic stems
    /// regressed to 0.1355 and 0.1371 on both edges, 0.0000 on the upright control —
    /// size-independent, and well below the 0.25 many rasterizers use for fake italic.
    /// </summary>
    internal const double SyntheticItalicSkew = 0.136;

    static readonly Dictionary<string, int> weightFromSuffix = new(StringComparer.OrdinalIgnoreCase)
    {
        [" Hairline"] = 100,
        [" Thin"] = 100,
        [" UltraLight"] = 200,
        [" ExtraLight"] = 200,
        [" Light"] = 300,
        [" Semilight"] = 350,
        [" Regular"] = 400,
        [" Book"] = 400,
        [" Medium"] = 500,
        [" Demi"] = 600,
        [" Demibold"] = 600,
        [" Semibold"] = 600,
        [" Bold"] = 700,
        [" UltraBold"] = 800,
        [" ExtraBold"] = 800,
        [" Heavy"] = 900,
        [" Black"] = 900,
        [" UltraBlack"] = 950,
    };

    /// <summary>
    /// Inspects a font family name for a trailing weight word and returns the corresponding
    /// OS/2 weight class, or <c>null</c> when no recognised suffix is present.
    /// </summary>
    internal static int? InferWeightFromName(string fontFamily)
    {
        foreach (var (suffix, weight) in weightFromSuffix)
        {
            if (fontFamily.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return weight;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the OS/2 weight class to score against when resolving a font, derived from
    /// the requested name's suffix when present, otherwise from the bold flag.
    /// </summary>
    /// <remarks>
    /// The name deliberately outranks the bold flag for FACE SELECTION: a bold run in
    /// "Segoe UI Semilight" must still resolve the Semilight face rather than jumping to weight
    /// 700 and landing on a different member of the family. Whether that face is then drawn
    /// emboldened is a separate question — see each backend's synthetic-embolden check.
    /// </remarks>
    internal static int ResolveTargetWeight(string fontFamily, bool bold) =>
        InferWeightFromName(fontFamily) ?? (bold ? 700 : 400);

    /// <summary>
    /// Score a face against the requested weight/italic/width. Lower is closer; the
    /// resolver picks the face with the smallest score.
    /// </summary>
    internal static int ScoreFace(FontFace face, int targetWeight, bool targetItalic, int targetWidth = 5)
    {
        // Italic mismatch dominates: we'd rather take a regular face at the wrong weight
        // than an italic face at the right weight when an upright was requested.
        var italicPenalty = face.Italic == targetItalic ? 0 : 10_000;

        // Width is secondary to weight. Some bundled fonts (e.g. Arial_700.ttf) have
        // a non-standard usWidthClass in their OS/2 table — penalising width too heavily
        // would beat a Bold face out by a Regular one whose width happens to read 5.
        var widthPenalty = Math.Abs(face.Width - targetWidth) * 100;

        // Weight is a smooth distance so 350 vs 400 (50) beats 350 vs 700 (350).
        var weightPenalty = Math.Abs(face.Weight - targetWeight);

        return italicPenalty + widthPenalty + weightPenalty;
    }

    /// <summary>
    /// Picks the face whose metrics best match the requested weight/italic/width from a
    /// list returned by <see cref="FontFileCache.TryGet(string, out FontFace[])"/>.
    /// </summary>
    internal static FontFace? PickBestFace(IEnumerable<FontFace> faces, int targetWeight, bool targetItalic, int targetWidth = 5)
    {
        FontFace? best = null;
        var bestScore = int.MaxValue;
        foreach (var face in faces)
        {
            var score = ScoreFace(face, targetWeight, targetItalic, targetWidth);
            if (score < bestScore)
            {
                best = face;
                bestScore = score;
            }
        }

        return best;
    }

    internal static string StripWeightSuffixes(string fontFamily)
    {
        // Slice rather than substring: "Calibri Light Italic" strips twice, and every intermediate
        // string was allocated only to be thrown away. The span narrows in place and only the final
        // result materialises.
        var result = fontFamily.AsSpan();
        bool changed;
        do
        {
            changed = false;
            foreach (var suffix in StyleSuffixes)
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result[..^suffix.Length];
                    changed = true;
                }
            }
        } while (changed);

        result = result.Trim();

        // A slice as long as the input is the input, so the common no-suffix case (every plain
        // "Arial" / "Times New Roman" lookup) returns the existing instance and allocates nothing.
        if (result.Length == fontFamily.Length)
        {
            return fontFamily;
        }

        return result.ToString();
    }

    /// <summary>
    /// Determines if a font name implies bold weight (for adjusting style when resolving from caches).
    /// </summary>
    static readonly string[] boldKeywords = ["Bold", "Black", "Heavy", "Medium", "Demi", "Semibold"];

    internal static bool ImpliesBold(string fontFamily)
    {
        foreach (var keyword in boldKeywords)
        {
            if (fontFamily.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes the candidate font family names to try when resolving a font, in priority order:
    /// the family as named, then the name with its weight and style words stripped.
    /// </summary>
    /// <remarks>
    /// A bold request keeps the family it names. Word treats a weight-named family ("Franklin Gothic
    /// Book", "Calibri Light", "Franklin Gothic Demi") as a family of its own, and where that family
    /// has no bold face it emboldens the named face rather than borrowing a bold from the family the
    /// name is built on: Word-probed on `_probe_synbold`, where every such family drew bold in its own
    /// face with the XPS bold simulation. The stripped name is only reached when the named family is
    /// not indexed at all.
    /// </remarks>
    internal static FontNameCandidates GetCandidateNames(string fontFamily)
    {
        var strippedName = StripWeightSuffixes(fontFamily);
        if (string.IsNullOrEmpty(strippedName) ||
            strippedName == fontFamily)
        {
            strippedName = null;
        }

        return new(fontFamily, strippedName);
    }

    /// <summary>
    /// Finds a fallback font name for any of the candidate names.
    /// Returns null if no fallback is configured.
    /// </summary>
    /// <summary>
    /// A family name Word cannot select although a face of that family is installed, so a run that
    /// names it draws in its <see cref="FontFallbacks"/> substitute whatever the host holds.
    /// Segoe UI Variable is a variable font that GDI lists only by its named instances (Display,
    /// Text, Small): agendas-minutes/02's theme minor font names the bare family, and Word's XPS
    /// embeds Calibri for all of that text, set at Calibri's line height (13.2pt lines at 10pt and
    /// 1.1, against 14.63 for the face's own metrics). <c>_probe_segvar</c> (2026-10-27) set the
    /// bare name at 40pt and 10pt beside Segoe UI and read Calibri's 1.2207-em pitch off both
    /// sizes, where Segoe UI kept its 1.330. A named instance ("Segoe UI Variable Display") is
    /// selectable, and Word draws it in the variable face, which Morph sets as Segoe UI: the face
    /// is indexed under its bare family name only, and its metrics are Segoe UI's.
    /// </summary>
    internal static bool WordCannotSelect(string name) =>
        name.Equals("Segoe UI Variable", StringComparison.OrdinalIgnoreCase);

    internal static string? FindFallback(FontNameCandidates candidates)
    {
        if (FontFallbacks.TryGetValue(candidates.Original, out var fallback))
        {
            return fallback;
        }

        if (candidates.Stripped != null &&
            FontFallbacks.TryGetValue(candidates.Stripped, out fallback))
        {
            return fallback;
        }

        return null;
    }

    // Marker glyphs the shipped text faces don't cover — these render in the embedded
    // "Morph Bullets" subset (◆/■ always shipped in it; the ▸/► triangles were drawn into it).
    // Every other marker keeps the paragraph font: that's what Word uses when the glyph exists
    // there, and Word glyph-falls-back to Segoe UI Symbol when it doesn't (which these four
    // mirror via the subset).
    internal static bool RequiresBulletFont(string? markerText) =>
        markerText is ['■' or '◆' or '▸' or '►'];

    // Bullets declared in Symbol/Wingdings always need the embedded subset — Linux/macOS don't
    // ship those proprietary faces.
    internal static bool IsProprietaryBulletFont(string? fontFamily) =>
        fontFamily != null &&
        (fontFamily.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase) ||
         fontFamily.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase));

    // Single marker-font decision shared by the three render backends.
    internal static bool UseBulletFont(string? markerText, string? declaredFontFamily) =>
        IsProprietaryBulletFont(declaredFontFamily) ||
        RequiresBulletFont(markerText);
}
