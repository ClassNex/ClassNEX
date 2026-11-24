#requires -Version 7.2
param(
    [string]$CiSource = 'E:/ci-source',
    [string]$CiRuntime = 'E:/ci-app/ClassIsland_app_windows_x64_full_folder/app-2.1.0.1-0',
    [string]$ClientRuntime,
    [ValidateSet('dark','light')][string]$Theme = 'dark',
    [ValidatePattern('^#[0-9a-fA-F]{6}$')][string]$Accent = '#459BAC',
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Round = ('ci-rail-' + [guid]::NewGuid().ToString('N').Substring(0,8))
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$ClientRuntime) { $ClientRuntime = Join-Path $root 'src/ClassNex/bin/Debug/net10.0-windows10.0.19041.0' }
$CiRuntime = (Resolve-Path -LiteralPath $CiRuntime).Path
$ClientRuntime = (Resolve-Path -LiteralPath $ClientRuntime).Path
$pin = '235914a2fac1733cc2a169457c7bad4edb2ca2f6'
$inputFile = 'ClassIsland/XamlThemes/FluentTheme/Styles.axaml'
$source = git -C $CiSource show "${pin}:$inputFile"
if ($LASTEXITCODE -or !$source) { throw "Could not read $inputFile at $pin" }
$work = Join-Path $root ('build/rail-probe-' + [guid]::NewGuid().ToString('N'))
$out = Join-Path $root "artifacts/ui-review/$Round"
if (Test-Path -LiteralPath $out) { throw "Output already exists: $out" }
$null = [IO.Directory]::CreateDirectory($work)
$null = [IO.Directory]::CreateDirectory($out)
$inputPath = Join-Path $work 'pinned-ci.axaml'
[IO.File]::WriteAllText($inputPath, ($source -join "`n"), [Text.UTF8Encoding]::new($false))
$proj = Join-Path $PSScriptRoot 'NotificationRailProbe/NotificationRailProbe.csproj'
$outputApp = Join-Path $work 'app'
dotnet build $proj -c Release "-p:CiRailRuntime=$CiRuntime" --artifacts-path (Join-Path $work 'obj-bin') -o $outputApp --nologo -v:q
if ($LASTEXITCODE) { throw 'The standalone rail probe did not build' }
$app = Join-Path $outputApp 'NotificationRailProbe.dll'
foreach ($side in @('reference','candidate')) {
    $runtime = if ($side -eq 'reference') { $CiRuntime } else { $ClientRuntime }
    $markup = if ($side -eq 'reference') { $inputPath } else { Join-Path $root 'src/ClassNex/Views/MainWindow.axaml' }
    dotnet $app $side $runtime $CiRuntime $markup (Join-Path $out $side) $Theme.ToLowerInvariant() $Accent
    if ($LASTEXITCODE) { throw "$side rail did not render; output: $out" }
}
$original = Get-Content -Raw -LiteralPath (Join-Path $out 'reference/result.json') | ConvertFrom-Json
$candidate = Get-Content -Raw -LiteralPath (Join-Path $out 'candidate/result.json') | ConvertFrom-Json
if ($original.Avalonia -notlike '11.3.17*' -or $original.Theme -notlike '2.4.1*') { throw 'Unexpected upstream Avalonia or FluentAvalonia runtime version' }
Add-Type -AssemblyName System.Drawing.Common
$captures = foreach ($stage in $original.Shots) {
    $target = $candidate.Shots | Where-Object Name -eq $stage.Name
    $a = [Drawing.Bitmap]::new((Join-Path $out ("reference/" + $stage.Name + '.png')))
    $b = [Drawing.Bitmap]::new((Join-Path $out ("candidate/" + $stage.Name + '.png')))
    try {
        if ($a.Size -ne $b.Size) { throw 'Capture dimensions differ' }
        $changed = 0
        for ($y = 0; $y -lt $a.Height; $y++) {
            for ($x = 0; $x -lt $a.Width; $x++) {
                if ($a.GetPixel($x,$y).ToArgb() -ne $b.GetPixel($x,$y).ToArgb()) { $changed++ }
            }
        }
        $geometry = @('Value','X','Y','Width','Height','FillX','FillY','FillWidth','FillHeight') |
            Where-Object { [math]::Abs($stage.$_ - $target.$_) -gt 0.001 }
        [pscustomobject]@{ Progress=$stage.Value; DifferentPixels=$changed; TotalPixels=$a.Width * $a.Height; DifferentGeometry=@($geometry | Where-Object { $_ }); ReferenceFill=$stage.Foreground; CandidateFill=$target.Foreground; Match=($changed -eq 0 -and !$geometry) }
    } finally { $a.Dispose(); $b.Dispose() }
}
$hashes = $original.Shots | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $out ("reference/" + $_.Name + '.png'))).Hash } | Sort-Object -Unique
if (@($hashes).Count -ne 3) { throw 'At least two reference frames rendered the same image' }
$passed = !($captures | Where-Object { !$_.Match })
$report = [ordered]@{ Passed=$passed; ReferenceCommit=$pin; ReferenceFile=$inputFile; ReferenceAvalonia=$original.Avalonia; ReferenceTheme=$original.Theme; CandidateAvalonia=$candidate.Avalonia; CandidateTheme=$candidate.Theme; Pixels='320 x 40 at 96 DPI'; ReferenceLayoutScaling=$original.LayoutScaling; CandidateLayoutScaling=$candidate.LayoutScaling; Theme=$Theme; Accent=$Accent; Captures=$captures }
[IO.File]::WriteAllText((Join-Path $out 'comparison.json'), ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$captures | Format-Table Progress,DifferentPixels,TotalPixels,Match
if (!$passed) { throw "Rail comparison differs: $out/comparison.json" }
"PASS: fixed-progress reference and candidate match; $out/comparison.json"
