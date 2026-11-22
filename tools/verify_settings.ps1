[CmdletBinding()]
param(
    [ValidateRange(10, 300)][int]$TimeoutSeconds = 90,
    [switch]$KeepOpen
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$clientRoot = Join-Path $repoRoot 'src\ClassNex'
$project = [xml][IO.File]::ReadAllText((Join-Path $clientRoot 'ClassNex.csproj'))
$framework = $project.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText.Trim()
$debugRoot = Join-Path (Join-Path $clientRoot 'bin\Debug') $framework
$manifestPath = Join-Path $debugRoot 'ClassNex.deps.json'
if (-not [IO.File]::Exists($manifestPath)) { throw "Missing Debug output. Rebuild src\ClassNex\ClassNex.csproj before running this script." }

function Get-PlainFile([string]$root, [string]$relative) {
    if ([IO.Path]::IsPathRooted($relative) -or ($relative -split '[/\\]' | Where-Object { $_ -in '.', '..', '' })) { throw "Unexpected relative file: $relative" }
    $base = [IO.Path]::GetFullPath($root)
    $path = [IO.Path]::GetFullPath((Join-Path $base $relative))
    $current = $path
    while ($true) {
        if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing linked path: $current" }
        if ($current -eq $base) { break }
        $current = [IO.Path]::GetDirectoryName($current)
        if (-not $current) { throw "Unexpected file outside source: $relative" }
    }
    if (-not [IO.File]::Exists($path)) { throw "Missing file: $path" }
    return $path
}

$sampleRelative = 'src/ClassNex/Assets/timetable.yaml'
$sampleSource = Get-PlainFile $repoRoot $sampleRelative
$null = & git -C $repoRoot ls-files --error-unmatch -- $sampleRelative
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the tracked example timetable.' }
& git -C $repoRoot diff --quiet HEAD -- $sampleRelative
if ($LASTEXITCODE -ne 0) { throw 'The example timetable differs from HEAD. No fixture was created.' }

$buildRoot = Join-Path $repoRoot 'build'
[void][IO.Directory]::CreateDirectory($buildRoot)
if (([IO.File]::GetAttributes($buildRoot) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing linked build directory: $buildRoot" }
$fixtureRoot = Join-Path $buildRoot ('verify-settings-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixtureRoot -ErrorAction Stop
$appDir = Join-Path $fixtureRoot 'app'
$dataDir = Join-Path $fixtureRoot 'data'
[void][IO.Directory]::CreateDirectory($appDir)
[void][IO.Directory]::CreateDirectory($dataDir)
Write-Host "Fixture: $fixtureRoot"

$files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in 'ClassNex.exe', 'ClassNex.deps.json', 'ClassNex.runtimeconfig.json', 'Assets\Fonts\HarmonyOS-Sans-LICENSE.txt', 'Assets\SettingsIcons\Fluent-LICENSE.txt') {
    [void]$files.Add($relative)
}
$manifest = [IO.File]::ReadAllText((Get-PlainFile $debugRoot 'ClassNex.deps.json')) | ConvertFrom-Json
$target = $manifest.targets.PSObject.Properties[$manifest.runtimeTarget.name].Value
if (-not $target) { throw 'Cannot find the active runtime target in ClassNex.deps.json.' }
foreach ($library in $target.PSObject.Properties) {
    foreach ($kind in 'runtime', 'native', 'resources', 'runtimeTargets') {
        $group = $library.Value.PSObject.Properties[$kind]
        if (-not $group) { continue }
        foreach ($asset in $group.Value.PSObject.Properties) {
            $name = $asset.Name
            if (-not $name.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) { continue }
            $leaf = [IO.Path]::GetFileName($name)
            if ($kind -eq 'runtimeTargets') {
                if ($name -notmatch '^runtimes/win(?:-[a-z0-9]+)?/') { continue }
                $relative = $name
            } elseif ($kind -eq 'resources') {
                $relative = Join-Path $asset.Value.locale $leaf
            } else { $relative = $leaf }
            [void]$files.Add($relative)
        }
    }
}
foreach ($relative in $files) {
    $source = Get-PlainFile $debugRoot $relative
    $destination = [IO.Path]::GetFullPath((Join-Path $appDir $relative))
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    [IO.File]::Copy($source, $destination, $false)
}
[IO.File]::Copy($sampleSource, (Join-Path $appDir 'Assets\timetable.yaml'), $false)
[IO.File]::Copy($sampleSource, (Join-Path $dataDir 'timetable.yaml'), $false)
$utf8 = [Text.UTF8Encoding]::new($false)
$loopback = 'http://127.0.0.1:1'
$settings = [ordered]@{
    IsOobeCompleted = $true; CentralManagement = @{ IsEnabled = $false; ServerUrl = $loopback }
    AccountEndpoint = $loopback; NexApiBaseUrl = $loopback
    ShowStartupSplash = $false; IsMainWindowVisible = $false
    IsNexVoiceWakeEnabled = $false; IsNexEdgeBarEnabled = $false
    AllowNotification = $false
    Notification = @{ IsClassOnNotificationEnabled = $false; IsClassOnPreparingNotificationEnabled = $false; IsClassOffNotificationEnabled = $false }
}
[IO.File]::WriteAllText((Join-Path $dataDir 'Settings.json'), ($settings | ConvertTo-Json -Depth 3), $utf8)
[IO.File]::WriteAllText((Join-Path $dataDir '.test-fixture'), 'ClassNEX settings verification', $utf8)

$exe = Get-PlainFile $appDir 'ClassNex.exe'
$logPath = Join-Path $appDir '_settings_verify.log'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $exe
$start.WorkingDirectory = $appDir
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($varName in @($start.EnvironmentVariables.Keys)) {
    if ($varName -like 'CLASSNEX_VERIFY*') { $start.EnvironmentVariables.Remove($varName) }
}
$start.EnvironmentVariables['CLASSNEX_VERIFY'] = '1'
$start.EnvironmentVariables['CLASSNEX_VERIFY_PAGE'] = 'home'
$start.EnvironmentVariables['CLASSNEX_VERIFY_SETTINGS'] = '1'
$start.EnvironmentVariables['CLASSNEX_VERIFY_EDIT_FIXTURE'] = '1'
$instance = $null
$passed = $false
try {
    $instance = [Diagnostics.Process]::Start($start)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $report = $null
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ([IO.File]::Exists($logPath)) {
            $stream = $null
            try {
                $stream = [IO.File]::Open($logPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
                $reader = [IO.StreamReader]::new($stream, $utf8)
                try { $report = $reader.ReadToEnd() } finally { $reader.Dispose() }
            } catch [IO.IOException] { } finally { if ($stream) { $stream.Dispose() } }
            if ($report) { break }
        }
        if ($instance.HasExited) { throw "Fixture exited ($($instance.ExitCode)) before reporting: $fixtureRoot" }
        Start-Sleep -Milliseconds 200
    }
    if (-not $report) { throw "No completed report after $TimeoutSeconds seconds: $fixtureRoot" }
    Write-Host $report.TrimEnd()
    $lines = $report -split '\r?\n'
    if ($lines -match '^FAIL\b') { throw "Settings verification failed: $logPath" }
    foreach ($required in 'signed-out greeting', 'dashboard groups contain the nine distinct real entries', 'compact card fits without horizontal page overflow', 'favorites filter and persistence', 'grouped row opens the matching settings page', 'slider writes real setting', 'toggle writes real setting') {
        if ($lines -notcontains "PASS $required") { throw "Incomplete report, missing '$required': $logPath" }
    }
    foreach ($shot in 'home_light', 'home_dark', 'home_narrow', 'cards_light', 'cards_dark', 'cards_narrow', 'appearance_narrow', 'overview_dark') {
        $image = Get-Item -LiteralPath (Join-Path $appDir "_settings_$shot.png")
        if ($image.Length -eq 0) { throw "Empty screenshot: $($image.FullName)" }
    }
    $passed = $true
    Write-Host "PASS: $(@($lines -match '^PASS ').Count) settings checks. Report: $logPath"
    if ($KeepOpen) { Write-Host "Fixture remains open (PID $($instance.Id)): $exe" }
} finally {
    if ($instance) {
        try {
            if ((-not $KeepOpen -or -not $passed) -and -not $instance.HasExited) {
                $imagePath = [IO.Path]::GetFullPath($instance.MainModule.FileName)
                if (-not [StringComparer]::OrdinalIgnoreCase.Equals($imagePath, $exe)) { throw "Refusing to stop a process outside this fixture: $imagePath" }
                $instance.Kill()
                if (-not $instance.WaitForExit(5000)) { throw "Fixture did not stop (PID $($instance.Id)): $exe" }
            }
        } finally { $instance.Dispose() }
    }
}
