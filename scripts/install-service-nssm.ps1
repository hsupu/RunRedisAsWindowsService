<#
.SYNOPSIS
    Installs redis-server directly as a Windows service using NSSM, without the RedisService wrapper.

.DESCRIPTION
    NSSM (https://nssm.cc) supervises redis-server.exe itself. This script mirrors the wrapper's two
    responsibilities so behavior matches:
      1. Pre-translates the config path to cygwin form (/cygdrive/x/...), since redis-windows is a
         cygwin build and NSSM does not translate paths. If your redis-server accepts native Windows
         paths, pass -NoCygwinPath to skip the translation.
      2. Configures AppStopMethodConsole so NSSM sends Ctrl-C on stop, letting redis persist and exit
         cleanly (the same graceful behavior the wrapper implements).

    Run from an elevated (Administrator) PowerShell prompt.

.EXAMPLE
    .\install-service-nssm.ps1 -WorkDir 'C:\var\redis-server' -ConfigFilePath '.\redis.windows.conf'
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'Redis',

    # Path to nssm.exe. Defaults to whatever is on PATH.
    [string]$Nssm = (Get-Command 'nssm.exe' -ErrorAction SilentlyContinue).Path,

    # Path to redis-server.exe. Defaults to whatever is on PATH.
    [string]$RedisServer = (Get-Command 'redis-server.exe' -ErrorAction SilentlyContinue).Path,

    [string]$WorkDir = 'C:\var\redis-server',

    # Optional path to a redis config file. Empty means rely on redis defaults.
    [string]$ConfigFilePath = '',

    # Skip cygwin path translation (use when your redis-server accepts native Windows paths).
    [switch]$NoCygwinPath,

    # Extra arguments forwarded to redis-server (e.g. --port 6390 --loglevel verbose).
    [string[]]$RedisArgs = @(),

    # Seconds NSSM waits for redis to exit after Ctrl-C before escalating.
    [int]$StopTimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'

function ConvertTo-CygwinPath([string]$Path, [string]$BaseDir) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if ($Path.StartsWith('/cygdrive/')) { return $Path }
    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = [System.IO.Path]::Combine($BaseDir, $Path)
    }
    $Path = [System.IO.Path]::GetFullPath($Path)
    $colon = $Path.IndexOf(':')
    if ($colon -le 0) { return $Path.Replace('\', '/') }
    # cygwin's /cygdrive mount uses lowercase drive letters by convention.
    $drive = $Path.Substring(0, $colon).ToLowerInvariant()
    return ('/cygdrive/' + $drive + $Path.Substring($colon + 1)).Replace('\', '/')
}

if ([string]::IsNullOrWhiteSpace($Nssm) -or -not (Test-Path -LiteralPath $Nssm)) {
    throw "nssm.exe not found. Install NSSM (https://nssm.cc) or pass -Nssm with the correct path."
}
if ([string]::IsNullOrWhiteSpace($RedisServer) -or -not (Test-Path -LiteralPath $RedisServer)) {
    throw "redis-server.exe not found. Pass -RedisServer with the correct path or add it to PATH."
}

$RedisServer = (Resolve-Path -LiteralPath $RedisServer).Path
$WorkDir = [System.IO.Path]::GetFullPath($WorkDir)

# Build AppParameters: config file first (redis usage: redis-server [config] [options]).
$appParams = @()
if (-not [string]::IsNullOrWhiteSpace($ConfigFilePath)) {
    $configArg = if ($NoCygwinPath) {
        (Resolve-Path -LiteralPath $ConfigFilePath).Path
    } else {
        ConvertTo-CygwinPath -Path $ConfigFilePath -BaseDir $WorkDir
    }
    $appParams += $configArg
}
$appParams += $RedisArgs
$appParametersValue = ($appParams -join ' ')

& $Nssm install $ServiceName $RedisServer
if (0 -ne $LASTEXITCODE) { throw "nssm install exited with code $LASTEXITCODE" }

& $Nssm set $ServiceName AppDirectory $WorkDir
& $Nssm set $ServiceName AppParameters $appParametersValue
& $Nssm set $ServiceName Start SERVICE_DEMAND_START
& $Nssm set $ServiceName ObjectName 'NT AUTHORITY\LocalService' ''

# Graceful stop: send Ctrl-C first and give redis time to persist and exit.
& $Nssm set $ServiceName AppStopMethodConsole ($StopTimeoutSeconds * 1000)

& $Nssm start $ServiceName
if (0 -ne $LASTEXITCODE) { throw "nssm start exited with code $LASTEXITCODE" }

Write-Host "Service '$ServiceName' installed via NSSM and started." -ForegroundColor Green
Write-Host "AppParameters: $appParametersValue" -ForegroundColor DarkGray
