[CmdletBinding()]
param(
    [ValidateSet('measure','finish','close','reduced')][string]$Mode = 'measure',
    [string]$Project = (Join-Path $PSScriptRoot '../src/ClassNex/ClassNex.csproj'),
    [string]$ArtifactsPath,
    [string]$RecordingPython,
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
$artifactArgs = if ($ArtifactsPath) { @('--artifacts-path', (Resolve-Path -LiteralPath $ArtifactsPath).Path) } else { @() }
dotnet publish (Resolve-Path -LiteralPath $Project).Path -c Debug --no-build --no-restore --no-self-contained -o $app --nologo -v:q @artifactArgs
if ($LASTEXITCODE -ne 0) { throw 'Build the Debug client before running this review.' }
$recorder = Join-Path $PSScriptRoot 'OobeCapture/bin/Debug/net10.0-windows10.0.19041.0/OobeCapture.exe'
if ($RecordingPython -or $Mode -eq 'finish') {
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
    if ($Mode -eq 'finish') {
        $post = [Diagnostics.Stopwatch]::StartNew()
        do {
            $copies = @(Get-Process -Name ClassNex -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe -and $_.Id -ne $run.Id })
            if ($copies.Count -gt 1) { throw 'Finish started more than one desktop process.' }
            if ($copies.Count -eq 1) {
                & $recorder $fixture ([string]$copies[0].Id) --classroom
                if ($LASTEXITCODE -ne 0) { throw "Restarted copy did not expose a classroom window: $fixture" }
                $room = Get-Content -LiteralPath (Join-Path $fixture 'classroom.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $summary.restartedClassroom = [bool]$room.visible; $summary.classroomTitle = $room.title
                $summary.restartedProcess = $room.pid; break
            }
            Start-Sleep -Milliseconds 60
        } while ($post.Elapsed.TotalSeconds -lt 25)
        if (!$summary.restartedClassroom) { throw "No classroom was visible after Finish: $fixture" }
    }
    $saved = Get-Content -LiteralPath (Join-Path $data 'Settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (([bool]$saved.IsOobeCompleted) -ne ($Mode -eq 'finish') -or $saved.IsNexVoiceWakeEnabled -or $saved.RunAtStartup -or $saved.RegisterUrlProtocol -or
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
