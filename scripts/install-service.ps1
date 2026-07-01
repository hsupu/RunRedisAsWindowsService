<#
.SYNOPSIS
    Installs the Redis Windows service wrapper.

.DESCRIPTION
    Registers RedisService.exe as a Windows service that runs redis-server.exe.
    Run this from an elevated (Administrator) PowerShell prompt.

.EXAMPLE
    .\install-service.ps1 -WorkDir 'C:\var\redis-server' -ConfigFilePath '.\redis.windows.conf'
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'Redis',

    # Path to RedisService.exe (the wrapper). Defaults to the copy next to this script.
    [string]$RedisService = (Join-Path $PSScriptRoot 'RedisService.exe'),

    # Path to redis-server.exe. Defaults to whatever is on PATH.
    [string]$RedisServer = (Get-Command 'redis-server.exe' -ErrorAction SilentlyContinue).Path,

    [string]$WorkDir = 'C:\var\redis-server',

    # Optional path to a redis config file. Empty means rely on redis defaults.
    [string]$ConfigFilePath = ''
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $RedisService)) {
    throw "RedisService.exe not found at '$RedisService'. Pass -RedisService with the correct path."
}
if ([string]::IsNullOrWhiteSpace($RedisServer) -or -not (Test-Path -LiteralPath $RedisServer)) {
    throw "redis-server.exe not found. Pass -RedisServer with the correct path or add it to PATH."
}

$RedisService = (Resolve-Path -LiteralPath $RedisService).Path
$RedisServer = (Resolve-Path -LiteralPath $RedisServer).Path

$binArgs = "-e `"$RedisServer`" -d `"$WorkDir`""
if (-not [string]::IsNullOrWhiteSpace($ConfigFilePath)) {
    $ConfigFilePath = (Resolve-Path -LiteralPath $ConfigFilePath).Path
    $binArgs += " -c `"$ConfigFilePath`""
}
$binPath = "`"$RedisService`" $binArgs"

# ServiceType own = run as its own process. LocalService has limited rights and works well.
& sc.exe create $ServiceName binPath= $binPath start= demand type= own obj= 'NT AUTHORITY\LocalService' password= ''
if (0 -ne $LASTEXITCODE) { throw "sc.exe create exited with code $LASTEXITCODE" }

& sc.exe qc $ServiceName
if (0 -ne $LASTEXITCODE) { throw "sc.exe qc exited with code $LASTEXITCODE" }

& sc.exe start $ServiceName
if (0 -ne $LASTEXITCODE) { throw "sc.exe start exited with code $LASTEXITCODE" }

Write-Host "Service '$ServiceName' installed and started." -ForegroundColor Green
