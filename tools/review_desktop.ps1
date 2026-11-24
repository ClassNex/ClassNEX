param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Round,
    [switch]$KeepOpen,
    [switch]$EnabledReminders,
    [switch]$RemindersOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$id = [Guid]::NewGuid().ToString('N').Substring(0,8)
$fixture = Join-Path $repo "build/ui-review-$Round-$id"
$output = Join-Path $repo "artifacts/ui-review/$Round"
if (Test-Path -LiteralPath $output) { throw "Review output exists: $output. Use a new round name." }
$app = Join-Path $fixture 'app'
$data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Path $data,$output | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
dotnet publish (Join-Path $repo 'src/ClassNex/ClassNex.csproj') -c Debug --no-build --no-restore --no-self-contained -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build the Debug client before reviewing' }
dotnet build (Join-Path $repo 'tests/Fixtures/ClassNex.SamplePlugin/ClassNex.SamplePlugin.csproj') -c Debug -v:q
if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated sample plugin' }
$sample = Join-Path $app 'plugins/fixture'
New-Item -ItemType Directory -Path $sample | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'tests/Fixtures/ClassNex.SamplePlugin/bin/Debug/net10.0/ClassNex.SamplePlugin.dll') -Destination $sample
[IO.File]::WriteAllText((Join-Path $sample 'plugin.json'),'{"id":"fixture","name":"课堂提醒示例","version":"1.0","assembly":"ClassNex.SamplePlugin.dll","entryType":"ClassNex.SamplePlugin.Entry","enabled":false,"permissions":["notifications"]}',$encoding)
[IO.File]::WriteAllText((Join-Path $fixture '.classnex-review'),'isolated desktop review',$encoding)
$settings = [ordered]@{ IsOobeCompleted=$true; IsDebugOptionsEnabled=$true; IsNexVoiceWakeEnabled=$false; IsNexEdgeBarEnabled=$false; IsMainWindowVisible=$true; IsClickThrough=$false; Topmost=$false; ThemeMode='light'; UserName=''; Email=''; MainWindowScale=1.35; AllowNotification=[bool]$EnabledReminders; CentralManagement=@{IsEnabled=$false}; SingleWeekStartTime='2026-09-07'; SampleSeedVersion=3 }
$settings.SampleSeedVersion = 1
$settings.BackgroundOpacity = 0.55
[IO.File]::WriteAllText((Join-Path $data 'Settings.json'),($settings | ConvertTo-Json -Depth 8),$encoding)
$yaml = @"
version: 1
subjects:
  - name: 数学
    simplified_name: 数
  - name: 信息科技跨学科实践：校园气象数据的采集与分析
    simplified_name: 信
    teacher: 陈老师
schedules:
  - name: 教学日
    enable_day: 1
    weeks: all
    classes:
      - subject: 数学
        start_time: "09:42:00"
        end_time: "10:22:00"
      - subject: 信息科技跨学科实践：校园气象数据的采集与分析
        start_time: "10:32:00"
        end_time: "11:12:00"
"@
[IO.File]::WriteAllText((Join-Path $data 'timetable.yaml'),$yaml,$encoding)
$program = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'ClassNex.exe'))
$program.WorkingDirectory = $app
$program.UseShellExecute = $false
$program.CreateNoWindow = $true
$program.ArgumentList.Add('--review-ui'); $program.ArgumentList.Add($output)
foreach ($name in @($program.Environment.Keys | Where-Object { $_ -like 'CLASSNEX_VERIFY*' -or $_ -like 'CLASSNEX_REVIEW*' -or $_ -eq 'CLASSNEX_NEX_API_KEY' })) { $null = $program.Environment.Remove($name) }
if ($KeepOpen) { $program.Environment['CLASSNEX_REVIEW_KEEP_OPEN'] = '1' }
if ($RemindersOnly) { $program.Environment['CLASSNEX_REVIEW_REMINDERS_ONLY'] = '1' }
$process = [Diagnostics.Process]::Start($program)
try {
    $report = Join-Path $output 'review.json'
    $timeout = [DateTime]::UtcNow.AddMinutes($(if ($RemindersOnly) { 2 } else { 6 }))
    while (!(Test-Path -LiteralPath $report) -and !$process.HasExited -and [DateTime]::UtcNow -lt $timeout) { Start-Sleep -Milliseconds 500 }
    if (!(Test-Path -LiteralPath $report)) { throw "Missing report from fixture PID $($process.Id); diagnostic data: $data" }
    if (-not $KeepOpen) { if (!$process.WaitForExit(10000)) { throw 'Review window did not shut down' } }
    $result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
    if (-not $result.Passed) { throw ($result.Checks[-1]) }
    "PASS: $($result.Captures.Count) rendered windows; $($result.Checks.Count) checks; $report; fixture PID $($process.Id)"
} finally {
    if (!$process.HasExited -and (!$KeepOpen -or !(Test-Path -LiteralPath $report))) { $process.Kill() }
    $process.Dispose()
}
