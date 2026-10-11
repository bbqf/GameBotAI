# Reads past runs of a GitHub Actions workflow and prints the time of each part.
# Queue time (push to first job start) is shown apart from step time, so a slow
# runner pool is not read as a slow job.
param(
  [string]$Workflow = "dotnet.yml",
  [int]$Limit = 20,
  [string]$Branch = "",
  [string]$Conclusion = "",
  [switch]$Steps,
  [string]$Json = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-Seconds {
  param([string]$From, [string]$To)
  if ([string]::IsNullOrEmpty($From) -or [string]::IsNullOrEmpty($To)) { return $null }
  $a = [datetimeoffset]::Parse($From, [System.Globalization.CultureInfo]::InvariantCulture)
  $b = [datetimeoffset]::Parse($To, [System.Globalization.CultureInfo]::InvariantCulture)
  return [math]::Round(($b - $a).TotalSeconds, 0)
}

function Get-Median {
  param([double[]]$Values)
  $sorted = @($Values | Sort-Object)
  if ($sorted.Count -eq 0) { return $null }
  $mid = [int][math]::Floor($sorted.Count / 2)
  if ($sorted.Count % 2 -eq 1) { return $sorted[$mid] }
  return ($sorted[$mid - 1] + $sorted[$mid]) / 2
}

$listArgs = @("run", "list", "--workflow", $Workflow, "--limit", "$Limit", "--json", "databaseId,createdAt,conclusion,headBranch,event,headSha")
if ($Branch) { $listArgs += @("--branch", $Branch) }
if ($Conclusion) { $listArgs += @("--status", $Conclusion) }

# Windows PowerShell 5.1 returns a JSON array as one object; the pipe unrolls it.
$runs = @(& gh @listArgs | ConvertFrom-Json | ForEach-Object { $_ })
$rows = @()
$jobTimes = @{}

foreach ($run in $runs) {
  $detail = & gh run view "$($run.databaseId)" --json "jobs,startedAt,updatedAt,conclusion" | ConvertFrom-Json
  $jobs = @($detail.jobs | Where-Object { $_.startedAt -and $_.completedAt -and $_.startedAt -notlike "0001*" })
  if ($jobs.Count -eq 0) { continue }

  $firstStart = ($jobs | Sort-Object startedAt | Select-Object -First 1).startedAt
  $lastEnd = ($jobs | Sort-Object completedAt | Select-Object -Last 1).completedAt
  $queue = Get-Seconds $run.createdAt $firstStart
  $critical = Get-Seconds $firstStart $lastEnd
  $longest = $jobs | Sort-Object { Get-Seconds $_.startedAt $_.completedAt } | Select-Object -Last 1

  $rows += [PSCustomObject]@{
    RunId = $run.databaseId
    Branch = $run.headBranch
    Result = $detail.conclusion
    QueueSec = $queue
    CriticalPathSec = $critical
    LongestJob = $longest.name
  }

  foreach ($job in $jobs) {
    $sec = Get-Seconds $job.startedAt $job.completedAt
    if (-not $jobTimes.ContainsKey($job.name)) { $jobTimes[$job.name] = @() }
    $jobTimes[$job.name] += $sec
    if ($Steps) {
      foreach ($step in @($job.steps)) {
        $stepSec = Get-Seconds $step.startedAt $step.completedAt
        if ($null -eq $stepSec) { continue }
        $key = "$($job.name) :: $($step.name)"
        if (-not $jobTimes.ContainsKey($key)) { $jobTimes[$key] = @() }
        $jobTimes[$key] += $stepSec
      }
    }
  }
}

if ($rows.Count -eq 0) {
  Write-Host "No runs found."
  exit 0
}

Write-Host "Runs:"
$rows | Format-Table -AutoSize | Out-String | Write-Host

$crit = @($rows | ForEach-Object { [double]$_.CriticalPathSec })
$queueValues = @($rows | ForEach-Object { [double]$_.QueueSec })
Write-Host ("Critical path (first job start to last job end): median {0}s, worst {1}s" -f (Get-Median $crit), ($crit | Measure-Object -Maximum).Maximum)
Write-Host ("Queue time (push to first job start): median {0}s, worst {1}s" -f (Get-Median $queueValues), ($queueValues | Measure-Object -Maximum).Maximum)
Write-Host ""
Write-Host "Time per job or step (seconds):"
$summary = $jobTimes.Keys | Sort-Object | ForEach-Object {
  $values = @($jobTimes[$_] | ForEach-Object { [double]$_ })
  [PSCustomObject]@{
    Part = $_
    Count = $values.Count
    Median = Get-Median $values
    Worst = ($values | Measure-Object -Maximum).Maximum
  }
}
$summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

if ($Json) {
  [PSCustomObject]@{ Runs = $rows; Parts = $summary } | ConvertTo-Json -Depth 5 | Set-Content -Path $Json -Encoding utf8
}
