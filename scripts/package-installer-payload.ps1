param(
  [Parameter(Mandatory = $false)]
  [string]$Configuration = "Release",

  [Parameter(Mandatory = $false)]
  [string]$Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$payloadRoot = Join-Path $repoRoot "installer/wix/payload"
$publishRoot = Join-Path $repoRoot "artifacts/installer-publish"
$serviceDllPath = Join-Path $publishRoot "service/GameBot.Service.dll"

if (Test-Path $publishRoot) {
  Remove-Item -Path $publishRoot -Recurse -Force
}
New-Item -Path $publishRoot -ItemType Directory -Force | Out-Null
New-Item -Path $payloadRoot -ItemType Directory -Force | Out-Null
New-Item -Path (Join-Path $payloadRoot "service") -ItemType Directory -Force | Out-Null
New-Item -Path (Join-Path $payloadRoot "web-ui") -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $repoRoot "src/GameBot.Service/GameBot.Service.csproj") -c $Configuration -r $Runtime --self-contained false -o (Join-Path $publishRoot "service")

$webUiDist = Join-Path $repoRoot "src/web-ui/dist"
if (-not (Test-Path $webUiDist)) {
  Write-Error "Web UI dist folder not found at '$webUiDist'. Build web UI before packaging payload."
}

Copy-Item -Path (Join-Path $publishRoot "service/*") -Destination (Join-Path $payloadRoot "service") -Recurse -Force
Copy-Item -Path (Join-Path $webUiDist "*") -Destination (Join-Path $payloadRoot "web-ui") -Recurse -Force

$serviceVersion = $null
if (Test-Path $serviceDllPath) {
  $serviceVersion = (Get-Item $serviceDllPath).VersionInfo.ProductVersion
}

$gitCommit = "unknown"
try {
  $gitCommit = (git -C $repoRoot rev-parse --short HEAD).Trim()
} catch {
}

$manifest = @{
  generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  configuration = $Configuration
  runtime = $Runtime
  payloadVersion = if ($serviceVersion) { $serviceVersion } else { "0.0.0-local" }
  serviceFileVersion = $serviceVersion
  sourceCommit = $gitCommit
  servicePath = "service"
  webUiPath = "web-ui"
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $payloadRoot "payload-manifest.json") -Encoding UTF8
Write-Host "Installer payload prepared at $payloadRoot"
