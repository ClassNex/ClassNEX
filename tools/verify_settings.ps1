[CmdletBinding()]
param(
    [ValidateRange(10, 300)][int]$TimeoutSeconds = 90,
    [switch]$KeepOpen,
    [string]$OutputDirectory,
    [string]$ExpectedProductVersion,
    [string[]]$RequiredCheck = @(),
    [string]$IntermediateDirectory,
    [switch]$KeepFixture
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$clientRoot = Join-Path $repoRoot 'src\ClassNex'
$utf8 = [Text.UTF8Encoding]::new($false)

function Assert-PlainPath([string]$path) {
    for ($current = [IO.Path]::GetFullPath($path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing linked path: $current" }
    }
}

function Assert-FixtureMarker([string]$path, [string]$value) {
    Assert-PlainPath $path
    $file = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($file -isnot [IO.FileInfo] -or $file.LinkType -or $file.Length -gt 128 -or [IO.File]::ReadAllText($file.FullName) -cne $value) {
        throw "Invalid fixture marker: $path"
    }
}

function Remove-VerifiedAppCopy([guid]$runId, [string]$appPath) {
    $id = $runId.ToString('N')
    $expectedRoot = [IO.Path]::Combine($repoRoot, 'build', ('verify-native-' + $id))
    $expectedApp = [IO.Path]::Combine($expectedRoot, 'app')
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath($appPath), $expectedApp)) { throw "Unexpected fixture path: $appPath" }
    Assert-PlainPath $expectedApp
    $target = Get-Item -LiteralPath $expectedApp -Force -ErrorAction Stop
    if ($target -isnot [IO.DirectoryInfo] -or -not [StringComparer]::OrdinalIgnoreCase.Equals($target.FullName, $expectedApp)) { throw "Invalid fixture directory: $expectedApp" }
    $checkMarkers = {
        Assert-FixtureMarker (Join-Path $expectedRoot 'data\.native-verify') 'disposable'
        Assert-FixtureMarker (Join-Path $expectedApp '.native-verify-owner') $id
    }
    & $checkMarkers
    $directories = [Collections.Generic.Stack[string]]::new()
    $directories.Push($target.FullName)
    while ($directories.Count) {
        $directory = $directories.Pop()
        Assert-PlainPath $directory
        foreach ($entry in (Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $entry.LinkType) { throw "Refusing linked fixture entry: $($entry.FullName)" }
            if ($entry.PSIsContainer) { $directories.Push($entry.FullName) }
        }
    }
    & $checkMarkers
    Remove-Item -LiteralPath $target.FullName -Recurse -Force -Confirm:$false -ErrorAction Stop
}

