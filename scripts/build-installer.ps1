param(
  [Parameter(Mandatory = $false)]
  [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$wixProject = Join-Path $repoRoot "installer/wix/GameBot.Installer.wixproj"
$msiProject = Join-Path $repoRoot "installer/wix/GameBot.Msi.wixproj"
$payloadMsiPath = Join-Path $repoRoot "installer/wix/payload/GameBot.msi"
$wixBinDir = Join-Path $repoRoot "installer/wix/bin"

& (Join-Path $PSScriptRoot "package-installer-payload.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
  throw "Payload packaging failed with exit code $LASTEXITCODE"
}

dotnet build $msiProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
  throw "MSI build failed with exit code $LASTEXITCODE"
}

$builtMsi = Get-ChildItem -Path $wixBinDir -Recurse -Filter "GameBot.msi" |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

if ($null -eq $builtMsi) {
  throw "Expected MSI not found under: $wixBinDir"
}

Copy-Item -Path $builtMsi.FullName -Destination $payloadMsiPath -Force

dotnet build $wixProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
  throw "Installer build failed with exit code $LASTEXITCODE"
}

Write-Host "Installer scaffold build completed."
Write-Host "Bundle/MSI outputs are under installer/wix/bin/$Configuration"
