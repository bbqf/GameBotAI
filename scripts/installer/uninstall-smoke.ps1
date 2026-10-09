param(
  [Parameter(Mandatory = $false)]
  [string]$InstallRoot,

  [Parameter(Mandatory = $false)]
  [string]$DataRoot,

  # Feature 132: the uninstall must keep the saved host and port file.
  [Parameter(Mandatory = $false)]
  [switch]$ExpectNetworkFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Installer uninstall smoke check placeholder"
if ($InstallRoot) {
  Write-Host "InstallRoot: $InstallRoot"
}
if ($DataRoot) {
  Write-Host "DataRoot: $DataRoot"
}

if ($InstallRoot -and (Test-Path $InstallRoot)) {
  throw "Install root still exists after uninstall: $InstallRoot"
}

if ($DataRoot -and (Test-Path $DataRoot)) {
  Write-Warning "Data root still exists after uninstall (may be retained by policy): $DataRoot"
}

if ($ExpectNetworkFile) {
  if (-not $DataRoot) {
    throw "The ExpectNetworkFile switch needs DataRoot."
  }
  $networkFile = Join-Path $DataRoot "config/network.json"
  if (-not (Test-Path $networkFile)) {
    throw "The saved network file was removed by the uninstall: $networkFile"
  }
  Write-Host "Saved network file kept after uninstall: $networkFile"
}

Write-Host "Uninstall smoke checks completed."
