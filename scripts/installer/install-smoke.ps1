param(
  [Parameter(Mandatory = $false)]
  [string]$InstallRoot,

  [Parameter(Mandatory = $false)]
  [string]$DataRoot,

  [Parameter(Mandatory = $false)]
  [int]$MaxInstallerLogs = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $InstallRoot) {
  $InstallRoot = Join-Path $env:LocalAppData "GameBot"
}

if (-not $DataRoot) {
  $DataRoot = Join-Path $env:LocalAppData "GameBot/data"
}

Write-Host "Running installer smoke checks"
Write-Host "InstallRoot: $InstallRoot"
Write-Host "DataRoot: $DataRoot"

if (-not (Test-Path $InstallRoot)) {
  throw "Install root not found: $InstallRoot"
}

if (-not (Test-Path $DataRoot)) {
  throw "Data root not found: $DataRoot"
}

$serviceExe = Join-Path $InstallRoot "GameBot.Service.exe"
if (-not (Test-Path $serviceExe)) {
  throw "Expected service executable missing: $serviceExe"
}

$webUiIndex = Join-Path $InstallRoot "web-ui/index.html"
if (-not (Test-Path $webUiIndex)) {
  Write-Warning "Web UI index was not found at expected location: $webUiIndex"
}

Write-Host "Installer smoke checks completed."

$networkConfigRoot = "HKCU:\Software\GameBot\Network"
if (Test-Path $networkConfigRoot) {
  $bindHost = (Get-ItemProperty -Path $networkConfigRoot -Name "BindHost" -ErrorAction SilentlyContinue).BindHost
  $port = (Get-ItemProperty -Path $networkConfigRoot -Name "Port" -ErrorAction SilentlyContinue).Port
  if ([string]::IsNullOrWhiteSpace($bindHost) -or [string]::IsNullOrWhiteSpace($port)) {
    throw "Expected persisted network properties (BindHost/Port) were not found after install/upgrade smoke check."
  }
  Write-Host "Upgrade retention check: persisted BindHost=$bindHost Port=$port"
}

# Feature 132: the installer writes the host and port to <data root>\config\network.json.
$networkFile = Join-Path $DataRoot "config/network.json"
if (-not (Test-Path $networkFile)) {
  throw "Expected saved network file was not found after install/upgrade: $networkFile"
}
$networkSettings = Get-Content -Path $networkFile -Raw | ConvertFrom-Json
$savedPort = 0
if (-not [int]::TryParse([string]$networkSettings.port, [ref]$savedPort) -or $savedPort -lt 1 -or $savedPort -gt 65535) {
  throw "The saved network file has no valid port: $networkFile"
}
if ([string]::IsNullOrWhiteSpace([string]$networkSettings.bindHost)) {
  throw "The saved network file has no valid bindHost: $networkFile"
}
foreach ($leftover in @("network.json.bak", "network.json.none", "network.json.tmp")) {
  if (Test-Path (Join-Path (Split-Path $networkFile) $leftover)) {
    throw "The installer left a temporary file next to the saved network file: $leftover"
  }
}
Write-Host "Saved network file check: bindHost=$($networkSettings.bindHost) port=$savedPort"

$logRoot = Join-Path $env:LocalAppData "GameBot/Installer/logs"
if (Test-Path $logRoot) {
  $logFiles = Get-ChildItem -Path $logRoot -File | Sort-Object LastWriteTime -Descending
  if ($logFiles.Count -gt $MaxInstallerLogs) {
    throw "Installer log retention exceeded: found $($logFiles.Count), expected <= $MaxInstallerLogs"
  }
}

$interactiveDurationSeconds = 0
$silentDurationSeconds = 0
Write-Host "Duration placeholders: interactive=${interactiveDurationSeconds}s silent=${silentDurationSeconds}s"
