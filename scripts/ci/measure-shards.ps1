# Runs each test shard of one project on this PC and checks the shard balance.
# Rule: a shard that is more than 20% above the average time of its project must be split.
# Split it in scripts/ci/test-shards.json. See docs/ci-time.md ("Shard balance rule").
#
#   measure-shards.ps1 -Project contract
#   measure-shards.ps1 -Project contract -TimesFile times.json   # no test run, times from a JSON file
#
# Build the solution in Release first: the shards run with dotnet test --no-build.
# Exit status: 0 = all shards within the limit, 1 = a shard must be split, 2 = wrong input.
param(
  [Parameter(Mandatory = $true)]
  [string]$Project,
  [string]$TimesFile = "",
  [double]$Limit = 0.2,
  [string]$Manifest = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $Manifest) { $Manifest = Join-Path $PSScriptRoot "test-shards.json" }
$filterScript = Join-Path $PSScriptRoot "get-test-shard-filter.ps1"
$projectFiles = @{
  unit        = "tests\unit\GameBot.UnitTests.csproj"
  contract    = "tests\contract\GameBot.ContractTests.csproj"
  integration = "tests\integration\GameBot.IntegrationTests.csproj"
}

function Stop-WithInputError {
  param([string]$Message)
  [Console]::Error.WriteLine($Message)
  exit 2
}

try {
  # Windows PowerShell 5.1 sends a JSON array down the pipe as one object, so unroll it.
  $names = [string[]]@(ConvertFrom-Json -InputObject (& $filterScript -Project $Project -List -Manifest $Manifest) | ForEach-Object { $_ })
}
catch {
  Stop-WithInputError "Cannot read the shards of project '$Project': $($_.Exception.Message)"
}

# Time of one shard run, in seconds.
function Measure-Shard {
  param([string]$Name)
  $filter = & $filterScript -Project $Project -Shard $Name -Manifest $Manifest
  $project = Join-Path (Split-Path $PSScriptRoot -Parent | Split-Path -Parent) $projectFiles[$Project]
  $arguments = @("test", $project, "-c", "Release", "--no-build", "--nologo", "-v", "q")
  if ($filter) { $arguments += @("--filter", $filter) }
  $watch = [Diagnostics.Stopwatch]::StartNew()
  & dotnet @arguments | Out-Null
  $watch.Stop()
  if ($LASTEXITCODE -ne 0) { Stop-WithInputError "Shard '$Name' failed. Fix the tests, then measure again." }
  return [math]::Round($watch.Elapsed.TotalSeconds, 1)
}

$times = [ordered]@{}
if ($TimesFile) {
  if (-not (Test-Path $TimesFile)) { Stop-WithInputError "Times file '$TimesFile' does not exist." }
  try { $given = Get-Content -Path $TimesFile -Raw | ConvertFrom-Json }
  catch { Stop-WithInputError "Times file '$TimesFile' is not valid JSON." }
  foreach ($name in $names) {
    if (-not ($given.PSObject.Properties.Name -contains $name)) { Stop-WithInputError "No time for shard '$name' in the times file." }
    $times[$name] = [double]$given.$name
  }
}
else {
  if (-not $projectFiles.ContainsKey($Project)) { Stop-WithInputError "No test project is known for '$Project'." }
  foreach ($name in $names) { $times[$name] = Measure-Shard $name }
}

function Get-Average {
  $sum = 0.0
  foreach ($value in $times.Values) { $sum += $value }
  return $sum / $times.Count
}

$average = Get-Average
$allowed = $average * (1 + $Limit)

# A shard above the limit runs again. The lower time counts, so one slow run does not force a split.
if (-not $TimesFile -and $names.Count -gt 1) {
  foreach ($name in $names) {
    if ($times[$name] -gt $allowed) {
      $again = Measure-Shard $name
      if ($again -lt $times[$name]) { $times[$name] = $again }
    }
  }
  $average = Get-Average
  $allowed = $average * (1 + $Limit)
}

$inv = [Globalization.CultureInfo]::InvariantCulture
$rows = foreach ($name in $names) {
  $status = if ($names.Count -gt 1 -and $times[$name] -gt $allowed) { "split" } else { "ok" }
  [pscustomobject]@{ Shard = $name; Seconds = $times[$name].ToString("0.0", $inv); Status = $status }
}
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Output

if ($names.Count -le 1) {
  Write-Output "Project '$Project' has one shard. There is no average to compare."
  exit 0
}

Write-Output ("Average: {0} s" -f $average.ToString("0.0", $inv))
Write-Output ("Limit: {0} s (average plus {1}%)" -f $allowed.ToString("0.0", $inv), ($Limit * 100).ToString("0.##", $inv))

$over = @($rows | Where-Object { $_.Status -eq "split" })
foreach ($row in $over) {
  Write-Output ("Split {0}: {1} s is above {2} s." -f $row.Shard, $row.Seconds, $allowed.ToString("0.0", $inv))
}
if ($over.Count -gt 0) { exit 1 }
exit 0
