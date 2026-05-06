"""
Build a tiny Bullets.ttf containing exactly the glyphs Morph needs to render
DOCX bullet markers - sourced from the Microsoft fonts Word actually uses
(Symbol, Wingdings, Courier New). Each source glyph is extracted and remapped
to a standard Unicode codepoint, so the resulting font is keyed by Unicode at
runtime even though the outlines come from PUA-encoded source fonts.

Why these sources: Word's bullet templates declare Symbol / Wingdings /
Courier New via per-level rFonts, and the dot/check/diamond glyphs in those
fonts are visually larger than the Noto-Sans-Symbols equivalents at the same
point size (Symbol's bullet is ~0.45em vs Noto's ~0.25em). Sourcing directly
from the MS faces means the output Bullets.ttf renders at the same visual
weight as Word, with no runtime scale-fudge.

Licensing: Symbol, Wingdings and Courier New are Microsoft-owned. The output
font is a *subset* of their outlines remapped to Unicode codepoints. Whether
redistributing such a subset is permitted depends on your interpretation of
the per-OS font EULA - check before publishing the resulting Bullets.ttf.

Usage (Windows; the source fonts ship with the OS):
    python tools/build-bullets-font.py \
        --symbol    C:/Windows/Fonts/symbol.ttf \
        --wingdings C:/Windows/Fonts/wingding.ttf \
        --courier   C:/Windows/Fonts/cour.ttf \
        --out       src/Morph/EmbeddedFonts/Bullets.ttf

Requires: pip install fonttools
"""

import argparse
from pathlib import Path
from fontTools.subset import Subsetter
from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables._c_m_a_p import CmapSubtable
from fontTools.merge import Merger


# Per source font: { source_codepoint_in_font: target_unicode_codepoint }.
# Source codepoints are the PUA-shifted (0xF000 + legacy) values Word emits;
# the script also falls back to the legacy 8-bit codepoint if the PUA one is
# absent in the font's cmap.

SYMBOL_GLYPHS = {
    0xF0B7: 0x2022,  # bullet                  -> U+2022 BULLET
    0xF06C: 0x25CF,  # filled circle           -> U+25CF BLACK CIRCLE
    0xF0A8: 0x25CB,  # hollow circle           -> U+25CB WHITE CIRCLE
    0xF0D8: 0x25C6,  # diamond                 -> U+25C6 BLACK DIAMOND
}

WINGDINGS_GLYPHS = {
    0xF06E: 0x25A0,  # filled square           -> U+25A0 BLACK SQUARE
    0xF0A7: 0x25AA,  # black small square      -> U+25AA BLACK SMALL SQUARE
    0xF076: 0x25AA,  # filled small square     -> U+25AA (same target; first wins)
    0xF0FC: 0x2713,  # check mark              -> U+2713 CHECK MARK
}

COURIER_GLYPHS = {
    0x006F: 0x006F,  # literal lowercase 'o' for Word's level-1 sub-bullet
}


def collect_cmap(font: TTFont) -> dict[int, str]:
    """
    Merge every cmap subtable into a single { codepoint: glyph_name } dict.
    getBestCmap() returns None for Symbol.ttf (its only subtable is the
    Microsoft Symbol encoding at platformID=3 / platEncID=0, which fontTools
    doesn't classify as Unicode), so we walk all subtables ourselves.
    """
    merged: dict[int, str] = {}
    for sub in font["cmap"].tables:
        if sub.cmap:
            for cp, glyph in sub.cmap.items():
                merged.setdefault(cp, glyph)
    return merged


def find_glyph_name(cmap: dict[int, str], cp: int) -> str | None:
    """Look up a glyph by source codepoint, falling back across the F000 PUA shift."""
    if cp in cmap:
        return cmap[cp]
    # Symbol/Wingdings: glyph may live at the legacy 8-bit codepoint instead of the
    # PUA-shifted one (or vice versa). Try the alternate.
    if cp >= 0xF000 and (cp - 0xF000) in cmap:
        return cmap[cp - 0xF000]
    if cp < 0x100 and (cp + 0xF000) in cmap:
        return cmap[cp + 0xF000]
    return None


