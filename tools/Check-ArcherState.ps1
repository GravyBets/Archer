#Requires -Version 5.1
<#
Read-only owner preflight before moving Archer state to a standalone Windows
installation or a private Linux server. It never copies, uploads, modifies,
decrypts or prints conversation text, tokens, or other saved values.
Run from a reviewed Archer checkout:
  powershell -NoProfile -File tools/Check-ArcherState.ps1
  powershell -NoProfile -File tools/Check-ArcherState.ps1 -StateFile 'D:\\Archer\\archer-state.json'
#>
[CmdletBinding()]
param(
  [string]$RepositoryRoot='',
  [string]$InstalledRoot='',
  [string]$StateFile=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

function Get-Count([object]$record,[string]$propertyName) {
  if ($null -eq $record) { return 0 }
  $item=$record.PSObject.Properties[$propertyName]
  if ($null -eq $item -or $null -eq $item.Value) { return 0 }
  return @($item.Value).Count
}

function Check-LocalListener {
  $socket=New-Object Net.Sockets.TcpClient
  try {
    $attempt=$socket.BeginConnect('127.0.0.1',5087,$null,$null)
    return ($attempt.AsyncWaitHandle.WaitOne(200) -and $socket.Connected)
  } catch {
    return $false
  } finally {
    $socket.Close()
  }
}

if (-not $RepositoryRoot) { $RepositoryRoot=Split-Path -Parent $PSScriptRoot }
if (-not $InstalledRoot) {
  $InstalledRoot=if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Archer' } else { '' }
}
$repository=[IO.Path]::GetFullPath($RepositoryRoot)
$found=New-Object 'System.Collections.Generic.List[object]'
$candidates=@()
if ($StateFile) {
  if ([IO.Path]::GetFileName($StateFile) -ne 'archer-state.json') {
    throw 'Select the Archer state file named archer-state.json. No file was read.'
  }
  $candidates=@(@{label='Explicit';path=[IO.Path]::GetFullPath($StateFile)})
} else {
  if ($InstalledRoot) {
    $candidates+=@{label='Installed Archer';path=(Join-Path $InstalledRoot 'Data/archer-state.json')}
  }
  $candidates+=@{label='Visual Studio Archer';path=(Join-Path $repository 'Archer.MissionControl/Data/archer-state.json')}
  $candidates+=@{label='Repository root';path=(Join-Path $repository 'Data/archer-state.json')}
}
$seen=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($candidate in $candidates) {
  $path=[IO.Path]::GetFullPath([string]$candidate.path)
  if (-not $seen.Add($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
  $file=Get-Item -LiteralPath $path
  if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    Write-Warning ('Skipped linked state file at '+$candidate.label+'. No data was read.')
    continue
  }
  if ($file.Length -gt 104857600) {
    Write-Warning ($candidate.label+': state is over 100MB. Manual review required; not parsed.')
    continue
  }
  try {
    $raw=[IO.File]::ReadAllText($path,[Text.Encoding]::UTF8)
    $state=ConvertFrom-Json -InputObject $raw -ErrorAction Stop
    if ($null -eq $state -or $state -is [string] -or $state -is [array] -or
        $null -eq $state.PSObject.Properties['missions'] -or
        $null -eq $state.PSObject.Properties['conversation']) {
      throw 'Unexpected state schema'
    }
    $fingerprint=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $found.Add([pscustomobject]@{
      Label=$candidate.label; Path=$path; Fingerprint=$fingerprint
      Bytes=$file.Length; LastUpdated=$file.LastWriteTime
      Missions=(Get-Count $state 'missions')
      Conversations=(Get-Count $state 'conversation')
      Projects=(Get-Count $state 'projects')
      DevelopmentBatches=(Get-Count $state 'developmentBatches')
      IncompleteWrite=(Test-Path -LiteralPath ($path+'.tmp'))
    })
  } catch {
    Write-Warning ($candidate.label+': invalid or unreadable state. No content was printed.')
  }
}
Write-Host 'ARCHER DATA CHECK — read-only, no data was copied or uploaded.'
$running=Check-LocalListener
if ($running) {
  Write-Warning 'An application is listening on 127.0.0.1:5087. Stop Archer before making a migration backup.'
} else {
  Write-Host 'No Archer listener detected on port 5087 (also check for other Archer processes before a backup).'
}
if ($found.Count -eq 0) {
  Write-Warning 'No valid state file found in these locations. Existing history may live elsewhere.'
  Write-Host 'No changes were made. Find the actual Archer data folder before installing or migrating.'
  exit 1
}
foreach($entry in $found) {
  Write-Host ('Candidate: '+$entry.Label)
  Write-Host ('  Location: '+$entry.Path)
  Write-Host ('  Saved: '+$entry.LastUpdated.ToString('yyyy-MM-dd HH:mm'))
  Write-Host ('  Recorded counts — missions: '+$entry.Missions+', chat entries: '+$entry.Conversations+
    ', projects: '+$entry.Projects+', development batches: '+$entry.DevelopmentBatches)
  if ($entry.IncompleteWrite) {
    Write-Warning ('An interrupted temporary state file exists alongside '+$entry.Label+'. Review before migration.')
  }
}
$unique=@($found | Select-Object -ExpandProperty Fingerprint -Unique)
if ($found.Count -gt 1 -and $unique.Count -gt 1) {
  Write-Warning 'Multiple DIFFERENT Archer state files exist. Do not overwrite, automatically merge or pick one by file date.'
  Write-Host 'Identify which instance has the current work before creating a verified private backup.'
  exit 2
}
if ($found.Count -gt 1) { Write-Host 'Multiple matching copies found; verify the active location before copying any data.' }
Write-Host 'Next step: stop all Archer processes, preserve the selected state in an owner-controlled encrypted backup,'
Write-Host 'verify a separate restore, then migrate that single selected snapshot. This check did NOT back up data.'
