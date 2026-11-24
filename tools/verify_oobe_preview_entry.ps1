[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$fixture = (New-Item -ItemType Directory -Path (Join-Path $repo ('build/preview-entry-' + [guid]::NewGuid().ToString('N')))).FullName
$install = Join-Path $fixture 'ordinary-install'
$app = Join-Path $install 'app'
$data = Join-Path $install 'data'
$utf8 = [Text.UTF8Encoding]::new($false)

dotnet publish (Join-Path $repo 'src/ClassNex/ClassNex.csproj') -c $Configuration --no-self-contained --artifacts-path (Join-Path $fixture 'msbuild') -o $app -m:1 -nr:false --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Isolated publish failed.' }

function Get-InstallSnapshot {
    foreach ($entry in (Get-ChildItem -LiteralPath $install -Recurse -Force | Sort-Object FullName)) {
        $relative = [IO.Path]::GetRelativePath($install, $entry.FullName)
        if ($entry.PSIsContainer) { "directory|$relative"; continue }
        $sha = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash
        "file|$relative|$($entry.Length)|$($entry.LastWriteTimeUtc.Ticks)|$sha"
    }
}

function Test-OrdinaryInstall([string]$caseName) {
    $before = @(Get-InstallSnapshot)
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'ClassNex.exe'))
    $start.WorkingDirectory = $app
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($key in @($start.Environment.Keys | Where-Object { $_ -like 'CLASSNEX_*' })) { [void]$start.Environment.Remove($key) }
    $start.ArgumentList.Add('--preview-migration')
    # NTFS can settle directory timestamps after publish. Observe actual entry changes, including transient files.
    $watch = [IO.FileSystemWatcher]::new($install)
    $watch.IncludeSubdirectories = $true
    $watch.NotifyFilter = [IO.NotifyFilters]::FileName -bor [IO.NotifyFilters]::DirectoryName
    $prefix = 'preview-entry-' + [guid]::NewGuid().ToString('N')
    $events = @('Created', 'Deleted', 'Renamed', 'Error')
    try {
        foreach ($eventName in $events) { Register-ObjectEvent -InputObject $watch -EventName $eventName -SourceIdentifier "$prefix-$eventName" | Out-Null }
        $watch.EnableRaisingEvents = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (!$process.WaitForExit(20000)) { $process.Kill($true); throw "$caseName reached desktop startup instead of rejecting the flag." }
            if ($process.ExitCode -ne 2) { throw "$caseName exited with $($process.ExitCode), expected 2." }
        } finally { $process.Dispose() }
        $after = @(Get-InstallSnapshot)
        $delta = @(Compare-Object $before $after -CaseSensitive)
        if ($delta.Count) { throw "$caseName changed the installation snapshot: $($delta[0].InputObject)." }
        $received = @(Get-Event | Where-Object SourceIdentifier -Like "$prefix-*")
        if ($received.Count) { throw "$caseName caused a filesystem event: $($received[0].SourceIdentifier) $($received[0].SourceEventArgs.Name)." }
    } finally {
        $watch.Dispose()
        foreach ($eventName in $events) {
            Unregister-Event -SourceIdentifier "$prefix-$eventName" -ErrorAction SilentlyContinue
            Remove-Event -SourceIdentifier "$prefix-$eventName" -ErrorAction SilentlyContinue
        }
    }
    [ordered]@{ case=$caseName; exitCode=2; unchanged=$true; entryChanges=0; filesHashed=@($before -clike 'file|*').Count; dataExistsAfter=(Test-Path -LiteralPath $data) }
}

if (Test-Path -LiteralPath $data) { throw 'A clean publish unexpectedly contains runtime data.' }
$emptyCase = Test-OrdinaryInstall 'empty-install'
if ($emptyCase.dataExistsAfter) { throw 'The rejected preview created data.' }
[void](New-Item -ItemType Directory -Path $data)
[IO.File]::WriteAllText((Join-Path $data 'Settings.json'), '{"UserName":"fixture sentinel"}', $utf8)
[IO.File]::WriteAllText((Join-Path $install '.classnex-review'), 'isolated migration preview', $utf8)
$existingCase = Test-OrdinaryInstall 'existing-settings-with-preview-marker'
$report = [ordered]@{ configuration=$Configuration; cases=@($emptyCase, $existingCase) } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $fixture 'result.json'), $report, $utf8)
$report
"Evidence: $fixture"