def subset_and_remap(src_path: Path, mapping: dict[int, int], out_path: Path) -> None:
    """
    Subset src_path to the glyphs referenced by mapping's source codepoints,
    then replace its cmap with a fresh Unicode-BMP table that points the
    target codepoints at those same glyphs.
    """
    font = TTFont(str(src_path))

    # Drop tables that aren't uniformly present across the three sources -
    # fontTools.merge refuses to combine fonts that disagree on which optional
    # tables are present. Bullets are horizontal text only, so vert metrics
    # and OpenType layout aren't needed.
    drop = ["vhea", "vmtx", "VORG", "GPOS", "GSUB", "GDEF", "BASE",
            "JSTF", "MATH", "DSIG", "FFTM", "MVAR", "STAT", "kern",
            "EBLC", "EBDT", "EBSC", "LTSH", "hdmx", "VDMX"]
    for tag in drop:
        if tag in font:
            del font[tag]

    # Resolve source codepoints to glyph names BEFORE subsetting (the cmap
    # gets rewritten below; the resolution lookup uses the original cmap).
    cmap = collect_cmap(font)
    resolved: dict[int, str] = {}
    for src_cp, tgt_cp in mapping.items():
        glyph = find_glyph_name(cmap, src_cp)
        if glyph is None:
            print(f"  WARNING: codepoint U+{src_cp:04X} not found in {src_path.name}")
            continue
        # First wins - if multiple sources map to the same target, keep the first.
        resolved.setdefault(tgt_cp, glyph)

    # Subset to the resolved glyph set. Use glyph names (not unicodes) because
    # the source codepoints may be outside the Unicode-BMP cmap subtable
    # (Symbol's encoding is platformID=3, platEncID=0, not 3/1).
    sub = Subsetter()
    sub.populate(glyphs=list(set(resolved.values())))
    sub.subset(font)

    # Build a fresh cmap with a single Unicode-BMP subtable mapping the target
    # codepoints to the surviving glyph names.
    new_cmap = newTable("cmap")
    new_cmap.tableVersion = 0
    bmp = CmapSubtable.newSubtable(4)
    bmp.platformID = 3
    bmp.platEncID = 1  # Unicode BMP
    bmp.language = 0
    bmp.cmap = dict(resolved)
    new_cmap.tables = [bmp]
    font["cmap"] = new_cmap

    font.save(str(out_path))
    targets = ", ".join(f"U+{cp:04X}" for cp in sorted(resolved))
    print(f"  subset {src_path.name}: {out_path.stat().st_size:,} bytes  ({targets})")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--symbol", required=True, type=Path,
                    help="Path to symbol.ttf (e.g. C:/Windows/Fonts/symbol.ttf)")
    ap.add_argument("--wingdings", required=True, type=Path,
                    help="Path to wingding.ttf (e.g. C:/Windows/Fonts/wingding.ttf)")
    ap.add_argument("--courier", required=True, type=Path,
                    help="Path to cour.ttf (Courier New regular)")
    ap.add_argument("--out", required=True, type=Path,
                    help="Output path, e.g. src/Morph/EmbeddedFonts/Bullets.ttf")
    args = ap.parse_args()

    work = args.out.parent / ".bullets-build"
    work.mkdir(parents=True, exist_ok=True)
    sym_sub = work / "symbol-subset.ttf"
    wing_sub = work / "wingdings-subset.ttf"
    cour_sub = work / "courier-subset.ttf"

    print("Subsetting source fonts:")
    subset_and_remap(args.symbol, SYMBOL_GLYPHS, sym_sub)
    subset_and_remap(args.wingdings, WINGDINGS_GLYPHS, wing_sub)
    subset_and_remap(args.courier, COURIER_GLYPHS, cour_sub)

    print("Merging into single TTF:")
    merger = Merger()
    merged = merger.merge([str(sym_sub), str(wing_sub), str(cour_sub)])

    # Rename so the renderer can request it by family.
    name_table = merged["name"]
    for record in name_table.names:
        if record.nameID in (1, 4, 6):  # Family, Full name, PostScript name
            record.string = "Morph Bullets".encode("utf-16-be") if record.platformID == 3 \
                else b"Morph Bullets"
    merged.save(str(args.out))
    print(f"\nWrote {args.out} ({args.out.stat().st_size:,} bytes)")

    # Cleanup intermediates.
    sym_sub.unlink()
    wing_sub.unlink()
    cour_sub.unlink()
    work.rmdir()


if __name__ == "__main__":
    main()
