param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Round,
    [switch]$KeepOpen,
    [switch]$EnabledReminders,
    [switch]$RemindersOnly,
    [switch]$ReminderGeometryOnly,
    [switch]$ReminderInputOnly,
    [switch]$DesktopPointerOnly,
    [switch]$SubjectOnly,
    [switch]$InteractiveWheel,
    [switch]$ReducedMotion
)
$ErrorActionPreference = 'Stop'
if ($DesktopPointerOnly) {
    if ($KeepOpen -or $EnabledReminders -or $InteractiveWheel -or $RemindersOnly -or $ReminderGeometryOnly -or $ReminderInputOnly) { throw '-DesktopPointerOnly runs independently on a private desktop.' }
    $guard = @"
import ctypes as c, re, sys
u=c.windll.user32; u.GetThreadDesktop.restype=c.c_void_p
d=c.c_void_p(u.GetThreadDesktop(c.windll.kernel32.GetCurrentThreadId())); size=c.c_ulong(); name=c.create_unicode_buffer(128); active=c.c_int(1)
ok=d.value and u.GetUserObjectInformationW(d,2,name,c.sizeof(name),c.byref(size)) and re.fullmatch('ClassNEX-review-[0-9a-f]{32}',name.value) and u.GetUserObjectInformationW(d,6,c.byref(active),c.sizeof(active),c.byref(size)) and active.value==0
sys.exit(0 if ok else 1)
"@
    py -3 -c $guard
    if ($LASTEXITCODE -ne 0) { throw 'A desktop pointer review must be launched through tools/run_review_offscreen.py; no window was opened.' }
}
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
if (!($RemindersOnly -or $ReminderGeometryOnly -or $ReminderInputOnly -or $InteractiveWheel -or $DesktopPointerOnly -or $SubjectOnly)) {
    $pluginOutput = Join-Path $fixture 'plugin-build'
    dotnet build (Join-Path $repo 'tests/Fixtures/ClassNex.SamplePlugin/ClassNex.SamplePlugin.csproj') -c Debug --artifacts-path $pluginOutput -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated sample plugin' }
    $sample = Join-Path $app 'plugins/fixture'
    New-Item -ItemType Directory -Path $sample | Out-Null
    Copy-Item -LiteralPath (Join-Path $pluginOutput 'bin/ClassNex.SamplePlugin/debug/ClassNex.SamplePlugin.dll') -Destination $sample
    [IO.File]::WriteAllText((Join-Path $sample 'plugin.json'),'{"id":"fixture","name":"课堂提醒示例","version":"1.0","assembly":"ClassNex.SamplePlugin.dll","entryType":"ClassNex.SamplePlugin.Entry","enabled":false,"permissions":["notifications"]}',$encoding)
}
[IO.File]::WriteAllText((Join-Path $fixture '.classnex-review'),'isolated desktop review',$encoding)
$settings = [ordered]@{ IsOobeCompleted=$true; IsDebugOptionsEnabled=$true; IsNexVoiceWakeEnabled=$false; IsNexEdgeBarEnabled=$false; IsMainWindowVisible=$true; IsClickThrough=$false; Topmost=$false; ThemeMode='light'; UserName=''; Email=''; MainWindowScale=1.35; AllowNotification=[bool]$EnabledReminders; CentralManagement=@{IsEnabled=$false}; SingleWeekStartTime='2026-09-07'; SampleSeedVersion=3 }
$settings.SampleSeedVersion = 1
$settings.BackgroundOpacity = 0.55
if ($InteractiveWheel) { $settings.Topmost = $true }
[IO.File]::WriteAllText((Join-Path $data 'Settings.json'),($settings | ConvertTo-Json -Depth 8),$encoding)
$yaml = @"
version: 1
subjects:
  - name: 数学
    simplified_name: 数
  - name: 信息科技：七年级班级板报图文排版与小组展示
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
      - subject: 信息科技：七年级班级板报图文排版与小组展示
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
if ($ReminderGeometryOnly) { $program.Environment['CLASSNEX_REVIEW_REMINDER_GEOMETRY'] = '1' }
if ($DesktopPointerOnly) { $program.Environment['CLASSNEX_REVIEW_DESKTOP_POINTER'] = '1' }
if ($SubjectOnly) { $program.Environment['CLASSNEX_REVIEW_SUBJECT'] = '1' }
if ($ReminderInputOnly -or $InteractiveWheel) { $program.Environment['CLASSNEX_REVIEW_REMINDER_INPUT'] = '1' }
if ($InteractiveWheel) { $program.Environment['CLASSNEX_REVIEW_NATIVE_WHEEL'] = '1' }
if ($ReducedMotion) { $program.Environment['CLASSNEX_REVIEW_REDUCED_MOTION'] = '1' }
$process = [Diagnostics.Process]::Start($program)
try {
    $report = Join-Path $output 'review.json'
    $timeout = [DateTime]::UtcNow.AddMinutes($(if ($RemindersOnly) { 4 } else { 6 }))
    while (!(Test-Path -LiteralPath $report) -and !$process.HasExited -and [DateTime]::UtcNow -lt $timeout) { Start-Sleep -Milliseconds 500 }
    if (!(Test-Path -LiteralPath $report)) { throw "Missing report from fixture PID $($process.Id); diagnostic data: $data" }
    if (-not $KeepOpen) { if (!$process.WaitForExit(10000)) { throw 'Review window did not shut down' } }
    $result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
    if (-not $result.Passed) { throw ($result.Checks[-1]) }
    $surface = if ($DesktopPointerOnly) { 'classroom and native backing button on a non-input desktop' } else { "$($result.Captures.Count) rendered windows" }
    "PASS: $surface; $($result.Checks.Count) checks; $report; fixture PID $($process.Id)"
} finally {
    if (!$process.HasExited -and (!$KeepOpen -or !(Test-Path -LiteralPath $report))) { $process.Kill() }
    $process.Dispose()
}
