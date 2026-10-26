param([Parameter(Mandatory=$true)][string]$DotnetPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location $taskRoot
$taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($taskIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this explicitly authorized integration test elevated.' }
$taskName = 'GLTest' + [Guid]::NewGuid().ToString('N').Substring(0,10)
$taskPassword = 'Aa9!' + [Guid]::NewGuid().ToString('N')
$taskFolder = Join-Path $taskRoot ('artifacts\profile-' + $taskName)
$taskHelper = Join-Path $taskRoot 'artifacts\test-game'
$taskUser = $null
$taskAcl = $null
$taskResult = Join-Path $taskRoot 'artifacts\windows-profile-result.txt'
try {
    New-Item -ItemType Directory -Path $taskFolder | Out-Null
    $taskUser = New-LocalUser -Name $taskName -Password (ConvertTo-SecureString $taskPassword -AsPlainText -Force) -Description 'Temporary GameLimiter test'
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $taskUser
    $taskSid = $taskUser.SID
    $taskAcl = Get-Acl -LiteralPath $taskHelper
    $taskReadAcl = Get-Acl -LiteralPath $taskHelper
    $taskReadAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($taskSid, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    Set-Acl -LiteralPath $taskHelper -AclObject $taskReadAcl
    $taskWriteAcl = Get-Acl -LiteralPath $taskFolder
    $taskWriteAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($taskSid, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    Set-Acl -LiteralPath $taskFolder -AclObject $taskWriteAcl
    $env:GL_PROFILE_TEST_ACCOUNT = '.\' + $taskName
    $env:GL_PROFILE_TEST_PASSWORD = $taskPassword
    $env:GL_PROFILE_TEST_FOLDER = $taskFolder
    $env:GAMELIMITER_TEST_GAME = Join-Path $taskHelper 'GameLimiter.TestGame.exe'
    & $DotnetPath test -c Release --no-build --filter FullyQualifiedName~RealAlternateUserProfile --logger 'trx;LogFileName=windows-profile.trx' --results-directory artifacts/test-results *> artifacts/windows-profile-test.log
    if ($LASTEXITCODE) { throw 'Windows profile integration test failed; see artifacts/windows-profile-test.log' }
    'PASS: direct launch, launcher discovery, SID, USERPROFILE, AppData, DPAPI roundtrip, invalid password, process termination.' | Set-Content -LiteralPath $taskResult
} catch {
    ('FAIL: ' + $_.Exception.Message) | Set-Content -LiteralPath $taskResult
    throw
} finally {
    $env:GL_PROFILE_TEST_PASSWORD = $null
    $taskPassword = $null
    if ($taskAcl) { Set-Acl -LiteralPath $taskHelper -AclObject $taskAcl }
    if ($taskUser) {
        $taskProfiles = @(Get-CimInstance Win32_UserProfile | Where-Object SID -EQ $taskUser.SID.Value)
        foreach ($taskProfile in $taskProfiles) {
            $taskExpectedProfile = Join-Path (Split-Path $env:USERPROFILE -Parent) $taskName
            if ($taskProfile.LocalPath -ne $taskExpectedProfile -or $taskProfile.Loaded) { throw 'Test profile cleanup requires review: unexpected path or profile still loaded.' }
            $taskProfile | Remove-CimInstance
        }
        Remove-LocalUser -SID $taskUser.SID
        Add-Content -LiteralPath $taskResult 'Temporary account and its profile removed.'
    }
}
