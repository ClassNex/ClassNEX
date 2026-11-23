param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Round)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$fixture = Join-Path $repo ('build/tray-' + [guid]::NewGuid().ToString('N'))
$output = Join-Path $repo "artifacts/tray-review/$Round"
if (Test-Path -LiteralPath $output) { throw "Tray review already exists: $output" }
$app = Join-Path $fixture 'app'; $data = Join-Path $fixture 'data'
New-Item -ItemType Directory -Path $data,$output | Out-Null
dotnet publish (Join-Path $repo 'src/ClassNex/ClassNex.csproj') -c Debug --no-build --no-restore --no-self-contained -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw 'Rebuild the Debug client before checking the notification area' }
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $fixture '.classnex-review'), 'isolated tray review', $utf8)
foreach ($scenario in 'legacy-0','legacy-1','legacy-2','legacy-3','oobe') {
    $setup = $scenario -eq 'oobe'
    $legacy = if ($setup) { 0 } else { [int]$scenario.Substring(7) }
    $settings = @{ IsOobeCompleted=(!$setup); IsMainWindowVisible=($legacy % 2 -eq 0); TrayClickBehavior=$legacy;
        AllowNotification=$false; IsNexVoiceWakeEnabled=$false; IsNexEdgeBarEnabled=$false; RunAtStartup=$false;
        RegisterUrlProtocol=$false; CentralManagement=@{ IsEnabled=$false }; SampleSeedVersion=3 }
    [IO.File]::WriteAllText((Join-Path $data 'Settings.json'), ($settings | ConvertTo-Json -Depth 4), $utf8)
    $launch = [Diagnostics.ProcessStartInfo]::new((Join-Path $app 'ClassNex.exe'))
    $launch.WorkingDirectory=$app; $launch.UseShellExecute=$false; $launch.CreateNoWindow=$true
    $launch.ArgumentList.Add('--review-tray')
    foreach ($key in @($launch.Environment.Keys | Where-Object { $_ -like 'CLASSNEX_*' })) { $null=$launch.Environment.Remove($key) }
    $process=[Diagnostics.Process]::Start($launch)
    try {
        if (!$process.WaitForExit(35000)) { $process.Kill(); throw "Tray fixture timed out: $scenario" }
        $reportPath = Join-Path $fixture 'tray-review.json'
        if (!(Test-Path -LiteralPath $reportPath)) { throw "No report: $scenario, exit=$($process.ExitCode)" }
        $target = Join-Path $output "$scenario.json"
        Move-Item -LiteralPath $reportPath -Destination $target
        $report = Get-Content -LiteralPath $target -Raw -Encoding utf8 | ConvertFrom-Json
        if (!$report.Passed -or $process.ExitCode -ne 0) { throw "Tray review failed: $scenario, $($report.Error); $target" }
        "PASS: $scenario, $($report.Checks.Count) checks; $target"
    } finally { $process.Dispose() }
}
