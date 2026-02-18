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
$bundleTargetName = "GameBotInstaller"

Import-Module (Join-Path $PSScriptRoot "installer/common.psm1") -Force

$isCi = ($env:CI -eq "true") -or ($env:GITHUB_ACTIONS -eq "true")
$buildContext = if ($isCi) { "ci" } else { "local" }
$versionResolution = Resolve-InstallerVersion -RepoRoot $repoRoot -BuildContext $buildContext
$installerVersion = $versionResolution.Version
Write-Host "Resolved installer version: $installerVersion (source=$($versionResolution.Source), persisted=$($versionResolution.Persisted))"

& (Join-Path $PSScriptRoot "package-installer-payload.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
  throw "Payload packaging failed with exit code $LASTEXITCODE"
}

dotnet build $msiProject -c $Configuration /p:InstallerVersion=$installerVersion
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

$installerProcessNames = @("GameBotInstaller")
$runningInstallers = Get-Process -ErrorAction SilentlyContinue |
  Where-Object {
    ($installerProcessNames -contains $_.ProcessName) -or
    ($_.MainWindowTitle -like "GameBot Installer*")
  }

foreach ($proc in $runningInstallers) {
  try {
    Stop-Process -Id $proc.Id -Force -ErrorAction Stop
    Write-Host "Stopped running installer process: $($proc.ProcessName) ($($proc.Id))"
  } catch {
    Write-Warning "Failed to stop installer process $($proc.ProcessName) ($($proc.Id)): $($_.Exception.Message)"
  }
}

dotnet build $wixProject -c $Configuration /p:InstallerVersion=$installerVersion
if ($LASTEXITCODE -ne 0) {
  throw "Installer build failed with exit code $LASTEXITCODE"
}

& (Join-Path $PSScriptRoot "installer/scope-smoke.ps1") -RepoRoot $repoRoot
if ($LASTEXITCODE -ne 0) {
  throw "Installer scope smoke checks failed with exit code $LASTEXITCODE"
}

$builtBundle = Get-ChildItem -Path $wixBinDir -Recurse -Filter "$bundleTargetName.exe" |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

if ($null -eq $builtBundle) {
  throw "Expected bundle EXE not found under: $wixBinDir"
}

Write-Host "Installer scaffold build completed."
Write-Host "Bundle/MSI outputs are under installer/wix/bin/$Configuration"
Write-Host "Bundle EXE: $($builtBundle.FullName)"
