#requires -Version 5.1
<#
.SYNOPSIS
  Makes an isolated test copy of the GameBot MSI (feature 162, research R12).

.DESCRIPTION
  The real MSI is per-user and has fixed identities. A test install of it would replace the production product.
  This script copies the MSI and changes these values in the copy, so it shares nothing with production:
    - UpgradeCode, ProductCode, package code, and product name
    - all component GUIDs (derived from the instance name)
    - the registry root (HKCU\Software\GameBot is replaced by HKCU\Software\GameBotTest\<instance>)
    - the autostart value name, the Start-Menu folder, and the default install folder
    - the property NETWORK_REGISTRY_ROOT (the older host and port values, read and deleted by the installer)
  The default MSI is not changed. Install the copy with msiexec, a temporary APPLICATIONFOLDER, and a temporary PORT.

.PARAMETER MsiPath
  The MSI to copy. Default: the Release build, then the Debug build.

.PARAMETER InstanceName
  Letters and digits only, 1 to 20 characters. Example: T162.

.PARAMETER Revision
  Optional. A second package of the same instance with another revision has the same UpgradeCode and another
  ProductCode. An install of it over the first one runs a major upgrade, as the web UI and the API do.

.PARAMETER FailAfterInstallFiles
  Optional. Adds an action that stops the install with an error after the files are copied. By then the old product
  is removed and the saved file is not yet written. Use it to check that a failed upgrade rolls back (feature 162, FR-012).

.PARAMETER OutputPath
  The path of the test MSI. Default: next to the source MSI, named GameBot.<InstanceName>.msi.
#>
[CmdletBinding()]
param(
  [string]$MsiPath,
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^[A-Za-z0-9]{1,20}$')]
  [string]$InstanceName,
  [string]$OutputPath,
  [ValidatePattern('^[A-Za-z0-9]{0,20}$')]
  [string]$Revision = '',
  [switch]$FailAfterInstallFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $MsiPath) {
  foreach ($configuration in 'Release', 'Debug') {
    $candidate = Join-Path $repoRoot "installer\wix\bin\x64\$configuration\GameBot.msi"
    if (Test-Path $candidate) { $MsiPath = $candidate; break }
  }
}
if (-not $MsiPath -or -not (Test-Path $MsiPath)) { throw 'No MSI found. Build installer\wix\GameBot.Msi.wixproj first, or give -MsiPath.' }
$MsiPath = (Resolve-Path $MsiPath).Path
if (-not $OutputPath) { $OutputPath = Join-Path (Split-Path $MsiPath) "GameBot.$InstanceName.msi" }
if ([IO.Path]::GetFullPath($OutputPath) -eq [IO.Path]::GetFullPath($MsiPath)) {
  throw 'The output path must not be the source MSI.'
}

Copy-Item -LiteralPath $MsiPath -Destination $OutputPath -Force
Set-ItemProperty -LiteralPath $OutputPath -Name IsReadOnly -Value $false

function New-DerivedGuid([string]$Seed) {
  $md5 = [System.Security.Cryptography.MD5]::Create()
  try { $bytes = $md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($Seed)) } finally { $md5.Dispose() }
  return '{' + ([Guid]::new($bytes)).ToString().ToUpperInvariant() + '}'
}

function Invoke-Com($Target, [string]$Member, $Arguments) {
  return $Target.GetType().InvokeMember($Member, 'InvokeMethod', $null, $Target, $Arguments)
}

function Open-View($Database, [string]$Sql) {
  $view = Invoke-Com $Database 'OpenView' @($Sql)
  Invoke-Com $view 'Execute' $null | Out-Null
  return $view
}

function Get-Rows($Database, [string]$Sql, [int]$Columns) {
  $view = Open-View $Database $Sql
  $rows = @()
  while ($true) {
    $record = Invoke-Com $view 'Fetch' $null
    if ($null -eq $record) { break }
    $values = @()
    for ($i = 1; $i -le $Columns; $i++) { $values += $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @($i)) }
    $rows += , $values
  }
  Invoke-Com $view 'Close' $null | Out-Null
  return , $rows
}

