param(
  [Parameter(Mandatory = $false)]
  [string]$InstallerPath = ".\\GameBotInstaller.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "Example: background app per-user install"
Write-Host "$InstallerPath /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT=\"$env:LocalAppData\\GameBot\\data\" BACKEND_PORT=5000 WEB_PORT=8080 PROTOCOL=http ALLOW_ONLINE_PREREQ_FALLBACK=1"

Write-Host "Example: service mode per-machine install"
Write-Host "$InstallerPath /quiet MODE=service SCOPE=perMachine DATA_ROOT=\"$env:ProgramData\\GameBot\\data\" BACKEND_PORT=5000 WEB_PORT=8088 PROTOCOL=https ENABLE_HTTPS=1 CERTIFICATE_REF=thumbprint:TODO ALLOW_ONLINE_PREREQ_FALLBACK=0"
