# Decides the result of the ci-gate job from the results of the needed jobs.
#
# Rules:
#   - The changes job must succeed.
#   - A .NET job (compile, analyze, test-*) must succeed when dotnet=true.
#     It may be skipped when dotnet=false (a path rule skipped it).
#   - The web UI job must succeed when webui=true. It may be skipped when webui=false.
#   - A job that failed, was cancelled, or produced no result always fails the gate.
#   - A job skipped while its switch is true counts as a failure (an earlier job failed).
#
# Input: -NeedsJson is the JSON of the "needs" context of the workflow.
# Exit code: 0 pass, 1 fail.
param(
  [Parameter(Mandatory = $true)]
  [string]$NeedsJson,
  [Parameter(Mandatory = $true)]
  [AllowEmptyString()]
  [string]$Dotnet,
  [Parameter(Mandatory = $true)]
  [AllowEmptyString()]
  [string]$Webui
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$needs = $NeedsJson | ConvertFrom-Json

$expectations = @(
  [PSCustomObject]@{ Job = "changes"; Required = $true },
  [PSCustomObject]@{ Job = "compile"; Required = ($Dotnet -eq "true") },
  [PSCustomObject]@{ Job = "analyze"; Required = ($Dotnet -eq "true") },
  [PSCustomObject]@{ Job = "test-unit"; Required = ($Dotnet -eq "true") },
  [PSCustomObject]@{ Job = "test-integration"; Required = ($Dotnet -eq "true") },
  [PSCustomObject]@{ Job = "test-contract"; Required = ($Dotnet -eq "true") },
  [PSCustomObject]@{ Job = "web-ui-tests"; Required = ($Webui -eq "true") }
)

$failed = $false

foreach ($item in $expectations) {
  $result = "missing"
  if ($needs.PSObject.Properties.Name -contains $item.Job) {
    $result = $needs.($item.Job).result
  }

  $ok = $false
  if ($result -eq "success") { $ok = $true }
  elseif ($result -eq "skipped" -and -not $item.Required) { $ok = $true }

  $state = if ($ok) { "ok" } else { "FAIL" }
  Write-Host ("{0,-18} result={1,-10} required={2,-5} {3}" -f $item.Job, $result, $item.Required, $state)
  if (-not $ok) { $failed = $true }
}

if ($failed) {
  Write-Host "ci-gate: FAIL"
  exit 1
}

Write-Host "ci-gate: pass"
