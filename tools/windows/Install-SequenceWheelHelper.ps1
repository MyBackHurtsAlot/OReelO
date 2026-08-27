$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "SequenceWheelHelper.exe"
$installDir = Join-Path $env:LOCALAPPDATA "SequenceWheelHelper"
$target = Join-Path $installDir "SequenceWheelHelper.exe"

if (-not (Test-Path -LiteralPath $source)) {
    throw "SequenceWheelHelper.exe not found next to this installer."
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -LiteralPath $source -Destination $target -Force
New-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "SequenceWheelHelper" -Value ('"' + $target + '"') -PropertyType String -Force | Out-Null
Start-Process -FilePath $target -WindowStyle Hidden

Write-Host "Sequence Wheel Helper installed and started."
