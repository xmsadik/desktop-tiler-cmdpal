<#
.SYNOPSIS
  Build a Release MSIX of the extension, optionally signed with a self-signed certificate.

.DESCRIPTION
  Output goes to dist\. Without -Sign the package is unsigned and only useful for
  inspection. With -Sign, a self-signed code-signing certificate whose subject matches the
  manifest Publisher (the Partner Center publisher id) is created in CurrentUser\My on first
  use, its public part is exported to dist\DesktopTiler.cer, and the MSIX is signed. Microsoft
  Store builds are unsigned instead (see pack-store.ps1): the Store signs them itself.

  To install a self-signed package, the target machine must trust the .cer once
  (elevated):  Import-Certificate dist\DesktopTiler.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

.PARAMETER Platform
  x64 or ARM64.
#>
[CmdletBinding()]
param(
  [ValidateSet('x64', 'ARM64')] [string]$Platform = 'x64',
  [switch]$Sign
)

$ErrorActionPreference = 'Stop'
$root    = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\DesktopTiler\DesktopTiler.csproj'
$dist    = Join-Path $root 'dist'
[xml]$manifestXml = Get-Content (Join-Path $root 'src\DesktopTiler\Package.appxmanifest')
$subject = $manifestXml.Package.Identity.Publisher

# A trailing backslash immediately before the closing quote (-p:AppxPackageDir="$dist\") breaks
# once $dist contains a space: Windows argv parsing treats \" as an escaped literal quote, not
# "backslash then end-of-string", so the argument merges with the next one instead of terminating.
# A forward slash sidesteps that escaping rule entirely - MSBuild treats it the same as a backslash
# in a path on Windows - so it's used here instead of doubling the backslash.
dotnet publish $project -c Release -p:Platform=$Platform -p:PublishProfile=win-$($Platform.ToLower()) `
  -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxPackageDir="$dist/"
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }

$msix = Get-ChildItem $dist -Recurse -Filter "*_$Platform.msix" |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "No MSIX produced under $dist" }

if ($Sign) {
  $cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
  if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
      -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(3) `
      -TextExtension @('2.5.29.19={text}')
    Write-Host "Created signing certificate $($cert.Thumbprint)"
  }
  Export-Certificate -Cert $cert -FilePath (Join-Path $dist 'DesktopTiler.cer') | Out-Null

  $signtool = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter signtool.exe |
    Where-Object { $_.DirectoryName -like '*\x64' } | Select-Object -First 1
  if (-not $signtool) { throw 'signtool.exe not found; run a build first so the SDK.BuildTools package is restored.' }

  & $signtool.FullName sign /fd SHA256 /sha1 $cert.Thumbprint /s My /tr http://timestamp.digicert.com /td sha256 $msix.FullName
  if ($LASTEXITCODE -ne 0) { throw "Signing failed ($LASTEXITCODE)" }
}

Write-Host "Package: $($msix.FullName)"
