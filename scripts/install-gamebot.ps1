param(
  [Parameter(Mandatory = $false)]
  [ValidateSet("service", "backgroundApp")]
  [string]$Mode = "backgroundApp",

  [Parameter(Mandatory = $false)]
  [ValidateRange(1, 65535)]
  [int]$BackendPort = 5000,

  [Parameter(Mandatory = $false)]
  [ValidateRange(1, 65535)]
  [int]$WebPort = 0,

  [Parameter(Mandatory = $false)]
  [ValidateSet("http", "https")]
  [string]$Protocol = "http",

  [Parameter(Mandatory = $false)]
  [switch]$StartOnLogin,

  [Parameter(Mandatory = $false)]
  [switch]$NoStartOnLogin,

  [Parameter(Mandatory = $false)]
  [bool]$ConfirmFirewallFallback = $true,

  [Parameter(Mandatory = $false)]
  [string]$ServiceBaseUrl = "http://127.0.0.1:5000",

  [Parameter(Mandatory = $false)]
  [switch]$Unattended,

  [Parameter(Mandatory = $false)]
  [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Show-Usage {
  Write-Host "Usage: .\scripts\install-gamebot.ps1 [-Mode service|backgroundApp] [-BackendPort 5000] [-WebPort 8080] [-Protocol http|https] [-StartOnLogin] [-NoStartOnLogin] [-ConfirmFirewallFallback true|false] [-ServiceBaseUrl http://127.0.0.1:5000] [-Unattended]"
}

if ($Help) {
  Show-Usage
  exit 0
}

if (-not $Unattended) {
  Write-Error "This script runs unattended installer mode only. Pass -Unattended."
  Show-Usage
  exit 2
}

if ($StartOnLogin -and $NoStartOnLogin) {
  Write-Error "StartOnLogin and NoStartOnLogin cannot both be set."
  exit 2
}

$startOnLogin = $true
if ($NoStartOnLogin) {
  $startOnLogin = $false
}
elseif ($StartOnLogin) {
  $startOnLogin = $true
}

$request = @{
  installMode = $Mode
  backendPort = $BackendPort
  requestedWebUiPort = $(if ($WebPort -gt 0) { $WebPort } else { $null })
  protocol = $Protocol
  unattended = $true
  startOnLogin = $startOnLogin
  confirmFirewallFallback = $ConfirmFirewallFallback
}

$preflightUri = "$ServiceBaseUrl/api/installer/preflight"
$executeUri = "$ServiceBaseUrl/api/installer/execute"

try {
  $preflight = Invoke-RestMethod -Method Post -Uri $preflightUri -Body ($request | ConvertTo-Json -Depth 10) -ContentType "application/json"
}
catch {
  Write-Error "Preflight failed: $($_.Exception.Message)"
  exit 3
}

if (-not $preflight.canProceed) {
  Write-Error "Preflight returned cannot proceed."
  exit 3
}

if ($preflight.warnings) {
  Write-Host "Warnings:"
  $preflight.warnings | ForEach-Object { Write-Host " - $_" }
}

try {
  $result = Invoke-RestMethod -Method Post -Uri $executeUri -Body ($request | ConvertTo-Json -Depth 10) -ContentType "application/json"
}
catch {
  Write-Error "Execution failed: $($_.Exception.Message)"
  exit 4
}

Write-Host "Installation run id: $($result.runId)"
if ($result.endpointConfiguration) {
  Write-Host "Web UI URL: $($result.endpointConfiguration.announcedWebUiUrl)"
  Write-Host "Backend URL: $($result.endpointConfiguration.announcedBackendUrl)"
}

exit 0
