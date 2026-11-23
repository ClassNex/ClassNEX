param(
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [Parameter(Mandatory)][string]$DesktopDirectory,
    [Parameter(Mandatory)][string]$Launcher
)
$ErrorActionPreference = 'Stop'
foreach ($binary in @($Launcher, (Join-Path $DesktopDirectory 'ClassNex.exe'), (Join-Path $DesktopDirectory 'ClassNex.dll'))) {
    $file = Get-Item -LiteralPath $binary
    $actual = $file.VersionInfo.ProductVersion
    if ($actual -cne $ExpectedVersion) { throw "Product version of '$($file.FullName)' is '$actual'; expected '$ExpectedVersion'." }
}
