"""Generate the src/Fonts/*.wordadvances sidecars by measuring Word itself.

Windows-only, like every reference-generation path: it drives Word through RenderHelper
(build src/RenderHelper/RenderHelper.csproj first) and reads Word's per-glyph advances out
of the XPS it exports. Run per face, e.g.:

    python scripts/generate-word-advances.py 400     # Calibri regular
    python scripts/generate-word-advances.py 700 400i 300 700i

Why the sidecars exist (evidence in docs/word-features.md, Fonts): Word does not lay text
out on the font's linear hmtx advances. It rounds the em to whole pixels on its 120-dpi
layout grid per size and takes per-glyph GDI natural widths, most of which snap to whole
pixels at text sizes - and the snap depends on the authored point size, not just the
resulting pixel em (10.5pt and 11pt both render on an 18px em with different 'n' advances).
No public API reproduces the values (DirectWrite's GDI-compatible mode rounds cells Word
keeps fractional), so the sidecar memoizes Word directly, keyed by half-point size: for
every size and probe glyph, a 20-repeat run is rendered through Word (MORPH_KEEP_XPS=1)
and the mean advance is stored in px on the 120-dpi grid - the reference grid
CanonicalTextMeasurer accumulates in, so values add into the measurer directly. Cells
within 0.04px of the runtime's linear fallback (design/upm * (round(pt*5/3) + 1/24)) are
omitted. Regenerate whenever a bundled face or the reference Word version changes.
"""
import zipfile, re, os, subprocess, shutil

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INPUTS = os.path.join(REPO, r'src\Tests\Inputs\word')
VSTEST = r'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe'
RH_DLL = os.path.join(REPO, r'src\RenderHelper\bin\Debug\net481\RenderHelper.dll')

SIZES = list(range(12, 61)) + [64, 72, 80, 88, 96, 108, 120, 144]  # half-points: 6..30pt + display
REPS = 20


def chars():
    cs = [chr(c) for c in range(33, 127)]
    cs += ['\u2018', '\u2019', '\u201C', '\u201D', '\u2013', '\u2014', '\u2026',
           '\u00E0', '\u00E9', '\u00E8', '\u00ED', '\u00F3', '\u00FA', '\u00F1',
           '\u00FC', '\u00F6', '\u00E4', '\u00E7', '\u00C9']
    # NO SPACE: a run of consecutive spaces measures the HINTED space (uniform 5px at 12pt),
    # but Word gives a single inter-word space its fractional linear width, pen-rounded per
    # context (table_colors XPS: 'Header 1' space = 4px in the same document whose space-run
    # probe said 5px). Storing the run value made every inter-word gap ~10% wide at 12pt and
    # wrapped lines early corpus-wide, so spaces stay on the runtime's linear fallback.
    return cs


def esc(c):
    return c.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('"', '&quot;')


def build_docx(out, family, bold, italic):
    flags = ('<w:b/>' if bold else '') + ('<w:i/>' if italic else '')
    body = []
    for sz in SIZES:
        rpr = ('<w:rPr><w:rFonts w:ascii="%s" w:hAnsi="%s"/>%s<w:sz w:val="%d"/></w:rPr>'
               % (family, family, flags, sz))
        for c in chars():
            # bracket spaces with 'n' so the run keeps leading/trailing advances honest
            text = ('n' + c * REPS + 'n') if c == ' ' else c * REPS
            body.append('<w:p><w:pPr>%s</w:pPr><w:r>%s'
                        '<w:t xml:space="preserve">%s</w:t></w:r></w:p>' % (rpr, rpr, esc(text)))
    doc = ('<?xml version="1.0" encoding="utf-8"?>'
           '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>'
           + ''.join(body) +
           '<w:sectPr><w:pgSz w:w="12240" w:h="15840" w:orient="portrait" />'
           '<w:pgMar w:top="720" w:right="720" w:bottom="720" w:left="720" /></w:sectPr>'
           '</w:body></w:document>')
    base = os.path.join(INPUTS, 'table_borders', 'input.docx')
    zin = zipfile.ZipFile(base)
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as zout:
        for name in zin.namelist():
            zout.writestr(name, doc.encode('utf-8') if name == 'word/document.xml' else zin.read(name))


def unescape(s):
    return (s.replace('&lt;', '<').replace('&gt;', '>').replace('&quot;', '"')
            .replace('&apos;', "'").replace('&amp;', '&'))