function Invoke-Sql($Database, [string]$Sql) {
  $view = Open-View $Database $Sql
  Invoke-Com $view 'Close' $null | Out-Null
}

function Escape-Sql([string]$Value) { return $Value.Replace("'", "''") }

# MSICONDITION_TRUE (1) means that the table exists.
function Test-Table($Database, [string]$Name) {
  return ($Database.GetType().InvokeMember('TablePersistent', 'GetProperty', $null, $Database, @($Name)) -eq 1)
}

$registryRoot = "Software\GameBotTest\$InstanceName"
$networkRoot = "$registryRoot\Network"
$folderName = "GameBotTest$InstanceName"
$autostartName = "GameBotTest$InstanceName"
$newUpgradeCode = New-DerivedGuid "upgrade|$InstanceName"
$newProductCode = New-DerivedGuid "product|$InstanceName|$Revision"
$newPackageCode = New-DerivedGuid "package|$InstanceName|$Revision|$([IO.File]::GetLastWriteTimeUtc($MsiPath).Ticks)"

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = Invoke-Com $installer 'OpenDatabase' @($OutputPath, 1)   # 1 = transact (read and write)

# The MSI must come from this branch. An older MSI has no NETWORK_REGISTRY_ROOT property.
if ((Get-Rows $database 'SELECT `Value` FROM `Property` WHERE `Property`=''NETWORK_REGISTRY_ROOT''' 1).Count -eq 0) {
  throw 'The MSI has no NETWORK_REGISTRY_ROOT property. Build the MSI again from this branch (feature 162).'
}

