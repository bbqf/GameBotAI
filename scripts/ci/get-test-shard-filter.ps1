# Prints the test filter of one test shard, or lists the shards of a project.
# The shards come from scripts/ci/test-shards.json. The last shard of a project
# is the complement of all named filters, so a new test class always runs in
# one shard and is never lost.
#
#   get-test-shard-filter.ps1 -Project integration -Shard integration-1
#   get-test-shard-filter.ps1 -Project integration -List
#   get-test-shard-filter.ps1 -Project integration -Pinned
param(
  [Parameter(Mandatory = $true)]
  [string]$Project,
  [string]$Shard = "",
  [switch]$List,
  [switch]$Pinned,
  [switch]$PerformanceShard,
  [string]$Manifest = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $Manifest) { $Manifest = Join-Path $PSScriptRoot "test-shards.json" }

$data = Get-Content -Path $Manifest -Raw | ConvertFrom-Json
if (-not ($data.PSObject.Properties.Name -contains $Project)) {
  throw "Project '$Project' is not in $Manifest."
}

$shards = @($data.$Project.shards)

if ($Pinned -or $PerformanceShard) {
  # Name of the shard that collects coverage (-Pinned) or runs the performance gate, or nothing.
  $flag = if ($Pinned) { "collectsCoverage" } else { "performanceGate" }
  $pinnedShard = @($shards | Where-Object { $_.PSObject.Properties.Name -contains $flag -and $_.$flag } | ForEach-Object { $_.name })
  if ($pinnedShard.Count -gt 1) { throw "More than one shard has $flag in project '$Project'." }
  if ($pinnedShard.Count -eq 1) { $pinnedShard[0] }
  return
}

if ($List) {
  # JSON array of shard names, ready for a workflow matrix.
  ConvertTo-Json -InputObject @($shards | ForEach-Object { $_.name }) -Compress
  return
}

if (-not $Shard) { throw "Give -Shard or -List." }

$target = $shards | Where-Object { $_.name -eq $Shard } | Select-Object -First 1
if (-not $target) { throw "Shard '$Shard' is not in project '$Project'." }

function Get-Includes {
  param($Item)
  if ($Item.PSObject.Properties.Name -contains "include") { return @($Item.include) }
  return @()
}

if ($target.PSObject.Properties.Name -contains "complement" -and $target.complement) {
  $named = @($shards | Where-Object { $_.name -ne $Shard } | ForEach-Object { Get-Includes $_ })
  if ($named.Count -eq 0) {
    # A project with a single shard runs every test: no filter.
    return
  }
  ($named | ForEach-Object { "FullyQualifiedName!~$_" }) -join "&"
  return
}

$includes = @(Get-Includes $target)
if ($includes.Count -eq 0) { throw "Shard '$Shard' has no include entries and is not the complement." }
($includes | ForEach-Object { "FullyQualifiedName~$_" }) -join "|"
