param(
  [Parameter(Mandatory = $false)]
  [string]$InstallerPath = ".\\GameBotInstaller.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Example: background app per-user install"
Write-Host "$InstallerPath /quiet DATA_ROOT=\"$env:LocalAppData\\GameBot\\data\" BIND_HOST=127.0.0.1 PORT=8080 ALLOW_ONLINE_PREREQ_FALLBACK=1"