# Properties
$oldUpgradeCode = (Get-Rows $database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='UpgradeCode'" 1)[0][0]
Invoke-Sql $database "UPDATE ``Property`` SET ``Value``='$newUpgradeCode' WHERE ``Property``='UpgradeCode'"
Invoke-Sql $database "UPDATE ``Property`` SET ``Value``='$newProductCode' WHERE ``Property``='ProductCode'"
Invoke-Sql $database "UPDATE ``Property`` SET ``Value``='GameBot $InstanceName (test)' WHERE ``Property``='ProductName'"
Invoke-Sql $database "UPDATE ``Property`` SET ``Value``='$(Escape-Sql $networkRoot)' WHERE ``Property``='NETWORK_REGISTRY_ROOT'"

# Upgrade table: the upgrade code is a key column, so delete the rows and insert them again.
$upgradeRows = Get-Rows $database 'SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`, `Remove`, `ActionProperty` FROM `Upgrade`' 7
Invoke-Sql $database 'DELETE FROM `Upgrade`'
$upgradeView = Open-View $database 'SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`, `Remove`, `ActionProperty` FROM `Upgrade`'
foreach ($row in $upgradeRows) {
  $record = Invoke-Com $installer 'CreateRecord' @(7)
  $code = if ($row[0] -eq $oldUpgradeCode) { $newUpgradeCode } else { $row[0] }
  $record.GetType().InvokeMember('StringData', 'SetProperty', $null, $record, @(1, $code)) | Out-Null
  for ($i = 2; $i -le 4; $i++) {
    if (-not [string]::IsNullOrEmpty($row[$i - 1])) { $record.GetType().InvokeMember('StringData', 'SetProperty', $null, $record, @($i, $row[$i - 1])) | Out-Null }
  }
  $record.GetType().InvokeMember('IntegerData', 'SetProperty', $null, $record, @(5, [int]$row[4])) | Out-Null
  for ($i = 6; $i -le 7; $i++) {
    if (-not [string]::IsNullOrEmpty($row[$i - 1])) { $record.GetType().InvokeMember('StringData', 'SetProperty', $null, $record, @($i, $row[$i - 1])) | Out-Null }
  }
  Invoke-Com $upgradeView 'Modify' @(1, $record) | Out-Null   # 1 = insert
}
Invoke-Com $upgradeView 'Close' $null | Out-Null
# Component GUIDs
$components = Get-Rows $database 'SELECT `Component`, `ComponentId` FROM `Component`' 2
foreach ($row in $components) {
  if ([string]::IsNullOrEmpty($row[1])) { continue }
  $derived = New-DerivedGuid "component|$InstanceName|$($row[1])"
  Invoke-Sql $database "UPDATE ``Component`` SET ``ComponentId``='$derived' WHERE ``Component``='$(Escape-Sql $row[0])'"
}

# Registry: own root, and own autostart name
$registryRows = Get-Rows $database 'SELECT `Registry`, `Key`, `Name` FROM `Registry`' 3
foreach ($row in $registryRows) {
  $key = $row[1]
  $name = $row[2]
  $newKey = $key
  if ($key -eq 'Software\GameBot' -or $key.StartsWith('Software\GameBot\')) {
    $newKey = $registryRoot + $key.Substring('Software\GameBot'.Length)
  }
  $newName = $name
  if ($key -eq 'Software\Microsoft\Windows\CurrentVersion\Run' -and $name -eq 'GameBot') { $newName = $autostartName }
  if ($newKey -ne $key -or $newName -ne $name) {
    Invoke-Sql $database "UPDATE ``Registry`` SET ``Key``='$(Escape-Sql $newKey)', ``Name``='$(Escape-Sql $newName)' WHERE ``Registry``='$(Escape-Sql $row[0])'"
  }
}

# Registry searches (the saved protocol)
$locatorRows = if (Test-Table $database 'RegLocator') { Get-Rows $database 'SELECT `Signature_`, `Key` FROM `RegLocator`' 2 } else { @() }
foreach ($row in $locatorRows) {
  $key = $row[1]
  if ($key -eq 'Software\GameBot' -or $key.StartsWith('Software\GameBot\')) {
    $newKey = $registryRoot + $key.Substring('Software\GameBot'.Length)
    Invoke-Sql $database "UPDATE ``RegLocator`` SET ``Key``='$(Escape-Sql $newKey)' WHERE ``Signature_``='$(Escape-Sql $row[0])'"
  }
}

# Folders: Start-Menu folder and default install folder
Invoke-Sql $database "UPDATE ``Directory`` SET ``DefaultDir``='$folderName' WHERE ``Directory``='ApplicationProgramsFolder'"
Invoke-Sql $database "UPDATE ``Directory`` SET ``DefaultDir``='$folderName' WHERE ``Directory``='APPLICATIONFOLDER'"
Invoke-Sql $database "UPDATE ``CustomAction`` SET ``Target``='[LocalAppDataFolder]$folderName' WHERE ``Action``='SetApplicationFolder'"

if ($FailAfterInstallFiles) {
  # Type 19 stops the install with the text in the Target column. Sequence 4100 is after InstallFiles (4000).
  Invoke-Sql $database 'INSERT INTO `CustomAction` (`Action`, `Type`, `Target`) VALUES (''GbForcedFailure'', 19, ''Forced failure for the rollback test'')'
  Invoke-Sql $database 'INSERT INTO `InstallExecuteSequence` (`Action`, `Sequence`) VALUES (''GbForcedFailure'', 4100)'
}

# Package code (summary information, property 9)
$summary = $database.GetType().InvokeMember('SummaryInformation', 'GetProperty', $null, $database, @(20))
$summary.GetType().InvokeMember('Property', 'SetProperty', $null, $summary, @(9, $newPackageCode)) | Out-Null
Invoke-Com $summary 'Persist' $null | Out-Null

Invoke-Com $database 'Commit' $null | Out-Null
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($database)
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
# Release the file at once. An install that starts right after the script would fail with error 1619 otherwise.
[GC]::Collect()
[GC]::WaitForPendingFinalizers()

Write-Host "Test MSI: $OutputPath"
Write-Host "  UpgradeCode:    $newUpgradeCode"
Write-Host "  ProductCode:    $newProductCode"
Write-Host "  Registry root:  HKCU\$registryRoot"
Write-Host "  Network root:   HKCU\$networkRoot"
Write-Host "  Start-Menu:     $folderName"
Write-Host "  Autostart name: $autostartName"
Write-Host "  Default folder: [LocalAppDataFolder]$folderName"
Write-Host 'Install it with a temporary folder and a temporary port, for example:'
Write-Host "  msiexec /i `"$OutputPath`" /qn APPLICATIONFOLDER=`"C:\Temp\gb-test`" PORT=18080 BIND_HOST=127.0.0.1"