def parse_runs(xps_path):
    """list of (empx_attr, unicode_string, [advance_px_abs, ...]) in reading order"""
    out = []
    z = zipfile.ZipFile(xps_path)
    pages = sorted((n for n in z.namelist() if n.endswith('.fpage')),
                   key=lambda n: int(re.search(r'(\d+)\.fpage', n).group(1)))
    for page in pages:
        d = z.read(page).decode('utf-16')
        els = []
        for m in re.finditer(r'<Glyphs\b[^>]*>', d, re.S):
            el = m.group(0)

            def attr(name, el=el):
                a = re.search(name + r'="([^"]*)"', el)
                return a.group(1) if a else None

            text = attr('UnicodeString')
            if not text:
                continue
            em = float(attr('FontRenderingEmSize'))
            empx = em * 120 / 72
            ind = attr('Indices')
            advs = [None] * len(text)
            if ind:
                advs = []
                for tok in ind.split(';'):
                    parts = tok.split(',')
                    advs.append(float(parts[1]) / 100 * empx if len(parts) > 1 and parts[1] else None)
            els.append((float(attr('OriginY')), float(attr('OriginX')), empx, unescape(text), advs))
        els.sort()
        # Word emits some paragraphs as consecutive single-glyph runs with no Indices;
        # recover their advances from the OriginX deltas (page units are pt; px = pt*5/3)
        merged = []
        for el in els:
            oy, ox, empx, text, advs = el
            if (merged and len(text) == 1 and advs == [None]
                    and merged[-1][0] == oy and len(merged[-1][3]) >= 1
                    and merged[-1][3][-1] == text and merged[-1][4][-1] is None):
                py, px_, pempx, ptext, padvs = merged[-1]
                # advance of the previous glyph = this X minus the previous pen X
                pen = px_ + sum(a for a in padvs[:-1] if a is not None) * 72 / 120
                padvs[-1] = (ox - pen) * 120 / 72
                merged[-1] = (py, px_, pempx, ptext + text, padvs + [None])
            else:
                merged.append((oy, ox, empx, text, list(advs)))
        for oy, ox, empx, text, advs in merged:
            out.append((empx, text, advs))
    z.close()
    return out


def run_word(scenario):
    env = dict(os.environ, MORPH_KEEP_XPS='1')
    r = subprocess.run([VSTEST, RH_DLL, '/TestCaseFilter:FullyQualifiedName~' + scenario],
                       capture_output=True, text=True, env=env)
    if 'Test Run Successful' not in r.stdout:
        print(r.stdout[-3000:])
        raise RuntimeError('word render failed')


def design_units(font_path):
    """read upm + per-codepoint advances from the ttf (head/hhea/hmtx/cmap fmt4)"""
    import struct
    data = open(font_path, 'rb').read()
    num = struct.unpack('>H', data[4:6])[0]
    tables = {}
    for i in range(num):
        tag, cks, off, ln = struct.unpack('>4sIII', data[12 + 16 * i:28 + 16 * i])
        tables[tag] = off
    off = tables[b'head']
    upm = struct.unpack('>H', data[off + 18:off + 20])[0]
    off = tables[b'hhea']
    num_h = struct.unpack('>H', data[off + 34:off + 36])[0]
    off = tables[b'hmtx']
    aw = [struct.unpack('>H', data[off + 4 * i:off + 4 * i + 2])[0] for i in range(num_h)]
    coff = tables[b'cmap']
    ntab = struct.unpack('>H', data[coff + 2:coff + 4])[0]
    sub = None
    for i in range(ntab):
        pid, eid, so = struct.unpack('>HHI', data[coff + 4 + 8 * i:coff + 12 + 8 * i])
        if (pid, eid) in ((3, 1), (0, 3), (0, 4)):
            sub = coff + so
            break
    fmt = struct.unpack('>H', data[sub:sub + 2])[0]
    assert fmt == 4, fmt
    cmap = {}
    segX2 = struct.unpack('>H', data[sub + 6:sub + 8])[0]
    seg = segX2 // 2
    ends = struct.unpack('>%dH' % seg, data[sub + 14:sub + 14 + segX2])
    starts = struct.unpack('>%dH' % seg, data[sub + 16 + segX2:sub + 16 + 2 * segX2])
    deltas = struct.unpack('>%dh' % seg, data[sub + 16 + 2 * segX2:sub + 16 + 3 * segX2])
    rngOffPos = sub + 16 + 3 * segX2
    rngOff = struct.unpack('>%dH' % seg, data[rngOffPos:rngOffPos + segX2])
    for i in range(seg):
        if starts[i] == 0xFFFF:
            continue
        for cp in range(starts[i], min(ends[i], 0xFFFE) + 1):
            if rngOff[i] == 0:
                g = (cp + deltas[i]) & 0xFFFF
            else:
                gpos = rngOffPos + 2 * i + rngOff[i] + 2 * (cp - starts[i])
                g = struct.unpack('>H', data[gpos:gpos + 2])[0]
                if g:
                    g = (g + deltas[i]) & 0xFFFF
            if g:
                cmap[cp] = g
    adv = {}
    for cp, g in cmap.items():
        adv[cp] = aw[g] if g < len(aw) else aw[-1]
    return upm, adv


