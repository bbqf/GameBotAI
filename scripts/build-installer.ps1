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
$licenseSourcePath = Join-Path $repoRoot "LICENSE"
$licenseRtfPath = Join-Path $repoRoot "installer/wix/Assets/License.generated.rtf"
$licenseRtfDirectory = Split-Path -Parent $licenseRtfPath

Import-Module (Join-Path $PSScriptRoot "installer/common.psm1") -Force

function Get-GitHubRepoContext {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
  )

  try {
    $origin = (git -C $RepoRoot remote get-url origin 2>$null).Trim()
    $branch = (git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null).Trim()
  }
  catch {
    return $null
  }

  if ([string]::IsNullOrWhiteSpace($origin) -or [string]::IsNullOrWhiteSpace($branch)) {
    return $null
  }

  $match = [regex]::Match($origin, 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+?)(?:\.git)?$')
  if (-not $match.Success) {
    return $null
  }

  return [PSCustomObject]@{
    Owner = $match.Groups['owner'].Value
    Repo = $match.Groups['repo'].Value
    Branch = $branch
  }
}

function Get-LatestCiRunNumberForBranch {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Owner,
    [Parameter(Mandatory = $true)]
    [string]$Repo,
    [Parameter(Mandatory = $true)]
    [string]$Branch
  )

  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    return $null
  }

  try {
    $apiPath = "/repos/$Owner/$Repo/actions/workflows/release-installer.yml/runs"
    $runNumberText = gh api $apiPath --method GET -f branch=$Branch -f per_page=1 -f status=completed --jq ".workflow_runs[0].run_number" 2>$null
    if ([string]::IsNullOrWhiteSpace($runNumberText)) {
      return $null
    }

    $parsed = 0
    if ([int]::TryParse($runNumberText.Trim(), [ref]$parsed)) {
      return $parsed
    }
  }
  catch {
    return $null
  }

  return $null
}

if (-not (Test-Path $licenseSourcePath)) {
  throw "License source file not found at $licenseSourcePath"
}

if (-not (Test-Path $licenseRtfDirectory)) {
  New-Item -Path $licenseRtfDirectory -ItemType Directory -Force | Out-Null
}

$licenseLines = Get-Content -Path $licenseSourcePath
$escapedLines = $licenseLines | ForEach-Object {
  $_.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
}

$rtfBody = [string]::Join("`r`n", ($escapedLines | ForEach-Object { "$_\par" }))
$rtf = "{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}\fs20\f0`r`n$rtfBody`r`n}"
Set-Content -Path $licenseRtfPath -Value $rtf -Encoding Ascii
Write-Host "Generated installer license RTF from root LICENSE: $licenseRtfPath"

$isCi = ($env:CI -eq "true") -or ($env:GITHUB_ACTIONS -eq "true")
$buildContext = if ($isCi) { "ci" } else { "local" }
$ciBuildNumber = $null
if ($isCi -and -not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_NUMBER)) {
  $parsed = 0
  if ([int]::TryParse($env:GITHUB_RUN_NUMBER, [ref]$parsed)) {
    $ciBuildNumber = $parsed
  }
}

if (-not $isCi) {
  $repoContext = Get-GitHubRepoContext -RepoRoot $repoRoot
  if ($null -ne $repoContext) {
    $latestCiRunNumber = Get-LatestCiRunNumberForBranch -Owner $repoContext.Owner -Repo $repoContext.Repo -Branch $repoContext.Branch
    if ($null -ne $latestCiRunNumber) {
      $ciBuildNumber = $latestCiRunNumber + 1
      Write-Host "Resolved local build number from latest CI run: $latestCiRunNumber -> using $ciBuildNumber"
    }
    else {
      Write-Host "Could not resolve latest CI run number for branch '$($repoContext.Branch)'; using local counter fallback."
    }
  }
  else {
    Write-Host "Could not resolve GitHub repository context; using local counter fallback."
  }
}

if ($null -ne $ciBuildNumber) {
  $versionResolution = Resolve-InstallerVersion -RepoRoot $repoRoot -BuildContext $buildContext -BuildNumberOverride $ciBuildNumber
}
else {
  $versionResolution = Resolve-InstallerVersion -RepoRoot $repoRoot -BuildContext $buildContext
}
$installerVersion = $versionResolution.Version
Write-Host "Resolved installer version: $installerVersion (source=$($versionResolution.Source), persisted=$($versionResolution.Persisted))"

& (Join-Path $PSScriptRoot "package-installer-payload.ps1") -Configuration $Configuration -InstallerVersion $installerVersion
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
