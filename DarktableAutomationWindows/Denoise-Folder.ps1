<#
.SYNOPSIS
  AI raw denoise for a whole folder, using darktable.

.DESCRIPTION
  For every raw photo in the folder darktable's "neural restore" module writes
  a denoised DNG (<name>_raw-denoise.dng). The original file is never touched.
  darktable must already be installed, with AI features enabled and a
  rawdenoise model activated (see README.md).

.EXAMPLE
  .\Denoise-Folder.ps1 D:\foto\2026-09-15

.EXAMPLE
  .\Denoise-Folder.ps1 -Strength 70 -Output D:\foto\denoised D:\foto\2026-09-15
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true, Position = 0)]
  [string] $Folder,

  # denoise strength, 100 = full model output
  [ValidateRange(0, 100)]
  [int] $Strength = 100,

  # write the DNGs here instead of next to the source files
  [string] $Output = '',

  # rawdenoise model to use, default is whichever is active in darktable
  [string] $Model = '',

  # include subfolders
  [switch] $Recurse,

  # give up when no new DNG appears for this many seconds
  [int] $Timeout = 900,

  # path to darktable.exe
  [string] $DarktablePath = $env:DARKTABLE_BIN,

  # override the darktable action path of the "process" button
  [string] $ActionPath = '',

  # keep the temporary work directory for debugging
  [switch] $KeepWorkDir
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$luaScript = Join-Path $scriptDir 'denoise_batch.lua'

$rawExtensions = @('.cr2', '.cr3', '.nef', '.nrw', '.arw', '.srf', '.sr2',
                   '.raf', '.orf', '.rw2', '.pef', '.dng', '.raw', '.rwl',
                   '.iiq', '.3fr', '.fff', '.erf', '.mos', '.mrw', '.dcr',
                   '.kdc', '.x3f')

function Write-Utf8Lines($path, [string[]] $lines) {
  # no BOM: the Lua script reads these files byte by byte
  [System.IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding($false)))
}

function Quote($value) { '"' + ($value -replace '"', '\"') + '"' }

# darktable removes its lock files on a clean exit; after a kill they stay
# behind and block the next start
function Remove-StaleLock($lockPath) {
  if (-not (Test-Path -LiteralPath $lockPath)) { return }
  $pidText = (Get-Content -LiteralPath $lockPath -Raw -ErrorAction SilentlyContinue) -replace '\D', ''
  if ($pidText -and (Get-Process -Id ([int]$pidText) -ErrorAction SilentlyContinue)) { return }
  Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------ inputs

if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
  throw "not a folder: $Folder"
}
if (-not (Test-Path -LiteralPath $luaScript)) {
  throw "missing $luaScript"
}
$Folder = (Resolve-Path -LiteralPath $Folder).Path
if ($Output) {
  if (-not (Test-Path -LiteralPath $Output)) {
    New-Item -ItemType Directory -Path $Output -Force | Out-Null
  }
  $Output = (Resolve-Path -LiteralPath $Output).Path
}

# ---------------------------------------------------------------- darktable

if (-not $DarktablePath) {
  $roots = @(
    @($env:ProgramFiles, 'darktable\bin\darktable.exe'),
    @(${env:ProgramFiles(x86)}, 'darktable\bin\darktable.exe'),
    @($env:LOCALAPPDATA, 'Programs\darktable\bin\darktable.exe')
  )
  $DarktablePath = $roots |
    Where-Object { $_[0] } |
    ForEach-Object { Join-Path $_[0] $_[1] } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
  if (-not $DarktablePath) {
    $onPath = Get-Command darktable.exe -ErrorAction SilentlyContinue
    if ($onPath) { $DarktablePath = $onPath.Source }
  }
}
if (-not $DarktablePath -or -not (Test-Path -LiteralPath $DarktablePath)) {
  throw 'darktable.exe not found - install darktable or pass -DarktablePath'
}

if (Get-Process -Name darktable -ErrorAction SilentlyContinue) {
  throw 'darktable is running - close it first, it locks the shared database'
}

$configDir = @(
  (Join-Path $env:LOCALAPPDATA 'darktable'),
  (Join-Path $env:APPDATA 'darktable')
) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'darktablerc') } | Select-Object -First 1

if (-not $configDir) { $configDir = Join-Path $env:LOCALAPPDATA 'darktable' }
$darktablerc = Join-Path $configDir 'darktablerc'

# ---------------------------------------------------------------- file list

$gci = @{ LiteralPath = $Folder; File = $true }
if ($Recurse) { $gci.Recurse = $true }

$files = Get-ChildItem @gci |
  Where-Object {
    $rawExtensions -contains $_.Extension.ToLowerInvariant() -and
    $_.BaseName -notlike '*_raw-denoise*'   # do not denoise our own output again
  } |
  Sort-Object FullName

if (-not $files) { throw "no raw photos found in $Folder" }

$duplicates = $files | Group-Object BaseName | Where-Object { $_.Count -gt 1 }
foreach ($group in $duplicates) {
  Write-Warning "$($group.Name) exists with several extensions, output names may shift"
}

# ------------------------------------------------------------ work directory

