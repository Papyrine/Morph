# Excel's standard row heights

`src/Morph/Spreadsheet/ExcelRowHeights.txt` holds, for every family the bundled fonts declare and
every size from 6 to 36pt in half points, the two standard row heights Excel prints rows through:
its screen's (120 dpi) and its printer's (600 dpi), in whole pixels. `ExcelRowHeights.cs` has the
rule they feed:

    printed px = round(zoom × floor(u × printerStd / (4 × screenStd) + 1/8))

where `u` is the row's height in quarter pixels of the screen (0.15pt), and why neither height
can be computed from the font file (the screen's is measured through DirectWrite and is not any
ratio of the font's tables; the printer's is GDI's cell plus 8px on most faces but not on Segoe UI,
Gill Sans MT, Century Gothic, Batang or Verdana 11).

## Regenerating

On Windows with Excel, a 120-dpi screen (125% scaling) and Microsoft Print to PDF as the default
printer; `probe.ps1` checks the first and last and refuses otherwise.

```powershell
python -I build.py --families ..\..\src\Fonts > $env:TEMP\families.txt
./probe.ps1 -Families $env:TEMP\families.txt -Out $env:TEMP\excel-row-heights
python -I build.py $env:TEMP\excel-row-heights ..\..\src\Morph\Spreadsheet\ExcelRowHeights.txt
```

The probe takes about two hours for ~100 families: an XPS export per size for the printer, and a
fresh workbook per size for the screen. It resumes where it stopped. `build.py` leaves out a family
Excel printed in another face (it reads the fonts the XPS embeds): one that is not installed, a
variable font Excel substitutes (Bahnschrift prints as Calibri), or an Office cloud font not yet
downloaded.

## What the numbers depend on

- **The screen's DPI.** Excel lays rows out in its screen's pixels, so the same file prints
  differently from a 96-dpi screen: Arial 10's standard height is 17px there and 22px at 120 dpi,
  and its 30pt rows print 3% short instead of 6.4%. The corpus references come from a 120-dpi
  screen, so the table is of one.
- **The printer's resolution.** Microsoft Print to PDF is 600 dpi.
- **Not** the theme, `x14ac:knownFonts`, `dyDescent` or `defaultRowHeight` — each varied with no
  effect on a fresh workbook's standard height.

## Probe hygiene

- `Worksheet.StandardHeight` is only trustworthy on a **fresh** workbook. Change the Normal font
  repeatedly in one workbook and it lags or sticks (one scan returned Trebuchet MS's values for Times
  New Roman). An existing sheet can also keep a stored value: simple-yellow-standard-invoice's sheet
  reports 13.8 for Corbel 11 whatever its Normal font is set to, where a new sheet in the same
  workbook reports 14.4. The printed rows are always measured afresh.
- Read printed rows off the XPS **gridlines** (`PrintGridlines`), which Excel writes as vector paths
  on 600-dpi pixels. On a real template, clear the cells' formats and delete the drawings first, or
  borders and fills interleave with the gridlines.
- Verify the paper is A4 by the page the export produces (1123x794 at 96 dpi), never by what Excel or
  `Get-PrintConfiguration` reports: Excel reports the A4 it was asked for while a Letter driver
  exports Letter.
- Validate a hand-built fixture against a real workbook. Every fixture cloned from one minimal base
  carries that base's body font, which is the variable that decides row heights.
