#requires -Version 7.5
param(
    [string]$SourceCheckout = 'E:/ci-source',
    [string]$Launcher = 'E:/ci-app/ClassIsland_app_windows_x64_full_folder/ClassIsland.exe'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runtime = Join-Path $repo 'tests/ClassNex.Core.Tests/bin/Debug/net10.0'
$example = (Resolve-Path -LiteralPath (Join-Path $SourceCheckout 'examples/example.json')).Path
$launcherFile = Get-Item -LiteralPath $Launcher
if ($launcherFile.VersionInfo.ProductName -ne 'ClassIsland' -or $launcherFile.VersionInfo.ProductMajorPart -ne 2) { throw 'Expected the original ClassIsland 2.x launcher' }
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $runtime 'YamlDotNet.dll'))
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $runtime 'ClassNex.Core.dll'))
$fixture = [System.IO.Directory]::CreateTempSubdirectory('ClassNex-public-profile-')
$install = $fixture.CreateSubdirectory('ClassIsland')
$profiles = $install.CreateSubdirectory('data/Profiles')
$destination = $fixture.CreateSubdirectory('ClassNexData')
$exe = Join-Path $install.FullName 'ClassIsland.exe'
Copy-Item -LiteralPath $launcherFile.FullName -Destination $exe
Copy-Item -LiteralPath $example -Destination (Join-Path $profiles.FullName 'Default.json')
[IO.File]::WriteAllText((Join-Path $install.FullName 'PackageType'), 'folder')
[IO.File]::WriteAllText((Join-Path $install.FullName 'data/Settings.json'), '{}')
$token = [Threading.CancellationToken]::None
$plan = [ClassNex.Services.ClassIslandImporter]::Inspect($exe,$token)
$result = [ClassNex.Services.ClassIslandImporter]::Apply($plan,[ClassNex.Models.AppSettings]::new(),$destination.FullName,$token)
$profile = [ClassNex.Services.CsesCodec]::Load($result.ActiveProfilePath)
$classes = $profile.GetCourses(1, 'odd')
if ($plan.ProfileCount -ne 1 -or $plan.WidgetCount -ne 2 -or $classes.Count -ne 2 -or $classes[0].StartTime -ne '08:00:00' -or
    $classes[1].EndTime -ne '09:40:00' -or $profile.ImportedTimeLayout.Times.Count -ne 4) { throw "Unexpected public sample conversion: $($fixture.FullName)" }
if (-not [IO.File]::Exists($exe) -or (Get-FileHash -LiteralPath $example).Hash -ne (Get-FileHash -LiteralPath (Join-Path $profiles.FullName 'Default.json')).Hash) { throw 'Input was modified' }
[pscustomobject]@{ LauncherVersion=$launcherFile.VersionInfo.ProductVersion; Profiles=$plan.ProfileCount; Classes=$classes.Count; TimePoints=$profile.ImportedTimeLayout.Times.Count; Layout=($result.Settings.Widgets.Type -join ', '); TestDirectory=$fixture.FullName }
