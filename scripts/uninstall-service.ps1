<#
.SYNOPSIS
    Stops and removes the Redis Windows service.

.DESCRIPTION
    Run this from an elevated (Administrator) PowerShell prompt.

.EXAMPLE
    .\uninstall-service.ps1 -ServiceName 'Redis'
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'Redis'
)

$ErrorActionPreference = 'Stop'

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Service '$ServiceName' is not installed; nothing to do." -ForegroundColor Yellow
    return
}

if ($service.Status -ne 'Stopped') {
    & sc.exe stop $ServiceName | Out-Null
    # Give the SCM a moment to stop the service before deleting it.
    for ($i = 0; $i -lt 20 -and (Get-Service -Name $ServiceName).Status -ne 'Stopped'; $i++) {
        Start-Sleep -Milliseconds 500
    }
}

& sc.exe delete $ServiceName
if (0 -ne $LASTEXITCODE) { throw "sc.exe delete exited with code $LASTEXITCODE" }

Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
