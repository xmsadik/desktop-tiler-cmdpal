<#
.SYNOPSIS
  Install a signed DesktopTiler MSIX and restart Command Palette so the extension loads properly.

.DESCRIPTION
  Installing the package is not enough on its own: Command Palette only picks up the new build
  after a restart, and the first start of freshly installed binaries can be slow (antivirus /
  EDR scanning them). Command Palette gives an extension about 10 seconds; one that comes up
  later loads without its Dock band and hotkeys wired. So after installing, this script:

    1. runs DesktopTiler.exe once on its own, so the new files get scanned now (started outside
       Command Palette it exits right away, but the scan happens all the same);
    2. restarts Command Palette, which starts the extension again (the extension quits together
       with Command Palette, so it starts fresh);
    3. reads Command Palette's own log to see whether it loaded the extension ("Loaded N
       command(s) and 1 band(s) from ABAPer.DesktopTiler_...") or gave up waiting ("Starting
       extension ... timed out"), and restarts Command Palette once more in the latter case. If
       the log can't be found, it falls back to timing when the extension process started.

  The signing certificate must already be trusted (README, "Installation", step 3).

.PARAMETER Path
  The .msix to install. Default: the newest *_x64.msix under dist\.

.PARAMETER NoRestart
  Only install; leave Command Palette alone.

.PARAMETER OnTimeSeconds
  Fallback check only (when Command Palette's log can't be read): how soon after Command Palette
  starts the extension process must start to count as on time.

.EXAMPLE
  scripts\install.ps1
  scripts\install.ps1 -Path dist\DesktopTiler_0.3.36.0_x64_Test\DesktopTiler_0.3.36.0_x64.msix
#>
[CmdletBinding()]
param(
  [string]$Path,
  [switch]$NoRestart,
  [int]$OnTimeSeconds = 6
)

$ErrorActionPreference = 'Stop'
$packageName   = 'ABAPer.DesktopTiler'
$extensionName = 'DesktopTiler'
$cmdPalName    = 'Microsoft.CmdPal.UI'
$cmdPalApp     = 'shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App'
$startTimeout  = [TimeSpan]::FromSeconds(90)
$cmdPalLogs    = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\CmdPal\Logs'

