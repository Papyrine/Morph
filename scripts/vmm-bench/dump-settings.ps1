# Dump Docker Desktop's full live settings JSON to stdout, de-chunked.
# A file rather than an inline -Command string: Git Bash mangles the quoting otherwise.
$ErrorActionPreference = 'Stop'
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
$body = $raw.Substring($raw.IndexOf("`r`n`r`n") + 4)
($body -split "`r`n" | Where-Object { $_ -notmatch '^[0-9a-fA-F]+$' }) -join ''
