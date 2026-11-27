param([Parameter(Mandatory)][ValidatePattern('^26w[0-9]{2}[a-z]$')][string]$ExpectedVersion, [switch]$Migration)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$id = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$root = Join-Path $repo "build/preview-$ExpectedVersion-$id"
$app = Join-Path $root 'app'
$data = New-Item -ItemType Directory -Path (Join-Path $root 'data')
dotnet publish (Join-Path $repo 'src/ClassNex/ClassNex.csproj') -c Debug --no-build --no-restore --no-self-contained --nologo -o $app
if ($LASTEXITCODE) { throw 'Build the Debug desktop client before opening a preview.' }
$exe = Get-Item -LiteralPath (Join-Path $app 'ClassNex.exe')
if ($exe.VersionInfo.ProductVersion -cne $ExpectedVersion) { throw "Preview build is $($exe.VersionInfo.ProductVersion), expected $ExpectedVersion." }
if ($ExpectedVersion -cge '26w48b' -and !(Test-Path -LiteralPath (Join-Path $app 'Assets/app-mark.png') -PathType Leaf)) { throw 'Preview is missing the ClassNEX product icon.' }
$prefs = [ordered]@{ IsOobeCompleted=$true; SampleSeedVersion=3; IsMainWindowVisible=$true; IsClickThrough=$false; Topmost=$true;
    ThemeMode='dark'; MainWindowScale=1.45; RunAtStartup=$false; RegisterUrlProtocol=$false; IsNexVoiceWakeEnabled=$false; IsNexEdgeBarEnabled=$false;
    CentralManagement=@{IsEnabled=$false} }
if ($Migration) {
    $prefs['IsNexIntelligenceEnabled'] = $false
    $prefs['AccountEndpoint'] = 'http://127.0.0.1:9'
    $prefs.CentralManagement['ServerUrl'] = 'http://127.0.0.1:9'
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $data.FullName 'Settings.json'), ($prefs | ConvertTo-Json), $utf8)
$now = Get-Date
$start = [Math]::Clamp($now.Hour * 60 + $now.Minute - 15, 0, 1335)
$day = if ($now.DayOfWeek -eq [DayOfWeek]::Sunday) {7} else {[int]$now.DayOfWeek}
function Clock([int]$minute) { [TimeSpan]::FromMinutes($minute).ToString('hh\:mm\:ss') }
$profile = @"
version: 1
subjects:
  - name: 信息科技
    simplified_name: 信
    teacher: 林老师
  - name: 体育与健康
    simplified_name: 体
    teacher: 陈老师
    x_classnex_outdoor: true
schedules:
  - name: 预览课表
    enable_day: $day
    weeks: all
    classes:
      - subject: 信息科技
        start_time: "$(Clock $start)"
        end_time: "$(Clock ($start + 40))"
      - subject: 体育与健康
        start_time: "$(Clock ($start + 50))"
        end_time: "$(Clock ($start + 95))"
"@
[IO.File]::WriteAllText((Join-Path $data.FullName 'timetable.yaml'), $profile, $utf8)
$launch = [Diagnostics.ProcessStartInfo]::new($exe.FullName)
$launch.WorkingDirectory = $app
$launch.UseShellExecute = $false
foreach ($name in @($launch.Environment.Keys | Where-Object { $_ -like 'CLASSNEX_VERIFY*' -or $_ -like 'CLASSNEX_REVIEW*' -or $_ -eq 'CLASSNEX_NEX_API_KEY' })) { $null = $launch.Environment.Remove($name) }
if ($Migration) {
    [IO.File]::WriteAllText((Join-Path $root '.classnex-review'), 'isolated migration preview', $utf8)
    $launch.ArgumentList.Add('--preview-migration')
    $launch.Environment['CLASSNEX_VERIFY_OOBE'] = 'Data'
} else { $launch.ArgumentList.Add('classnex://settings') }
$process = [Diagnostics.Process]::Start($launch)
"$ExpectedVersion · PID $($process.Id) · $($exe.FullName)"
