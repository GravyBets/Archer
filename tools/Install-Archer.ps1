#Requires -Version 5.1
<#
Install or upgrade Archer from its manually approved GitHub Actions bundle.
No network downloads, automatic deployment, process termination or API calls.
Archer's persistent Data folder lives OUTSIDE the replaceable app directory.
#>
[CmdletBinding(DefaultParameterSetName='Install')]
param(
  [Parameter(ParameterSetName='Install',Mandatory=$true)][string]$PackagePath,
  [Parameter(ParameterSetName='Install',Mandatory=$true)][string]$HashPath,
  [Parameter(ParameterSetName='Rollback',Mandatory=$true)][switch]$Rollback,
  [string]$InstallRoot=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (-not $InstallRoot) {
  if (-not $env:LOCALAPPDATA) { throw 'LOCALAPPDATA missing; Windows user installation only.' }
  $InstallRoot=Join-Path $env:LOCALAPPDATA 'Archer'
}
$root=[IO.Path]::GetFullPath($InstallRoot)
$current=Join-Path $root 'current'
$previous=Join-Path $root 'previous'
$data=Join-Path $root 'Data'
$stage=Join-Path $root ('staging-'+[guid]::NewGuid().ToString('N'))

function Assert-NotRunning {
  $client=New-Object Net.Sockets.TcpClient
  try {
    $attempt=$client.BeginConnect('127.0.0.1',5087,$null,$null)
    if ($attempt.AsyncWaitHandle.WaitOne(300) -and $client.Connected) {
      throw 'Archer is already running on localhost:5087. Close it before updating. No processes were stopped.'
    }
  } finally { $client.Close() }
}
function Assert-App([string]$at) {
  foreach($file in @('Archer.MissionControl.exe','wwwroot/index.html','Run-Archer.cmd','Start-Archer.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $at $file) -PathType Leaf)) {
      throw ('Incomplete Archer package: '+$file)
    }
  }
}
function Assert-Zip([string]$archive,[string]$destination) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $zip=[IO.Compression.ZipFile]::OpenRead($archive)
  try {
    $prefix=[IO.Path]::GetFullPath($destination).TrimEnd(
      [IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar
    )+[IO.Path]::DirectorySeparatorChar
    foreach($entry in $zip.Entries) {
      if (-not $entry.FullName) { throw 'Empty ZIP path.' }
      if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
        throw 'ZIP symlink refused.'
      }
      $target=[IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
      if (-not $target.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Archive path escape refused.'
      }
    }
  } finally { $zip.Dispose() }
}
function Shortcut {
  if ($env:ARCHER_INSTALL_TEST_MODE -eq '1') { return }
  if ($env:OS -ne 'Windows_NT') { return }
  try {
    $desktop=[Environment]::GetFolderPath('Desktop')
    if (-not $desktop) { return }
    $shell=New-Object -ComObject WScript.Shell
    $shortcut=$shell.CreateShortcut((Join-Path $desktop 'Archer.lnk'))
    $shortcut.TargetPath=Join-Path $current 'Run-Archer.cmd'
    $shortcut.WorkingDirectory=$current
    $shortcut.Description='Open local Archer'
    $shortcut.Save()
  } catch { Write-Warning 'Shortcut not created; use current\\Run-Archer.cmd.' }
}

Assert-NotRunning
New-Item -ItemType Directory -Path $root,$data -Force | Out-Null
if ($Rollback) {
  if (-not (Test-Path -LiteralPath $previous -PathType Container)) {
    throw 'No previous Archer version to restore.'
  }
  Assert-App $previous
  $temp=Join-Path $root ('rollback-'+[guid]::NewGuid().ToString('N'))
  try {
    if (Test-Path -LiteralPath $current) {
      Move-Item -LiteralPath $current -Destination $temp
    }
    Move-Item -LiteralPath $previous -Destination $current
    if (Test-Path -LiteralPath $temp) {
      Move-Item -LiteralPath $temp -Destination $previous
    }
    Shortcut
    Write-Host ('Previous Archer binary restored; data untouched: '+$data)
    return
  } catch {
    if ((Test-Path $temp) -and (-not (Test-Path $current))) {
      Move-Item -LiteralPath $temp -Destination $current
    }
    throw
  }
}
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $HashPath -PathType Leaf)) {
  throw 'Owner-built archer-app.zip and archer-app.sha256 are both required.'
}
$expected=(Get-Content -LiteralPath $HashPath -Raw).Trim().ToUpperInvariant()
if ($expected -notmatch '^[A-F0-9]{64}$') { throw 'Invalid SHA256 manifest.' }
$actual=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actual -ne $expected) { throw 'SHA256 mismatch; installation unchanged.' }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$moved=$false
try {
  Assert-Zip $PackagePath $stage
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [IO.Compression.ZipFile]::ExtractToDirectory(
    [IO.Path]::GetFullPath($PackagePath),[IO.Path]::GetFullPath($stage))
  Assert-App $stage
  if (Test-Path -LiteralPath $previous) {
    Remove-Item -LiteralPath $previous -Recurse -Force
  }
  if (Test-Path -LiteralPath $current) {
    Move-Item -LiteralPath $current -Destination $previous
    $moved=$true
  }
  try { Move-Item -LiteralPath $stage -Destination $current }
  catch {
    if ($moved -and (-not (Test-Path -LiteralPath $current))) {
      Move-Item -LiteralPath $previous -Destination $current
    }
    throw
  }
  Shortcut
  Write-Host ('Archer installed at '+$current)
  Write-Host ('Mission data preserved at '+$data)
  Write-Host 'Open Archer.lnk on your desktop. No provider request was made.'
} finally {
  if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
  }
}
