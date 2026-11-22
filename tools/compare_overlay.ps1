#requires -Version 7.2
param([Parameter(Mandatory)][ValidateSet('Alpha','Candidate')][string]$Version,
      [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Round = 'overlay-history')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $repo "artifacts/ui-review/$Round"
$target = Join-Path $output ("overlay-" + $Version.ToLowerInvariant() + '.png')
if (Test-Path -LiteralPath $target) { throw "Capture already exists: $target" }
$null = [IO.Directory]::CreateDirectory($output)
$port = Join-Path $repo ('build/overlay-review-' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $port 'data'
$app = Join-Path $port 'app'
$null = [IO.Directory]::CreateDirectory($data)
if ($Version -eq 'Alpha') {
    $sha = git -C $repo rev-parse --verify 'refs/tags/26w46d_Alpha^{commit}'
    if ($LASTEXITCODE -ne 0 -or $sha -ne '60d7b575b1b866a0bc388c9b80986c546950a2c7') { throw 'Alpha tag mismatch' }
    $source = Join-Path $port 'source'; $archive = Join-Path $port 'source.zip'
    $inputs = @('src/ClassNex','src/ClassNex.Core','src/ClassNex.Notification','src/ClassNex.Plugins','src/ClassNex.AI','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','global.json','NuGet.Config')
    $paths = @(git -C $repo ls-tree --name-only $sha -- $inputs)
    git -C $repo archive --format=zip "--output=$archive" $sha -- $paths
    if ($LASTEXITCODE) { throw 'Could not export the tagged Alpha code' }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive,$source)
    dotnet publish (Join-Path $source 'src/ClassNex/ClassNex.csproj') -c Debug --no-self-contained -m:1 -nr:false -o $app --nologo -v:q
} else {
    dotnet publish (Join-Path $repo 'src/ClassNex/ClassNex.csproj') -c Debug --no-build --no-restore --no-self-contained -o $app --nologo -v:q
}
if ($LASTEXITCODE) { throw "Cannot publish $Version; rebuild the current Debug client before comparing it." }
$ids = if ($Version -eq 'Alpha') { 'date','clock','schedule' } else { 'date','schedule' }
$widgets = for ($i=0; $i -lt $ids.Count; $i++) { @{ Type=$ids[$i]; Order=$i; IsEnabled=$true } }
$settings = @{ ThemeMode='dark'; Topmost=$false; IsClickThrough=$true; HoverOpacity=1; MainWindowScale=1.35; FontScale=1;
    BackgroundOpacity=.55; IsOobeCompleted=$true; IsMainWindowVisible=$true; ShowStartupSplash=$false; SampleSeedVersion=3;
    IsNexIntelligenceEnabled=$false; IsNexVoiceWakeEnabled=$false; IsNexEdgeBarEnabled=$false; AllowNotification=$false;
    CentralManagement=@{ IsEnabled=$false; ServerUrl='http://127.0.0.1:1/Control' }; AccountEndpoint='http://127.0.0.1:1';
    NexApiBaseUrl='http://127.0.0.1:1'; Widgets=@($widgets) }
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $data 'Settings.json'),($settings | ConvertTo-Json -Depth 6),$utf8)
$now = Get-Date
$split = [TimeSpan]::FromMinutes([Math]::Min(1439, ([Math]::Floor(($now.Hour * 60 + $now.Minute) / 30) + 1) * 30)).ToString('hh\:mm\:ss')
$timetable = "version: 1`nsubjects:`n  - name: 数学`n    simplified_name: 数`n  - name: 语文`n    simplified_name: 语`nschedules:`n" + ((1..7 | ForEach-Object {
    "  - name: 课堂$_`n    enable_day: $_`n    weeks: all`n    classes:`n      - subject: 数学`n        start_time: '00:00:00'`n        end_time: '$split'`n      - subject: 语文`n        start_time: '$split'`n        end_time: '23:59:59'`n"
}) -join '')
[IO.File]::WriteAllText((Join-Path $data 'timetable.yaml'),$timetable,$utf8)
[IO.File]::WriteAllText((Join-Path $port '.classnex-review'),'anonymous overlay comparison')
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'ClassNex.exe'))
$start.WorkingDirectory = $app; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
foreach ($key in @($start.Environment.Keys | Where-Object {$_ -like 'CLASSNEX*'})) { $null = $start.Environment.Remove($key) }
$start.Environment['CLASSNEX_VERIFY'] = '1'; $start.Environment['CLASSNEX_VERIFY_SHOT'] = '1'
$process = [Diagnostics.Process]::Start($start)
try {
    $shot = Join-Path $app '_shot_main.png'
    $until = [DateTime]::UtcNow.AddSeconds(45)
    while ((!([IO.File]::Exists($shot)) -or (Get-Item -LiteralPath $shot).Length -lt 250) -and !$process.HasExited -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 150 }
    if (!(Test-Path -LiteralPath $shot)) { throw "Missing real window capture in $port" }
    Copy-Item -LiteralPath $shot -Destination $target
    Write-Output "$Version classroom: $target"
} finally {
    if (!$process.HasExited) {
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit(1500) -and $process.MainModule.FileName -eq $start.FileName) { $process.Kill(); $null = $process.WaitForExit(5000) }
    }
    $process.Dispose()
}
