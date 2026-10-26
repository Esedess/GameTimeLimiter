param([string]$IsccPath, [switch]$SkipBuild, [string]$PublishDirectory = 'artifacts/publish/win-x64')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location $taskRoot
if (-not $SkipBuild) { & (Join-Path $taskRoot 'build.ps1') -PublishDirectory $PublishDirectory }
if (-not $IsccPath) {
    $candidates = @(
        (Join-Path $taskRoot '.tools/InnoSetup/ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $IsccPath) { $IsccPath = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) { throw 'Install Inno Setup 6/7 from https://jrsoftware.org/isdl.php or pass -IsccPath.' }
[xml]$project = Get-Content src/GameLimiter.App/GameLimiter.App.csproj
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(-beta\.\d+)?$') { throw 'Application Version must be major.minor.patch.' }
$publishDir = Join-Path $taskRoot $PublishDirectory
$releaseDir = Join-Path $taskRoot 'artifacts/release'
if (-not (Test-Path "$publishDir/GameLimiter.exe")) { throw 'Publish output is missing; run build.ps1 first.' }
$publishedVersion = (Get-Item "$publishDir/GameLimiter.exe").VersionInfo.ProductVersion.Split('+')[0]
if ($publishedVersion -ne $version) { throw "Published version $publishedVersion does not match source version $version. Rebuild." }
New-Item -ItemType Directory -Force $releaseDir | Out-Null
& $IsccPath "/DAppVersion=$version" "/DPublishDir=$publishDir" "/DReleaseDir=$releaseDir" 'installer/GameLimiter.iss'
if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
$portable = Join-Path $releaseDir "GameLimiter-$version-win-x64-Portable.zip"
Compress-Archive -Path "$publishDir/*" -DestinationPath $portable -Force
$setup = Join-Path $releaseDir "GameLimiter-$version-win-x64-Setup.exe"
@($setup, $portable) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_)
} | Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Release files: $releaseDir"
