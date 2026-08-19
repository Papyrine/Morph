# Read the VMM backend Docker Desktop is actually running, straight from its own settings
# API (the named pipe the GUI uses). A run labelled "dockervmm" that is still on WSL 2 is
# the one failure mode that would silently invalidate the whole comparison, so bench.sh
# records this rather than trusting the label.
#
# Kept as a file rather than a -Command string: Windows PowerShell 5.1 mis-parses the
# constructor call when the script arrives as one quoted argument from Git Bash.

$ErrorActionPreference = 'Stop'
try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream '.', 'dockerBackendApiServer', ([System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(5000)
    $writer = New-Object System.IO.StreamWriter $pipe
    $writer.NewLine = "`r`n"
    $writer.AutoFlush = $true
    $writer.WriteLine('GET /app/settings HTTP/1.1')
    $writer.WriteLine('Host: localhost')
    $writer.WriteLine('Connection: close')
    $writer.WriteLine('')
    $reader = New-Object System.IO.StreamReader $pipe
    $raw = $reader.ReadToEnd()
    $pipe.Dispose()

    function Field($pattern) {
        $m = [regex]::Match($raw, $pattern)
        if ($m.Success) { $m.Groups[1].Value } else { '?' }
    }

    $wsl  = Field '"wslEngineEnabled":\{[^}]*"value":(true|false)'
    $krun = Field '"useLibkrun":(true|false)'
    $mem  = Field '"memoryMiB":\{[^}]*"value":([0-9]+)'
    $cpu  = Field '"cpus":\{[^}]*"value":([0-9]+)'
    $swap = Field '"swapMiB":\{[^}]*"value":([0-9]+)'

    "wslEngineEnabled=$wsl;useLibkrun=$krun;memoryMiB=$mem;cpus=$cpu;swapMiB=$swap"
}
catch {
    "probe_failed=$($_.Exception.Message -replace '[;\r\n]', ' ')"
}
