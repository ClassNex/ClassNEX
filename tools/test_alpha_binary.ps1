param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tag = git -C $repo rev-parse 'refs/tags/26w46d_Alpha^{commit}'
if ($LASTEXITCODE -ne 0 -or $tag -ne '60d7b575b1b866a0bc388c9b80986c546950a2c7') { throw 'Expected the original Alpha tag' }
$dir = Join-Path $repo ('build/alpha-compat-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $dir 'app'; $data = Join-Path $dir 'data'; $plugin = Join-Path $app 'plugins/compat'; $archive = Join-Path $dir 'sdk.zip'
New-Item -ItemType Directory -Path $data,$plugin | Out-Null
git -C $repo archive --format=zip --output=$archive $tag src/ClassNex.Core
if ($LASTEXITCODE -ne 0) { throw 'SDK archive failed' }
Expand-Archive -LiteralPath $archive -DestinationPath $dir
$oldCore = Join-Path $dir 'src/ClassNex.Core/ClassNex.Core.csproj'
Copy-Item -LiteralPath (Join-Path $repo 'tests/Fixtures/AlphaBinary') -Destination (Join-Path $dir 'probe') -Recurse
dotnet build (Join-Path $dir 'probe/AlphaBinary.csproj') "-p:AlphaCoreProject=$oldCore" -c Release -v:q
if ($LASTEXITCODE -ne 0) { throw 'Could not build against the original SDK' }
Copy-Item -LiteralPath (Join-Path $dir 'probe/bin/Release/net10.0/AlphaBinary.dll') -Destination $plugin
dotnet publish (Join-Path $repo 'src/ClassNex/ClassNex.csproj') -c Debug --no-build --no-restore --no-self-contained -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build the new Debug client first' }
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $dir '.classnex-review'),'legacy binary fixture',$encoding)
[IO.File]::WriteAllText((Join-Path $data 'Settings.json'),'{"IsOobeCompleted":true,"IsNexVoiceWakeEnabled":false,"CentralManagement":{"IsEnabled":false}}',$encoding)
[IO.File]::WriteAllText((Join-Path $plugin 'plugin.json'),'{"id":"compat","name":"Alpha SDK fixture","version":"1.0","assembly":"AlphaBinary.dll","entryType":"AlphaBinary.Legacy","enabled":true}',$encoding)
$child = Start-Process -FilePath (Join-Path $app 'ClassNex.exe') -ArgumentList '--review-alpha-plugin' -WindowStyle Hidden -PassThru
try {
    if (!$child.WaitForExit(30000)) { $child.Kill(); throw 'Legacy plugin test timed out' }
    if ($child.ExitCode -ne 0) { throw "Legacy plugin failed: $data" }
    $calls = [IO.File]::ReadAllLines((Join-Path $app 'alpha-lifecycle.txt'))
    if (($calls -join ',') -ne 'initialize,shutdown') { throw 'Expected exactly one initialization and shutdown' }
    "PASS: unchanged plugin built against $tag loaded and stopped once in the new desktop host"
} finally { $child.Dispose() }
