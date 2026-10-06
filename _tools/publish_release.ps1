# ClassNEX one-click release upload.
# Usage:  right-click -> Run with PowerShell   (or)   pwsh -File publish_release.ps1
# It picks the newest build\ClassNEX-*-Alpha-win-x64.zip automatically,
# asks for your GitHub token (hidden input), then creates the prerelease and uploads the zip.

param(
    [string]$Zip = "",
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'
$Root = 'E:\ClassNex'

if (-not $Zip) {
    $Zip = Get-ChildItem "$Root\build\ClassNEX-*-Alpha-win-x64.zip" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Zip) { Write-Host "ERROR: no zip found under $Root\build" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $Zip)) { Write-Host "ERROR: zip not found: $Zip" -ForegroundColor Red; exit 1 }

if (-not $Version -and ($Zip -match 'ClassNEX-([0-9A-Za-z]+)-Alpha')) { $Version = $Matches[1] }
if (-not $Version) { Write-Host "ERROR: cannot parse version from zip name, pass -Version" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "  zip     : $Zip" -ForegroundColor Cyan
Write-Host "  size    : $([math]::Round((Get-Item $Zip).Length/1MB,1)) MB" -ForegroundColor Cyan
Write-Host "  version : $Version" -ForegroundColor Cyan
Write-Host "  tag     : ${Version}_Alpha" -ForegroundColor Cyan
Write-Host "  notes   : $Root\RELEASE_NOTES.md" -ForegroundColor Cyan
Write-Host ""

$sec = Read-Host "Paste your GitHub token (hidden, then press Enter)" -AsSecureString
$env:GITHUB_TOKEN = [System.Net.NetworkCredential]::new('', $sec).Password
if (-not $env:GITHUB_TOKEN) { Write-Host "ERROR: empty token" -ForegroundColor Red; exit 1 }

$env:GH_TAG = "${Version}_Alpha"
$env:GH_TITLE = "ClassNEX ${Version}_Alpha"

Write-Host ""
Write-Host "Uploading..." -ForegroundColor Yellow
python "$Root\_tools\gh_release.py" $Zip
if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: upload failed (exit $LASTEXITCODE)" -ForegroundColor Red; exit $LASTEXITCODE }

Write-Host ""
Write-Host "Done. Verifying on GitHub..." -ForegroundColor Yellow
try {
    $releases = Invoke-RestMethod "https://api.github.com/repos/ClassNex/ClassNEX/releases" -Headers @{ 'User-Agent' = 'ClassNex-Release' } -TimeoutSec 60
    $releases | Select-Object tag_name, name, @{ N = 'asset'; E = { ($_.assets | ForEach-Object { $_.name }) -join ',' } }, published_at |
        Format-Table -AutoSize
}
catch { Write-Host "Verify skipped: $($_.Exception.Message)" -ForegroundColor DarkYellow }
