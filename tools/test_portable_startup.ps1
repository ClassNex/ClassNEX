param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{128}$')][string]$ExpectedSha512,
    [ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$ReviewedPristineSource
)

$ErrorActionPreference = 'Stop'
$zip = (Resolve-Path -LiteralPath $Archive).Path
$report = py -3 -B (Join-Path $PSScriptRoot 'verify_portable.py') $zip
if ($LASTEXITCODE -ne 0) { throw 'ZIP inspection failed; no program was launched.' }
$archiveInfo = ($report -join "`n") | ConvertFrom-Json
if ($archiveInfo.archiveSha512 -ne $ExpectedSha512) { throw 'The archive is not the expected candidate.' }
$pristine = -not [string]::IsNullOrEmpty($ReviewedPristineSource)
if ($pristine -and $archiveInfo.sourceCommit -ne $ReviewedPristineSource) { throw 'Unseeded startup requires the exact commit independently reviewed for microphone-free OOBE.' }

$run = (New-Item -ItemType Directory -Path (Join-Path (Split-Path -Parent $zip) ('smoke-' + [Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $run)
$rootApp = Join-Path $run 'ClassNex.exe'
$child = Join-Path (Join-Path $run $archiveInfo.payload) 'ClassNex.exe'
$data = Join-Path $run 'data'
if (Test-Path -LiteralPath $data) { throw 'Fresh extraction already contained a data directory.' }
$settingsFile = Join-Path $data 'Settings.json'
$fixture = 'Untouched freshly extracted Release; no data directory, settings, marker, account or timetable before launch.'
if (-not $pristine) {
    $fixture = 'Only local voice, edge handle, AI and management disabled before launch; no account, timetable or completed OOBE.'
    [void][IO.Directory]::CreateDirectory($data)
    [IO.File]::WriteAllText($settingsFile, '{"IsNexVoiceWakeEnabled":false,"IsNexEdgeBarEnabled":false,"IsNexIntelligenceEnabled":false,"CentralManagement":{"IsEnabled":false}}', [Text.UTF8Encoding]::new($false))
    $core = [Reflection.Assembly]::LoadFrom((Join-Path (Split-Path -Parent $child) 'ClassNex.Core.dll'))
    $store = $core.GetType('ClassNex.Services.SettingsStore', $true).GetConstructor([type[]]@([string])).Invoke([object[]]@([string]$settingsFile))
    $safe = $store.Load()
    if ($safe.IsOobeCompleted -or $safe.IsNexVoiceWakeEnabled -or $safe.IsNexEdgeBarEnabled -or $safe.IsNexIntelligenceEnabled -or $safe.CentralManagement.IsEnabled -or $safe.UserName -or $safe.Email -or $safe.TimetablePath) { throw 'Privacy-only first-run fixture was not honored; the desktop will not be started.' }
}
if (($pristine -and (Test-Path -LiteralPath $data)) -or (Test-Path -LiteralPath (Join-Path $data 'Logs'))) { throw 'Unexpected data before GUI start.' }
function Get-TestProcesses([string[]]$Paths) {
    Get-Process -Name ClassNex -ErrorAction SilentlyContinue | Where-Object { $Paths -contains $_.Path }
}
$executables = @($rootApp, $child)
if (@(Get-TestProcesses $executables).Count) { throw 'The unique smoke-test path already has a process.' }

$record = [ordered]@{
    passed = $false; archive = $zip; archiveSha512 = $archiveInfo.archiveSha512; sourceCommit = $archiveInfo.sourceCommit; isolatedCopy = $run;
    launcherExitCode = $null; payloadProcessId = $null; payloadExecutable = $child; windowTitle = $null; windowObservedMs = $null;
    closeRequested = $false; exitedWithin20Seconds = $false; payloadExitCode = $null;
    postCloseSamples = 0; postCloseObservedMs = 0; residualProcessIds = $null; recoveryStoppedIds = @(); recoveryErrors = @();
    extractedWithoutData = $true; guiStartedWithoutData = $pristine; guiCreatedLogs = $false; noSeededAccountOrSchedule = $true;
    guiFixture = $fixture
}
$launcher = $null
$app = $null
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    $launcher = Start-Process -FilePath $rootApp -WorkingDirectory $run -WindowStyle Hidden -PassThru
    if (-not $launcher.WaitForExit(10000)) { throw 'Root launcher did not exit within 10 seconds.' }
    $record.launcherExitCode = $launcher.ExitCode
    if ($record.launcherExitCode -ne 0) { throw "Root launcher failed: $($record.launcherExitCode)." }
    do {
        $payloads = @(Get-TestProcesses $child)
        if ($payloads.Count -gt 1) { throw 'More than one payload was launched.' }
        $app = $payloads | Select-Object -First 1
        if ($app -and $app.MainWindowTitle -ceq '欢迎使用 ClassNEX') { break }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 45)
    if (-not $app -or $app.MainWindowTitle -cne '欢迎使用 ClassNEX') { throw 'Fresh Release payload did not expose the expected setup window.' }
    $timer.Stop()
    $record.payloadProcessId = $app.Id
    $record.windowTitle = $app.MainWindowTitle
    $record.windowObservedMs = $timer.ElapsedMilliseconds
    if (-not (Test-Path -LiteralPath (Join-Path $data 'Logs') -PathType Container)) { throw 'data/Logs was not created at the portable root.' }
    $record.guiCreatedLogs = $true
    if (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $child) 'data')) { throw 'Data was created inside the versioned payload.' }
    foreach ($name in @('timetable.yaml', 'account.json', 'account_avatar.png', 'Logs/crash.log')) {
        if (Test-Path -LiteralPath (Join-Path $data $name)) { throw "Unexpected fresh-start file: $name" }
    }
    if (Test-Path -LiteralPath $settingsFile) {
        $fresh = Get-Content -LiteralPath $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($fresh.IsOobeCompleted -or $fresh.UserName -or $fresh.Email -or $fresh.TimetablePath) { throw 'Unexpected identity, schedule, or completed setup in fresh settings.' }
    }
    [void]$app.Handle
    $record.closeRequested = $app.CloseMainWindow()
    $record.exitedWithin20Seconds = $record.closeRequested -and $app.WaitForExit(20000)
    if ($record.exitedWithin20Seconds) { $record.payloadExitCode = $app.ExitCode }

    $residual = [Collections.Generic.HashSet[int]]::new()
    $observation = [Diagnostics.Stopwatch]::StartNew()
    do {
        foreach ($owned in @(Get-TestProcesses $executables)) {
            [void]$residual.Add($owned.Id)
            $owned.Dispose()
        }
        $record.postCloseSamples++
        if ($observation.ElapsedMilliseconds -ge 2000) { break }
        Start-Sleep -Milliseconds 150
    } while ($true)
    $record.postCloseObservedMs = $observation.ElapsedMilliseconds
    $record.residualProcessIds = @($residual | Sort-Object)
    if (-not $record.exitedWithin20Seconds -or $null -eq $record.payloadExitCode -or $record.payloadExitCode -ne 0) {
        throw "OOBE close posted: $($record.closeRequested); exited within 20 s: $($record.exitedWithin20Seconds); exit code: $($record.payloadExitCode)."
    }
    if ($residual.Count) { throw 'A process from this exact portable copy remained or restarted after closing OOBE.' }
    if (Test-Path -LiteralPath (Join-Path $data 'Logs/crash.log')) { throw 'The payload recorded an error during shutdown.' }
    if (Test-Path -LiteralPath $settingsFile) {
        $after = Get-Content -LiteralPath $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($after.IsOobeCompleted -or $after.UserName -or $after.Email -or $after.TimetablePath) { throw 'Closing OOBE must not complete setup or seed an identity or timetable.' }
    }
    $record.passed = $true
} catch {
    $record.error = $_.Exception.Message
    throw
} finally {
    if (-not $record.passed) {
        foreach ($owned in @(Get-TestProcesses $executables)) {
            try {
                if (-not $owned.HasExited -and $executables -contains $owned.Path) {
                    $owned.Kill()
                    $record.recoveryStoppedIds += $owned.Id
                    if (-not $owned.WaitForExit(5000)) { $record.recoveryErrors += "Process $($owned.Id) did not exit." }
                }
            } catch { $record.recoveryErrors += $_.Exception.Message }
            finally { $owned.Dispose() }
        }
    }
    if ($null -ne $launcher) { $launcher.Dispose() }
    if ($null -ne $app) { $app.Dispose() }
    $result = $record | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $zip) ('startup-' + (Split-Path -Leaf $run) + '.json')), $result, [Text.UTF8Encoding]::new($false))
    $result
}
