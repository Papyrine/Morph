# Accept all received test files as verified baselines
# Usage: powershell -ExecutionPolicy Bypass -File accept-received.ps1

$files = Get-ChildItem -Path $PSScriptRoot\Inputs -Recurse -Filter '*.received.*'
$count = 0
foreach ($file in $files) {
    $newName = $file.FullName -replace '\.received\.', '.verified.'
    Copy-Item $file.FullName $newName -Force
    Remove-Item $file.FullName
    Write-Host "Accepted: $($file.Name)"
    $count++
}
Write-Host "`nAccepted $count files"
