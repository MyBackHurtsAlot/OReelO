$ErrorActionPreference = "Stop"

$installDir = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "SequenceWheelHelper"))
$allowedRoot = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
if (-not $installDir.StartsWith($allowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove a path outside LocalAppData."
}

Get-Process -Name "SequenceWheelHelper" -ErrorAction SilentlyContinue | Stop-Process
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "SequenceWheelHelper" -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}

Write-Host "OReelO Helper uninstalled."
