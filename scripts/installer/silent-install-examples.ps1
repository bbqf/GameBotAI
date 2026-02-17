param(
  [Parameter(Mandatory = $false)]
  [string]$InstallerPath = ".\\GameBotInstaller.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Example: background app per-user install"
Write-Host "$InstallerPath /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT=\"$env:LocalAppData\\GameBot\\data\" BACKEND_PORT=5000 WEB_PORT=8080 PROTOCOL=http ALLOW_ONLINE_PREREQ_FALLBACK=1"
