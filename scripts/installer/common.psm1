Set-StrictMode -Version Latest

function Get-InstallerRepoRoot {
  $modulePath = Split-Path -Parent $PSCommandPath
  return (Resolve-Path (Join-Path $modulePath "../..")).Path
}

function Get-DefaultDataRoot {
  param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("perMachine", "perUser")]
    [string]$InstallScope
  )

  if ($InstallScope -eq "perMachine") {
    return Join-Path $env:ProgramData "GameBot/data"
  }

  return Join-Path $env:LocalAppData "GameBot/data"
}

function New-InstallerLogDirectory {
  $logRoot = Join-Path $env:ProgramData "GameBot/Installer/logs"
  New-Item -Path $logRoot -ItemType Directory -Force | Out-Null
  return $logRoot
}

Export-ModuleMember -Function Get-InstallerRepoRoot, Get-DefaultDataRoot, New-InstallerLogDirectory