$workdir = Join-Path $env:TEMP ("dt-denoise-" + [System.Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $workdir -Force | Out-Null
$rcBackup = Join-Path $workdir 'darktablerc.backup'
if (Test-Path -LiteralPath $darktablerc) { Copy-Item -LiteralPath $darktablerc $rcBackup -Force }

Write-Utf8Lines (Join-Path $workdir 'files.txt') @($files.FullName)
Write-Utf8Lines (Join-Path $workdir 'job.conf') @(
  "input_dir=$Folder",
  "output_dir=$Output",
  "strength=$Strength",
  "stall_timeout=$Timeout",
  "action_path=$ActionPath"
)

$resultFile = Join-Path $workdir 'result.txt'
$luaLog     = Join-Path $workdir 'lua.log'
$stdoutLog  = Join-Path $workdir 'darktable.log'
$stderrLog  = Join-Path $workdir 'darktable.err.log'
$proc = $null

try {
  # ------------------------------------------------------------ run darktable

  $confOutput = if ($Output) { $Output } else { '$(FILE_FOLDER)' }
  $luaPath = $luaScript -replace '\\', '/'

  $dtArgs = @(
    '--library', (Quote (Join-Path $workdir 'library.db')),
    '--conf', (Quote 'plugins/ai/enabled=TRUE'),
    '--conf', (Quote 'plugins/lighttable/act_on=FALSE'),
    '--conf', (Quote 'plugins/lighttable/neural_restore/active_page=0'),
    '--conf', (Quote "plugins/lighttable/neural_restore/raw_strength=$Strength"),
    # the Lua script watches the throw-away library to notice finished DNGs
    '--conf', (Quote 'plugins/lighttable/neural_restore/add_to_catalog=TRUE'),
    '--conf', (Quote "plugins/lighttable/neural_restore/output_directory=$confOutput"),
    '--conf', (Quote 'write_sidecar_files=never'),
    '--luacmd', (Quote "dofile('$luaPath')")
  )
  if ($Model) {
    $dtArgs += @('--conf', (Quote "plugins/ai/models/active/rawdenoise=$Model"))
  }

  Write-Host "darktable: $DarktablePath"
  Write-Host "photos:    $($files.Count)"
  Write-Host "strength:  $Strength%"
  Write-Host ("output:    " + $(if ($Output) { $Output } else { 'next to the source files' }))
  Write-Host ''

  $env:DT_DENOISE_WORKDIR = $workdir
  # no -WindowStyle here: it belongs to a different parameter set than the
  # output redirection, so darktable opens its normal window
  $proc = Start-Process -FilePath $DarktablePath -ArgumentList ($dtArgs -join ' ') `
                        -PassThru `
                        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog

  # ------------------------------------------------------------------- wait

  $overallTimeout = $Timeout * ($files.Count + 1) + 300
  $started = Get-Date
  $printed = 0

  while ($true) {
    if (Test-Path -LiteralPath $luaLog) {
      $lines = @(Get-Content -LiteralPath $luaLog -ErrorAction SilentlyContinue)
      if ($lines.Count -gt $printed) {
        $lines[$printed..($lines.Count - 1)] | ForEach-Object { Write-Host $_ }
        $printed = $lines.Count
      }
    }

    if (Test-Path -LiteralPath $resultFile) { break }
    if ($proc.HasExited) { break }
    if (((Get-Date) - $started).TotalSeconds -gt $overallTimeout) {
      Write-Warning "timed out after $overallTimeout s"
      break
    }
    Start-Sleep -Seconds 2
  }

  # give the Lua script's quit request a moment to take effect
  $null = $proc.WaitForExit(30000)

  # -------------------------------------------------------------- report

  if (-not (Test-Path -LiteralPath $resultFile)) {
    Write-Host ''
    Write-Warning 'darktable exited without a result, last log lines:'
    Get-Content -LiteralPath $stdoutLog -Tail 20 -ErrorAction SilentlyContinue
    Get-Content -LiteralPath $stderrLog -Tail 20 -ErrorAction SilentlyContinue
    exit 1
  }

  $result = @{}
  foreach ($line in Get-Content -LiteralPath $resultFile) {
    $key, $value = $line -split '=', 2
    if ($key -eq 'output' -or $key -eq 'missing') { continue }
    $result[$key] = $value
  }

  Write-Host ''
  Write-Host "$($result['status']): $($result['message'])"
  Write-Host "written $($result['done']) of $($result['total']) DNG file(s)"

  if ($result['status'] -ne 'ok') { exit 1 }
}
finally {
  if ($proc -and -not $proc.HasExited) {
    $null = $proc.CloseMainWindow()
    if (-not $proc.WaitForExit(10000)) {
      Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
  }

  Remove-StaleLock (Join-Path $configDir 'data.db.lock')
  Remove-StaleLock (Join-Path $workdir 'library.db.lock')

  # --conf values are written back to darktablerc on exit; restore the
  # user's settings so the automation leaves no trace in the GUI
  if (Test-Path -LiteralPath $rcBackup) {
    Copy-Item -LiteralPath $rcBackup $darktablerc -Force
  }

  Remove-Item Env:\DT_DENOISE_WORKDIR -ErrorAction SilentlyContinue

  if ($KeepWorkDir) {
    Write-Host "work directory kept: $workdir"
  } else {
    Remove-Item -LiteralPath $workdir -Recurse -Force -ErrorAction SilentlyContinue
  }
}
