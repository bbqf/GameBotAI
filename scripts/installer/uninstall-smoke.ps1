param(
  [Parameter(Mandatory = $false)]
  [string]$InstallRoot,

  [Parameter(Mandatory = $false)]
  [string]$DataRoot
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

Write-Host "Uninstall smoke checks completed."
