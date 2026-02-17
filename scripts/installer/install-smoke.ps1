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
