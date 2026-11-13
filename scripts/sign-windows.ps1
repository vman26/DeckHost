# Signs Windows files with the code-signing certificate from the environment:
#   WINDOWS_CERTIFICATE           the .pfx, base64-encoded
#   WINDOWS_CERTIFICATE_PASSWORD  its password
#   WINDOWS_TIMESTAMP_URL         optional (default: DigiCert's public timestamp server)
# Without a certificate it does nothing, so unsigned builds keep working.
#
#   pwsh scripts/sign-windows.ps1 <file> [<file> ...]
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Files)
$ErrorActionPreference = 'Stop'

if (-not $env:WINDOWS_CERTIFICATE) {
    Write-Host 'No WINDOWS_CERTIFICATE: leaving the files unsigned.'
    exit 0
}

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1
if (-not $signtool) { throw 'signtool.exe not found (Windows SDK).' }

$pfx = Join-Path $env:RUNNER_TEMP 'deckhost-signing.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:WINDOWS_CERTIFICATE))
$timestamp = if ($env:WINDOWS_TIMESTAMP_URL) { $env:WINDOWS_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
try {
    foreach ($file in $Files) {
        & $signtool.FullName sign /f $pfx /p $env:WINDOWS_CERTIFICATE_PASSWORD /fd SHA256 /tr $timestamp /td SHA256 /d DeckHost $file
        if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file" }
    }
}
finally {
    Remove-Item $pfx -Force -ErrorAction SilentlyContinue
}
