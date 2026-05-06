"""
Build a tiny bullets.ttf containing only the glyphs Morph needs to render
DOCX bullet markers. Source fonts are both SIL OFL (Noto Sans Symbols 2 +
Noto Sans Mono) - safe to redistribute. Both share unitsPerEm=1000 so
fontTools.merge produces a single coherent TTF.

Usage:
    python tools/build-bullets-font.py \
        --noto path/to/NotoSansSymbols2-Regular.ttf \
        --mono path/to/NotoSansMono-Regular.ttf \
        --out  src/Fonts/Bullets.ttf

Inputs are not vendored - download from:
    https://fonts.google.com/noto/specimen/Noto+Sans+Symbols+2
    https://fonts.google.com/noto/specimen/Noto+Sans+Mono

Requires: pip install fonttools
"""

import argparse
from pathlib import Path
from fontTools.subset import Subsetter
from fontTools.ttLib import TTFont
from fontTools.merge import Merger


# Codepoints Morph remaps PUA bullet chars to (see DocumentParser.cs:2158-2174)
# plus the literal ASCII 'o' Word emits at the second bullet level in Courier.
SYMBOL_LIKE_CODEPOINTS = [
    0x2022,  # BULLET                            <- F0B7, F0A7 fallback
    0x25CF,  # BLACK CIRCLE                      <- F06C filled circle
    0x2713,  # CHECK MARK                        <- F0FC Wingdings checkmark
    0x25CB,  # WHITE CIRCLE                      <- F0A8 hollow circle
    0x25C6,  # BLACK DIAMOND                     <- F0D8 diamond
    0x25A0,  # BLACK SQUARE                      <- F076 / F0A7 square
    0x25AA,  # BLACK SMALL SQUARE                (Wingdings small square)
    0x2014,  # EM DASH                           (occasional dash bullet)
]

MONOSPACE_CODEPOINTS = [
    ord("o"),  # Word's 2nd-level bullet is literal lowercase 'o' in Courier New
]


def subset(src_path: Path, codepoints: list[int], out_path: Path) -> None:
    font = TTFont(str(src_path))
    # Drop optional tables that aren't uniformly present across both source
    # fonts - the merger refuses to combine a font that has e.g. 'vhea' with
    # one that does not. Bullets are horizontal text only, so vert metrics
    # and OpenType layout aren't needed.
    drop = ["vhea", "vmtx", "VORG", "GPOS", "GSUB", "GDEF", "BASE",
            "JSTF", "MATH", "DSIG", "FFTM", "MVAR", "STAT"]
    for tag in drop:
        if tag in font:
            del font[tag]
    sub = Subsetter()
    sub.populate(unicodes=codepoints)
    sub.subset(font)
    font.save(str(out_path))
    print(f"  subset {src_path.name}: {out_path.stat().st_size:,} bytes")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--noto", required=True, type=Path,
                    help="Path to NotoSansSymbols2-Regular.ttf")
    ap.add_argument("--mono", required=True, type=Path,
                    help="Path to DejaVuSansMono.ttf (or similar OFL monospace)")
    ap.add_argument("--out", required=True, type=Path,
                    help="Output path, e.g. src/Fonts/Bullets.ttf")
    args = ap.parse_args()

    work = args.out.parent / ".bullets-build"
    work.mkdir(parents=True, exist_ok=True)
    noto_sub = work / "noto-subset.ttf"
    mono_sub = work / "mono-subset.ttf"

    print("Subsetting source fonts:")
    subset(args.noto, SYMBOL_LIKE_CODEPOINTS, noto_sub)
    subset(args.mono, MONOSPACE_CODEPOINTS, mono_sub)

    print("Merging into single TTF:")
    merger = Merger()
    merged = merger.merge([str(noto_sub), str(mono_sub)])
    # Rename so the renderer can request it by family.
    name_table = merged["name"]
    for record in name_table.names:
        if record.nameID in (1, 4, 6):  # Family, Full name, PostScript name
            record.string = "Morph Bullets".encode("utf-16-be") if record.platformID == 3 \
                else b"Morph Bullets"
    merged.save(str(args.out))
    print(f"\nWrote {args.out} ({args.out.stat().st_size:,} bytes)")

    # Cleanup intermediates.
    noto_sub.unlink()
    mono_sub.unlink()
    work.rmdir()


if __name__ == "__main__":
    main()
