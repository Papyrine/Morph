<#
.SYNOPSIS
Asks Excel, over COM, for the two standard row heights its printed rows are built from, for every
family in a list and every size from 6 to 36pt in half points.

.DESCRIPTION
Excel prints a row at round(zoom * floor(u * printerStd / (4 * screenStd) + 1/8)) printer pixels,
u being the row's height in quarter pixels of the screen it was laid out on (see
src/Morph/Spreadsheet/ExcelRowHeights.cs). Both standard heights are the Normal style's font's
standard row height, one measured for the screen and one for the printer, and neither follows from
the font's tables, so they are asked of Excel:

  screen   Worksheet.StandardHeight of a FRESH workbook whose Normal font is the face and size.
           A workbook reused across sizes reports stale values, and an existing sheet keeps a
           stored one, so every reading is a new workbook.
  printer  The printed pitch of auto-height rows, read from an XPS export by build.py.

The numbers belong to the machine they are measured on: the screen's DPI and the printer driver's
resolution both enter them. The corpus references were rendered at 120 dpi to Microsoft Print to
PDF at 600 dpi, so the script refuses to run anywhere else.

.EXAMPLE
./probe.ps1 -Families families.txt -Out C:\temp\excel-row-heights
then: python -I build.py C:\temp\excel-row-heights ..\..\src\Morph\Spreadsheet\ExcelRowHeights.txt
#>
param(
    [Parameter(Mandatory)] [string]$Families,
    [Parameter(Mandatory)] [string]$Out,
    [double]$From = 6,
    [double]$To = 36,
    [double]$Step = 0.5
)
$ErrorActionPreference = 'Stop'

Add-Type @"
using System.Runtime.InteropServices;
public static class ProbeDpi {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
}
"@
[ProbeDpi]::SetProcessDPIAware() | Out-Null
$dpi = [ProbeDpi]::GetDpiForSystem()
if ($dpi -ne 120) {
    throw "The screen is $dpi dpi; the table is of a 120-dpi screen (125% scaling)."
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$printed = Join-Path $Out 'printer'
New-Item -ItemType Directory -Force -Path $printed | Out-Null
$screenCsv = Join-Path $Out 'screen.csv'
if (-not (Test-Path $screenCsv)) {
    'family,size,points' | Set-Content $screenCsv -Encoding UTF8
}
$done = @{}
foreach ($line in Get-Content $screenCsv -Encoding UTF8 | Select-Object -Skip 1) {
    $done[$line.Substring(0, $line.LastIndexOf(','))] = $true
}

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
$xl.ScreenUpdating = $false
try {
    if ($xl.ActivePrinter -notlike 'Microsoft Print to PDF*') {
        throw "Excel's printer is '$($xl.ActivePrinter)'; the table is of Microsoft Print to PDF."
    }
    $sizes = @()
    for ($s = $From; $s -le $To + 1e-9; $s += $Step) { $sizes += [Math]::Round($s, 1) }

    foreach ($family in Get-Content $Families -Encoding UTF8) {
        if (-not $family) { continue }
        $folder = Join-Path $printed ($family -replace '[^A-Za-z0-9]', '_')
        New-Item -ItemType Directory -Force -Path $folder | Out-Null

        # The printer side: one workbook, its Normal font stepped through the sizes, a page of
        # auto-height rows with gridlines exported at each.
        $wb = $xl.Workbooks.Add()
        while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }
        $ws = $wb.Worksheets.Item(1)
        $ws.Range('A1:B6').Value2 = 'x'
        $setup = $ws.PageSetup
        $setup.PaperSize = 9
        $setup.Zoom = 100
        $setup.PrintGridlines = $true
        $normal = $wb.Styles.Item('Normal')
        $normal.Font.Name = $family
        foreach ($size in $sizes) {
            $xps = Join-Path $folder ('{0}.xps' -f $size)
            if (Test-Path $xps) { continue }
            $normal.Font.Size = $size
            $wb.ExportAsFixedFormat(1, $xps, 0, $true, $false)
        }
        $wb.Close($false)

        # The screen side: a fresh workbook for every size.
        foreach ($size in $sizes) {
            $key = '{0},{1}' -f $family, $size
            if ($done.ContainsKey($key)) { continue }
            $fresh = $xl.Workbooks.Add()
            $font = $fresh.Styles.Item('Normal').Font
            $font.Name = $family
            $font.Size = $size
            $points = $fresh.Worksheets.Item(1).StandardHeight
            $fresh.Close($false)
            '{0},{1}' -f $key, $points | Add-Content $screenCsv -Encoding UTF8
        }
        "$family done"
    }
}
finally {
    $xl.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
}
