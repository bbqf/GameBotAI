<#
.SYNOPSIS
  Measures the clean build time and the incremental build time of GameBot.sln.

.DESCRIPTION
  Does these steps:
    1. Restore (not timed).
    2. Clean step: delete the bin and obj folders under src and tests.
    3. Timed clean build: dotnet build GameBot.sln (Debug, no extra option).
    4. Timed incremental build: the same command with no source change.
  Then it prints each time against the limits and the PC description.

  Limits: clean build under 4 minutes (target: 3 minutes or less), incremental build under 30 seconds.
  The limits apply to the developer PC. Do not run other heavy programs during the measurement.
  See docs/build-time.md.

.PARAMETER Runs
  Number of timed clean builds. Use 3 for an official result (worst and median). Default: 1.

.PARAMETER SkipIncremental
  Do not measure the incremental build.
#>
[CmdletBinding()]
param(
  [int]$Runs = 1,
  [switch]$SkipIncremental
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'GameBot.sln'
$cleanLimitSeconds = 240
$cleanTargetSeconds = 180
$incrementalLimitSeconds = 30

function Remove-BuildOutput {
  # Deletes the bin and obj folders of every project under src and tests.
  foreach ($top in 'src', 'tests') {
    $topPath = Join-Path $repoRoot $top
    Get-ChildItem -Path $topPath -Directory -Recurse -Include bin, obj -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -notmatch '\\node_modules\\' -and $_.FullName -notmatch '\\web-ui\\' } |
      ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
  }
}

function Measure-Build {
  # Runs the build command and returns the wall time in seconds. Stops when the build fails.
  $elapsed = Measure-Command { & dotnet build $solution | Out-Host }
  if ($LASTEXITCODE -ne 0) { throw "The build failed with exit code $LASTEXITCODE." }
  return [math]::Round($elapsed.TotalSeconds, 1)
}

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
Write-Host "PC: $($cpu.Name), $($cpu.NumberOfCores) cores, $ramGb GB RAM"
Write-Host "SDK: $(& dotnet --version)"

Write-Host 'Restore (not timed).'
& dotnet restore $solution | Out-Host
if ($LASTEXITCODE -ne 0) { throw "The restore failed with exit code $LASTEXITCODE." }

$cleanTimes = @()
for ($i = 1; $i -le $Runs; $i++) {
  Write-Host "Clean step, then timed clean build $i of $Runs."
  & dotnet build-server shutdown | Out-Null
  Remove-BuildOutput
  $cleanTimes += Measure-Build
}

$incrementalTime = $null
if (-not $SkipIncremental) {
  Write-Host 'Timed incremental build (no source change).'
  $incrementalTime = Measure-Build
}

$sorted = $cleanTimes | Sort-Object
$worst = $sorted[-1]
$median = if ($sorted.Count % 2 -eq 1) { $sorted[($sorted.Count - 1) / 2] } else { ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2 }

function Get-Verdict([double]$value, [double]$limit) {
  if ($value -lt $limit) { return 'OK' } else { return 'OVER THE LIMIT' }
}

Write-Host ''
Write-Host 'Result'
Write-Host "  Clean build times (s): $($cleanTimes -join ', ')"
Write-Host "  Clean build worst:  $worst s   limit $cleanLimitSeconds s   $(Get-Verdict $worst $cleanLimitSeconds)"
$medianVerdict = if ($median -le $cleanTargetSeconds) { 'OK' } else { 'ABOVE THE TARGET' }
Write-Host "  Clean build median: $median s   target $cleanTargetSeconds s   $medianVerdict"
if ($null -ne $incrementalTime) {
  Write-Host "  Incremental build:  $incrementalTime s   limit $incrementalLimitSeconds s   $(Get-Verdict $incrementalTime $incrementalLimitSeconds)"
}
if ($Runs -lt 3) {
  Write-Host '  Note: use -Runs 3 for an official result (worst and median of 3 runs).'
}
Write-Host '  Record the PC description above with the result.'
