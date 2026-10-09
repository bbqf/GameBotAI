# Writes update-manifest.json for a built MSI (feature 131, contract release-manifest.md).
# The installed bot reads this file from the GitHub Release. It checks the download against
# the size and the SHA-256 checksum, so the file must describe the exact MSI that is published.
param(
  [Parameter(Mandatory = $false)]
  [string]$MsiPath,

  [Parameter(Mandatory = $true)]
  [string]$OutputPath,

  # The 4-part version of the release. If it is empty, the script reads the payload version
  # from payload-manifest.json. That is the version that the installed bot reports.
  [Parameter(Mandatory = $false)]
  [string]$Version,

  [Parameter(Mandatory = $false)]
  [string]$PayloadManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($MsiPath)) {
  $MsiPath = Join-Path $repoRoot "installer/wix/payload/GameBot.msi"
}
if ([string]::IsNullOrWhiteSpace($PayloadManifestPath)) {
  $PayloadManifestPath = Join-Path $repoRoot "installer/wix/payload/payload-manifest.json"
}

if (-not (Test-Path $MsiPath)) {
  throw "MSI not found at '$MsiPath'. Build the installer first."
}

function ConvertTo-FourPartVersion {
  param([string]$Text)

  # Remove a suffix such as "+commit", like the bot does.
  $plus = $Text.IndexOf('+')
  $clean = if ($plus -ge 0) { $Text.Substring(0, $plus) } else { $Text }
  if ($clean -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version '$Text' is not a 4-part version (major.minor.patch.build)."
  }
  return $clean
}

$payloadVersion = $null
if (Test-Path $PayloadManifestPath) {
  $payload = Get-Content -Path $PayloadManifestPath -Raw | ConvertFrom-Json
  if ($payload.PSObject.Properties.Name -contains "payloadVersion" -and -not [string]::IsNullOrWhiteSpace($payload.payloadVersion)) {
    $payloadVersion = ConvertTo-FourPartVersion -Text $payload.payloadVersion
  }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
  if ($null -eq $payloadVersion) {
    throw "No version given and no payload version found in '$PayloadManifestPath'."
  }
  $Version = $payloadVersion
}
else {
  $Version = ConvertTo-FourPartVersion -Text $Version
  if ($null -ne $payloadVersion -and $payloadVersion -ne $Version) {
    throw "Version '$Version' does not match the payload version '$payloadVersion'. The bot would offer a wrong update."
  }
}

$msi = Get-Item -Path $MsiPath
$sha = (Get-FileHash -Path $msi.FullName -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
  schemaVersion = 1
  version = $Version
  msi = [ordered]@{
    fileName = $msi.Name
    sizeBytes = [long]$msi.Length
    sha256 = $sha
  }
}

$json = $manifest | ConvertTo-Json -Depth 5
$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path $outputDirectory)) {
  New-Item -Path $outputDirectory -ItemType Directory -Force | Out-Null
}

# UTF-8 without a BOM: the bot parses the file with System.Text.Json.
[System.IO.File]::WriteAllText($OutputPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote update manifest for $Version to $OutputPath (sha256 $sha, $($msi.Length) bytes)."