def generate(font_file, family, bold, italic):
    scenario = '_probe_advgen'
    outdir = os.path.join(INPUTS, scenario)
    if not os.path.exists(os.path.join(outdir, 'word_output.xps')):
        os.makedirs(outdir, exist_ok=True)
        build_docx(os.path.join(outdir, 'input.docx'), family, bold, italic)
        run_word(scenario)
    upm, dadv = design_units(os.path.join(REPO, 'src', 'Fonts', font_file))

    # consume glyph runs in reading order, matching the authored (size, char) sequence;
    # every paragraph line trails an artifact space-only run (the paragraph mark) - skip those
    expected = [(sz, c) for sz in SIZES for c in chars()]
    runs = parse_runs(os.path.join(outdir, 'word_output.xps'))
    results = {}
    ei = 0
    pending = []

    def flush(sz, ch, advs):
        # for the bracketed space paragraph keep only the space advances
        vals = [a for c2, a in advs if c2 == ch and a is not None]
        if vals:
            results[(sz, ch)] = sum(vals) / len(vals)

    for empx, text, advs in runs:
        if ei >= len(expected):
            break
        sz, ch = expected[ei]
        if text.strip(' ') == '' and ch != ' ':
            continue
        if ch == ' ' and text.strip(' ') == '' and not pending:
            continue
        for ch2, a in zip(text, advs):
            sz, ch = expected[ei]
            total = REPS + 2 if ch == ' ' else REPS
            pending.append((ch2, a))
            if len(pending) == total:
                flush(sz, ch, pending)
                pending = []
                ei += 1
                if ei >= len(expected):
                    break
    assert ei == len(expected), (ei, len(expected))

    kept = 0
    sidecar = os.path.join(REPO, 'src', 'Fonts', os.path.splitext(font_file)[0] + '.wordadvances')
    with open(sidecar, 'w', newline='\n') as f:
        f.write('# Word-measured advances, px on the 120-dpi layout grid, keyed by half-point size.\n')
        f.write('# Generated from Word XPS output via RenderHelper (scripts context: see todo/docs).\n')
        f.write('# Codepoints absent at a listed size, and sizes absent entirely, fall back to\n')
        f.write('# linear: design/upm * (round(pt*120/72)*  + 1/24).\n')
        cur = None
        for (sz, ch), mean in sorted(results.items()):
            cp = ord(ch)
            if cp not in dadv:
                continue
            empx = round(sz / 2 * 120 / 72)
            linear = dadv[cp] / upm * (empx + 1 / 24)
            if abs(mean - linear) < 0.04:
                continue
            if sz != cur:
                f.write('sz %d\n' % sz)
                cur = sz
            f.write('%d %.3f\n' % (cp, mean))
            kept += 1
    print(font_file, 'kept', kept, 'of', len(results), '->', sidecar)
    shutil.rmtree(outdir)


if __name__ == '__main__':
    import sys
    faces = {
        '400': ('Calibri_400.ttf', 'Calibri', False, False),
        '700': ('Calibri_700.ttf', 'Calibri', True, False),
        '400i': ('Calibri_400_Italic.ttf', 'Calibri', False, True),
        '300': ('Calibri_300.ttf', 'Calibri Light', False, False),
        '700i': ('Calibri_700_Italic.ttf', 'Calibri', True, True),
    }
    for which in sys.argv[1:] or ['400']:
        generate(*faces[which])
