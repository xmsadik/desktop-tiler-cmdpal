<#
.SYNOPSIS
  Build the extension and register it with Windows as an unsigned dev package.

.DESCRIPTION
  Requires Developer Mode. Re-running replaces the previous registration.
  After deploying, run "Reload" in Command Palette to pick up the new build.

.PARAMETER Remove
  Unregister the dev package and exit.
#>
[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
  [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\DesktopTiler\DesktopTiler.csproj'

# 'DesktopTiler' is the pre-Store identity (v0.1-v0.2); it registers the same COM class, so it
# must not stay installed next to the current one.
$existing = @(Get-AppxPackage -Name 'ABAPer.DesktopTiler') + @(Get-AppxPackage -Name 'DesktopTiler')
if ($existing) {
  # The host keeps the COM server alive; stop it so the files can be replaced.
  Get-Process -Name 'DesktopTiler' -ErrorAction SilentlyContinue | Stop-Process -Force
  foreach ($package in $existing) {
    Remove-AppxPackage -Package $package.PackageFullName
    Write-Host "Removed $($package.PackageFullName)"
  }
}
if ($Remove) { return }

dotnet build $project -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

$manifest = Join-Path $PSScriptRoot "..\src\DesktopTiler\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml"
Add-AppxPackage -Register (Resolve-Path $manifest)
Get-AppxPackage -Name 'ABAPer.DesktopTiler' | Select-Object Name, Version, InstallLocation
Write-Host 'Deployed. In Command Palette, run "Reload" to load the new build.'
