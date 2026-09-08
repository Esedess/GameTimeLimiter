param([string]$Configuration = 'Release', [string]$PublishDirectory = 'artifacts/publish/win-x64')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$taskDotnet = if (Test-Path '.dotnet/dotnet.exe') { Join-Path $PSScriptRoot '.dotnet/dotnet.exe' } else { 'dotnet' }
& $taskDotnet restore
if ($LASTEXITCODE) { throw 'Restore failed' }
& $taskDotnet build -c Debug --no-restore
if ($LASTEXITCODE) { throw 'Debug build failed' }
& $taskDotnet build -c Release --no-restore
if ($LASTEXITCODE) { throw 'Release build failed' }
& $taskDotnet publish tools/GameLimiter.TestGame/GameLimiter.TestGame.csproj -c Release --self-contained true -o artifacts/test-game
if ($LASTEXITCODE) { throw 'Test helper publish failed' }
$env:GAMELIMITER_TEST_GAME = Join-Path $PSScriptRoot 'artifacts/test-game/GameLimiter.TestGame.exe'
& $taskDotnet test -c Release --no-build --logger 'trx;LogFileName=release.trx' --results-directory artifacts/test-results
if ($LASTEXITCODE) { throw 'Tests failed' }
& $taskDotnet publish src/GameLimiter.App/GameLimiter.App.csproj -c $Configuration -r win-x64 --self-contained true -o $PublishDirectory
if ($LASTEXITCODE) { throw 'Publish failed' }
$taskSmoke = Start-Process -FilePath (Join-Path (Join-Path $PSScriptRoot $PublishDirectory) 'GameLimiter.exe') -ArgumentList '--smoke-test', ('"' + (Join-Path $PSScriptRoot 'artifacts/smoke') + '"') -WindowStyle Hidden -PassThru
if (-not $taskSmoke.WaitForExit(45000)) { throw 'Published application smoke check timed out' }
if ($taskSmoke.ExitCode) { throw 'Published application smoke check failed; see artifacts/smoke/failure.txt' }
