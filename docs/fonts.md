# Fonts

Morph resolves fonts by reading each font file's own metadata, not by querying the OS font manager or matching against a hardcoded table of names. This page describes how that works, what the public knobs do, and how to control resolution for tests and deterministic builds.

## How resolution works

When a Word document declares a font like `<w:rFonts w:ascii="Segoe UI Semilight"/>`, Morph treats that string as authoritative — it is literally Name ID 4 of `segoeuisl.ttf`. The resolver:

1. **Indexes every available font file by every name it declares.** At first use, Morph scans each font directory (system, user, Office, cloud, or a custom directory supplied via `FontDirectory`), parses each file's OpenType `name` and `OS/2` tables once, and indexes the file under all of:
   - **Family Name** (Name ID 1) — `Segoe UI`
   - **Full Name** (Name ID 4) — `Segoe UI Semilight`
   - **PostScript Name** (Name ID 6) — `SegoeUI-Semilight`
   - **Typographic Family** (Name ID 16) — `Segoe UI`
   - **Typographic Family + Subfamily** — `Segoe UI Semilight`, when both are present
   - **Family + Subfamily** when the subfamily isn't `Regular`/`Bold`/`Italic`/`Bold Italic`

   English-language Windows records (platform 3, language 0x0409) are preferred over Mac Roman or non-English records when the same name is declared multiple times.

2. **Looks up the requested name directly.** A request for `"Segoe UI Semilight"` matches Name ID 4 of `segoeuisl.ttf` immediately — no string parsing, no suffix-stripping, no platform font manager involved.

3. **Picks the best face by metric distance.** When a single name resolves to multiple faces (e.g. `"Segoe UI"` matches every face of the family), the resolver picks the one whose `OS/2` metrics are closest to the request:
   - **Italic mismatch** is heavily penalised — an upright face at the wrong weight beats an italic face at the right weight when an upright was requested.
   - **Width** distance is next (`usWidthClass`: 5 = Normal, 1–4 = Condensed, 6–9 = Expanded).
   - **Weight** distance is the smooth tie-breaker (`usWeightClass`: 100 = Thin, 350 = Semilight, 400 = Regular, 700 = Bold, 900 = Black).

4. **Derives the target weight from the requested name when possible.** A request for `"Segoe UI Semilight"` targets weight 350; `"Segoe UI Bold"` targets 700; `"Arial"` with `bold=false` targets 400. The bold flag from the run still applies as a fallback when the name carries no weight word.

The implementation is in:
- `src/Morph/Rendering/OpenTypeReader.cs` — minimal `name` + `OS/2` parser.
- `src/Morph/Rendering/FontFace.cs` — per-face metadata record.
- `src/Morph/Rendering/FontFileCache.cs` — name-indexed view of a font directory.
- `src/Morph/Rendering/FontHelpers.cs` — weight inference, scoring, fallback dictionary.

## Search path

By default, Morph indexes fonts from these locations in order. The first cache that produces a hit wins; faces from that cache are then scored against the request.

| Tier | Windows | macOS | Linux |
|---|---|---|---|
| User | `%LOCALAPPDATA%\Microsoft\Windows\Fonts` | n/a | n/a |
| Office | `%PROGRAMFILES%\Microsoft Office\root\vfs\Fonts\private` | `/Applications/Microsoft Word.app/Contents/Resources/DFonts` (and Excel/PowerPoint) | n/a |
| Cloud | `%LOCALAPPDATA%\Microsoft\FontCache\4\CloudFonts` | n/a | n/a |
| System | `%WINDIR%\Fonts` | `/Library/Fonts`, `/System/Library/Fonts` | `/usr/share/fonts`, `/usr/local/share/fonts` |

If `ConversionOptions.FontDirectory` is set, **only** that directory is searched (recursively). System/user/Office/cloud caches and the OS font manager are ignored, and unresolved fonts throw immediately. Use this for hermetic, machine-independent rendering.

## Fallback behaviour

When a name doesn't match any indexed face, Morph falls back in this order:

