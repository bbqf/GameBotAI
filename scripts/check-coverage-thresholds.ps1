# Enforces per-area coverage thresholds from coverlet JSON output produced by a
# single "dotnet test GameBot.sln /p:CollectCoverage=true" run, so the test
# suite does not have to be re-run once per coverage gate.
param(
  [Parameter(Mandatory = $false)]
  [double]$Threshold = 80
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

function Get-CoverageStats {
  param(
    [Parameter(Mandatory = $true)][string]$CoverageJsonPath,
    [Parameter(Mandatory = $true)][string]$ModuleName,
    [Parameter(Mandatory = $true)][string]$ClassPrefix
  )

  if (-not (Test-Path $CoverageJsonPath)) {
    throw "Coverage file not found: $CoverageJsonPath. Ensure the test run used /p:CollectCoverage=true with /p:CoverletOutputFormat=json."
  }

  $coverage = Get-Content -Path $CoverageJsonPath -Raw | ConvertFrom-Json

  $linesTotal = 0
  $linesCovered = 0
  $branchesTotal = 0
  $branchesCovered = 0
  $classesMatched = 0

  foreach ($moduleProp in $coverage.PSObject.Properties) {
    $moduleKey = [System.IO.Path]::GetFileNameWithoutExtension($moduleProp.Name)
    if ($moduleKey -ne $ModuleName) { continue }

    foreach ($fileProp in $moduleProp.Value.PSObject.Properties) {
      foreach ($classProp in $fileProp.Value.PSObject.Properties) {
        if (-not $classProp.Name.StartsWith($ClassPrefix, [System.StringComparison]::Ordinal)) { continue }
        $classesMatched++

        foreach ($methodProp in $classProp.Value.PSObject.Properties) {
          $method = $methodProp.Value

          foreach ($lineProp in $method.Lines.PSObject.Properties) {
            $linesTotal++
            if ([int]$lineProp.Value -gt 0) { $linesCovered++ }
          }

          foreach ($branch in @($method.Branches)) {
            $branchesTotal++
            if ([int]$branch.Hits -gt 0) { $branchesCovered++ }
          }
        }
      }
    }
  }

  if ($classesMatched -eq 0) {
    throw "No coverage data for classes matching '$ClassPrefix' in module '$ModuleName' found in $CoverageJsonPath. The gate would be a silent no-op; failing instead."
  }

  $linePercent = if ($linesTotal -gt 0) { [math]::Round(100.0 * $linesCovered / $linesTotal, 2) } else { 100 }
  $branchPercent = if ($branchesTotal -gt 0) { [math]::Round(100.0 * $branchesCovered / $branchesTotal, 2) } else { 100 }

  return [PSCustomObject]@{
    ClassesMatched = $classesMatched
    LinePercent = $linePercent
    BranchPercent = $branchPercent
  }
}

$checks = @(
  [PSCustomObject]@{
    Name = "Installer endpoints (integration run)"
    Json = Join-Path $repoRoot "tests\integration\TestResults\coverage\coverage.json"
    Module = "GameBot.Service"
    ClassPrefix = "GameBot.Service.Endpoints.EmulatorImageEndpoints"
    CheckBranch = $false
  },
  [PSCustomObject]@{
    Name = "Execution log repository (unit run)"
    Json = Join-Path $repoRoot "tests\unit\coverage.json"
    Module = "GameBot.Domain"
    ClassPrefix = "GameBot.Domain.Logging.FileExecutionLogRepository"
    CheckBranch = $true
  }
)

$failed = $false

foreach ($check in $checks) {
  $stats = Get-CoverageStats -CoverageJsonPath $check.Json -ModuleName $check.Module -ClassPrefix $check.ClassPrefix

  $branchInfo = if ($check.CheckBranch) { ", branch $($stats.BranchPercent)%" } else { "" }
  Write-Host "$($check.Name): line $($stats.LinePercent)%$branchInfo (classes matched: $($stats.ClassesMatched), threshold: $Threshold%)"

  if ($stats.LinePercent -lt $Threshold) {
    Write-Host "FAIL: $($check.Name) line coverage $($stats.LinePercent)% is below $Threshold%"
    $failed = $true
  }
  if ($check.CheckBranch -and $stats.BranchPercent -lt $Threshold) {
    Write-Host "FAIL: $($check.Name) branch coverage $($stats.BranchPercent)% is below $Threshold%"
    $failed = $true
  }
}

if ($failed) {
  exit 1
}

Write-Host "All coverage thresholds satisfied."
