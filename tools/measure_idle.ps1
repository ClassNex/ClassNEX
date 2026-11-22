#requires -Version 7.2
param(
    [ValidateNotNullOrEmpty()][string]$CandidateCommit = 'HEAD',
    [ValidateRange(5,120)][int]$WarmupSeconds = 15,
    [ValidateRange(10,300)][int]$SampleSeconds = 30,
    [ValidateRange(1,5)][int]$IntervalSeconds = 2,
    [ValidateRange(1,4)][int]$Pairs = 2
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'A local Windows desktop and .NET 10 are required.' }
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$tag = '26w46d_Alpha'
$expected = '60d7b575b1b866a0bc388c9b80986c546950a2c7'
$baseline = git -C $repo rev-parse --verify --end-of-options "refs/tags/${tag}^{commit}"
if ($LASTEXITCODE -ne 0 -or $baseline -ne $expected) { throw 'The pinned Alpha tag is missing or has moved.' }
$candidate = git -C $repo rev-parse --verify --end-of-options "${CandidateCommit}^{commit}"
if ($LASTEXITCODE -ne 0 -or $candidate -notmatch '^[0-9a-f]{40}$') { throw "Not a candidate commit: $CandidateCommit" }
git -C $repo merge-base --is-ancestor $baseline $candidate
if ($LASTEXITCODE -ne 0) { throw "Candidate $candidate does not descend from $tag, or its ancestry is unavailable." }
$round = Join-Path $repo ('build/idle-performance-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $round
$snapshot = @{}
$commits = @{alpha=$baseline; candidate=$candidate}
$inputs = 'src/ClassNex','src/ClassNex.Core','src/ClassNex.AI','src/ClassNex.Plugin','src/ClassNex.Plugins',
          'src/ClassNex.Notification.Service','src/ClassNex.Notification','Directory.Build.props','Directory.Build.targets',
          'Directory.Packages.props','global.json','NuGet.Config','nuget.config'
foreach ($version in 'alpha','candidate') {
    $commit = $commits[$version]
    $source = Join-Path $round "$version/source"
    $null = New-Item -ItemType Directory -Path $source
    $paths = @(git -C $repo ls-tree --name-only $commit -- $inputs)
    if ($LASTEXITCODE -ne 0 -or $paths -notcontains 'src/ClassNex') { throw "Could not locate the desktop project in $commit." }
    $archive = Join-Path $round "$version-src.zip"
    git -C $repo archive --format=zip "--output=$archive" $commit -- $paths
    if ($LASTEXITCODE -ne 0) { throw "Could not export $version at $commit." }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive,$source)
    $hashes = @(Get-ChildItem -LiteralPath $source -Recurse -File | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($source,$_.FullName).Replace('\','/') + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash
    })
    $snapshot[$version] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($hashes -join "`n"))))
    dotnet publish (Join-Path $source 'src/ClassNex/ClassNex.csproj') -c Release --no-self-contained --artifacts-path (Join-Path $round "$version/msbuild") -o (Join-Path $round "$version/payload") -m:1 -nr:false --nologo -v:q
    if ($LASTEXITCODE) { throw "$version failed to publish." }
}
$settings = @{
    IsOobeCompleted=$true; IsMainWindowVisible=$true; Topmost=$true; IsClickThrough=$true; ThemeMode='light'; IsDebugOptionsEnabled=$false
    MainWindowScale=1.35; FontScale=1; BackgroundOpacity=0.5; SingleWeekStartTime='2026-09-07'; SampleSeedVersion=3
    AllowNotification=$false; AllowNotificationEffect=$false; AllowMinorNotification=$false; IsNexIntelligenceEnabled=$false; IsNexVoiceWakeEnabled=$false; IsNexEdgeBarEnabled=$false
    CentralManagement=@{IsEnabled=$false;ServerUrl='http://127.0.0.1:1/Control'}; AccountEndpoint='http://127.0.0.1:1'; NexApiBaseUrl='http://127.0.0.1:1'; NexApiKey=''; UserName=''; Email=''
    Widgets=@(@{Type='date';Order=0;IsEnabled=$true},@{Type='schedule';Order=1;IsEnabled=$true})
} | ConvertTo-Json -Depth 6
$timetable = "version: 1`nsubjects:`n  - name: 数学`n  - name: 语文`nschedules:`n" + ((1..7 | ForEach-Object {
    "  - name: 课堂$_`n    enable_day: $_`n    weeks: all`n    classes:`n      - subject: 数学`n        start_time: '00:00:00'`n        end_time: '23:55:00'`n      - subject: 语文`n        start_time: '23:56:00'`n        end_time: '23:59:00'`n"
}) -join '')
function Get-Median($values) {
    $a = @($values | Sort-Object); $n = $a.Count
    ($a[[Math]::Floor(($n-1)/2)] + $a[[Math]::Floor($n/2)]) / 2
}
$order = @(for ($pair=0; $pair -lt $Pairs; $pair++) { if ($pair % 2) { 'candidate'; 'alpha' } else { 'alpha'; 'candidate' } })
$runs = @(); $index = 0
foreach ($version in $order) {
    $index++
    $fixture = Join-Path $round "run-$index-$version"
    $data = Join-Path $fixture 'data'
    $app = Join-Path $fixture 'app'
    $null = New-Item -ItemType Directory -Path $data
    Copy-Item -LiteralPath (Join-Path $round "$version/payload") -Destination $app -Recurse
    [IO.File]::WriteAllText((Join-Path $data 'Settings.json'),$settings)
    [IO.File]::WriteAllText((Join-Path $data 'timetable.yaml'),$timetable)
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'ClassNex.exe'))
    $start.WorkingDirectory = $app; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    foreach ($key in @($start.Environment.Keys | Where-Object { $_ -like 'CLASSNEX*' -or $_ -eq 'CORECLR_ENABLE_PROFILING' -or $_ -eq 'DOTNET_STARTUP_HOOKS' })) { $null = $start.Environment.Remove($key) }
    $start.Environment['DOTNET_EnableDiagnostics'] = '0'
    Write-Host "Run $index/$($order.Count): $version, $WarmupSeconds s warm-up + $SampleSeconds s capture"
    $process = [Diagnostics.Process]::Start($start)
    try {
        Start-Sleep -Seconds $WarmupSeconds
        if ($process.HasExited -or (Test-Path -LiteralPath (Join-Path $data 'Logs/crash.log'))) { throw "Boot failure in $fixture" }
        $process.Refresh(); $initialCpu = $process.TotalProcessorTime.TotalSeconds
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $samples = @(while ($true) {
            $process.Refresh()
            if ($process.HasExited) { throw "Unexpected exit of this fixture: $fixture" }
            [pscustomobject]@{ Seconds=$watch.Elapsed.TotalSeconds; CpuSeconds=$process.TotalProcessorTime.TotalSeconds; WorkingMiB=$process.WorkingSet64/1MB; PrivateMiB=$process.PrivateMemorySize64/1MB }
            $remaining = $SampleSeconds - $watch.Elapsed.TotalSeconds
            if ($remaining -le 0) { break }
            Start-Sleep -Milliseconds ([Math]::Max(1,[int](1000*[Math]::Min($remaining,$IntervalSeconds))))
        })
        $watch.Stop(); $duration=$samples[-1].Seconds
        if ((Test-Path -LiteralPath (Join-Path $data 'account.json')) -or (Test-Path -LiteralPath (Join-Path $data 'Logs/crash.log'))) { throw "Unexpected account or crash in $fixture" }
        $runs += [pscustomobject]@{ Order=$index; Version=$version; Seconds=$duration; CpuSeconds=($samples[-1].CpuSeconds-$initialCpu); CpuOneCorePercent=100*($samples[-1].CpuSeconds-$initialCpu)/$duration; PrivateMedianMiB=(Get-Median $samples.PrivateMiB); WorkingMedianMiB=(Get-Median $samples.WorkingMiB); Samples=$samples }
    } finally {
        if (!$process.HasExited) { $process.Kill(); if (!$process.WaitForExit(10000)) { throw "Could not stop owned PID $($process.Id)" } }
        $process.Dispose()
    }
}
$result = [ordered]@{ DateUtc=[DateTime]::UtcNow.ToString('o'); Baseline=$baseline; CandidateCommit=$candidate; SourceSHA256=$snapshot; SDK=(dotnet --version); Windows=[Environment]::OSVersion.Version.ToString(); LogicalCPUs=[Environment]::ProcessorCount; WarmupSeconds=$WarmupSeconds; SampleSeconds=$SampleSeconds; IntervalSeconds=$IntervalSeconds; Runs=$runs }
[IO.File]::WriteAllText((Join-Path $round 'results.json'),($result | ConvertTo-Json -Depth 6))
$runs | Format-Table Order,Version,CpuSeconds,CpuOneCorePercent,PrivateMedianMiB,WorkingMedianMiB
Write-Host "Raw counters: $round/results.json"
