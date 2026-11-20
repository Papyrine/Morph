"""Build src/Morph/Spreadsheet/ExcelRowHeights.txt from what probe.ps1 asked of Excel.

Usage:
  python -I build.py --families FONTS_DIR            list the bundled families, one per line, for probe.ps1
  python -I build.py PROBE_OUT TABLE                 write the table from probe.ps1's output

For each family and size the table holds Excel's standard row height twice, in device pixels: on
the 120-dpi screen (Worksheet.StandardHeight of a fresh workbook) and on the 600-dpi printer (the
printed pitch of auto-height rows, read off the XPS export's gridlines). A family is left out when
the XPS shows Excel printed it in another face: a family that is not installed, or an Office cloud
font not yet downloaded, measures the substitute, and those numbers would be wrong for it.
"""
import importlib.util
import os
import re
import statistics
import struct
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [round(6 + 0.5 * k, 1) for k in range(61)]

# read-word-xps.py is loaded as a module; leave no __pycache__ beside it
sys.dont_write_bytecode = True


def xps_reader():
    spec = importlib.util.spec_from_file_location('read_word_xps', os.path.join(HERE, '..', 'read-word-xps.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def bundled_families(fonts_dir):
    """Every family name (name ID 1, Windows platform) a bundled font file declares."""
    families = set()
    for name in sorted(os.listdir(fonts_dir)):
        if not name.lower().endswith(('.ttf', '.otf', '.ttc')):
            continue
        data = open(os.path.join(fonts_dir, name), 'rb').read()
        offsets = [0]
        if data[:4] == b'ttcf':
            count = struct.unpack('>I', data[8:12])[0]
            offsets = list(struct.unpack(f'>{count}I', data[12:12 + 4 * count]))
        for offset in offsets:
            tables = {}
            for i in range(struct.unpack('>H', data[offset + 4:offset + 6])[0]):
                tag, _, start, length = struct.unpack('>4sIII', data[offset + 12 + 16 * i:offset + 28 + 16 * i])
                tables[tag] = start
            if b'name' not in tables:
                continue
            table = tables[b'name']
            count, strings = struct.unpack('>HH', data[table + 2:table + 6])
            for record in range(count):
                platform, _, language, name_id, length, start = struct.unpack(
                    '>HHHHHH', data[table + 6 + 12 * record:table + 18 + 12 * record])
                if platform == 3 and language == 0x409 and name_id == 1:
                    at = table + strings + start
                    families.add(data[at:at + length].decode('utf-16-be', errors='replace'))
    return sorted(families)


def printed_pitch(xps):
    """The printed pitch of the page's rows in 600-dpi pixels: the median gap between gridlines."""
    with zipfile.ZipFile(xps) as package:
        page = next(n for n in package.namelist() if re.search(r'Pages/1\.fpage$', n))
        raw = package.read(page)
    text = raw.decode('utf-16') if raw[:2] in (b'\xff\xfe', b'\xfe\xff') else raw.decode('utf-8')
    ys = sorted({float(m.group(2)) for m in re.finditer(r'<Path\b[^>]*?Data="M ([-\d.]+),([-\d.]+) L ([-\d.]+),\2 "', text)})
    # a gridline is drawn with a hairline rectangle around it, and the page's edge rule sits 0.42pt
    # from the first: gaps under 10px are those, not rows
    gaps = [(b - a) * 600 / 72 for a, b in zip(ys, ys[1:])]
    gaps = [gap for gap in gaps if gap > 10]
    return round(statistics.median(gaps))


def build(probe_out, table_path):
    reader = xps_reader()
    screen = {}
    order = []
    with open(os.path.join(probe_out, 'screen.csv'), encoding='utf-8-sig') as handle:
        next(handle)
        for line in handle:
            family, size, points = line.rstrip('\n').rsplit(',', 2)
            pixels = float(points) * 120 / 72
            if abs(pixels - round(pixels)) > 1e-6:
                raise SystemExit(f'{family} {size}pt: {points}pt is not whole 120-dpi pixels')
            screen[(family, float(size))] = round(pixels)
            if family not in order:
                order.append(family)
    lines = []
    for family in order:
        if any((family, size) not in screen for size in SIZES):
            print(f'left out {family}: its screen readings are incomplete', file=sys.stderr)
            continue
        folder = os.path.join(probe_out, 'printer', re.sub(r'[^A-Za-z0-9]', '_', family))
        printer = []
        substitute = None
        for size in SIZES:
            xps = os.path.join(folder, f'{size:g}.xps')
            embedded = reader.font_families(xps)
            if family.lower() not in {name.lower() for name in embedded}:
                substitute = ', '.join(sorted(embedded))
                break
            printer.append(printed_pitch(xps))
        if substitute is not None:
            print(f'left out {family}: Excel printed it in {substitute}', file=sys.stderr)
            continue
        screens = [screen[(family, size)] for size in SIZES]
        lines.append(f"{family}\t{' '.join(map(str, screens))}\t{' '.join(map(str, printer))}")
    header = [
        "# Excel's standard row height for each family the fonts bundle, in device pixels: the 120-dpi",
        '# screen, then the 600-dpi printer (Microsoft Print to PDF), for the sizes 6 to 36pt in half points.',
        '# Generated by scripts/excel-row-heights (probe.ps1, then build.py); see ExcelRowHeights.cs.',
        '# family<TAB>61 screen heights<TAB>61 printer heights',
    ]
    with open(table_path, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write('\n'.join(header + lines) + '\n')
    print(f'{len(lines)} families written to {table_path}')


def main():
    if len(sys.argv) == 3 and sys.argv[1] == '--families':
        print('\n'.join(bundled_families(sys.argv[2])))
    elif len(sys.argv) == 3:
        build(sys.argv[1], sys.argv[2])
    else:
        print(__doc__)
        sys.exit(2)


main()