# Command Palette keeps its log open for writing, so the size a directory listing reports lags
# behind (NTFS updates it lazily for open files). The length of an opened stream is current.
# FileShare.ReadWrite because the writer still has it open.
function Open-Log([string]$Path) { [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite') }

# Command Palette's log files (Logs\<version>\Log_<date>.log) and their current sizes, so only what
# gets written after this point is read later.
function Get-LogMarks {
  $marks = @{}
  foreach ($file in Get-ChildItem $cmdPalLogs -Recurse -Filter 'Log_*.log' -ErrorAction SilentlyContinue) {
    $stream = Open-Log $file.FullName
    try { $marks[$file.FullName] = $stream.Length } finally { $stream.Dispose() }
  }
  return $marks
}

# Text appended to Command Palette's logs since $Marks (files that are new count from the start).
function Read-LogSince($Marks) {
  $text = New-Object Text.StringBuilder
  foreach ($file in Get-ChildItem $cmdPalLogs -Recurse -Filter 'Log_*.log' -ErrorAction SilentlyContinue) {
    $from = if ($Marks.ContainsKey($file.FullName)) { $Marks[$file.FullName] } else { 0 }
    $stream = Open-Log $file.FullName
    try {
      if ($stream.Length -le $from) { continue }
      [void]$stream.Seek($from, 'Begin')
      $reader = New-Object IO.StreamReader($stream)
      [void]$text.Append($reader.ReadToEnd())
    }
    finally { $stream.Dispose() }
  }
  return $text.ToString()
}

# Waits for Command Palette to report on the extension in its log: 'Loaded' (with the command and
# band counts), 'TimedOut', or $null if neither shows up in time.
function Wait-LoadResult($Marks, [string]$PackageFullName) {
  $name = [regex]::Escape($PackageFullName)
  $script:loadResult = $null
  [void](Wait-Until {
    $log = Read-LogSince $Marks
    if ($log -match "Starting extension $name timed out") { $script:loadResult = 'TimedOut'; return $true }
    $m = [regex]::Match($log, "Loaded (\d+) command\(s\) and (\d+) band\(s\) from $name")
    if ($m.Success) { $script:loadResult = "Loaded $($m.Groups[1].Value) commands, $($m.Groups[2].Value) Dock band(s)"; return $true }
    return $false
  } $startTimeout)
  $result = $script:loadResult
  $script:loadResult = $null
  return $result
}

function Wait-Until([scriptblock]$Condition, [TimeSpan]$Timeout) {
  $watch = [Diagnostics.Stopwatch]::StartNew()
  while ($watch.Elapsed -lt $Timeout) {
    if (& $Condition) { return $true }
    Start-Sleep -Milliseconds 200
  }
  return $false
}

# Stops Command Palette (and the extension, should it outlive it), starts Command Palette again
# and returns how many seconds after it the extension started, or $null if it didn't within
# $startTimeout.
function Restart-CommandPalette {
  Get-Process -Name $cmdPalName -ErrorAction SilentlyContinue | Stop-Process -Force
  [void](Wait-Until { -not (Get-Process -Name $cmdPalName -ErrorAction SilentlyContinue) } ([TimeSpan]::FromSeconds(10)))

  # The extension exits with its host (HostProcessWatch); don't let a straggler be mistaken for
  # the new instance.
  if (-not (Wait-Until { -not (Get-Process -Name $extensionName -ErrorAction SilentlyContinue) } ([TimeSpan]::FromSeconds(5)))) {
    Get-Process -Name $extensionName -ErrorAction SilentlyContinue | Stop-Process -Force
  }

  Start-Process $cmdPalApp
  if (-not (Wait-Until { Get-Process -Name $cmdPalName -ErrorAction SilentlyContinue } ([TimeSpan]::FromSeconds(30)))) {
    throw 'Command Palette did not start. Is PowerToys Command Palette installed?'
  }
  $hostStart = (Get-Process -Name $cmdPalName | Sort-Object StartTime | Select-Object -First 1).StartTime

  if (-not (Wait-Until { Get-Process -Name $extensionName -ErrorAction SilentlyContinue } $startTimeout)) {
    return $null
  }
  $extensionStart = (Get-Process -Name $extensionName | Sort-Object StartTime | Select-Object -First 1).StartTime
  return [Math]::Max(0, ($extensionStart - $hostStart).TotalSeconds)
}

# --- Install -----------------------------------------------------------------------------------
if (-not $Path) {
  $dist = Join-Path $PSScriptRoot '..\dist'
  $newest = Get-ChildItem $dist -Recurse -Filter '*_x64.msix' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $newest) { throw "No .msix under $dist. Build one with: scripts\pack.ps1 -Sign" }
  $Path = $newest.FullName
}
if (-not (Test-Path $Path)) { throw "Package not found: $Path" }
$Path = (Resolve-Path $Path).Path

Write-Host "Installing $Path"
try {
  Add-AppxPackage -Path $Path -ForceApplicationShutdown
}
catch {
  $text = "$($_.Exception.Message)"
  if ($text -match '0x80073CFB') {
    throw "This version is already installed. Raise Version in src\DesktopTiler\Package.appxmanifest (scripts\pack.ps1 -Sign -Install does it for you), or remove the installed one first: Get-AppxPackage $packageName | Remove-AppxPackage"
  }
  if ($text -match '0x800B0109|0x800B010A') {
    throw 'The signing certificate is not trusted on this machine. Import dist\DesktopTiler.cer into LocalMachine\TrustedPeople once, as Administrator (README, "Installation", step 3).'
  }
  throw
}

$package = Get-AppxPackage -Name $packageName
Write-Host "Installed $($package.Name) $($package.Version)"
if ($NoRestart) { return }

# --- Warm up -----------------------------------------------------------------------------------
$exe = Join-Path $package.InstallLocation "$extensionName.exe"
try {
  $watch = [Diagnostics.Stopwatch]::StartNew()
  $warm = Start-Process $exe -WindowStyle Hidden -PassThru
  if ($warm.WaitForExit([int]$startTimeout.TotalMilliseconds)) {
    Write-Host ("Warm-up run took {0:N1} s" -f $watch.Elapsed.TotalSeconds)
  }
  else {
    $warm | Stop-Process -Force
    Write-Warning 'Warm-up run did not finish; continuing.'
  }
}
catch {
  # Not fatal: the check below restarts Command Palette again if the extension comes up late.
  Write-Warning "Warm-up run failed ($($_.Exception.Message)); continuing."
}

# --- Restart and check -------------------------------------------------------------------------
$canReadLog = Test-Path $cmdPalLogs
if (-not $canReadLog) {
  Write-Warning "Command Palette's log folder ($cmdPalLogs) wasn't found; judging by start time instead."
}

for ($attempt = 1; $attempt -le 2; $attempt++) {
  Write-Host "Restarting Command Palette (attempt $attempt)..."
  $marks = if ($canReadLog) { Get-LogMarks } else { @{} }
  $delay = Restart-CommandPalette
  if ($null -eq $delay) {
    Write-Warning "The extension did not start within $($startTimeout.TotalSeconds) s."
    continue
  }

  if ($canReadLog) {
    $result = Wait-LoadResult $marks $package.PackageFullName
    if ($result -like 'Loaded*') {
      Write-Host "Done: Command Palette loaded the extension ($result)."
      return
    }
    if ($result -eq 'TimedOut') {
      Write-Warning 'Command Palette gave up waiting for the extension (it started too slowly), so its Dock band and hotkeys would not work.'
      continue
    }
    Write-Warning "Command Palette's log said nothing about the extension; judging by start time instead."
  }

  if ($delay -le $OnTimeSeconds) {
    Write-Host ("Done: the extension started {0:N1} s after Command Palette." -f $delay)
    return
  }
  Write-Warning ("The extension started {0:N1} s after Command Palette, likely too late to be wired." -f $delay)
}

throw 'The extension still did not load on time. Restart Command Palette once more by hand; if that does not help, check the extension log (README, "Development").'