function Get-AppFileList([string]$outputPath) {
    $prefix = $outputPath.TrimEnd('\', '/') + '\'
    $roots = if ($IntermediateDirectory) {
        $selected = Get-Item -LiteralPath $IntermediateDirectory
        if (-not $selected.PSIsContainer) { throw "Expected the MSBuild intermediate directory: $IntermediateDirectory" }
        $selected.FullName
    } else {
        for ($dir = [IO.DirectoryInfo]::new($outputPath); $dir -and $dir.Parent; $dir = $dir.Parent) {
            if ($dir.Name -eq 'ClassNex' -and $dir.Parent.Name -in 'bin', 'publish' -and $dir.Parent.Parent) {
                Join-Path $dir.Parent.Parent.FullName 'obj\ClassNex'
                break
            }
        }
        Join-Path $clientRoot 'obj'
    }
    $manifests = foreach ($root in $roots) {
        if (-not [IO.Directory]::Exists($root)) { continue }
        Assert-PlainPath $root
        Get-ChildItem -LiteralPath $root -File -Recurse -Include 'ClassNex.csproj.FileListAbsolute.txt', 'PublishOutputs.*.txt'
    }
    foreach ($manifest in ($manifests | Sort-Object LastWriteTimeUtc -Descending -Unique)) {
        Assert-PlainPath $manifest.FullName
        $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in [IO.File]::ReadLines($manifest.FullName)) {
            if (-not [IO.Path]::IsPathRooted($entry)) { continue }
            $full = [IO.Path]::GetFullPath($entry)
            if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $relative = $full.Substring($prefix.Length)
            if ($relative -match '(^|\\)(data|plugins?|logs?|cache|\.git)(\\|$)|^_(shot|settings|verify|native)|(^|\\)account_avatar\.' -or
                $relative -match '\.(pdb|log|binlog|ya?ml|pfx|pem|key|user)$') { continue }
            if ($relative -match '\.json$' -and $relative -notmatch '^ClassNex\.(deps|runtimeconfig)\.json$|^WindowsAppSDK-VersionInfo\.json$') { continue }
            [void]$files.Add($relative)
        }
        $required = 'ClassNex.exe', 'ClassNex.dll', 'ClassNex.WinUI.dll', 'ClassNex.deps.json', 'ClassNex.runtimeconfig.json'
        if (@($required | Where-Object { -not $files.Contains($_) }).Count -ne 0) { continue }
        Write-Host "Build output list: $($manifest.FullName)"
        return $files
    }
    throw "No matching native MSBuild output list for $outputPath. Select its original build/publish output and, if using a custom obj layout, pass -IntermediateDirectory."
}

