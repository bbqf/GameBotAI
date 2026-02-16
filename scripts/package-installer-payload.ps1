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

$wixRoot = Join-Path $repoRoot "installer/wix"
$generatedFragmentPath = Join-Path $wixRoot "Fragments/PayloadFiles.Generated.wxs"

function Get-DeterministicId {
  param(
    [Parameter(Mandatory = $true)][string]$Prefix,
    [Parameter(Mandatory = $true)][string]$Value
  )

  $normalized = $Value.Replace('/', '\\').ToLowerInvariant()
  $md5 = [System.Security.Cryptography.MD5]::Create()
  try {
    $hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalized))
  }
  finally {
    $md5.Dispose()
  }

  $hex = [System.BitConverter]::ToString($hash).Replace("-", "")
  return "$Prefix$hex"
}

function Convert-ToWixRelativePath {
  param(
    [Parameter(Mandatory = $true)][string]$WixRoot,
    [Parameter(Mandatory = $true)][string]$AbsolutePath
  )

  $rootFull = (Resolve-Path $WixRoot).Path.TrimEnd('\\')
  $fileFull = (Resolve-Path $AbsolutePath).Path

  if ($fileFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    return $fileFull.Substring($rootFull.Length).TrimStart('\\')
  }

  $rootUri = New-Object System.Uri(($rootFull + '\\'))
  $fileUri = New-Object System.Uri($fileFull)
  $relativeUri = $rootUri.MakeRelativeUri($fileUri)
  return [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', '\\')
}

function Add-DirectoryContent {
  param(
    [Parameter(Mandatory = $true)][string]$CurrentDirectory,
    [Parameter(Mandatory = $true)][string]$WixRoot,
    [Parameter(Mandatory = $true)][string]$InstallGroupPrefix,
    [System.Collections.Generic.List[string]]$Lines,
    [System.Collections.Generic.List[string]]$ComponentIds,
    [Parameter(Mandatory = $true)][int]$IndentLevel
  )

  $indent = "  " * $IndentLevel

  $files = Get-ChildItem -Path $CurrentDirectory -File | Sort-Object Name
  foreach ($file in $files) {
    $relativeToWixRoot = Convert-ToWixRelativePath -WixRoot $WixRoot -AbsolutePath $file.FullName

    $normalizedRelativePath = $relativeToWixRoot.Replace('/', '\\')
    if ($normalizedRelativePath.EndsWith("payload\service\GameBot.Service.exe", [System.StringComparison]::OrdinalIgnoreCase)) {
      continue
    }

    $componentId = Get-DeterministicId -Prefix "CMP_" -Value $relativeToWixRoot
    $componentIds.Add($componentId) | Out-Null

    $Lines.Add([string]::Format('{0}<Component Id="{1}" Guid="*">', $indent, $componentId)) | Out-Null
    $Lines.Add([string]::Format('{0}  <File Source="{1}" KeyPath="yes" />', $indent, $relativeToWixRoot)) | Out-Null
    $Lines.Add([string]::Format('{0}</Component>', $indent)) | Out-Null
  }

  $subDirectories = Get-ChildItem -Path $CurrentDirectory -Directory | Sort-Object Name
  foreach ($subDirectory in $subDirectories) {
    $relativeToPayload = Convert-ToWixRelativePath -WixRoot $payloadRoot -AbsolutePath $subDirectory.FullName
    $directoryId = Get-DeterministicId -Prefix "DIR_" -Value ("$InstallGroupPrefix|$relativeToPayload")

    $Lines.Add([string]::Format('{0}<Directory Id="{1}" Name="{2}">', $indent, $directoryId, $subDirectory.Name)) | Out-Null
    Add-DirectoryContent -CurrentDirectory $subDirectory.FullName -WixRoot $WixRoot -InstallGroupPrefix $InstallGroupPrefix -Lines $Lines -ComponentIds $ComponentIds -IndentLevel ($IndentLevel + 1)
    $Lines.Add([string]::Format('{0}</Directory>', $indent)) | Out-Null
  }
}

$serviceComponentIds = New-Object 'System.Collections.Generic.List[string]'
$webUiComponentIds = New-Object 'System.Collections.Generic.List[string]'
$lines = New-Object 'System.Collections.Generic.List[string]'

$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">') | Out-Null
$lines.Add('  <Fragment>') | Out-Null
$lines.Add('    <DirectoryRef Id="APPLICATIONFOLDER">') | Out-Null
Add-DirectoryContent -CurrentDirectory (Join-Path $payloadRoot "service") -WixRoot $wixRoot -InstallGroupPrefix "service" -Lines $lines -ComponentIds $serviceComponentIds -IndentLevel 3
$lines.Add('    </DirectoryRef>') | Out-Null
$lines.Add('') | Out-Null
$lines.Add('    <DirectoryRef Id="WEBUIFOLDER">') | Out-Null
Add-DirectoryContent -CurrentDirectory (Join-Path $payloadRoot "web-ui") -WixRoot $wixRoot -InstallGroupPrefix "web-ui" -Lines $lines -ComponentIds $webUiComponentIds -IndentLevel 3
$lines.Add('    </DirectoryRef>') | Out-Null
$lines.Add('  </Fragment>') | Out-Null
$lines.Add('') | Out-Null
$lines.Add('  <Fragment>') | Out-Null
$lines.Add('    <ComponentGroup Id="ServicePayloadFiles">') | Out-Null
foreach ($componentId in $serviceComponentIds) {
  $lines.Add([string]::Format('      <ComponentRef Id="{0}" />', $componentId)) | Out-Null
}
$lines.Add('    </ComponentGroup>') | Out-Null
$lines.Add('') | Out-Null
$lines.Add('    <ComponentGroup Id="WebUiPayloadFiles">') | Out-Null
foreach ($componentId in $webUiComponentIds) {
  $lines.Add([string]::Format('      <ComponentRef Id="{0}" />', $componentId)) | Out-Null
}
$lines.Add('    </ComponentGroup>') | Out-Null
$lines.Add('  </Fragment>') | Out-Null
$lines.Add('</Wix>') | Out-Null

$lines | Set-Content -Path $generatedFragmentPath -Encoding UTF8
Write-Host "Installer payload prepared at $payloadRoot"
