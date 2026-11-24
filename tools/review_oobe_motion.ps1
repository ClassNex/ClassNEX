[CmdletBinding()]
param(
    [ValidateSet('measure','finish','close','reduced','migration-keep','migration-recycle')][string]$Mode = 'measure',
    [string]$Project = (Join-Path $PSScriptRoot '../src/ClassNex/ClassNex.csproj'),
    [string]$ArtifactsPath,
    [string]$RecordingPython,
    [string]$ClassIslandPackage,
    [switch]$CompareOld
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$fixture = (New-Item -ItemType Directory -Path (Join-Path $root ('build/oobe-review-' + [guid]::NewGuid().ToString('N')))).FullName
$app = Join-Path $fixture 'app'
$data = (New-Item -ItemType Directory -Path (Join-Path $fixture 'data')).FullName
$utf8 = [Text.UTF8Encoding]::new($false)
$settings = '{"IsOobeCompleted":false,"IsNexVoiceWakeEnabled":false,"IsNexEdgeBarEnabled":false,"IsNexIntelligenceEnabled":false,"RegisterUrlProtocol":false,"RunAtStartup":false,"CentralManagement":{"IsEnabled":false},"ThemeMode":"light","Language":"zh-CN","AllowNotification":false}'
[IO.File]::WriteAllText((Join-Path $data 'Settings.json'), $settings, $utf8)
[IO.File]::WriteAllText((Join-Path $fixture '.classnex-oobe-review'), 'anonymous-oobe-v1', $utf8)
$migration = $Mode -like 'migration-*'
$expectVisible = $Mode -ne 'migration-keep'
if ($migration) {
    if (!$ClassIslandPackage) { throw 'Pass -ClassIslandPackage with the path to a ClassIsland 2.x portable package; only executable files will be read.' }
    $reference = (Resolve-Path -LiteralPath $ClassIslandPackage).Path
    $payloadName = Get-ChildItem -LiteralPath $reference -Directory | Where-Object Name -Match '^app-2\.[0-9.]+-\d+$' | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
    if (!$payloadName) { throw 'No ClassIsland 2.x version folder in the reference package.' }
    $source = (New-Item -ItemType Directory -Path (Join-Path $fixture 'source/ClassIsland')).FullName
    $payload = (New-Item -ItemType Directory -Path (Join-Path $source $payloadName)).FullName
    $sourceData = Join-Path $source 'data'
    $layout = (New-Item -ItemType Directory -Path (Join-Path $sourceData 'Config/ComponentLayouts')).FullName
    Copy-Item -LiteralPath (Join-Path $reference 'ClassIsland.exe') -Destination $source
    $manifestFiles = @{}
    foreach ($file in @('ClassIsland.Desktop.exe','ClassIsland.dll','ClassIsland.Desktop.dll','ClassIsland.Desktop.deps.json','ClassIsland.Desktop.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path (Join-Path $reference $payloadName) $file) -Destination $payload
        $manifestFiles[$file] = @{}
    }
    [IO.File]::WriteAllText((Join-Path $source 'PackageType'),'folder',$utf8)
    [IO.File]::WriteAllText((Join-Path $source 'files.json'),(@{ Components = @{ app = @{ Files = $manifestFiles } } } | ConvertTo-Json -Depth 8),$utf8)
    $sourceSettings = [ordered]@{ Scale=1.35; Opacity=0.62; Theme=2; ColorSource=0; PrimaryColor='#B6377EFF'; IsMainWindowVisible=$expectVisible; CurrentComponentConfig='Review'; SelectedProfile='Default.json' } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $sourceData 'Settings.json'),$sourceSettings,$utf8)
    $components = '{"Lines":[{"Children":[{"Id":"DF3F8295-21F6-482E-BADA-FA0E5F14BB66"},{"Id":"9E1AF71D-8F77-4B21-A342-448787104DD9","Settings":{"ShowSeconds":true}},{"Id":"1DB2017D-E374-4BC6-9D57-0B4ADF03A6B8"},{"Id":"cb06ce36-a579-4b7a-adee-b6e6d7dfeabd","NameCache":"Anonymous unsupported widget"}]}]}'
    [IO.File]::WriteAllText((Join-Path $layout 'Review.json'),$components,$utf8)
    $profiles = (New-Item -ItemType Directory -Path (Join-Path $sourceData 'Profiles')).FullName
    $subjectId = '10000000-0000-4000-8000-000000000001'; $layoutId = '10000000-0000-4000-8000-000000000002'
    $profile = @{
        Subjects = @{ $subjectId = @{ Name='地理'; Initial='地' } }
        TimeLayouts = @{ $layoutId = @{ Name='常规作息'; Layouts=@(
            @{ TimeType=0; StartTime='08:00:00'; EndTime='08:45:00' },
            @{ TimeType=1; BreakName='晨间休息'; StartTime='08:45:00'; EndTime='09:00:00' }) } }
        ClassPlans = @{ '10000000-0000-4000-8000-000000000003' = @{ Name='周一'; TimeLayoutId=$layoutId;
            TimeRule=@{ WeekDay=1 }; Classes=@(@{ SubjectId=$subjectId }) } }
    } | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText((Join-Path $profiles 'Default.json'),$profile,$utf8)
}
$shouldFinish = $Mode -eq 'finish' -or $migration
$artifactArgs = if ($ArtifactsPath) { @('--artifacts-path', (Resolve-Path -LiteralPath $ArtifactsPath).Path) } else { @() }
dotnet publish (Resolve-Path -LiteralPath $Project).Path -c Debug --no-build --no-restore --no-self-contained -o $app --nologo -v:q @artifactArgs
if ($LASTEXITCODE -ne 0) { throw 'Build the Debug client before running this review.' }
$recorder = Join-Path $PSScriptRoot 'OobeCapture/bin/Debug/net10.0-windows10.0.19041.0/OobeCapture.exe'
if ($RecordingPython -or $shouldFinish) {
    dotnet build (Join-Path $PSScriptRoot 'OobeCapture/OobeCapture.csproj') -c Debug -m:1 -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the window-only capture helper.' }
}
$exe = Join-Path $app 'ClassNex.exe'
$start = [Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = $app
$start.ArgumentList.Add('--review-oobe-motion'); $start.ArgumentList.Add($Mode)
if ($CompareOld) { $start.ArgumentList.Add('--baseline') }
foreach ($key in @($start.Environment.Keys | Where-Object { $_ -like 'CLASSNEX_*' })) { [void]$start.Environment.Remove($key) }
$clock = [Diagnostics.Stopwatch]::StartNew()
$run = [Diagnostics.Process]::Start($start)
$capture = $null
$summary = [ordered]@{ mode=$Mode; fixture=$fixture; pid=$run.Id; processToWindowManifestMs=$null; restartedClassroom=$false }
try {
    $manifest = Join-Path $fixture 'wizard.json'
    while (!(Test-Path -LiteralPath $manifest) -and !$run.HasExited -and $clock.Elapsed.TotalSeconds -lt 60) { Start-Sleep -Milliseconds 15 }
    if (!(Test-Path -LiteralPath $manifest)) { throw "Cold first-run window never registered: $fixture (process exit: $($run.HasExited))" }
    $summary.processToWindowManifestMs = $clock.ElapsedMilliseconds
    if ($RecordingPython) {
        $rec = [Diagnostics.ProcessStartInfo]::new($recorder)
        $rec.UseShellExecute = $false; $rec.CreateNoWindow = $true; $rec.RedirectStandardError = $true
        foreach ($arg in @($fixture, [string]$run.Id)) { $rec.ArgumentList.Add($arg) }
        $capture = [Diagnostics.Process]::Start($rec)
        $recordingErrors = $capture.StandardError.ReadToEndAsync()
    }
    if (!$run.WaitForExit(45000)) { throw "Wizard did not close: $fixture" }
    if ($run.ExitCode -ne 0) { throw "Wizard exited with $($run.ExitCode): $fixture" }
    $report = Get-Content -LiteralPath (Join-Path $fixture 'oobe-motion.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($report.error) { throw $report.error }
    $failed = @($report.checks.PSObject.Properties | Where-Object { !$_.Value } | ForEach-Object Name)
    if ($CompareOld) { $failed = @($failed | Where-Object { $_ -ne 'textAndFooterReadableOnFirstFrame' }) }
    if ($failed.Count) { throw "Failed: $($failed -join ', ') in $fixture" }
    if ($capture -and (!$capture.WaitForExit(30000) -or $capture.ExitCode -ne 0)) {
        $message = if ($recordingErrors.IsCompleted) { $recordingErrors.Result } else { 'Recorder timed out' }
        throw "Window-only recording failed: $message"
    }
    if ($capture) {
        & $RecordingPython (Join-Path $PSScriptRoot 'encode_oobe_frames.py') $fixture
        if ($LASTEXITCODE -ne 0) { throw 'Window frame encoding failed.' }
    }
    if ($shouldFinish) {
        $post = [Diagnostics.Stopwatch]::StartNew()
        do {
            $copies = @(Get-Process -Name ClassNex -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe -and $_.Id -ne $run.Id })
            if ($copies.Count -gt 1) { throw 'Finish started more than one desktop process.' }
            if ($copies.Count -eq 1) {
                $visibilityCheck = if ($expectVisible) { '--classroom' } else { '--classroom-hidden' }
                & $recorder $fixture ([string]$copies[0].Id) $visibilityCheck
                if ($LASTEXITCODE -ne 0) { throw "Restarted classroom visibility did not match the chosen setting: $fixture" }
                $room = Get-Content -LiteralPath (Join-Path $fixture 'classroom.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $summary.restartedClassroom = [bool]$room.visible -eq $expectVisible; $summary.classroomTitle = $room.title
                $summary.classroomVisible = [bool]$room.visible; $summary.observedHiddenMs = $room.observedHiddenMs
                $summary.restartedProcess = $room.pid; break
            }
            Start-Sleep -Milliseconds 60
        } while ($post.Elapsed.TotalSeconds -lt 25)
        if (!$summary.restartedClassroom) { throw "Classroom startup was not verified after Finish: $fixture" }
    }
    $saved = Get-Content -LiteralPath (Join-Path $data 'Settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (([bool]$saved.IsOobeCompleted) -ne $shouldFinish -or $saved.IsNexVoiceWakeEnabled -or $saved.RunAtStartup -or $saved.RegisterUrlProtocol -or
        $saved.UserName -or $saved.Email -or (Test-Path -LiteralPath (Join-Path $data 'Logs/crash.log'))) { throw 'Unexpected persistence, identity, or crash in fixture.' }
    $summary.savedVoiceOff = $true; $summary.completedPersisted = [bool]$saved.IsOobeCompleted
    if ($Mode -eq 'reduced') {
        $summary.discardedDraftPreferences = $saved.Language -eq 'zh-CN' -and $saved.ThemeMode -eq 'light' -and !$saved.RunAtStartup
        if (!$summary.discardedDraftPreferences) { throw 'Closing setup persisted unconfirmed preferences.' }
    }
    if ($Mode -eq 'finish') {
        $summary.chosenSemesterPersisted = ([datetime]$saved.SemesterStartDate).ToString('yyyy-MM-dd') -eq '2026-09-15'
        if (!$summary.chosenSemesterPersisted) { throw 'The chosen semester was not saved on Finish.' }
    }
    if ($migration) {
        $summary.preservedImportOnRestart = $saved.ThemeMode -eq 'dark' -and $saved.ThemeAccentColor -eq '#FFB6377E' -and $saved.IsMainWindowVisible -eq $expectVisible -and [math]::Abs($saved.MainWindowScale - 1.35) -lt 0.001 -and [math]::Abs($saved.BackgroundOpacity - 0.62) -lt 0.001
        $profilePath = [IO.Path]::GetFullPath($saved.TimetablePath)
        if (![IO.Path]::GetRelativePath($data,$profilePath).StartsWith('ClassIsland-', [StringComparison]::Ordinal) -or
            !(Test-Path -LiteralPath $profilePath -PathType Leaf)) { throw 'Restart lost the imported active profile.' }
        $yaml = [IO.File]::ReadAllText($profilePath)
        $summary.preservedScheduleAndWidgetOrder = $yaml.Contains('地理') -and $yaml.Contains('08:45:00') -and $yaml.Contains('晨间休息') -and
            ($saved.Widgets.Type -join ',') -ceq 'date,clock,schedule' -and $saved.Widgets[1].ShowSeconds
        if (!$summary.preservedScheduleAndWidgetOrder) { throw 'Restart lost a course, a named break, or the imported widget order.' }
        $summary.sourcePreserved = Test-Path -LiteralPath $source
        if (!$summary.preservedImportOnRestart -or $summary.sourcePreserved -ne ($Mode -eq 'migration-keep')) { throw "Import, cleanup or restart evidence mismatch: $fixture" }
        Add-Type -AssemblyName System.Drawing
        $sample = Get-Content -LiteralPath (Join-Path $fixture 'migration-accent-sample.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $shot = [Drawing.Bitmap]::new((Join-Path $fixture 'migration-result.png'))
        try { $pixel = $shot.GetPixel([int]$sample.x,[int]$sample.y) } finally { $shot.Dispose() }
        $summary.renderedAccentPixel = ('#{0:X2}{1:X2}{2:X2}{3:X2}' -f $pixel.A,$pixel.R,$pixel.G,$pixel.B)
        if ($pixel.A -lt 220 -or $pixel.R -le $pixel.G + 8 -or $pixel.B -le $pixel.G + 8) { throw "Imported accent is missing from the rendered receipt button: $($summary.renderedAccentPixel) at $($sample.x),$($sample.y) in $fixture" }
    }
    $summary.systemAnimationsEnabled = $report.systemAnimations
    $summary.checks = $report.checks; $summary.timing = $report.timing
    $output = $summary | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText((Join-Path $fixture 'result.json'),$output,$utf8)
    $output
} finally {
    foreach ($p in @(Get-Process -Name ClassNex -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })) {
        if (!$p.HasExited -and $p.Path -eq $exe) { $p.Kill(); [void]$p.WaitForExit(5000) }
        $p.Dispose()
    }
    if ($capture) { $capture.Dispose() }
    $run.Dispose()
}