1. **Suffix-stripped lookup.** `"Calibri Bold"` → strip ` Bold` → look up `"Calibri"`. The full list of recognised weight/width suffixes lives in `FontHelpers.StyleSuffixes`. This is a defensive safety net — well-formed fonts declare their full name in the `name` table and hit step 1 directly.
2. **Built-in alias dictionary.** A small map handles commonly-missing fonts:

   | Requested | Substituted |
   |---|---|
   | `Segoe UI Variable` (Display/Text/Small) | `Segoe UI` |
   | `Avenir Next LT Pro` and variants | `Century Gothic` |
   | `Eras Light/Medium ITC` | `Century Gothic` |
   | `Sagona`, `Sagona ExtraLight/Light` | `Georgia` |
   | `Grandview Display` | `Grandview` |
   | `Cambria Math` | `Cambria` |

3. **User `FontFallback` delegate**, if `ConversionOptions.FontFallback` is supplied. Called with the original family name; return an alternative or `null`.
4. **Platform font manager.** Skia's `SKTypeface.FromFamilyName` / ImageSharp's `SystemFonts` get a final chance, useful for fonts the user installed after Morph's caches loaded.

If all four fall through, an `InvalidOperationException` is thrown listing every directory that was searched.

## Configuration

All font-related configuration lives on [`ConversionOptions`](../src/Morph/ConversionOptions.cs):

| Option | Type | Default | Description |
|---|---|---|---|
| `FontDirectory` | `string?` | `null` | Path to a directory of fonts to use exclusively. When set, system/user/Office/cloud caches and OS font fallbacks are bypassed. |
| `FontFallback` | `Func<string, string?>?` | `null` | Called when a requested font isn't resolved any other way. Return an alternative family name or `null` to throw. |
| `DefaultFont` | `string?` | `null` (uses `Georgia`) | Family used when the document doesn't declare a default in `docDefaults`. Per-conversion override; doesn't affect other callers. |
| `FontWidthScale` | `double` | `1.0` | Scale factor for measured glyph advances. `1.07` better matches Microsoft Word's text rendering and causes earlier line wrapping to compensate for Skia/ImageSharp running glyphs slightly tighter than GDI. |
| `DeterministicRendering` | `bool?` | `null` (uses static default `false`) | When `true`, the Skia backend disables sub-pixel positioning and font hinting, falling back to integer-positioned greyscale anti-aliasing. Output is pixel-identical across machines and rasterizer versions; text is slightly softer. Intended for snapshot tests; leave off in production. |

## Recipes

### Render the same way on every machine

Bundle the required fonts with the app, point Morph at them, and disable subpixel rendering:

```csharp
var options = new ConversionOptions
{
    FontDirectory = Path.Combine(AppContext.BaseDirectory, "Fonts"),
    DeterministicRendering = true,
};
```

With `FontDirectory` set, an unresolved font throws immediately rather than silently falling back to a different system font.

### Substitute a missing font

```csharp
var options = new ConversionOptions
{
    FontFallback = name => name switch
    {
        "Helvetica Neue" => "Helvetica",
        "Proxima Nova" => "Open Sans",
        _ => null,
    },
};
```

Returning `null` propagates to the next fallback tier (the OS font manager); throwing surfaces as a regular exception.

### Match Word's text width

Word's GDI rendering tends to be slightly looser than Skia/ImageSharp. If long lines are wrapping later than Word does, scale glyph advances up by ~7%:

```csharp
var options = new ConversionOptions { FontWidthScale = 1.07 };
```

## Why this differs from `SKTypeface.FromFamilyName`

SkiaSharp's family-name lookup defers to DirectWrite (Windows), CoreText (macOS), or Fontconfig (Linux). DirectWrite collapses `"Segoe UI Semilight"` to the parent `"Segoe UI"` family at weight 400 — even though Windows reports Semilight as a separate `InstalledFontCollection` family. The same applies to other extended families (`Light`, `Black`, `Variable Display/Text/Small`, etc.).

Reading the `name` table directly bypasses the platform font manager entirely, so:

- Resolution is identical on Windows, macOS, and Linux.
- Vendor-specific naming (`Avenir Next LT Pro`, `Eras Light ITC`) works without a fallback table.
- A document targeting an extended weight (`Semilight`, `Black`) gets that weight even when the OS doesn't expose it as a separate family.
- `.ttc` collections work uniformly — each face is indexed independently with its own weight/width/italic.
