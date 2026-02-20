Set-StrictMode -Version Latest

function Get-InstallerRepoRoot {
  $modulePath = Split-Path -Parent $PSCommandPath
  return (Resolve-Path (Join-Path $modulePath "../..")).Path
}

function Get-DefaultDataRoot {
  return Join-Path $env:LocalAppData "GameBot/data"
}

function New-InstallerLogDirectory {
  $logRoot = Join-Path $env:LocalAppData "GameBot/Installer/logs"
  New-Item -Path $logRoot -ItemType Directory -Force | Out-Null
  return $logRoot
}

function Get-DowngradeRemediationHint {
  return "Install a newer GameBot package or uninstall the currently installed newer version before proceeding."
}

function Get-InstallerVersioningPaths {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
  )

  $versioningRoot = Join-Path $RepoRoot "installer/versioning"
  return [PSCustomObject]@{
    Root = $versioningRoot
    Override = Join-Path $versioningRoot "version.override.json"
    ReleaseLineMarker = Join-Path $versioningRoot "release-line.marker.json"
    CiBuildCounter = Join-Path $versioningRoot "ci-build-counter.json"
  }
}

function Get-InstallerVersioningState {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
  )

  $paths = Get-InstallerVersioningPaths -RepoRoot $RepoRoot
  $override = Get-Content -Path $paths.Override -Raw | ConvertFrom-Json
  $marker = Get-Content -Path $paths.ReleaseLineMarker -Raw | ConvertFrom-Json
  $counter = Get-Content -Path $paths.CiBuildCounter -Raw | ConvertFrom-Json

  return [PSCustomObject]@{
    Paths = $paths
    Override = $override
    ReleaseLineMarker = $marker
    CiBuildCounter = $counter
  }
}

function Set-CiBuildCounter {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,
    [Parameter(Mandatory = $true)]
    [int]$LastBuild,
    [Parameter(Mandatory = $false)]
    [string]$UpdatedBy = "ci"
  )

  $paths = Get-InstallerVersioningPaths -RepoRoot $RepoRoot
  $payload = [PSCustomObject]@{
    lastBuild = $LastBuild
    updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    updatedBy = $UpdatedBy
  }

  $json = $payload | ConvertTo-Json
  Set-Content -Path $paths.CiBuildCounter -Value $json
}

function Resolve-InstallerVersion {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,
    [Parameter(Mandatory = $true)]
    [ValidateSet("ci", "local")]
    [string]$BuildContext,
    [Parameter(Mandatory = $false)]
    [int]$BaselineMajor = 1,
    [Parameter(Mandatory = $false)]
    [int]$BaselineMinor = 0,
    [Parameter(Mandatory = $false)]
    [int]$BaselinePatch = 0,
    [Parameter(Mandatory = $false)]
    [int]$PreviousReleaseLineSequence,
    [Parameter(Mandatory = $false)]
    [int]$CurrentReleaseLineSequence
    ,[Parameter(Mandatory = $false)]
    [int]$BuildNumberOverride
  )

  $state = Get-InstallerVersioningState -RepoRoot $RepoRoot
  $override = $state.Override
  $counter = [int]$state.CiBuildCounter.lastBuild

  $major = if ($null -ne $override.major) { [int]$override.major } else { $BaselineMajor }

  $releaseTransitionDetected = $false
  if ($PSBoundParameters.ContainsKey('PreviousReleaseLineSequence') -and $PSBoundParameters.ContainsKey('CurrentReleaseLineSequence')) {
    $releaseTransitionDetected = $CurrentReleaseLineSequence -gt $PreviousReleaseLineSequence
  }

  if ($null -ne $override.minor) {
    $minor = [int]$override.minor
  }
  elseif ($releaseTransitionDetected) {
    $minor = $BaselineMinor + 1
  }
  else {
    $minor = $BaselineMinor
  }

  if ($null -ne $override.patch) {
    $patch = [int]$override.patch
  }
  elseif ($releaseTransitionDetected -and $null -eq $override.minor) {
    $patch = 0
  }
  else {
    $patch = $BaselinePatch
  }

  if ($PSBoundParameters.ContainsKey('BuildNumberOverride')) {
    if ($BuildNumberOverride -lt 0) {
      throw "BuildNumberOverride must be non-negative."
    }
    $nextBuild = $BuildNumberOverride
  }
  else {
    $nextBuild = $counter + 1
  }
  $version = "$major.$minor.$patch.$nextBuild"

  if ($BuildContext -eq "ci" -and -not $PSBoundParameters.ContainsKey('BuildNumberOverride')) {
    Set-CiBuildCounter -RepoRoot $RepoRoot -LastBuild $nextBuild -UpdatedBy "ci"
  }

  $persisted = ($BuildContext -eq "ci") -and (-not $PSBoundParameters.ContainsKey('BuildNumberOverride'))

  return [PSCustomObject]@{
    Version = $version
    Persisted = $persisted
    Source = $BuildContext
    LastBuild = $nextBuild
    ReleaseTransitionDetected = $releaseTransitionDetected
  }
}

Export-ModuleMember -Function Get-InstallerRepoRoot, Get-DefaultDataRoot, New-InstallerLogDirectory, Get-DowngradeRemediationHint, Get-InstallerVersioningPaths, Get-InstallerVersioningState, Set-CiBuildCounter, Resolve-InstallerVersion
