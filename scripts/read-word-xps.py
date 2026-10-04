"""Read what Word drew out of the XPS it exports: the fonts it embedded and every glyph run.

RenderHelper leaves word_output.xps beside the reference PNGs when MORPH_KEEP_XPS=1 is set
(CLAUDE.md, "Ad hoc Word probes"). Run on one:

    python scripts/read-word-xps.py src/Tests/Inputs/word/_probe_x/word_output.xps

It prints the family names of the embedded fonts, then one line per glyph run in reading order:
page, baseline origin in points, the em size Word declared, the text, and each glyph's advance
in pixels on Word's 120-dpi grid. For anything more, import it: font_families(path) and
runs(path).

WHAT THE NUMBERS ARE. These are the positions Word DRAWS glyphs at. They are not the widths it
lays text out with. Word draws on an em rounded to whole pixels at 120 dpi (8pt Calibri is
declared 7.8pt), gives each glyph a whole-pixel advance there, lets the pen fall about 5px
behind its linear position inside a run before widening a glyph, catches it up at a space, and
squeezes a line that drew wider than its measure. It BREAKS lines and SIZES autofit columns on
the font's plain linear advances at the size as authored (docs/layout-engine.md, "The crux").
Per-glyph advance tables read from this file - the .wordadvances sidecars the engine measured
with from 2026-08-30 until 2026-10-02 - came out 1.6% under to 4.2% over the width Word breaks
on. So: to learn a layout rule, vary the measure and watch where the break falls. Use this file
for baselines, origins, which face Word really used, and how it spaces what it draws.
"""
import re
import struct
import sys
import zipfile

# Word's layout grid. The page itself is declared in 1/96in, but Word wraps its content in a canvas
# scaled by 96/72, so every origin and em size inside is in points.
GRID_DPI = 120


def unescape(text):
    """UnicodeString is XML-entity-encoded: &quot; is ONE glyph."""
    return (text.replace('&lt;', '<').replace('&gt;', '>').replace('&quot;', '"')
            .replace('&apos;', "'").replace('&amp;', '&'))


def runs(xps_path):
    """Every glyph run, in reading order, as (page, origin_x, origin_y, em_points, text, advances).

    Origins are in points. advances is in pixels on the 120-dpi grid, one entry per character, with
    None where Word wrote no advance - which is always the case for a run's last glyph. A run that
    Word shaped with ligatures carries cluster maps instead, and its advances are then one per
    GLYPH and may be fewer than its characters."""
    out = []
    package = zipfile.ZipFile(xps_path)
    pages = sorted((name for name in package.namelist() if name.endswith('.fpage')),
                   key=lambda name: int(re.search(r'(\d+)\.fpage', name).group(1)))
    for page_number, page in enumerate(pages, 1):
        markup = package.read(page).decode('utf-16')
        elements = []
        for match in re.finditer(r'<Glyphs\b[^>]*>', markup, re.S):
            element = match.group(0)

            def attribute(name, element=element):
                found = re.search(name + r'="([^"]*)"', element)
                if found:
                    return found.group(1)
                return None

            text = attribute('UnicodeString')
            if not text:
                continue
            text = unescape(text)
            em = float(attribute('FontRenderingEmSize'))
            em_pixels = em * GRID_DPI / 72
            indices = attribute('Indices')
            advances = []
            if indices:
                # Each entry is "glyph,advance" with the advance in 1/100 em; either half may be absent,
                # and Word stops writing entries after the last advance it has to state.
                for token in indices.split(';'):
                    parts = token.split(',')
                    if len(parts) > 1 and parts[1]:
                        advances.append(float(parts[1]) / 100 * em_pixels)
                    else:
                        advances.append(None)
            if not indices or '(' not in indices:
                advances += [None] * (len(text) - len(advances))
            elements.append((float(attribute('OriginY')), float(attribute('OriginX')), em, text, advances, bool(indices)))
        elements.sort(key=lambda element: (element[0], element[1]))

        # Word emits some paragraphs as consecutive single-glyph runs with no Indices. Join those back
        # into one run and recover each advance from the next glyph's origin. A glyph more than two ems
        # on is not the next glyph of the same text (a tab, another table cell), so it starts a new run.
        merged = []
        for origin_y, origin_x, em, text, advances, indexed in elements:
            single = not indexed and len(text) == 1
            previous = merged[-1] if merged else None
            if single and previous and previous['joinable'] and previous['y'] == origin_y and previous['em'] == em:
                known = sum(advance for advance in previous['advances'][:-1])
                advance = (origin_x - previous['x']) * GRID_DPI / 72 - known
                if 0 <= advance <= 2 * em * GRID_DPI / 72:
                    previous['advances'][-1] = advance
                    previous['text'] += text
                    previous['advances'].append(None)
                    continue
            merged.append({'x': origin_x, 'y': origin_y, 'em': em, 'text': text, 'advances': list(advances), 'joinable': single})
        for run in merged:
            out.append((page_number, run['x'], run['y'], run['em'], run['text'], run['advances']))
    package.close()
    return out


def font_families(xps_path):
    """The family names of the fonts Word embedded - the check that a probe really rendered in
    the face it asked for and not a substitute. Word swaps in Calibri while an Office cloud font
    is still downloading (docs/fidelity-audit.md, "Render twice"), and the page looks plausible.
    XPS obfuscates each font by XORing its first 32 bytes with the GUID in the file name."""
    package = zipfile.ZipFile(xps_path)
    families = set()
    for name in package.namelist():
        if not name.endswith('.odttf'):
            continue
        guid = re.search(r'([0-9A-Fa-f-]{36})\.odttf', name).group(1).replace('-', '')
        key = bytes.fromhex(guid)[::-1]
        data = bytearray(package.read(name))
        for i in range(32):
            data[i] ^= key[i % 16]
        table_count = struct.unpack('>H', data[4:6])[0]
        for i in range(table_count):
            tag, _, offset, length = struct.unpack('>4sIII', data[12 + 16 * i:28 + 16 * i])
            if tag != b'name':
                continue
            table = data[offset:offset + length]
            count, string_offset = struct.unpack('>HH', table[2:6])
            for record in range(count):
                platform, _, _, name_id, string_length, string_start = struct.unpack(
                    '>HHHHHH', table[6 + 12 * record:18 + 12 * record])
                if name_id == 1 and platform == 3:
                    start = string_offset + string_start
                    families.add(bytes(table[start:start + string_length]).decode('utf-16-be', errors='replace'))
    package.close()
    return families


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    path = sys.argv[1]
    # A console code page that cannot show a glyph must not stop the listing.
    sys.stdout.reconfigure(errors='backslashreplace')
    print('embedded fonts: ' + ', '.join(sorted(font_families(path))))
    for page, origin_x, origin_y, em, text, advances in runs(path):
        shown = ' '.join('-' if advance is None else '%g' % round(advance, 3) for advance in advances)
        print('p%d x=%.2f y=%.2f em=%gpt %r | %s' % (page, origin_x, origin_y, em, text, shown))


if __name__ == '__main__':
    main()
