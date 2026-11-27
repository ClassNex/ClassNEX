#requires -Version 7.2
param(
    [string]$CiSource = 'E:/ci-source',
    [string]$CiRuntime = 'E:/ci-app/ClassIsland_app_windows_x64_full_folder/app-2.1.0.1-0',
    [string]$ClientRuntime,
    [ValidateSet('dark','light')][string]$Theme = 'dark',
    [ValidatePattern('^#[0-9a-fA-F]{6}$')][string]$Accent = '#459BAC',
    [ValidateSet('Exact','Compact')][string]$LayoutContract = 'Exact',
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
if ($original.Variant -ne $Theme -or $candidate.Variant -ne $Theme -or $original.Accent -ne $Accent -or $candidate.Accent -ne $Accent -or
    $original.RenderScaling -le 0 -or $original.RenderScaling -ne $candidate.RenderScaling) { throw 'Theme, accent or layout scaling mismatch' }
$expected = [ordered]@{ prepare=4; 'prepare-teacher'=5; 'break'=4; 'break-teacher'=5; dismissal=1 }
foreach ($side in $original,$candidate) {
    if ($side.Shots.Count -ne $expected.Count -or (Compare-Object @($expected.Keys | Sort-Object) @($side.Shots.Id | Sort-Object))) { throw 'Unexpected or missing provider scenes' }
}
Add-Type -AssemblyName System.Drawing.Common
function ComparePixels([string]$refImage,[string]$nexImage,[int]$matte,[int]$minInk) {
    $a = [Drawing.Bitmap]::new((Join-Path $out "reference/$refImage"))
    try {
        $b = [Drawing.Bitmap]::new((Join-Path $out "candidate/$nexImage"))
        try {
            if ($a.Size -ne $b.Size) { return [pscustomobject]@{ Equal=$false; SameSize=$false; ReferenceSize=$a.Size.ToString(); CandidateSize=$b.Size.ToString() } }
            $refInk = $nexInk = $different = 0
            $hasRefMatte = $hasNexMatte = $false
            for ($y = 0; $y -lt $a.Height; $y++) {
                for ($x = 0; $x -lt $a.Width; $x++) {
                    $ca = $a.GetPixel($x,$y); $cb = $b.GetPixel($x,$y)
                    if ($ca.ToArgb() -eq $matte) { $hasRefMatte = $true } elseif ($ca.A) { $refInk++ }
                    if ($cb.ToArgb() -eq $matte) { $hasNexMatte = $true } elseif ($cb.A) { $nexInk++ }
                    if ($ca.ToArgb() -ne $cb.ToArgb()) { $different++ }
                }
            }
            if ($refInk -lt $minInk -or $nexInk -lt $minInk) { throw "Blank capture: $refImage / $nexImage" }
            if (!$hasRefMatte -or !$hasNexMatte) { throw "Missing opaque theme backdrop: $refImage / $nexImage" }
            [pscustomobject]@{ Equal=($different -eq 0); SameSize=$true; Width=$a.Width; Height=$a.Height; Different=$different; ReferenceInk=$refInk; CandidateInk=$nexInk }
        } finally { $b.Dispose() }
    } finally { $a.Dispose() }
}
$matte = [Drawing.ColorTranslator]::FromHtml($(if ($Theme -eq 'dark') { '#202020' } else { '#F4F4F4' })).ToArgb()
$captures = @(foreach ($shot in $original.Shots) {
    $nex = $candidate.Shots | Where-Object Id -ceq $shot.Id
    if ($shot.Text.Count -ne $expected[$shot.Id] -or $nex.Text.Count -ne $shot.Text.Count) { throw "Missing or hidden provider text: $($shot.Id)" }
    $row = ComparePixels ($shot.Id + '.png') ($shot.Id + '.png') $matte 100
    if (!$row.SameSize -or $row.Width -ne $shot.Width -or $row.Height -ne $shot.Height -or $nex.Width -ne $shot.Width -or $nex.Height -ne $shot.Height) { throw "Capture dimensions differ: $($shot.Id)" }
    $layout = @(for ($i = 0; $i -lt $shot.Text.Count; $i++) {
        $left = $shot.Text[$i]; $right = $nex.Text[$i]
        foreach ($key in 'X','Y','Width','Height','Size','Weight','Brush','Margin','Text') {
            if ($left.$key -cne $right.$key) { [pscustomobject]@{ Label=$i; Property=$key; Reference=$left.$key; Candidate=$right.$key } }
        }
    })
    foreach ($key in 'X','Y','Width','Height') {
        if ($shot.Body.$key -ne $nex.Body.$key) { $layout += [pscustomobject]@{ Label='Body'; Property=$key; Reference=$shot.Body.$key; Candidate=$nex.Body.$key } }
    }
    $labels = @(for ($i = 0; $i -lt $shot.Text.Count; $i++) {
        $left = $shot.Text[$i]; $right = $nex.Text[$i]
        if (!$left.Text -or !$left.LabelImage -or !$right.LabelImage) { throw "Missing rendered label: $($shot.Id) [$i]" }
        $glyph = ComparePixels $left.LabelImage $right.LabelImage $matte 12
        $refY = $shot.Body.Y + $left.Y; $nexY = $nex.Body.Y + $right.Y
        [pscustomobject]@{ Text=$left.Text; CandidateText=$right.Text; IsolatedGlyph=$glyph; ReferenceRowY=$refY; CandidateRowY=$nexY; SameRowY=($refY -eq $nexY) }
    })
    $style = !($layout | Where-Object { $_.Label -ne 'Body' -and $_.Property -in 'Width','Height','Size','Weight','Brush','Text' })
    $glyphs = !($labels | Where-Object { !$_.IsolatedGlyph.Equal }); $vertical = !($labels | Where-Object { !$_.SameRowY })
    $exact = $row.Equal -and !$layout -and $glyphs
    $geometry = $null
    if ($shot.Id -ne 'dismissal') {
        $last = $shot.Text.Count-1
        $expectedTimeMargin = if ($shot.Id.EndsWith('-teacher')) { '2,0,0,0' } else { '6,0,0,0' }
        $margins = $shot.Text[$last].Margin -ceq '2,0,0,0' -and $nex.Text[$last].Margin -ceq $expectedTimeMargin -and
            !($layout | Where-Object { $_.Property -eq 'Margin' -and $_.Label -ne $last })
        $gap = $nex.Text[1].X - ($nex.Text[0].X + $nex.Text[0].Width)
        $imbalance = $nex.Body.X - ($nex.Width - $nex.Body.X - $nex.Body.Width)
        $firstEdge = $nex.Body.X + $nex.Text[0].X; $lastEdge = $nex.Body.X + $nex.Text[$last].X + $nex.Text[$last].Width
        $inkBoxImbalance = $firstEdge - ($nex.Width - $lastEdge)
        $lessonGaps = @(for ($i=2; $i -le $last; $i++) {
            $refGap = $shot.Text[$i].X - ($shot.Text[$i-1].X + $shot.Text[$i-1].Width)
            $nexGap = $nex.Text[$i].X - ($nex.Text[$i-1].X + $nex.Text[$i-1].Width)
            # For these integral-width subject fixtures, snap the new 6-DIP margin itself, not 4 DIP added to an independently snapped 2-DIP gap.
            $wanted = if ($i -eq $last -and !$shot.Id.EndsWith('-teacher')) { [Math]::Round(6 * $candidate.RenderScaling, [MidpointRounding]::ToEven) / $candidate.RenderScaling } else { $refGap }
            [pscustomobject]@{ Before=$nex.Text[$i].Text; Reference=$refGap; Candidate=$nexGap; Expected=$wanted; Match=([Math]::Abs($nexGap-$wanted) -lt 1e-9) }
        })
        $pixel = 1.0 / $candidate.RenderScaling
        # Centering can round one device pixel; measuring a column width rounds up by less than one DIP.
        $geometry = [pscustomobject]@{ GroupGapDip=$gap; ExpectedGroupGapDip=24; OutsideInsetDifferenceDip=$imbalance;
            LayoutPixelDip=$pixel; ReferenceTimeMargin=$shot.Text[$last].Margin; CandidateTimeMargin=$nex.Text[$last].Margin; ExpectedTimeMargin=$expectedTimeMargin;
            MarginsMatch=$margins; LessonGaps=$lessonGaps; TextBoxInsetDifferenceDip=$inkBoxImbalance; Centered=([Math]::Abs($imbalance) -le $pixel + 1e-9);
            TextGroupCentered=([Math]::Abs($inkBoxImbalance) -le 1 + $pixel + 1e-9); ContentSizedGap=([Math]::Abs($gap - 24) -le 1 + $pixel + 1e-9) }
        $compact = $style -and $glyphs -and $vertical -and $geometry.MarginsMatch -and $geometry.Centered -and $geometry.TextGroupCentered -and
            $geometry.ContentSizedGap -and !($lessonGaps | Where-Object { !$_.Match })
    } else {
        $compact = $exact
    }
    [pscustomobject]@{ Scene=$shot.Id; Match=$exact; ExactFullRowPixels=$row.Equal; DifferentPixels=$row.Different; TotalPixels=$row.Width*$row.Height;
        ReferenceInk=$row.ReferenceInk; CandidateInk=$row.CandidateInk; SameLabelStyles=$style; SameIsolatedGlyphs=$glyphs; SameLabelRowY=$vertical;
        CompactLayoutMatch=$compact; Geometry=$geometry; Labels=$labels; Layout=$layout }
})
$exactPassed = !($captures | Where-Object { !$_.Match }); $compactPassed = !($captures | Where-Object { !$_.CompactLayoutMatch })
$passed = if ($LayoutContract -eq 'Exact') { $exactPassed } else { $compactPassed }
$report = [ordered]@{ Passed=$passed; LayoutContract=$LayoutContract; ExactPassed=$exactPassed; CompactPassed=$compactPassed; Commit=$pin; Sources=$files; Reference=$original.Module; Candidate=$candidate.Module; CandidateSha256=$candidate.ModuleSha256;
    ReferenceRuntime=$original.Runtime; CandidateRuntime=$candidate.Runtime; Theme=$Theme; Accent=$Accent;
    Scope='Unshifted whole-row ARGB pixels and raw layout differences are always preserved. Isolated glyphs are separate unscaled renders of the actual arranged text controls at their own origins, not pixel comparisons at their row positions. Shared vertical positions are measured from the row top. Excludes screen placement, mask, rail and temporal transitions.'; Captures=$captures }
[IO.File]::WriteAllText((Join-Path $out 'comparison.json'), ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$captures | Format-Table Scene,ExactFullRowPixels,DifferentPixels,SameIsolatedGlyphs,SameLabelRowY,CompactLayoutMatch
if (!$passed) { throw "Provider $LayoutContract contract failed; see original pixels and individual results in $out/comparison.json" }
"PASS: $LayoutContract contract; original unshifted rows exactly equal: $(@($captures | Where-Object ExactFullRowPixels).Count)/$($captures.Count); $out/comparison.json"
