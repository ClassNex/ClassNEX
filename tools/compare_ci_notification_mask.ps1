#requires -Version 7.2
param(
    [string]$CiSource = 'E:/ci-source',
    [string]$CiRuntime = 'E:/ci-app/ClassIsland_app_windows_x64_full_folder/app-2.1.0.1-0',
    [string]$ClientRuntime,
    [ValidateSet('dark','light')][string]$Theme = 'dark',
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Round = ('ci-mask-' + [guid]::NewGuid().ToString('N').Substring(0,8))
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$ClientRuntime) { $ClientRuntime = Join-Path $root 'src/ClassNex/bin/Debug/net10.0-windows10.0.19041.0' }
$ClientRuntime = (Resolve-Path -LiteralPath $ClientRuntime).Path
$CiRuntime = (Resolve-Path -LiteralPath $CiRuntime).Path
$pin = '235914a2fac1733cc2a169457c7bad4edb2ca2f6'
$upstreamFile = 'ClassIsland/XamlThemes/FluentTheme/Styles.axaml'
$work = Join-Path $root ('build/mask-probe-' + [guid]::NewGuid().ToString('N'))
$out = Join-Path $root "artifacts/ui-review/$Round"
if (Test-Path -LiteralPath $out) { throw "Output already exists: $out" }
$null = [IO.Directory]::CreateDirectory($work)
$null = [IO.Directory]::CreateDirectory($out)
$null = New-Item -ItemType File -Path (Join-Path $work '.classnex-mask-probe')
$archive = Join-Path $work 'source.zip'
git -C $CiSource archive --format=zip "--output=$archive" $pin -- $upstreamFile
if ($LASTEXITCODE) { throw 'Cannot extract upstream notification mask' }
$source = Join-Path $work 'upstream'
Expand-Archive -LiteralPath $archive -DestinationPath $source
$probe = Join-Path $work 'probe'
dotnet build (Join-Path $PSScriptRoot 'NotificationMaskProbe/NotificationMaskProbe.csproj') -c Release "-p:CiRuntime=$CiRuntime" --artifacts-path (Join-Path $work 'obj-bin') -o $probe --nologo -v:q
if ($LASTEXITCODE) { throw 'Mask probe did not compile' }
foreach ($side in @('reference','candidate')) {
    $runtime = if ($side -eq 'reference') { $CiRuntime } else { $ClientRuntime }
    dotnet (Join-Path $probe 'NotificationMaskProbe.dll') $side $runtime $CiRuntime $source (Join-Path $out $side) $Theme
    if ($LASTEXITCODE) { throw "$side mask did not render; $out" }
}
$original = Get-Content -LiteralPath (Join-Path $out 'reference/result.json') -Raw | ConvertFrom-Json
$candidate = Get-Content -LiteralPath (Join-Path $out 'candidate/result.json') -Raw | ConvertFrom-Json
Add-Type -AssemblyName System.Drawing.Common
$captures = @(foreach ($shot in $original.Shots) {
    $other = @($candidate.Shots | Where-Object Id -eq $shot.Id)
    if ($other.Count -ne 1) { throw "Missing $($shot.Id)" }; $other = $other[0]
    $a = [Drawing.Bitmap]::new((Join-Path $out ("reference/" + $shot.Id + '.png')))
    $b = [Drawing.Bitmap]::new((Join-Path $out ("candidate/" + $shot.Id + '.png')))
    $td = [Drawing.Bitmap]::new((Join-Path $out ("reference/" + $shot.Id + '-title.png')))
    $te = [Drawing.Bitmap]::new((Join-Path $out ("candidate/" + $shot.Id + '-title.png')))
    try {
        if ($a.Size -ne $b.Size -or $a.Width -ne 320 -or $a.Height -ne 40) { throw 'Invalid dimensions' }
        $different = $differentTitle = $differentCoverage = $nonempty = $colorEdges = 0
        $fill = [Drawing.Color]::FromArgb(255, [Convert]::ToInt32($shot.Fill.Substring(3,2),16),[Convert]::ToInt32($shot.Fill.Substring(5,2),16),[Convert]::ToInt32($shot.Fill.Substring(7,2),16))
        for ($y=0; $y -lt $a.Height; $y++) { for ($x=0; $x -lt $a.Width; $x++) {
            $pixel = $a.GetPixel($x,$y); $targetPixel = $b.GetPixel($x,$y)
            if ($pixel.A) { $nonempty++ }; if ($pixel.A -eq 255 -and $pixel.ToArgb() -ne $fill.ToArgb()) { $colorEdges++ }
            if ($pixel.ToArgb() -ne $targetPixel.ToArgb()) { $different++ }; if ($pixel.A -ne $targetPixel.A) { $differentCoverage++ }
            if ($td.GetPixel($x,$y).ToArgb() -ne $te.GetPixel($x,$y).ToArgb()) { $differentTitle++ }
        }}
        $deltas = @(
            foreach ($key in @('Time','Opacity','ScaleX','ScaleY','X','Y','LabelWidth','LabelHeight','Width','Height')) {
                if ([math]::Abs($shot.$key - $other.$key) -gt 0.00001) { $key }
            }
            foreach ($key in @('Text','TitleBrush','Radius','Fill')) { if ($shot.$key -ne $other.$key) { $key } }
            for ($i=0; $i -lt 5; $i++) { if ([math]::Abs($shot.Regions[$i] - $other.Regions[$i]) -gt 0.00001) { "Region$i" } }
        )
        [pscustomobject]@{ Frame=$shot.Id; ElapsedMs=$shot.Time; DifferentPixels=$different; DifferentTitlePixels=$differentTitle; DifferentCoveragePixels=$differentCoverage; DifferentProperties=$deltas; VisibleReferencePixels=$nonempty; OpaqueReferencePixelsOutsideFill=$colorEdges; Match=($different -eq 0 -and $differentTitle -eq 0 -and !$deltas.Count) }
    } finally { $a.Dispose(); $b.Dispose(); $td.Dispose(); $te.Dispose() }
})
$distinct = @($original.Shots | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $out ("reference/" + $_.Id + '.png'))).Hash } | Sort-Object -Unique)
$openFrame = $original.Shots | Where-Object Id -eq 'open'
$openPixels = $captures | Where-Object Frame -eq 'open'
$valid = $distinct.Count -eq 5 -and ($captures | Measure-Object VisibleReferencePixels -Minimum).Minimum -gt 100 -and $openFrame.Opacity -eq 1 -and $openPixels.OpaqueReferencePixelsOutsideFill -gt 100 -and @($openFrame.Regions | Where-Object { $_ -ne 1 }).Count -eq 0
$passed = $valid -and !@($captures | Where-Object { !$_.Match }).Count
$report = [ordered]@{ Passed=$passed; ValidAnimatedReference=$valid; ReferenceCommit=$pin; Source=$upstreamFile; ReferenceModule=$original.ModuleSha256; CandidateModule=$candidate.ModuleSha256; ReferenceAvalonia=$original.Avalonia; CandidateAvalonia=$candidate.Avalonia; Theme=$Theme; Captures=$captures }
[IO.File]::WriteAllText((Join-Path $out 'comparison.json'), ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$captures | Format-Table Frame,ElapsedMs,DifferentPixels,VisibleReferencePixels,Match
if (!$passed) { throw "Title and slants differ or the reference did not animate: $out/comparison.json" }
"PASS: five title and mask frames; $out/comparison.json"
