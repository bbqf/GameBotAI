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
  $counter = Get-Content -Path $paths.CiBuildCounter -Raw | ConvertFrom-Json

  return [PSCustomObject]@{
    Paths = $paths
    Override = $override
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
    [int]$BuildNumberOverride
  )

  $state = Get-InstallerVersioningState -RepoRoot $RepoRoot
  $override = $state.Override
  $counter = [int]$state.CiBuildCounter.lastBuild

  $major = if ($null -ne $override.major) { [int]$override.major } else { $BaselineMajor }

  if ($null -ne $override.minor) {
    $minor = [int]$override.minor
  }
  else {
    $minor = $BaselineMinor
  }

  if ($null -ne $override.patch) {
    $patch = [int]$override.patch
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

  if ($BuildContext -eq "ci") {
    if (-not $PSBoundParameters.ContainsKey('BuildNumberOverride')) {
      Set-CiBuildCounter -RepoRoot $RepoRoot -LastBuild $nextBuild -UpdatedBy "ci"
    }
  }

  $persistedLegacy = ($BuildContext -eq "ci")
  $persisted = $persistedLegacy -and (-not $PSBoundParameters.ContainsKey('BuildNumberOverride'))

  return [PSCustomObject]@{
    Version = $version
    Persisted = ($BuildContext -eq "ci")
    PersistedCounterWrite = $persisted
    Source = $BuildContext
    LastBuild = $nextBuild
  }
}

# Feature 163. State of an isolated test install (made with new-test-variant.ps1). The rollback check (feature 162,
# FR-012) takes one snapshot before an upgrade and one after a forced failure, then compares them.
function Get-TestInstallSnapshot {
  param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9]{1,20}$')]
    [string]$InstanceName,
    [Parameter(Mandatory = $true)]
    [string]$InstallFolder
  )

  # The same derivation as in new-test-variant.ps1.
  $md5 = [System.Security.Cryptography.MD5]::Create()
  try { $bytes = $md5.ComputeHash([Text.Encoding]::UTF8.GetBytes("upgrade|$InstanceName")) } finally { $md5.Dispose() }
  $upgradeCode = '{' + ([Guid]::new($bytes)).ToString().ToUpperInvariant() + '}'

  $installer = New-Object -ComObject WindowsInstaller.Installer
  $products = @()
  try {
    $related = $installer.GetType().InvokeMember('RelatedProducts', 'GetProperty', $null, $installer, @($upgradeCode))
    foreach ($code in $related) {
      $version = $installer.GetType().InvokeMember('ProductInfo', 'GetProperty', $null, $installer, @($code, 'VersionString'))
      $products += "$code $version"
    }
  }
  catch { }
  finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer) }

  $autostartName = "GameBotTest$InstanceName"
  $runValue = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $autostartName -ErrorAction SilentlyContinue)
  $runText = if ($null -ne $runValue) { [string]$runValue.$autostartName } else { '' }

  $menuFolder = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\GameBotTest$InstanceName"
  $shortcuts = @()
  if (Test-Path $menuFolder) { $shortcuts = @(Get-ChildItem $menuFolder -File | ForEach-Object Name | Sort-Object) }

  $networkFile = Join-Path $InstallFolder 'data\config\network.json'
  $networkText = if (Test-Path $networkFile) { (Get-Content $networkFile -Raw) } else { '' }

  return [pscustomobject]@{
    Products    = @($products | Sort-Object)
    RunValue    = $runText
    Shortcuts   = $shortcuts
    NetworkJson = $networkText
  }
}

function Compare-TestInstallSnapshot {
  param(
    [Parameter(Mandatory = $true)] $Before,
    [Parameter(Mandatory = $true)] $After
  )

  $differences = @()
  foreach ($name in 'Products', 'Shortcuts') {
    if ((@($Before.$name) -join '|') -ne (@($After.$name) -join '|')) {
      $differences += "$name differ. Before: [$(@($Before.$name) -join ', ')] After: [$(@($After.$name) -join ', ')]"
    }
  }
  foreach ($name in 'RunValue', 'NetworkJson') {
    if ($Before.$name -ne $After.$name) { $differences += "$name differs." }
  }
  return , $differences
}

Export-ModuleMember -Function Get-InstallerRepoRoot, Get-DefaultDataRoot, New-InstallerLogDirectory, Get-DowngradeRemediationHint, Get-InstallerVersioningPaths, Get-InstallerVersioningState, Set-CiBuildCounter, Resolve-InstallerVersion, Get-TestInstallSnapshot, Compare-TestInstallSnapshot