function Read-NativeReport([string]$path) {
    if (-not [IO.File]::Exists($path)) { return }
    try {
        Assert-PlainPath $path
        $stream = [IO.File]::Open($path, 'Open', 'Read', [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        try {
            $reader = [IO.StreamReader]::new($stream, $utf8)
            try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    } catch [IO.IOException] { return }
}

if (-not $OutputDirectory) {
    $project = [xml][IO.File]::ReadAllText((Join-Path $clientRoot 'ClassNex.csproj'))
    $framework = $project.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText.Trim()
    $arch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $candidates = foreach ($base in "bin\Debug\$framework", "bin\$arch\Debug\$framework") {
        foreach ($suffix in '', "\win-$arch") {
            $dll = Join-Path $clientRoot "$base$suffix\ClassNex.WinUI.dll"
            if ([IO.File]::Exists($dll)) { Get-Item -LiteralPath $dll }
        }
    }
    $nativeDll = $candidates | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $nativeDll) { throw 'No native Debug output. Run dotnet build src\ClassNex\ClassNex.csproj -c Debug -t:Rebuild first.' }
    $OutputDirectory = $nativeDll.DirectoryName
}
$sourceRoot = (Get-Item -LiteralPath $OutputDirectory).FullName
Assert-PlainPath $sourceRoot
if (-not $ExpectedProductVersion) {
    $versionResult = @(& dotnet msbuild (Join-Path $clientRoot 'ClassNex.csproj') -nologo -getProperty:ClassNexDesktopVersion)
    if ($LASTEXITCODE -ne 0 -or $versionResult.Count -ne 1) { throw 'Could not determine the desktop product version from ClassNex.csproj.' }
    $ExpectedProductVersion = $versionResult[0].Trim()
}
if ($ExpectedProductVersion -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._-]*\z') { throw "Invalid product version: $ExpectedProductVersion" }
foreach ($binary in 'ClassNex.exe', 'ClassNex.dll') {
    $binaryPath = Join-Path $sourceRoot $binary
    Assert-PlainPath $binaryPath
    $actual = (Get-Item -LiteralPath $binaryPath).VersionInfo.ProductVersion
    if ($actual -cne $ExpectedProductVersion) { throw "Version mismatch in ${binary}: expected '$ExpectedProductVersion', found '$actual'. Rebuild or select the intended output." }
}
$outputFiles = @(Get-AppFileList $sourceRoot)
$depsPath = Join-Path $sourceRoot 'ClassNex.deps.json'
Assert-PlainPath $depsPath
$deps = [IO.File]::ReadAllText($depsPath) | ConvertFrom-Json
if (-not ($deps.libraries.PSObject.Properties.Name -match '^ClassNex\.WinUI/')) { throw "ClassNex.deps.json does not reference the native client: $sourceRoot" }
if ($outputFiles -contains 'coreclr.dll') {
    foreach ($asset in 'ClassNex.pri', 'Microsoft.UI.Xaml.dll') {
        if ($outputFiles -notcontains $asset) { throw "Standalone WinUI resource missing from the publish list: $asset" }
    }
}
$requiredNativeChecks = @(
    'one-home-on-first-open', 'winui-page-transition', 'local-edit-preserves-session', 'managed-theme-updates-live-controls',
    'notification-restores-course-and-bounds', 'day-cache-midnight-invalidates', 'bridge-close-restores-settings-without-edit',
    'native-toggle-switch', 'toggle-persisted', 'switch-state-survives-reentry', 'switch-off-state-and-disk-agree',
    'general-shows-saved-startup-values', 'exact-desktop-product-version', 'about-shows-exact-product-version',
    'update-shows-exact-product-version', 'profile-editor-exact-product-version', 'deep-link-breadcrumb-opens-parent', 'deep-link-back-skips-child'
    foreach ($category in 'basic', 'widgets', 'notification', 'nex', 'privacy') { "category-list-$category" }
    foreach ($detail in 'startup', 'runtime', 'widget-layout', 'appearance', 'window', 'reminder-rules', 'reminder-display', 'nex-assistant', 'nex-wake', 'privacy-storage', 'privacy-services') {
        "option-opens-$detail"
        "breadcrumb-returns-$detail"
    }
) + $RequiredCheck
if ($ExpectedProductVersion -ceq '26w48a') {
    $requiredNativeChecks += 'option-opens-schedule', 'breadcrumb-returns-schedule'
} else {
    $requiredNativeChecks += 'about-displays-decoded-product-mark', 'quick-appearance-previews-before-saving-and-keeps-its-anchor', 'desktop-moving-back-removes-empty-row', 'home-preferences-apply-without-navigation', 'home-opens-independent-editor-without-settings-navigation',
        'search-opens-timeplan-in-same-editor', 'legacy-schedule-link-reuses-editor', 'home-opens-temporary-schedule-directly', 'update-idle-without-an-unsolicited-check',
        'widgets-start-in-horizontal-desktop-order', 'horizontal-selection-opens-matching-instance', 'separate-rows-have-separate-visible-strips',
        'merged-horizontal-row-drops-empty-strip', 'row-add-opens-component-library', 'row-returns-from-library-to-clock',
        'widget-list-and-selected-inspector', 'widget-library-has-add-and-drag', 'new-widget-selected-in-place', 'editing-one-widget-preserves-its-neighbor',
        'widget-copy-keeps-text-and-selection', 'hiding-copy-leaves-original-visible', 'widget-move-keeps-selected-instance', 'widget-remove-preserves-original', 'widget-list-switches-properties-without-navigation',
        'moving-widget-to-new-row-preserves-its-inspector', 'empty-widget-row-is-removed-without-changing-other-instances',
        'widget-appearance-is-optional', 'widget-width-margin-alignment-opacity-reach-live-classroom', 'widget-radius-and-background-update-without-recreating-clock',
        'reset-appearance-keeps-original-clock-control-and-settings', 'desktop-gear-and-grips-are-real-hit-targets', 'desktop-gear-opens-selected-clock-drawer',
        'drawer-edit-updates-same-clock-without-closing', 'closing-drawer-preserves-desktop-editor', 'pointer-drag-reorders-live-desktop-and-disk',
        'preset-picker-does-not-materialize-a-legacy-install', 'preset-dialog-rejects-existing-name', 'saving-preset-copy-leaves-current-desktop',
        'preset-copy-loads-original-values', 'switching-preset-flushes-both-field-and-slider', 'new-preset-starts-with-date-clock-and-schedule',
        'renaming-active-preset-keeps-layout', 'cancel-preset-delete-keeps-all-layouts', 'deleting-inactive-preset-preserves-edited-original',
        'reentering-page-restores-preset-selection', 'damaged-presets-leave-the-widget-editor-available', 'recovered-presets-retain-damaged-bytes-and-live-desktop',
        'widget-period-selector-starts-unrestricted', 'widget-period-selection-persists-and-reaches-live-clock', 'break-shows-clock-and-adjoining-date-divider',
        'activity-replaces-clock-and-collapses-empty-rows', 'clock-returns-at-end-of-activity', 'next-lesson-restores-classroom-row',
        'empty-day-shows-only-its-conditional-row', 'same-day-timetable-edits-update-widget-periods', 'edit-mode-reveals-widgets-outside-their-period',
        'leaving-edit-mode-reapplies-periods', 'all-filtered-widgets-remove-surface-and-release-pointer', 'reminder-can-use-otherwise-empty-classroom',
        'dismissed-reminder-leaves-no-empty-surface', 'nex-panel-can-use-otherwise-empty-classroom', 'hidden-nex-releases-empty-classroom-pointer',
        'break-restores-previously-empty-classroom-and-input'
}
if ($ExpectedProductVersion -ceq '26w48b') {
    $requiredNativeChecks += 'option-opens-reminder-after-school', 'breadcrumb-returns-reminder-after-school', 'dismissal-opt-in-default', 'dismissal-saved-through-navigation'
}

$sample = 'src/ClassNex/Assets/timetable.yaml'
$samplePath = Join-Path $repoRoot $sample
Assert-PlainPath $samplePath
$null = & git -C $repoRoot ls-files --error-unmatch -- $sample
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the tracked example timetable.' }
& git -C $repoRoot diff --quiet HEAD -- $sample
if ($LASTEXITCODE -ne 0) { throw 'The example timetable differs from HEAD. No fixture was created.' }

$buildRoot = Join-Path $repoRoot 'build'
[void][IO.Directory]::CreateDirectory($buildRoot)
Assert-PlainPath $buildRoot
$fixtureId = [Guid]::NewGuid()
$fixtureRoot = Join-Path $buildRoot ('verify-native-' + $fixtureId.ToString('N'))
$null = New-Item -ItemType Directory -Path $fixtureRoot -ErrorAction Stop
$appDir = Join-Path $fixtureRoot 'app'
$dataDir = Join-Path $fixtureRoot 'data'
[void][IO.Directory]::CreateDirectory($appDir)
[void][IO.Directory]::CreateDirectory($dataDir)
$null = New-Item -ItemType File -Path (Join-Path $appDir '.native-verify-owner') -Value ($fixtureId.ToString('N')) -ErrorAction Stop
Write-Host "Fixture: $fixtureRoot"
foreach ($relative in $outputFiles) {
    $source = Join-Path $sourceRoot $relative
    Assert-PlainPath $source
    $destination = Join-Path $appDir $relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    [IO.File]::Copy($source, $destination, $false)
}
[void][IO.Directory]::CreateDirectory((Join-Path $appDir 'Assets'))
[IO.File]::Copy($samplePath, (Join-Path $appDir 'Assets\timetable.yaml'), $false)
[IO.File]::Copy($samplePath, (Join-Path $dataDir 'timetable.yaml'), $false)
$loopback = 'http://127.0.0.1:1'
$settings = [ordered]@{
    IsOobeCompleted = $true; CentralManagement = @{ IsEnabled = $false; ServerUrl = $loopback }
    UserName = ''; Email = ''; AccountEndpoint = $loopback; NexApiBaseUrl = $loopback
    ShowStartupSplash = $false; IsMainWindowVisible = $false
    TeachingSafeMode = $false; RunAtStartup = $false; RegisterUrlProtocol = $false
    IsNexVoiceWakeEnabled = $false; IsNexEdgeBarEnabled = $false; AllowNotification = $false
    Notification = @{ IsClassOnNotificationEnabled = $false; IsClassOnPreparingNotificationEnabled = $false; IsClassOffNotificationEnabled = $false }
}
[IO.File]::WriteAllText((Join-Path $dataDir 'Settings.json'), ($settings | ConvertTo-Json -Depth 3), $utf8)
[IO.File]::WriteAllText((Join-Path $dataDir '.native-verify'), 'disposable', $utf8)

$exe = Join-Path $appDir 'ClassNex.exe'
$logPath = Join-Path $dataDir '_native_verify.log'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $exe
$start.Arguments = '--verify-native'
$start.WorkingDirectory = $appDir
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($name in @($start.EnvironmentVariables.Keys)) {
    if ($name -like 'CLASSNEX_VERIFY*') { $start.EnvironmentVariables.Remove($name) }
}
$start.EnvironmentVariables['CLASSNEX_VERIFY_PRODUCT_VERSION'] = $ExpectedProductVersion
$instance = $null
$passed = $false
$confirmedExit = $false
try {
    $instance = [Diagnostics.Process]::Start($start)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $exited = $instance.HasExited
        $report = Read-NativeReport $logPath
        $lines = @($report -split '\r?\n' | Where-Object { $_ -ne '' })
        $failures = @($lines -cmatch '^FAIL(?:\s|$)|^RESULT FAIL$')
        if ($failures.Count) { throw "Native verification failed: $($failures -join '; ') ($logPath)" }
        if ($exited -and $instance.ExitCode -ne 0) { throw "Native fixture exited with code $($instance.ExitCode): $logPath" }
        if ($lines.Count -and $lines[-1] -ceq 'RESULT PASS') {
            $checks = @($lines -cmatch '^PASS \S.*$')
            if (-not $checks.Count -or $checks.Count + 1 -ne $lines.Count) { throw "Malformed or empty native verification report: $logPath" }
            foreach ($key in $requiredNativeChecks) {
                if ($checks -cnotcontains "PASS $key") { throw "Missing native check '$key': $logPath" }
            }
            Write-Host $report.TrimEnd()
            Write-Host "Passed $($checks.Count) native checks. Report: $logPath"
            $passed = $true
            break
        }
        if ($exited) { throw "Fixture exited without a complete native result: $logPath" }
        if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "No complete native result in $TimeoutSeconds seconds: $logPath" }
        Start-Sleep -Milliseconds 200
    }
    if ($KeepOpen -and -not $instance.HasExited) { Write-Host "Fixture remains open (PID $($instance.Id)): $exe" }
} finally {
    if ($instance) {
        try {
            if (-not $KeepOpen -or -not $passed) {
                if ($instance.WaitForExit(300)) {
                    if ($passed -and $instance.ExitCode -ne 0) { throw "Native fixture exited with code $($instance.ExitCode): $logPath" }
                } else {
                    try {
                        $imagePath = [IO.Path]::GetFullPath($instance.MainModule.FileName)
                        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($imagePath, $exe)) { throw "Refusing to stop an unrelated process: $imagePath" }
                        $instance.Kill()
                    } catch {
                        if (-not $instance.HasExited) { throw }
                        if ($passed -and $instance.ExitCode -ne 0) { throw "Native fixture exited with code $($instance.ExitCode): $logPath" }
                    }
                    if (-not $instance.WaitForExit(5000)) { throw "Fixture did not stop (PID $($instance.Id)): $exe" }
                }
                $confirmedExit = $instance.HasExited
            }
        } finally { $instance.Dispose() }
    }
}
if ($passed -and $confirmedExit -and -not $KeepOpen -and -not $KeepFixture) {
    try {
        Remove-VerifiedAppCopy $fixtureId $appDir
        Write-Host "Removed verified app copy: $appDir. Logs and screenshots remain in $dataDir."
    } catch {
        Write-Warning "Fixture cleanup skipped or incomplete: $($_.Exception.Message). Evidence: $dataDir"
    }
}
