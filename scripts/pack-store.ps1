<#
.SYNOPSIS
  Build the Microsoft Store upload: one unsigned .msixbundle with the x64 and ARM64 packages.

.DESCRIPTION
  Runs pack.ps1 (unsigned - the Store signs the package itself) for x64 and ARM64, then bundles
  both with makeappx into dist\store\DesktopTiler_<version>_Bundle.msixbundle. Upload that file
  in Partner Center -> Packages. The package identity (Name/Publisher) must already be the one
  Partner Center assigned, or the upload is rejected.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root  = Resolve-Path (Join-Path $PSScriptRoot '..')
$dist  = Join-Path $root 'dist'
$store = Join-Path $dist 'store'

[xml]$manifest = Get-Content (Join-Path $root 'src\DesktopTiler\Package.appxmanifest')
$version = $manifest.Package.Identity.Version
if ($manifest.Package.Identity.Name -ne 'ABAPer.DesktopTiler') {
  Write-Warning 'Package identity is not the one Partner Center reserved; the upload will be rejected.'
}

$packages = foreach ($platform in 'x64', 'ARM64') {
  & (Join-Path $PSScriptRoot 'pack.ps1') -Platform $platform | Out-Host
  $msix = Get-ChildItem $dist -Recurse -Filter "*DesktopTiler_${version}_$($platform.ToLower()).msix" |
    Where-Object { $_.FullName -notlike "$store*" } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $msix) { throw "No $platform MSIX for version $version under $dist" }
  $msix
}

$makeappx = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter makeappx.exe |
  Where-Object { $_.DirectoryName -like '*\x64' } | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe not found; run a build first so the SDK.BuildTools package is restored.' }

New-Item -ItemType Directory -Force $store | Out-Null
$mapping = Join-Path $store 'bundle_mapping.txt'
$lines = @('[Files]') + ($packages | ForEach-Object { "`"$($_.FullName)`" `"$($_.Name)`"" })
Set-Content -Path $mapping -Value $lines -Encoding utf8

$bundle = Join-Path $store "DesktopTiler_${version}_Bundle.msixbundle"
& $makeappx.FullName bundle /o /f $mapping /p $bundle /bv $version
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed ($LASTEXITCODE)" }

Write-Host "Store bundle: $bundle"
