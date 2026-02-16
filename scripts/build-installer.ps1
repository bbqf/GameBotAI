param(
  [Parameter(Mandatory = $false)]
  [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$wixProject = Join-Path $repoRoot "installer/wix/GameBot.Installer.wixproj"

& (Join-Path $PSScriptRoot "package-installer-payload.ps1") -Configuration $Configuration

dotnet build $wixProject -c $Configuration

Write-Host "Installer scaffold build completed."
Write-Host "Bundle/MSI outputs are under installer/wix/bin/$Configuration"
