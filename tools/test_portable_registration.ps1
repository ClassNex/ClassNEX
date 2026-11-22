[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Launcher,
    [string]$SourceFile = (Join-Path $PSScriptRoot '../src/ClassNex/Services/StartupRegistry.cs')
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$source = "#nullable enable`nglobal using System;`nglobal using System.IO;`nglobal using System.Linq;`n" + [IO.File]::ReadAllText((Resolve-Path -LiteralPath $SourceFile).Path)
$type = Add-Type -TypeDefinition $source -PassThru | Where-Object FullName -eq 'ClassNex.Services.StartupRegistry'
$repair = $type.GetMethod('RepairExisting', [Reflection.BindingFlags]'Static,NonPublic')
if (!$repair) { throw 'The assembly does not include the portable registration migration.' }
$launcherFile = (Resolve-Path -LiteralPath $Launcher).Path
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($launcherFile).ProductName -ine 'ClassNex') { throw 'Supply a published ClassNEX root launcher.' }
$id = [guid]::NewGuid().ToString('N')
$fixture = (New-Item -ItemType Directory -Path (Join-Path $repo "build/portable-registration-$id")).FullName
$package = (New-Item -ItemType Directory -Path (Join-Path $fixture '课堂 安装')).FullName
$current = (New-Item -ItemType Directory -Path (Join-Path $package 'app-26w47a-0')).FullName
$exe = Join-Path $current 'ClassNex.exe'
$rootExe = Join-Path $package 'ClassNex.exe'
$marker = Join-Path $package 'PackageType'
[IO.File]::Copy($launcherFile,$rootExe)
[IO.File]::WriteAllText($marker,'folder')
[IO.File]::WriteAllText((Join-Path $package 'files.json'),'{}')
$former = Join-Path $package 'app-26w46d-0/ClassNex.exe'
$foreign = Join-Path $fixture '另一安装/app-26w46d-0/ClassNex.exe'
$run = 'Software\Microsoft\Windows\CurrentVersion\Run'
$proto = 'Software\Classes\classnex'
$cmd = $proto + '\shell\open\command'
$approval = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$scopeName = "Software\ClassNex-Registration-Review-$id"
if ($scopeName -notmatch '^Software\\ClassNex-Registration-Review-[0-9a-f]{32}$' -or [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($scopeName)) { throw 'Unsafe temporary registry scope.' }
$scope = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($scopeName)
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition,[string]$name) { if (!$condition) { throw $name }; $checks.Add($name) }
function Read-Sample([string]$path,[string]$name) {
    $key = $scope.OpenSubKey($path)
    try { if ($key) { return ,$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } } finally { if ($key) { $key.Dispose() } }
}
function Write-Sample([string]$path,[string]$name,$value,[Microsoft.Win32.RegistryValueKind]$kind = 'String') {
    $key = $scope.CreateSubKey($path)
    try { $key.SetValue($name,$value,$kind) } finally { $key.Dispose() }
}
function Migrate([bool]$startup = $true,[bool]$protocol = $true) {
    [void]$repair.Invoke($null, [object[]]@($scope,[string]$exe,[bool]$startup,[bool]$protocol))
}
function CommandFor([string]$path,[bool]$protocol = $false) { if($protocol){ return ('"{0}" "%1"' -f $path) }; return ('"{0}"' -f $path) }
try {
    Migrate
    Check ($scope.GetSubKeyNames().Count -eq 0) 'Absent entries and subkeys remain absent'
    Write-Sample $run 'ClassNEX' (CommandFor $former)
    Write-Sample $proto '' 'URL:ClassNEX Protocol'; Write-Sample $proto 'URL Protocol' ''
    Write-Sample $cmd '' (CommandFor $former $true)
    Migrate $false $false
    Check (((Read-Sample $run 'ClassNEX') -ceq (CommandFor $former)) -and ((Read-Sample $cmd '') -ceq (CommandFor $former $true))) 'Disabled app preferences do not modify existing values'
    Migrate $true $false
    Check (((Read-Sample $run 'ClassNEX') -ceq (CommandFor $rootExe)) -and ((Read-Sample $cmd '') -ceq (CommandFor $former $true))) 'Only enabled startup moves to the launcher when the old payload no longer exists'
    Migrate $false $true
    Check ((Read-Sample $cmd '') -ceq (CommandFor $rootExe $true)) 'Protocol keeps its single quoted URL argument'
    Migrate
    Check (((Read-Sample $run 'ClassNEX') -ceq (CommandFor $rootExe)) -and ((Read-Sample $cmd '') -ceq (CommandFor $rootExe $true))) 'Previously repaired registrations remain unchanged'
    Write-Sample $run 'ClassNEX' (CommandFor $exe); Migrate
    Check ((Read-Sample $run 'ClassNEX') -ceq (CommandFor $rootExe)) 'Current payload registration is also stable after the next update'
    foreach ($candidate in @((CommandFor $foreign), ((CommandFor $former)+' --custom'), (CommandFor (Join-Path $package 'app-demo/Other.exe')), (CommandFor (Join-Path $package 'app-26w46d-0/sub/ClassNex.exe')))) {
        Write-Sample $run 'ClassNEX' $candidate; Migrate
        Check ((Read-Sample $run 'ClassNEX') -ceq $candidate) 'Foreign install, customised arguments, or unexpected executable/layout is preserved'
    }
    foreach ($candidate in @((CommandFor $foreign $true), ((CommandFor $former $true)+' /custom'), (CommandFor $former))) {
        Write-Sample $cmd '' $candidate; Migrate
        Check ((Read-Sample $cmd '') -ceq $candidate) 'Unrecognised protocol registration is preserved'
    }
    Write-Sample $cmd '' (CommandFor $former $true); Write-Sample $proto '' 'Other product'; Migrate
    Check ((Read-Sample $cmd '') -ceq (CommandFor $former $true)) 'Another protocol owner is preserved'
    Write-Sample $run 'ClassNEX' (CommandFor $former); Write-Sample $approval 'ClassNEX' ([byte[]](3,0,0,0,0,0,0,0,0,0,0,0)) 'Binary'; Migrate
    Check ((Read-Sample $run 'ClassNEX') -ceq (CommandFor $former)) 'Windows-disabled startup is preserved'
    Write-Sample $approval 'ClassNEX' ([byte[]](2,0,0,0,0,0,0,0,0,0,0,0)) 'Binary'
    Write-Sample $run 'ClassNEX' (CommandFor $former) 'ExpandString'; Migrate
    Check ((Read-Sample $run 'ClassNEX') -ceq (CommandFor $former)) 'A custom expandable registry value is not rewritten'
    Write-Sample $run 'ClassNEX' (CommandFor $former)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($former)); [IO.File]::WriteAllBytes($former, [byte[]](1,2,3)); Migrate
    Check ((Read-Sample $run 'ClassNEX') -ceq (CommandFor $former)) 'An existing different executable under a matching folder is not adopted'
    [IO.File]::Copy($launcherFile,$former,$true); Migrate
    Check ((Read-Sample $run 'ClassNEX') -ceq (CommandFor $rootExe)) 'A still-present genuine old ClassNEX image is repaired'
    [IO.File]::WriteAllText($marker,'other'); Write-Sample $run 'ClassNEX' (CommandFor $former); Migrate
    Check ((Read-Sample $run 'ClassNEX') -ceq (CommandFor $former)) 'Loose/debug folders without a valid package marker do not migrate'

    $csc = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $sinkSource = Join-Path $fixture 'Capture.cs'
    [IO.File]::WriteAllText($sinkSource, 'using System; using System.IO; using System.Text; class Capture { static int Main(string[] args) { if (args.Length == 0 || args[0] != "--offline-argument-test") return 2; File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "received.txt"), Array.ConvertAll(args, a => Convert.ToBase64String(Encoding.UTF8.GetBytes(a)))); return 0; } }')
    & $csc /nologo /target:winexe "/out:$exe" $sinkSource
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the local, offline argument receiver.' }
    $argsSent = @('--offline-argument-test','classnex://settings','with spaces','参数含"引号"','%1')
    $start = [Diagnostics.ProcessStartInfo]::new($rootExe); $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    foreach ($entry in $argsSent) { $start.ArgumentList.Add($entry) }
    $process = [Diagnostics.Process]::Start($start)
    try { if (!$process.WaitForExit(8000) -or $process.ExitCode -ne 0) { throw 'The root launcher did not exit successfully.' } } finally { $process.Dispose() }
    $output = Join-Path $current 'received.txt'; $timer = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath $output) -and $timer.ElapsedMilliseconds -lt 8000) { Start-Sleep -Milliseconds 60 }
    $received = @([IO.File]::ReadAllLines($output) | ForEach-Object { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)) })
    Check (($received.Count -eq $argsSent.Count) -and ([string]::Join([char]0,$received) -ceq [string]::Join([char]0,$argsSent))) 'Real launcher forwards URL, spaces, Unicode, quotes and percent sign to an offline local executable'
    $result = [ordered]@{ passed=$true; checks=$checks; fixture=$fixture; productionRegistrationsTouched=$false; networkServiceContacted=$false } | ConvertTo-Json -Depth 3
    [IO.File]::WriteAllText((Join-Path $fixture 'result.json'),$result); $result
} finally {
    $scope.Dispose()
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($scopeName,$false)
}
