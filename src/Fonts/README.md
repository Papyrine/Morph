# Bundled test fonts

192 font files used by the test suite so rendering is deterministic across machines. Nothing here ships in a
NuGet package, and no project file references the directory: the tests point at it at run time through
`ProjectFonts.Directory`, which the scenario tests pass as `ConversionOptions.FontDirectory`. A render that
resolved fonts from the host would drift between a developer machine, CI, and the pinned `linux/amd64`
container, and the Verify baselines would stop matching.

This is distinct from `src/Morph/EmbeddedFonts/`, which *is* compiled into the library as an
`EmbeddedResource` — those are the Aptos faces and the `Bullets` marker subset that the renderer needs when
no font directory is configured at all.

## Naming

```
Family_Weight[_Italic].ttf
```

`Weight` is the OS/2 `usWeightClass` of the face — 300 Light, 400 Regular, 500 Medium, 600 Semibold, 700
Bold, 900 Black. The current spread is 97 regular, 67 bold, 8 light, 5 semibold, 3 black, one each at 350 and
500, plus 59 italics among them. A handful of files carry a trailing `__Token` (for example
`Bodoni_MT_400__BOD_R.ttf`) to break a collision where two distinct faces share a family and weight.

Several files predate the convention and keep their original names (`msyh.ttc`, `cambria.ttc`,
`helvetica-compressed.otf`, `OpenSans-Variable.ttf`). That is harmless, because **the filename is a
convenience for humans, not the lookup key**. `FontFileCache` reads each file's `name` table at startup and
indexes the face under every name it declares — Family (ID 1), Subfamily (ID 2), Full Name (ID 4), PostScript
Name (ID 6), and the typographic Family/Subfamily pair (IDs 16/17). A request for `Segoe UI Semilight`
therefore matches the face directly rather than through suffix-stripping guesswork.

## Provenance and licensing

Each file carries its own terms inside its `name` table:

| ID | Meaning |
| -- | ------- |
| 0  | Copyright notice |
| 7  | Trademark |
| 8  | Manufacturer / vendor |
| 13 | Licence description |
| 14 | Licence URL |

Those records are deliberately **not** transcribed into this file. A transcription is a second copy that can
drift from the file it claims to describe, and the authoritative statement already travels inside every
`.ttf`/`.otf`/`.ttc` here. Reading the `name` table of a specific file gives its exact terms; `OpenTypeReader`
in `src/Morph/Fonts/` already parses these records for resolution and can be used to dump them.

Redistribution is the reason the collection has the shape it does. Faces under open licences (SIL OFL,
Apache) sit alongside faces present for local rendering only, so before this directory is copied anywhere
outside the test harness, the per-file records above are the thing to check.

## Known gap: missing bold faces

25 families are used by a **bold** run somewhere in `src/Tests/Inputs/` but have no bundled face at weight
700 or above. Skia then approximates bold by dilating the regular outline and ImageSharp renders at normal
weight, so the two backends disagree and neither matches Word.

The gap is pinned rather than asserted away — see `BundledBoldCoverageTests`, which lists every affected
family with a scenario that uses it, and fails equally on a new entry or a stale one. Dropping a real bold
face into this directory closes an entry with no code change, because both backends gate synthesis on the
resolved weight; the test then fails until the entry is deleted.

Most of the 25 are proprietary (Microsoft, Monotype/ITC, Linotype) and cannot be redistributed here. Playfair
Display, Work Sans, Lato and Source Sans are the exceptions, being under open licences.

For why a synthesised bold cannot close the gap on its own, see the Bold section of `docs/word-features.md`:
a designed bold redraws letterforms rather than fattening them, so no single stroke width reconciles the two.
