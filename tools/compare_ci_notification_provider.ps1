#requires -Version 7.2
param(
    [string]$CiSource = 'E:/ci-source',
    [string]$CiRuntime = 'E:/ci-app/ClassIsland_app_windows_x64_full_folder/app-2.1.0.1-0',
    [string]$ClientRuntime,
    [ValidateSet('dark','light')][string]$Theme = 'dark',
    [ValidatePattern('^#[0-9a-fA-F]{6}$')][string]$Accent = '#459BAC',
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Round = ('ci-provider-' + [guid]::NewGuid().ToString('N').Substring(0,8))
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$ClientRuntime) { $ClientRuntime = Join-Path $root 'src/ClassNex/bin/Debug/net10.0-windows10.0.19041.0' }
$CiRuntime = (Resolve-Path -LiteralPath $CiRuntime).Path
$ClientRuntime = (Resolve-Path -LiteralPath $ClientRuntime).Path
$pin = '235914a2fac1733cc2a169457c7bad4edb2ca2f6'
if ((git -C $root hash-object --no-filters 'src/ClassNex/Styles/Upstream/FluentAvalonia/ListBoxStyles.axaml') -ne 'd85bf5a047d4799b2df2c5c5e4069188655cb720') { throw 'The original FluentAvalonia 2.4.1 list template changed' }
$files = @('ClassIsland/Controls/NotificationProviders/ClassNotificationProviderControl.axaml',
    'ClassIsland.Core/Controls/NotificationTemplates/SimpleTextOverlayTemplate.axaml',
    'ClassIsland.Core/Themes/SlidingListBox.axaml','ClassIsland.Core/Themes/Layers.axaml','ClassIsland.Core/Themes/TextBaselineFixes.axaml',
    'ClassIsland/Themes/LightTheme.axaml','ClassIsland/Themes/DarkTheme.axaml')
$work = Join-Path $root ('build/provider-probe-' + [guid]::NewGuid().ToString('N'))
$out = Join-Path $root "artifacts/ui-review/$Round"
if (Test-Path -LiteralPath $out) { throw "Output already exists: $out" }
$null = [IO.Directory]::CreateDirectory($work)
$null = [IO.Directory]::CreateDirectory($out)
$null = New-Item -ItemType File -Path (Join-Path $work '.classnex-provider-probe')
$archive = Join-Path $work 'source.zip'
git -C $CiSource archive --format=zip "--output=$archive" $pin -- $files
if ($LASTEXITCODE) { throw 'Cannot extract the pinned source' }
$source = Join-Path $work 'upstream'
Expand-Archive -LiteralPath $archive -DestinationPath $source
$proj = Join-Path $PSScriptRoot 'NotificationRailProbe/NotificationRailProbe.csproj'
$outputApp = Join-Path $work 'probe'
dotnet build $proj -c Release "-p:CiRailRuntime=$CiRuntime" --artifacts-path (Join-Path $work 'obj-bin') -o $outputApp --nologo -v:q
if ($LASTEXITCODE) { throw 'Provider probe did not compile' }
$app = Join-Path $outputApp 'NotificationRailProbe.dll'
foreach ($side in @('reference','candidate')) {
    $runtime = if ($side -eq 'reference') { $CiRuntime } else { $ClientRuntime }
    dotnet $app "provider-$side" $runtime $CiRuntime $source (Join-Path $out $side) $Theme $Accent
    if ($LASTEXITCODE) { throw "$side provider did not render; $out" }
}
$original = Get-Content -Raw -LiteralPath (Join-Path $out 'reference/result.json') | ConvertFrom-Json
$candidate = Get-Content -Raw -LiteralPath (Join-Path $out 'candidate/result.json') | ConvertFrom-Json
if ($original.Runtime -notlike '11.3.17*' -or $original.Theme -notlike '2.4.1*') { throw 'Unexpected reference runtime' }
Add-Type -AssemblyName System.Drawing.Common
$captures = foreach ($shot in $original.Shots) {
    $other = @($candidate.Shots | Where-Object Id -eq $shot.Id)
    if ($other.Count -ne 1 -or $shot.Text.Count -ne $other[0].Text.Count) { throw "Missing or hidden candidate text: $($shot.Id)" }
    $a = [Drawing.Bitmap]::new((Join-Path $out ("reference/" + $shot.Id + '.png')))
    $b = [Drawing.Bitmap]::new((Join-Path $out ("candidate/" + $shot.Id + '.png')))
    try {
        if ($a.Size -ne $b.Size -or $a.Width -ne $shot.Width -or $a.Height -ne $shot.Height) { throw 'Capture dimensions differ' }
        $matte = [Drawing.ColorTranslator]::FromHtml($(if ($Theme -eq 'dark') { '#202020' } else { '#F4F4F4' })).ToArgb()
        $visibleReference = $visibleCandidate = $different = 0
        for ($y = 0; $y -lt $a.Height; $y++) {
            for ($x = 0; $x -lt $a.Width; $x++) {
                $ca = $a.GetPixel($x,$y); $cb = $b.GetPixel($x,$y)
                if ($ca.A -and $ca.ToArgb() -ne $matte) { $visibleReference++ }; if ($cb.A -and $cb.ToArgb() -ne $matte) { $visibleCandidate++ }
                if ($ca.ToArgb() -ne $cb.ToArgb()) { $different++ }
            }
        }
        if ($visibleReference -lt 100 -or $visibleCandidate -lt 100) { throw "Blank capture: $($shot.Id)" }
        $layout = @(for ($i = 0; $i -lt $shot.Text.Count; $i++) {
            $left = $shot.Text[$i]; $right = $other[0].Text[$i]
            foreach ($key in @('X','Y','Width','Height','Size','Weight','Brush','Margin','Text')) {
                if ($left.$key -ne $right.$key) { [pscustomobject]@{ Label=$i; Property=$key; Reference=$left.$key; Candidate=$right.$key } }
            }
        })
        foreach ($key in @('X','Y','Width','Height')) {
            if ($shot.Body.$key -ne $other[0].Body.$key) { $layout += [pscustomobject]@{ Label='Body'; Property=$key; Reference=$shot.Body.$key; Candidate=$other[0].Body.$key } }
        }
        [pscustomobject]@{ Scene=$shot.Id; Match=($different -eq 0 -and !$layout); DifferentPixels=$different; TotalPixels=$a.Width*$a.Height; ReferenceInk=$visibleReference; CandidateInk=$visibleCandidate; Layout=$layout }
    } finally { $a.Dispose(); $b.Dispose() }
}
$passed = !($captures | Where-Object { !$_.Match })
$report = [ordered]@{ Passed=$passed; Commit=$pin; Sources=$files; Reference=$original.Module; Candidate=$candidate.Module; CandidateSha256=$candidate.ModuleSha256;
    ReferenceRuntime=$original.Runtime; CandidateRuntime=$candidate.Runtime; Theme=$Theme; Accent=$Accent;
    Scope='Fixed classroom width. Each settled body is placed at its measured layout offset on the full row. Excludes screen placement, mask, rail and temporal transitions.'; Captures=$captures }
[IO.File]::WriteAllText((Join-Path $out 'comparison.json'), ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$captures | Format-Table Scene,Match,DifferentPixels,ReferenceInk,CandidateInk
if (!$passed) { throw "Provider comparison differs: $out/comparison.json" }
"PASS: settled provider bodies match; $out/comparison.json"
