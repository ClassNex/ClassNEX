param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$SourceCommit,
    [string]$RestoreSource
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sha = (git -C $repo rev-parse --verify "${SourceCommit}^{commit}")
if ($LASTEXITCODE -ne 0 -or $sha -ne $SourceCommit) { throw 'The full source commit must exist locally.' }
$alpha = (git -C $repo rev-parse --verify '26w46d_Alpha^{commit}')
if ($LASTEXITCODE -ne 0 -or $alpha -ne '60d7b575b1b866a0bc388c9b80986c546950a2c7') { throw 'The expected Alpha baseline is unavailable.' }
git -C $repo merge-base --is-ancestor $alpha $sha
if ($LASTEXITCODE -ne 0) { throw 'The candidate does not descend from the specified Alpha.' }

$version = '26w46d-Refactor'
$output = Join-Path $repo 'build'
[void][IO.Directory]::CreateDirectory($output)
$run = (New-Item -ItemType Directory -Path (Join-Path $output ("portable-{0}-{1}" -f $sha.Substring(0, 8), [Guid]::NewGuid().ToString('N').Substring(0, 8)))).FullName
$sourceZip = Join-Path $run 'source.zip'
$source = Join-Path $run 'source'
$package = Join-Path $run 'package'
$payload = Join-Path $package "app-$version-0"
$launcher = Join-Path $run 'launcher'
$pending = Join-Path $run 'unverified.zip'
$zip = Join-Path $run "ClassNEX-$version-Alpha-win-x64.zip"

$inputs = @('src', 'LICENSE', 'tools/make_files_json.py')
$top = @(git -C $repo ls-tree --name-only $sha)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate the source tree.' }
foreach ($name in $top) {
    if ($name -match '^(Directory\.(Build\.(props|targets|rsp)|Packages\.props)|global\.json|NuGet\.Config|packages\.lock\.json|\.editorconfig)$') { $inputs += $name }
}
git -C $repo archive --format=zip "--output=$sourceZip" $sha -- @inputs
if ($LASTEXITCODE -ne 0) { throw 'Could not export the exact source commit.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($sourceZip, $source)

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=none', '-p:DebugSymbols=false', "-p:InformationalVersion=$version", "-p:SourceRevisionId=$sha")
if ($RestoreSource) { $common += "-p:RestoreSources=$RestoreSource" }
Push-Location -LiteralPath $source
try {
    dotnet publish 'src/ClassNex/ClassNex.csproj' @common --artifacts-path (Join-Path $run 'desktop-obj') -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    dotnet publish 'src/ClassNex.Launcher/ClassNex.Launcher.csproj' @common --artifacts-path (Join-Path $run 'launcher-obj') -p:PublishSingleFile=true -p:PublishTrimmed=true -o $launcher
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
} finally { Pop-Location }

$sample = Join-Path $payload 'Assets\timetable.yaml'
if (Test-Path -LiteralPath $sample -PathType Leaf) {
    $original = Join-Path $source 'src\ClassNex\Assets\timetable.yaml'
    if ((Get-FileHash -LiteralPath $sample).Hash -ne (Get-FileHash -LiteralPath $original).Hash) { throw 'Published sample is not the tracked fixture.' }
    Remove-Item -LiteralPath $sample
}
$notices = (New-Item -ItemType Directory -Path (Join-Path $payload 'Licenses')).FullName
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $notices 'ClassNex.txt')
foreach ($notice in @('HarmonyOS-Sans-LICENSE.txt', 'lucide-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $source "src\ClassNex\Assets\Fonts\$notice") -Destination (Join-Path $notices $notice)
}
$buildInfo = [ordered]@{ kind = 'refactor-candidate'; sourceCommit = $sha; baseCommit = $alpha } | ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $payload 'BuildInfo.json'), $buildInfo, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $launcher 'ClassNex.exe') -Destination (Join-Path $package 'ClassNex.exe')
[IO.File]::WriteAllBytes((Join-Path $package 'PackageType'), [Text.Encoding]::ASCII.GetBytes('folder'))
py -3 -B (Join-Path $source 'tools\make_files_json.py') $package $version 0
if ($LASTEXITCODE -ne 0) { throw 'Cannot generate the portable update manifest.' }
[IO.Compression.ZipFile]::CreateFromDirectory($package, $pending, [IO.Compression.CompressionLevel]::Optimal, $false)
$json = py -3 -B (Join-Path $PSScriptRoot 'verify_portable.py') $pending
if ($LASTEXITCODE -ne 0) { throw 'The candidate ZIP failed independent verification; do not distribute it.' }
$result = ($json -join "`n") | ConvertFrom-Json
if ($result.sourceCommit -ne $sha) { throw 'The archived provenance does not match the source export.' }
Move-Item -LiteralPath $pending -Destination $zip
$result.archive = $zip
$evidence = $result | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $run 'verification.json'), $evidence, [Text.UTF8Encoding]::new($false))
$evidence
